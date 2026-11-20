using ASPAssistant.Core.Control;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed record ControlSettings(bool Enabled = false, int Consent = 0, bool External = false,
    string Mode = "dryRun", string StopHotkey = "Ctrl+Alt+Q", bool Api = false)
{
    // TODO(design): 风险页上线时与界面的首版说明版本对齐。
    public const int ConsentVersion = 1;
    public ControlSettings Local()
    {
        bool enabled = Enabled && Consent == ConsentVersion;
        return this with { Enabled = enabled, External = enabled && External };
    }
}

public sealed partial class HostApi
{
    private ControlRunner? _control;
    private ScreenCaptureService? _controlCapture;
    private OcrScannerService? _controlScanner;
    private GameConnectionState? _controlTarget;
    private EmulatorDevice? _controlDevice;
    internal string ControlDirectory => Path.Combine(DataDirectory, "control");

    internal void InitializeControl(ScreenCaptureService capture, OcrScannerService scanner)
    {
        _controlCapture = capture;
        _controlScanner = scanner;
        SetControlCompiler(new ActionCompiler(), ControlPreview.Render);
        capture.Connection.Changed += state =>
        {
            if (_controlTarget is { } target && (target.Window != state.Window || target.Source != state.Source
                || state.Problem != null || capture.Connection.ConnectedDevice != _controlDevice
                || _controlDevice != null && (target.Width != state.Width || target.Height != state.Height)))
                StopControl("lost");
        };
    }

    internal void SetControlCompiler(IActionCompiler compiler, Func<ControlFrame, CompiledAction, byte[]> preview)
    {
        _control = new(ControlDirectory, compiler, preview);
        _control.Changed += RecordControlJob;
        _control.Changed += job => Changed?.Invoke("controlJob", job);
    }

    internal ControlJob RunActions(GameAction[] actions, string? mode, string source = "ui")
    {
        var settings = Settings.Control;
        if (!settings.Enabled) throw new InvalidOperationException("先在设置里打开自动操作");
        if (source == "api" && !settings.External) throw new InvalidOperationException("外部操作已关闭。");
        if (_control?.Current?.State is "running" or "waitingConfirm") throw new InvalidOperationException("上一批操作还没做完");
        if (Connection.State != "connected" || _controlCapture is null) throw new InvalidOperationException("还没连上游戏");
        var connection = _controlCapture.Connection;
        var target = connection.State;
        var device = connection.ConnectedDevice;
        var pc = target.Source == "pc";
        if (pc && ProcessElevation.ForWindow(target.Window) && !ProcessElevation.Current)
            throw new InvalidOperationException("游戏是以管理员身份运行的，助手也要以管理员身份运行才能执行操作。");
        if (_control is null) throw new InvalidOperationException("动作编译器尚未就绪。");

        void CheckTarget()
        {
            var now = connection.State;
            if (now.Source != target.Source || now.Window != target.Window || now.Problem != null ||
                (pc ? !User32.IsWindow(target.Window) || User32.IsIconic(target.Window) :
                    device is null || device != connection.ConnectedDevice || now.Width != target.Width || now.Height != target.Height))
                throw new ControlLostException("游戏连接已变化。");
        }
        async Task<ControlFrame> Capture(GameAction action, CancellationToken token)
        {
            CheckTarget();
            token.ThrowIfCancellationRequested();
            byte[]? bytes = null;
            Task<byte[]?> Read()
            {
                bytes = pc ? _controlCapture.CaptureWindowScreen() : connection.CaptureAdbScreen(int.MaxValue);
                if (bytes == null) throw new ControlLostException("游戏截图失败。");
                return Task.FromResult<byte[]?>(bytes);
            }
            FieldState? field = null;
            if (action is BuyAction or SellAction or MoveAction or FaceAction)
            {
                try { field = await _controlScanner!.ReadCurrentFieldAsync(Read, RecognizeFieldFrameAsync); }
                catch (Exception e) when (e is RecognitionException or InvalidOperationException)
                { throw new ControlScreenException(e.Message); }
            }
            else await Read();
            // 第一次下发时可能还没有场上快照，用首次动作前识别补齐基线。
            lock (_fieldGate)
                if (_beforeActions == null && field != null) _beforeActions = field;
            CheckTarget();
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes!);
            using var image = System.Drawing.Image.FromStream(stream);
            return new(bytes!, image.Width, image.Height, field, GetMatchState(), pc);
        }

        _controlTarget = target;
        _controlDevice = device;
        return _control.Start(source, mode ?? settings.Mode, settings.Mode, actions, Capture, () =>
        {
            CheckTarget();
            return pc ? new PcGameInput(target.Window) : connection.CreateControlInput(device!, target);
        });
    }

    internal void StopControl(string reason) => _control?.Stop(reason);
    internal Task StopControlAsync()
    {
        StopControl("user");
        return _control?.Completion ?? Task.CompletedTask;
    }

    internal void ObserveControlInput()
    {
        if (_controlTarget?.Source == "pc" && _control?.Current is { State: "running", Mode: not "dryRun" })
            StopControl("input");
    }
}
