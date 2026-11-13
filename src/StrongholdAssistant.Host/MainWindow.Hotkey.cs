using System.Windows.Interop;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

public partial class MainWindow
{
    private int _hotkeyId;
    private Hotkey? _hotkey;
    private KeyboardHook? _keyboardHook;
    private readonly KeyboardRouter _keyboardRouter = new();
    private readonly RouteHotkeyRouter _routeHotkeyRouter = new();
    private Hotkey? _refreshKey;
    private readonly RouteSystemHotkey _routeSystemHotkey = new();
    private bool _keysBlocked;

    private void RefreshRouteHotkey(bool gameElevated)
    {
        _keysBlocked = ProcessElevation.KeysBlocked(ProcessElevation.Current, gameElevated);
        _routeSystemHotkey.Configure(_keysBlocked, HotkeyParser.Parse(_api.Settings.Routes.Hotkey)!.Value,
            key => RegisterHotkey(3, key),
            () => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 3),
            blocked => { _routeHotkeyRouter.Reset(); _overlay.SetRouteTapOnly(blocked); });
    }

    private void InitializeHotkey()
    {
        if (!_connectGame) return;
        _refreshKey = HotkeyParser.Parse(_api.Settings.RefreshKey);
        var hwnd = new WindowInteropHelper(this).Handle;
        _keyboardHook = new KeyboardHook((key, down, modifiers, foreground) =>
        {
            var game = _tracker.CurrentGameWindowHandle;
            // 原始输入线程只投递，不等待 UI、日志文件或识别锁。
            Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                var actions = _keyboardRouter.Observe(key, down, modifiers, foreground,
                    game, hwnd, _hotkey, _refreshKey);
                var routeDown = _keysBlocked ? null : _routeHotkeyRouter.Observe(key, down, foreground,
                    game, hwnd, HotkeyParser.Parse(_api.Settings.Routes.Hotkey)!.Value, modifiers);
                if (routeDown is { } pressed) _api.PublishRouteHotkey(pressed);
                if (actions.Refresh)
                {
                    _scanner?.RefreshShop();
                    _shopItems = [];
                    _overlay.UpdateShopItems([]);
                    _overlay.Hide();
                }
                if (actions.Toggle) ToggleFromHotkey();
            });
        });
        var combo = _api.Settings.ToggleHotkey;
        if (combo.Length == 0) return;
        var hotkey = HotkeyParser.Parse(combo);
        if (hotkey is not { } key || !key.UsesHook && !RegisterHotkey(1, key))
        {
            _api.HotkeyFailed = true;
            return;
        }
        _hotkeyId = key.UsesHook ? 0 : 1;
        _hotkey = key;
    }

    private void ToggleFromHotkey()
    {
        if (WindowState == System.Windows.WindowState.Minimized)
        {
            WindowState = System.Windows.WindowState.Normal;
            Activate();
            User32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        }
        else Browser.CoreWebView2?.PostWebMessageAsJson("{\"event\":\"hotkey\",\"data\":null}");
    }

    private bool RegisterHotkey(int id, Hotkey key) => User32.RegisterHotKey(
        new WindowInteropHelper(this).Handle, id, key.Modifiers | 0x4000, key.Key);

    private void UnregisterHotkey()
    {
        if (_hotkeyId != 0) User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, _hotkeyId);
        _hotkeyId = 0;
        _hotkey = null;
    }

    private void SetHotkey(string combo)
    {
        if (combo == "")
        {
            UnregisterHotkey();
            _api.SaveHotkey(combo);
            return;
        }
        var key = HotkeyParser.Parse(combo)
            ?? throw new ArgumentException("这个按键用不了，请换一个试试。");
        if (_hotkey != key)
        {
            var nextId = _hotkeyId == 1 ? 2 : 1;
            if (!key.UsesHook && !RegisterHotkey(nextId, key))
                throw new ArgumentException("这个组合被别的软件占用了，换一个试试。");
            UnregisterHotkey();
            _hotkeyId = key.UsesHook ? 0 : nextId;
            _hotkey = key;
        }
        _api.SaveHotkey(combo);
    }
}
