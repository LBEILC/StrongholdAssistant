using System.ComponentModel;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

/// <summary>物资页专用截屏、排序切换及横向拖动；开始前把游戏切到前台，不点击卡片。</summary>
public sealed class RosterGameControl : IDisposable
{
    private readonly GameConnectionService _connection;
    private readonly GameConnectionState _start;
    private readonly ScreenCaptureService _screen;
    public RosterGameControl(GameConnectionService connection)
    {
        _screen = new(connection) { MaxCaptureWidth = int.MaxValue };
        _connection = connection; _start = connection.State;
        if (_start.Window == 0 || !User32.IsWindow(_start.Window)) throw new IOException("未找到游戏窗口。");
        if (User32.IsIconic(_start.Window)) throw new IOException("游戏窗口已最小化，请先恢复窗口。");
        if (!connection.IsAdb)
        {
            // 全屏的游戏不在前台时会停止刷新、不处理拖动：点完助手的「读取」焦点在助手上（v1.2.1 玩家诊断包）
            User32.SetForegroundWindow(_start.Window);
            Thread.Sleep(500);
        }
    }
    public bool Lost => _connection.State.Window != _start.Window || !User32.IsWindow(_start.Window) || User32.IsIconic(_start.Window);
    public byte[] Capture()
    {
        if (Lost) throw new IOException("游戏窗口丢失或已最小化。");
        if (_connection.IsAdb) return _connection.CaptureAdbScreen(1920) ?? throw new IOException("模拟器截图失败。");
        // 物资页允许静止画面，沿用原有读取行为；原生截图控制器与扫描共用。
        using var image = _connection.WindowCapture.Capture(_start.Window, requireFresh: false);
        if (image is null)
        {
            if (_connection.WindowCapture.HideCaptureBorder && _screen.CaptureBitBltWindow(_start.Window) is { } frame)
                return frame;
            throw new IOException("物资调配页截图失败。");
        }
        using var stream = new MemoryStream();
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
    public async Task SelectTierSortAsync(RosterSort sort, CancellationToken token)
    {
        if (Lost) throw new IOException("游戏窗口丢失或已最小化。");
        token.ThrowIfCancellationRequested();
        if (_connection.IsAdb) await Task.Run(() => _connection.SelectRosterTierAdb(sort.X, sort.Y), token);
        else
        {
            var rect = User32.GetClientRectScreen(_start.Window) ?? throw new IOException("无法读取游戏客户区。");
            var point = RosterCoordinates.ToScreen(sort.X, sort.Y, rect.Width, rect.Height);
            Mouse(() =>
            {
                MoveTo(rect.Left + point.X, rect.Top + point.Y);
                User32.SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
                Thread.Sleep(60);
                User32.SendMouse(User32.MOUSEEVENTF_LEFTUP);
            });
        }
        await Task.Delay(1000, token);
    }

    // 点击和拖动都用像真鼠标一样的相对位移输入：发给窗口的消息、MaaFramework 的绝对坐标输入（Seize），
    // 全屏的游戏都不认，点排序、拖列表没反应（2026-11-16 实机：只有相对位移能拖动）。
    public async Task DragAsync(bool left, int distance, CancellationToken token)
    {
        if (Lost) throw new IOException("游戏窗口丢失或已最小化。");
        token.ThrowIfCancellationRequested();
        if (_connection.IsAdb) await Task.Run(() => _connection.DragRosterAdb(left, distance), token);
        else
        {
            var rect = User32.GetClientRectScreen(_start.Window) ?? throw new IOException("无法读取游戏客户区。");
            var (start, end) = RosterCoordinates.Drag(left, distance, rect.Width, rect.Height);
            int steps = 40, dx = end.X - start.X;
            Mouse(() =>
            {
                MoveTo(rect.Left + start.X, rect.Top + start.Y);
                User32.SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
                Thread.Sleep(100);
                for (int i = 0; i < steps; i++)
                {
                    User32.SendMouse(User32.MOUSEEVENTF_MOVE, dx * (i + 1) / steps - dx * i / steps, 0);
                    Thread.Sleep(16);
                }
                Thread.Sleep(300);
                User32.SendMouse(User32.MOUSEEVENTF_LEFTUP);
            });
        }
        await Task.Delay(1000, token);
    }

    // 先把光标放到起点，再补一下相对位移，让游戏收到一次真实的移动事件
    private static void MoveTo(int x, int y)
    {
        User32.SetCursorPos(x, y);
        User32.SendMouse(User32.MOUSEEVENTF_MOVE, 1, 0);
        User32.SendMouse(User32.MOUSEEVENTF_MOVE, -1, 0);
        Thread.Sleep(50);
    }

    // 操作完把光标放回原处
    private static void Mouse(Action input)
    {
        User32.GetCursorPos(out var origin);
        try { input(); }
        catch (Win32Exception ex) { throw new IOException($"物资页鼠标操作失败：{ex.Message}", ex); }
        finally { User32.SetCursorPos(origin.X, origin.Y); }
    }

    public void Dispose() { }
}
