using System.Windows;
using System.Windows.Interop;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

public partial class MainWindow
{
    private bool _workbench;
    private bool _switchingLayout;
    private FreeWindowBounds? _sidebarBounds;

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
                if (_dock.WorkbenchBounds is null && _lastGameRect is { } game)
                {
                    var work = User32.GetMonitorGeometry(game.Left + game.Width / 2, game.Top + game.Height / 2).WorkArea;
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
                _api.UpdateConnection(_api.Connection with { Overlay = false });
            }
            else
            {
                _workbench = false;
                ApplyScale(_lastClientRect?.Height);
                ApplyBounds(_api.Settings.FollowGame ? _sidebarBounds! : _dock.FreeBounds ?? _sidebarBounds!);
                if (_api.Settings.FollowGame && _lastGameRect is { } game) UpdateGame(game);
                if (_api.Settings.FollowGame && _tracker.IsGameMinimized) WindowState = WindowState.Minimized;
            }
        }
        finally { _switchingLayout = false; }
        if (_workbench) RememberWorkbenchBounds();
        else _api.SettingsStore.SaveDock(_dock);
    }

    private void ApplyBounds(FreeWindowBounds bounds)
    {
        Left = bounds.Left; Top = bounds.Top; Width = bounds.Width; Height = bounds.Height;
    }

    private void RememberWorkbenchBounds()
    {
        if (!_workbench || _switchingLayout || WindowState != WindowState.Normal) return;
        _dock = _dock with { WorkbenchBounds = new(Left, Top, Width, Height) };
        _api.SettingsStore.SaveDock(_dock);
    }
}
