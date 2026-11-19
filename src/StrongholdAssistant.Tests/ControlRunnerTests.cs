using ASPAssistant.Core.Control;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class ControlRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly Compiler _compiler = new();
    private readonly Input _input = new();
    private readonly ControlRunner _runner;
    private readonly byte[][] _images = new[] { "collection-money-one.png", "collection-money-zero.png" }
        .Select(name => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name))).ToArray();
    private int _captures;
    public ControlRunnerTests() => _runner = new(_root, _compiler, (frame, _) => frame.Image);
    private ControlJob Start(string mode, int count = 2, string? ceiling = null) => _runner.Start("ui", mode, ceiling ?? mode,
        Enumerable.Repeat<GameAction>(new RefreshAction(), count).ToArray(), (_, _) =>
        {
            var bytes = _images[_captures++ % _images.Length];
            using var stream = new MemoryStream(bytes);
            using var image = System.Drawing.Image.FromStream(stream);
            return Task.FromResult(new ControlFrame(bytes, image.Width, image.Height, null, null, false));
        }, () => _input);

    [Theory]
    [InlineData("auto", "dryRun", 0)]
    [InlineData("dryRun", "auto", 0)]
    [InlineData("auto", "auto", 2)]
    public async Task ModeAndPreviews(string requested, string ceiling, int expected)
    {
        Start(requested, ceiling: ceiling);
        await _runner.Completion;
        var job = _runner.Current!;
        Assert.Equal("done", job.State);
        Assert.Equal(expected, _input.Count);
        Assert.All(job.Steps, step =>
        {
            Assert.Equal("done", step.State);
            var uri = new Uri(step.Preview!);
            Assert.Equal("control.local", uri.Host);
            Assert.True(File.Exists(Path.Combine(_root, uri.AbsolutePath.TrimStart('/'))));
        });
        if (expected > 1) Assert.True((_input.Times[1] - _input.Times[0]).TotalMilliseconds >= 250);
    }

    [Fact]
    public async Task ConfirmWaitsAndSkips()
    {
        var waits = System.Threading.Channels.Channel.CreateUnbounded<ControlJob>();
        _runner.Changed += j => { if (j.State == "waitingConfirm") waits.Writer.TryWrite(j); };
        Start("auto", ceiling: "confirm");
        var first = await waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, _input.Count);
        _runner.Confirm(first.Id, "skip");
        var second = await waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, _input.Count);
        _runner.Confirm(second.Id, "run");
        await _runner.Completion;
        Assert.Equal(1, _input.Count);
        Assert.Equal(new[] { "skipped", "done" }, _runner.Current!.Steps.Select(s => s.State));
    }

    [Theory]
    [InlineData("mismatch", 0)]
    [InlineData("verify", 2)]
    public async Task FailureCancelsRemaining(string reason, int checks)
    {
        _compiler.Failure = reason;
        Start("auto");
        await _runner.Completion;
        var job = _runner.Current!;
        Assert.Equal("failed", job.State);
        Assert.Equal(reason, job.Reason);
        Assert.Equal(checks, _compiler.Checks);
        Assert.Equal(new[] { "failed", "cancelled" }, job.Steps.Select(s => s.State));
    }

    [Fact]
    public async Task StopReleasesDrag()
    {
        _compiler.Step = new DragStep(0, 0, 1, 1, 20_000, 100, 50);
        Start("auto");
        await _input.Down.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _runner.Stop("user");
        await _runner.Completion;
        Assert.Equal("UP", _input.Commands.Last()[7]);
        Assert.Equal("user", _runner.Current!.Reason);
        Assert.All(_runner.Current.Steps, s => Assert.Equal("cancelled", s.State));
    }

    [Fact]
    public async Task LimitSpansBatches()
    {
        Start("dryRun", 60);
        await _runner.Completion;
        Start("auto", 1);
        await _runner.Completion;
        Assert.Equal("limit", _runner.Current!.Reason);
        Assert.Equal(0, _input.Count);
    }

    [Fact]
    public async Task AdbUsesBoundDisplayAndDragPhases()
    {
        List<string[]> commands = [];
        var adb = new AdbGameInput("test-device", 3, 800, 600, commands.Add);
        await adb.RunAsync(new TapStep(.5, .25), default);
        Assert.Equal(new[] { "-s", "test-device", "shell", "input", "-d", "3", "tap", "400", "150" }, commands[0]);
        await adb.RunAsync(new DragStep(.1, .2, 1, 1, 1, 2, 1), default);
        Assert.Equal(new[] { "DOWN", "80", "120" }, commands[1][7..]);
        Assert.Equal(new[] { "MOVE", "799", "599" }, commands[^2][7..]);
        Assert.Equal(new[] { "UP", "799", "599" }, commands[^1][7..]);
    }

    [Fact]
    public void OldSettingsGetLocalDefaults()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{}");
        var store = new HostSettingsStore(path);
        var settings = store.LoadSettings();
        Assert.Equal(new ControlSettings(false, 0, false, "dryRun", "Ctrl+Alt+Q", false), settings.Control);
        var enabled = new ControlSettings(true, 1, true, "confirm", "Alt+Q", true);
        store.SaveSettings(settings with { Control = enabled });
        Assert.Equal(enabled, store.LoadSettings().Control);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Compiler : IActionCompiler
    {
        public string? Failure;
        public int Checks;
        public InputStep Step = new TapStep(.5, .5);
        public CompileResult Compile(GameAction action, ControlFrame now) => Failure == "mismatch"
            ? new(null, Failure, "目标变化") : new(new(action, "刷新", [Step], 0), null, null);
        public VerifyResult Verify(GameAction action, ControlFrame before, ControlFrame after)
        { Checks++; return new(Failure != "verify", null); }
    }

    private sealed class Input : IGameInput
    {
        public int Count;
        public List<DateTimeOffset> Times = [];
        public List<string[]> Commands = [];
        public TaskCompletionSource Down = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RunAsync(InputStep step, CancellationToken token)
        {
            Count++;
            Times.Add(DateTimeOffset.UtcNow);
            return new AdbGameInput("test", 3, 800, 600, args =>
            {
                Commands.Add(args);
                if (args.Contains("DOWN")) Down.TrySetResult();
            }).RunAsync(step, token);
        }
    }
}
