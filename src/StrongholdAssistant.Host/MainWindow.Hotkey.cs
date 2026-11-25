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
    private readonly RouteHotkeyRouter _fieldHotkeyRouter = new();
    private readonly RouteSystemHotkey _fieldSystemHotkey = new();

    private void RefreshRouteHotkey(bool gameElevated)
    {
        _keysBlocked = ProcessElevation.KeysBlocked(ProcessElevation.Current, gameElevated);
        _fieldHotkeyRouter.Reset();
        if (HotkeyParser.Parse(_api.Settings.FieldHotkey) is { } fieldKey)
            _fieldSystemHotkey.Configure(_keysBlocked, fieldKey, key => RegisterSettingsHotkey(4, key, "识别场上", _api.Settings.FieldHotkey),
                () => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 4), _ => { });
        else _fieldSystemHotkey.Dispose(() => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 4));
        if (HotkeyParser.Parse(_api.Settings.Routes.Hotkey) is { } routeKey)
            _routeSystemHotkey.Configure(_keysBlocked, routeKey,
                key => RegisterSettingsHotkey(3, key, "路线", _api.Settings.Routes.Hotkey),
                () => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 3),
                blocked => { _routeHotkeyRouter.Reset(); _overlay.SetRouteTapOnly(blocked); });
        else _routeSystemHotkey.Dispose(() => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 3));
    }

    private void InitializeHotkey()
    {
        if (!_connectGame) return;
        _refreshKey = HotkeyParser.Parse(_api.Settings.RefreshKey);
        var hwnd = new WindowInteropHelper(this).Handle;
        _keyboardHook = new KeyboardHook((key, down, modifiers, foreground, device) =>
        {
            uint normalized = key switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, _ => key };
            if (device && down && HotkeyParser.Parse(_api.Settings.Control.StopHotkey) is { } stop && stop.Key == normalized
                && (modifiers & stop.Modifiers) == stop.Modifiers) _api.StopControl("hotkey");
            var game = _tracker.CurrentGameWindowHandle;
            // 原始输入线程只投递，不等待 UI、日志文件或识别锁。
            Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                var actions = _keyboardRouter.Observe(key, down, modifiers, foreground,
                    game, hwnd, _hotkey, _refreshKey);
                var routeDown = _keysBlocked || HotkeyParser.Parse(_api.Settings.Routes.Hotkey) is not { } routeKey ? null : _routeHotkeyRouter.Observe(key, down, foreground,
                    game, hwnd, routeKey, modifiers);
                if (routeDown is { } pressed) _api.PublishRouteHotkey(pressed);
                if (!_keysBlocked && HotkeyParser.Parse(_api.Settings.FieldHotkey) is { } fieldKey
                    && _fieldHotkeyRouter.Observe(key, down, foreground, game, hwnd, fieldKey, modifiers) == true)
                    _api.PublishFieldHotkey();
                if (actions.Refresh && !_api.IsRosterScanning)
                {
                    _scanner?.RefreshShop();
                    _shopItems = [];
                    _overlay.UpdateShopItems([]);
                    _overlay.Hide();
                }
                if (actions.Toggle) ToggleFromHotkey();
            });
        }, _api.NotifyRosterInput, _api.ObserveControlInput);
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
        if (_hiddenByMinimize || WindowState == System.Windows.WindowState.Minimized)
        {
            if (_hiddenByMinimize) ShowAfterMinimize();
            else WindowState = _workbench ? _stateBeforeMinimize : System.Windows.WindowState.Normal;
            Activate();
            User32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        }
        else Browser.CoreWebView2?.PostWebMessageAsJson("{\"event\":\"hotkey\",\"data\":null}");
    }

    private bool RegisterSettingsHotkey(int id, Hotkey key, string action, string combo)
    {
        if (RegisterHotkey(id, key)) return true;
        // TODO(design): 设置页接入各快捷键的常驻冲突提示后，替换宿主提示框。
        Dispatcher.BeginInvoke(() => System.Windows.MessageBox.Show(this,
            $"{action}快捷键「{combo}」已被占用，请在设置里换一个。"));
        return false;
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
