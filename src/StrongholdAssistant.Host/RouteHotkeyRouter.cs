namespace StrongholdAssistant.Host;

// 只观察键状态，不安装钩子、不拦截输入；左右修饰键视作同一个设置键。
public sealed class RouteHotkeyRouter
{
    private readonly HashSet<uint> _pressed = [];
    private uint? _active;
    internal Action<string> Log { get; set; } = message => ASPAssistant.Core.AppLogger.Info("Board", message);

    public void Reset() { _pressed.Clear(); _active = null; }

    public bool? Observe(uint key, bool down, nint foreground, nint game, nint assistant, Hotkey hotkey, uint modifiers = 0)
    {
        uint normalized = key switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, _ => key };
        uint modifier = normalized switch { 0x12 => 1u, 0x11 => 2u, 0x10 => 4u, 0x5B or 0x5C => 8u, _ => 0u };
        if (normalized != hotkey.Key && _active != key && (hotkey.Modifiers & modifier) == 0) return null;
        bool changed = down ? _pressed.Add(key) : _pressed.Remove(key);
        if (!changed) return null;
        Log($"快捷键 原始输入收到 key=0x{key:X} down={down}");
        bool? Result(bool? result, string reason)
        {
            Log($"快捷键 路由 down={down} result={result?.ToString() ?? "忽略"} reason={reason} foreground=0x{foreground:X} game=0x{game:X} assistant=0x{assistant:X}");
            return result;
        }
        if (!down)
        {
            if (_active is null || (_active != key && (hotkey.Modifiers & modifier) == 0)) return Result(null, "未激活");
            _active = null;
            return Result(false, "释放已激活键");
        }
        if (normalized != hotkey.Key || (modifiers & hotkey.Modifiers) != hotkey.Modifiers) return Result(null, "组合键未按齐");
        if (_active is not null) return Result(null, "已有按下键");
        if (!((game != 0 && foreground == game) || (assistant != 0 && foreground == assistant)))
            return Result(null, game == 0 ? "未连接游戏且前台非助手" : "前台非游戏或助手");
        _active = key;
        return Result(true, foreground == game ? "游戏前台" : "助手前台");
    }
}
