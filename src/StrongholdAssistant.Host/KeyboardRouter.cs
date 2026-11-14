namespace StrongholdAssistant.Host;

public readonly record struct KeyboardActions(bool Toggle, bool Refresh);

// 只计算动作；原生钩子始终放行，测试不需要安装钩子。
public sealed class KeyboardRouter
{
    private readonly HashSet<uint> _pressed = [];
    public KeyboardActions Observe(uint key, bool down, uint modifiers, nint foreground,
        nint game, nint assistant, Hotkey? toggle, Hotkey? refresh)
    {
        if (!down) { _pressed.Remove(key); return default; }
        if (!_pressed.Add(key)) return default;
        bool inGame = game != 0 && foreground == game;
        bool inAssistant = assistant != 0 && foreground == assistant;
        return new((inGame || inAssistant) && toggle is { UsesHook: true } t && t.Key == key && t.Modifiers == modifiers,
            inGame && refresh is { } r && r.Key == key && r.Modifiers == modifiers);
    }
}
