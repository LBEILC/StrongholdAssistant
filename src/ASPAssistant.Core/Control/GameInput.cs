using System.Diagnostics;
using System.Drawing;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Control;

internal static class ControlGesture
{
    public static Point Pixel(double x, double y, int w, int h) => new(
        Math.Clamp((int)Math.Round(x * w), 0, w - 1), Math.Clamp((int)Math.Round(y * h), 0, h - 1));

    public static async Task Drag(DragStep drag, Point start, Point end, Action<Point> down,
        Action<Point> move, Action<Point> up, CancellationToken token)
    {
        var point = start;
        down(start);
        try
        {
            await Task.Delay(drag.HoldMs, token);
            var clock = Stopwatch.StartNew();
            double fraction;
            do
            {
                int remaining = Math.Max(0, drag.MoveMs - (int)clock.ElapsedMilliseconds);
                await Task.Delay(Math.Min(16, remaining), token);
                token.ThrowIfCancellationRequested();
                fraction = drag.MoveMs <= 0 ? 1 : Math.Min(1, clock.Elapsed.TotalMilliseconds / drag.MoveMs);
                point = new(start.X + (int)Math.Round((end.X - start.X) * fraction),
                    start.Y + (int)Math.Round((end.Y - start.Y) * fraction));
                move(point);
            } while (fraction < 1);
            await Task.Delay(drag.ReleaseMs, token);
        }
        finally { up(point); }
    }
}

public sealed class AdbGameInput(string serial, int display, int width, int height, Action<string[]> send) : IGameInput
{
    private void Command(Point point, params string[] operation) =>
        send(["-s", serial, "shell", "input", "-d", display.ToString(), .. operation, point.X.ToString(), point.Y.ToString()]);

    public async Task RunAsync(InputStep step, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (step)
        {
            case TapStep tap:
                Command(ControlGesture.Pixel(tap.X, tap.Y, width, height), "tap");
                break;
            case DragStep drag:
                await ControlGesture.Drag(drag, ControlGesture.Pixel(drag.X1, drag.Y1, width, height),
                    ControlGesture.Pixel(drag.X2, drag.Y2, width, height), p => Command(p, "motionevent", "DOWN"),
                    p => Command(p, "motionevent", "MOVE"), p => Command(p, "motionevent", "UP"), token);
                break;
            case WaitStep wait: await Task.Delay(wait.Ms, token); break;
            default: throw new NotSupportedException("模拟器不支持键盘输入。");
        }
    }
}

public sealed class PcGameInput(nint window) : IGameInput, IDisposable
{
    private User32.POINT? _origin;
    private void CheckWindow()
    {
        if (!User32.IsWindow(window) || User32.IsIconic(window))
            throw new ControlLostException("游戏窗口丢失或已最小化。");
    }

    public void Activate()
    {
        CheckWindow();
        if (_origin is null)
        {
            User32.GetCursorPos(out var origin);
            _origin = origin;
        }
        User32.SetForegroundWindow(window);
    }

    private void CheckForeground()
    {
        CheckWindow();
        if (User32.GetForegroundWindow() != window) throw new ControlLostException("游戏窗口不在前台。");
    }

    public async Task RunAsync(InputStep step, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckForeground();
        var rect = User32.GetClientRectScreen(window) ?? throw new ControlLostException("无法读取游戏客户区。");
        Point Screen(double x, double y)
        {
            var p = ControlGesture.Pixel(x, y, rect.Width, rect.Height);
            return new(rect.Left + p.X, rect.Top + p.Y);
        }
        void Down(Point p)
        {
            CheckForeground();
            User32.SetCursorPos(p.X, p.Y);
            User32.SendMouse(User32.MOUSEEVENTF_MOVE, 1, 0);
            User32.SendMouse(User32.MOUSEEVENTF_MOVE, -1, 0);
            token.ThrowIfCancellationRequested();
            User32.SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
        }
        switch (step)
        {
            case WaitStep wait: await Task.Delay(wait.Ms, token); break;
            case KeyStep key:
                if (key.Key.Length != 1 || !char.IsAsciiLetterOrDigit(key.Key[0])) throw new ArgumentException("快捷键无效。");
                var vk = char.ToUpperInvariant(key.Key[0]);
                User32.SendScanKey(vk, false);
                try { await Task.Delay(45, token); }
                finally { User32.SendScanKey(vk, true); }
                break;
            case TapStep tap:
                Down(Screen(tap.X, tap.Y));
                try { await Task.Delay(60, token); }
                finally { User32.SendMouse(User32.MOUSEEVENTF_LEFTUP); }
                break;
            case DragStep drag:
                await ControlGesture.Drag(drag, Screen(drag.X1, drag.Y1), Screen(drag.X2, drag.Y2), Down, p =>
                {
                    CheckForeground();
                    User32.GetCursorPos(out var current);
                    User32.SendMouse(User32.MOUSEEVENTF_MOVE, p.X - current.X, p.Y - current.Y);
                }, _ => User32.SendMouse(User32.MOUSEEVENTF_LEFTUP), token);
                break;
        }
    }

    public void Dispose()
    {
        if (_origin is { } p) User32.SetCursorPos(p.X, p.Y);
    }
}
