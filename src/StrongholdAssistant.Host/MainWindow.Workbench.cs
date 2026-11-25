using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Text.Json;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public partial class MainWindow
{
    private bool _workbench;
    private bool _switchingLayout;
    private FreeWindowBounds? _sidebarBounds;
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    private Rect CurrentWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var rect = User32.ReadWindowRect(hwnd);
        var work = User32.GetMonitorGeometry(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2).WorkArea;
        var dpi = User32.GetWindowDpiScale(hwnd);
        return new(work.Left / dpi, work.Top / dpi, work.Width / dpi, work.Height / dpi);
    }

    private void SetWorkbench(bool workbench)
    {
        if (_workbench == workbench) return;
        if (workbench && _collapsed) SetCollapsed(false);
        if (!workbench) RememberWorkbenchBounds();
        _switchingLayout = true;
        try
        {
            if (workbench)
            {
                _sidebarBounds = new(Left, Top, Width, Height);
                if (!_api.Settings.FollowGame) RememberFreeBounds();
                _workbench = true;
                _overlayMode = false;
                // 首次打开时先进入游戏所在屏幕，让 WPF 应用目标屏幕的 DPI。
                if (_dock.WorkbenchBounds is null && _lastClientRect is { } viewport)
                {
                    var work = WindowGeometry.GameMonitor(viewport, User32.GetMonitorGeometry).WorkArea;
                    var hwnd = new WindowInteropHelper(this).Handle;
                    var rect = User32.ReadWindowRect(hwnd);
                    User32.SetWindowBounds(hwnd, new RECT { Left = work.Left, Top = work.Top,
                        Right = work.Left + rect.Width, Bottom = work.Top + rect.Height });
                }
                else if (_dock.WorkbenchBounds is { } saved)
                {
                    Left = saved.Left; Top = saved.Top;
                }
                ApplyScale(_lastClientRect?.Height);
                ApplyBounds(WorkbenchGeometry.Calculate(CurrentWorkArea(), _uiScale, _dock.WorkbenchBounds));
                if (_dock.WorkbenchBounds?.Maximized == true) WindowState = WindowState.Maximized;
                _api.UpdateConnection(_api.Connection with { Overlay = false });
            }
            else
            {
                _workbench = false;
                WindowState = WindowState.Normal;
                _stateBeforeMinimize = WindowState.Normal;
                ApplyScale(_lastClientRect?.Height);
                ApplyBounds(_api.Settings.FollowGame ? _sidebarBounds! : _dock.FreeBounds ?? _sidebarBounds!);
                if (_api.Settings.FollowGame && _lastGameRect is { } game) UpdateGame(game);
                if (_api.Settings.FollowGame && _tracker.IsGameMinimized) MinimizeAssistant();
            }
        }
        finally { _switchingLayout = false; }
        if (WindowState != WindowState.Minimized) _stateBeforeMinimize = WindowState;
        if (_workbench) RememberWorkbenchBounds();
        else _api.SettingsStore.SaveDock(_dock);
        PublishMaximized();
    }

    private void ApplyBounds(FreeWindowBounds bounds)
    {
        Left = bounds.Left; Top = bounds.Top; Width = bounds.Width; Height = bounds.Height;
    }

    private void RememberWorkbenchBounds()
    {
        if (!_workbench || _switchingLayout || WindowState == WindowState.Minimized) return;
        var maximized = WindowState == WindowState.Maximized;
        var bounds = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _dock = _dock with { WorkbenchBounds = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized) };
        _api.SettingsStore.SaveDock(_dock);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_switchingLayout) return;
        if (WindowState != WindowState.Minimized) _stateBeforeMinimize = WindowState;
        RememberWorkbenchBounds();
        PublishMaximized();
    }

    private void PublishMaximized() => Browser.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(
        new { @event = "maximized", data = _workbench && (WindowState == WindowState.Maximized
            || WindowState == WindowState.Minimized && _stateBeforeMinimize == WindowState.Maximized) }, HostApi.JsonOptions));

    private static int ResizeHit(string? edge) => edge switch
    {
        "w" => 10, "e" => 11, "n" => 12, "nw" => 13,
        "ne" => 14, "s" => 15, "sw" => 16, "se" => 17,
        _ => throw new ArgumentException("窗口命令无效。")
    };

    private void ResizeWindow(string edge)
    {
        if (_collapsed || WindowState != WindowState.Normal) return;
        ReleaseCapture();
        User32.GetCursorPos(out var point);
        var position = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        User32.SendMessage(new WindowInteropHelper(this).Handle, 0x00A1, (IntPtr)ResizeHit(edge), position);
    }

    private bool HandleWorkbenchMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 双击、快捷键与贴靠都由系统处理；侧栏禁用最大化入口。
        if (!_workbench && (message == 0x0112 && (wParam.ToInt64() & 0xfff0) == 0xf030
            || message == 0x00A3 && wParam.ToInt32() == 2))
        {
            handled = true;
            return true;
        }
        if ((_workbench || !_collapsed) && message == 0x0024) // WM_GETMINMAXINFO，物理像素，坐标相对当前屏幕。
        {
            var rect = User32.ReadWindowRect(hwnd);
            var monitor = User32.GetMonitorGeometry(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MaxPosition = new() { X = monitor.WorkArea.Left - monitor.Bounds.Left, Y = monitor.WorkArea.Top - monitor.Bounds.Top };
            info.MaxSize = new() { X = monitor.WorkArea.Width, Y = monitor.WorkArea.Height };
            var dpi = User32.GetWindowDpiScale(hwnd);
            info.MinTrackSize = new()
            {
                X = _workbench ? (int)Math.Ceiling(MinWidth * dpi) : Math.Min((int)Math.Ceiling(320 * _uiScale * dpi), monitor.WorkArea.Width),
                Y = _workbench ? (int)Math.Ceiling(MinHeight * dpi) : Math.Min((int)Math.Ceiling(480 * _uiScale * dpi), monitor.WorkArea.Height)
            };
            if (!_workbench)
                info.MaxTrackSize = new() { X = Math.Min((int)Math.Ceiling(720 * _uiScale * dpi), monitor.WorkArea.Width), Y = monitor.WorkArea.Height };
            Marshal.StructureToPtr(info, lParam, false);
            handled = true;
            return true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public User32.POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
