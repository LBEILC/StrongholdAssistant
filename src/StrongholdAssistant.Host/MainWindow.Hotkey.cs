using System.Windows.Interop;
using ASPAssistant.Core.Interop;

namespace StrongholdAssistant.Host;

public partial class MainWindow
{
    private int _hotkeyId;
    private Hotkey? _hotkey;

    private void InitializeHotkey()
    {
        var combo = _api.Settings.ToggleHotkey;
        if (combo.Length == 0) return;
        var hotkey = HotkeyParser.Parse(combo);
        if (hotkey is not { } key || !RegisterHotkey(1, key))
        {
            _api.HotkeyFailed = true;
            return;
        }
        _hotkeyId = 1;
        _hotkey = key;
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
            ?? throw new ArgumentException("这个组合用不了，请带上 Ctrl、Alt、Shift 或 Win，或者用 F1～F12。");
        if (_hotkey != key)
        {
            var nextId = _hotkeyId == 1 ? 2 : 1;
            if (!RegisterHotkey(nextId, key))
                throw new ArgumentException("这个组合被别的软件占用了，换一个试试。");
            UnregisterHotkey();
            _hotkeyId = nextId;
            _hotkey = key;
        }
        _api.SaveHotkey(combo);
    }
}
