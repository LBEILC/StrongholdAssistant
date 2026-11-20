using System.Diagnostics;

namespace ASPAssistant.Core.Control;

public sealed record ControlStep(GameAction Action, string Text = "", string State = "pending",
    string? Problem = null, string? Preview = null, bool? Verified = null);
public sealed record ControlJob(string Id, string Source, string Mode, string State, ControlStep[] Steps,
    string? Reason, string? Message, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed class ControlLostException(string message) : IOException(message);
public sealed class ControlScreenException(string message) : Exception(message);

public sealed class ControlRunner(string directory, IActionCompiler compiler, Func<ControlFrame, CompiledAction, byte[]> render)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<string>? _answer;
    private ControlJob? _job;
    private string? _reason;
    private int? _round;
    private int _count;
    private long? _lastInput;
    public ControlJob? Current { get { lock (_gate) return _job; } }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action<ControlJob>? Changed;

    public ControlJob Start(string source, string requested, string ceiling, GameAction[] actions,
        Func<GameAction, CancellationToken, Task<ControlFrame>> capture, Func<IGameInput> beginInput)
    {
        static int Level(string mode) => mode switch
        {
            "dryRun" => 0, "confirm" => 1, "auto" => 2, _ => throw new ArgumentException("执行模式无效。")
        };
        lock (_gate)
        {
            if (_stop != null) throw new InvalidOperationException("上一批操作还没做完");
            var mode = Level(requested) < Level(ceiling) ? requested : ceiling;
            var job = new ControlJob(Guid.NewGuid().ToString("N"), source, mode, "running",
                actions.Select(a => new ControlStep(a)).ToArray(), null, null, DateTimeOffset.UtcNow, null);
            _job = job;
            _reason = null;
            _stop = new();
            Changed?.Invoke(job);
            Completion = Task.Run(() => Execute(job, capture, beginInput, _stop.Token));
            return job;
        }
    }

    public void Confirm(string id, string answer)
    {
        lock (_gate)
            if (_job?.Id == id && answer is "run" or "skip") _answer?.TrySetResult(answer);
    }

    public void Stop(string reason)
    {
        lock (_gate)
        {
            if (_stop is null || _reason != null) return;
            _reason = reason;
            _stop.Cancel();
        }
    }

    private void Update(int index, ControlStep step, string state = "running")
    {
        lock (_gate)
        {
            var steps = (ControlStep[])_job!.Steps.Clone();
            steps[index] = step;
            _job = _job with { Steps = steps, State = state };
            Changed?.Invoke(_job);
        }
    }

    private async Task Execute(ControlJob job, Func<GameAction, CancellationToken, Task<ControlFrame>> capture,
        Func<IGameInput> beginInput, CancellationToken token)
    {
        int i = 0;
        string? failure = null, problem = null;
        IGameInput? input = null;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(directory, job.Id));
            foreach (var old in folder.Parent!.EnumerateDirectories().Where(d => d.Name != job.Id && Guid.TryParseExact(d.Name, "N", out _))
                .OrderByDescending(d => d.CreationTimeUtc).Skip(19)) old.Delete(true);
            if (job.Mode != "dryRun" && job.Steps.Length > 0)
            {
                token.ThrowIfCancellationRequested();
                input = beginInput();
                if (input is PcGameInput pc) pc.Activate();
            }
            ControlFrame? nextFrame = null;
            int stage = 0;
            for (; i < job.Steps.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var step = job.Steps[i] with { State = "running" };
                Update(i, step);
                if (nextFrame == null) stage = 0;
                var before = nextFrame ?? await capture(step.Action, token);
                nextFrame = null;
                var result = compiler.Compile(step.Action, before);
                if (result.Plan is not { } plan) { failure = result.Reason; problem = result.Problem; break; }
                var file = stage == 0 ? $"{i + 1}.png" : $"{i + 1}-{stage + 1}.png";
                var path = Path.Combine(folder.FullName, file);
                await File.WriteAllBytesAsync(path, render(before, plan), token);
                step = step with { Text = plan.Text, Preview = $"https://control.local/{job.Id}/{file}" };
                Update(i, step);
                if (job.Mode == "confirm")
                {
                    var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    lock (_gate) { _answer = answer; Update(i, step, "waitingConfirm"); }
                    var decision = await answer.Task.WaitAsync(token);
                    lock (_gate) _answer = null;
                    if (decision == "skip") { Update(i, step with { State = "skipped" }); continue; }
                    Update(i, step);
                    token.ThrowIfCancellationRequested();
                    if (input is PcGameInput confirmedPc) confirmedPc.Activate();
                    before = await capture(step.Action, token);
                    result = compiler.Compile(step.Action, before);
                    if (result.Plan is null) { failure = result.Reason; problem = result.Problem; break; }
                    // 重截后坐标会有一两像素的识别抖动，只比对动作描述（目标是谁、在哪格）
                    if (plan.Text != result.Plan.Text)
                    { failure = "mismatch"; problem = "确认期间目标已变化。"; break; }
                    plan = result.Plan;
                }
                if (before.State?.Round.Value is int round && _round != round) { _round = round; _count = 0; }
                if (_count >= 60) { failure = "limit"; problem = "本休整期已达到 60 个动作。"; break; }
                _count++;
                if (job.Mode == "dryRun") { Update(i, step with { State = "done" }); continue; }
                token.ThrowIfCancellationRequested();
                // 两个动作之间也沿用上一次输入结束的时间。
                foreach (var command in plan.Steps)
                {
                    if (_lastInput is long last)
                    {
                        var delay = TimeSpan.FromMilliseconds(250) - Stopwatch.GetElapsedTime(last);
                        if (delay > TimeSpan.Zero) await Task.Delay((int)Math.Ceiling(delay.TotalMilliseconds), token);
                    }
                    token.ThrowIfCancellationRequested();
                    try { await input!.RunAsync(command, token); }
                    finally { _lastInput = Stopwatch.GetTimestamp(); }
                }
                await Task.Delay(plan.SettleMs, token);
                var after = await capture(step.Action, token);
                var verification = compiler.Verify(step.Action, before, after);
                if (verification.Ok == false)
                {
                    await Task.Delay(800, token);
                    after = await capture(step.Action, token);
                    verification = compiler.Verify(step.Action, before, after);
                    if (verification.Ok != true)
                    {
                        string extension = after.Image[0] == 0xff ? "jpg" : "png";
                        await File.WriteAllBytesAsync(Path.Combine(folder.FullName, $"{i + 1}-after.{extension}"), after.Image, token);
                        Update(i, step with { Verified = verification.Ok });
                        failure = "verify"; problem = verification.Problem ?? "画面核对未通过。"; break;
                    }
                }
                if (plan.Continue && verification.Ok == true && !verification.Complete)
                {
                    nextFrame = after;
                    stage++;
                    i--;
                    continue;
                }
                Update(i, step with { State = "done", Verified = verification.Ok, Problem = verification.Problem });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            failure = e is ControlLostException ? "lost" : e is ControlScreenException ? "screen" : "error";
            problem = e.Message;
        }
        finally
        {
            (input as IDisposable)?.Dispose();
            lock (_gate)
            {
                var reason = _reason ?? failure;
                var state = reason == null ? "done" : _reason != null || reason == "lost" ? "stopped" : "failed";
                var steps = _job!.Steps.Select((s, n) => s.State is "pending" or "running"
                    ? s with { State = n == i && state == "failed" ? "failed" : "cancelled", Problem = n == i ? problem : null }
                    : s).ToArray();
                _job = _job with { State = state, Steps = steps, Reason = reason,
                    Message = reason == null ? null : $"第 {Math.Min(i + 1, steps.Length)} 步：{problem ?? StopText(reason)}", EndedAt = DateTimeOffset.UtcNow };
                _answer = null;
                _stop!.Dispose();
                _stop = null;
                AppLogger.Info("Control", $"{job.Id} {job.Source} {job.Mode} {state} {reason} 步骤={Math.Min(i + 1, steps.Length)}");
                Changed?.Invoke(_job);
            }
        }
    }

    private static string StopText(string reason) => reason switch
    {
        "disabled" => "自动操作已关闭", "lost" => "游戏连接已断开", "hotkey" => "按下停止键",
        "input" => "检测到玩家输入", _ => "已停止"
    };
}
