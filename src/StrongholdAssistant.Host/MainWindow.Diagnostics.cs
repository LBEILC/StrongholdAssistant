using System.Windows.Interop;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

public partial class MainWindow
{
    internal object ReadDiagnosticWindows()
    {
        // 只读当前连接；无游戏模式不查找或截取游戏。
        var game = _tracker.CurrentGameWindowHandle;
        return new
        {
            game = Snapshot(game),
            gameClient = Rectangle(User32.GetClientRectScreen(game)),
            captureViewport = Rectangle(game == 0 ? null : _capture.Connection.GetViewport(game)),
            sidebar = Snapshot(new WindowInteropHelper(this).Handle),
            overlay = Snapshot(new WindowInteropHelper(_overlay).EnsureHandle())
        };
    }

    private static object? Snapshot(nint hwnd)
    {
        if (hwnd == 0 || !User32.GetWindowRect(hwnd, out var bounds)) return null;
        return new { bounds = Rectangle(bounds), dpi = User32.GetDpiForWindow(hwnd),
            visible = User32.IsWindowVisible(hwnd),
            dpiAwareness = User32.DescribeDpiAwareness(User32.GetWindowDpiAwarenessContext(hwnd)) };
    }

    private static object? Rectangle(RECT? bounds) => bounds is { } r
        ? new { left = r.Left, top = r.Top, width = r.Width, height = r.Height } : null;
}
