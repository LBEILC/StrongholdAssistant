namespace StrongholdAssistant.Host;

internal sealed class RouteSystemHotkey
{
    private (bool Blocked, Hotkey Key)? _state;
    private bool _registered;

    public void Configure(bool blocked, Hotkey key, Func<Hotkey, bool> register, Action unregister, Action<bool> tapOnly)
    {
        if (_state == (blocked, key)) return;
        Dispose(unregister);
        _state = (blocked, key);
        tapOnly(blocked);
        if (blocked && !key.UsesHook) _registered = register(key);
        ASPAssistant.Core.AppLogger.Info("Board", $"快捷键 keysBlocked={blocked} 系统热键={(_registered ? "注册成功" : blocked && !key.UsesHook ? "注册失败" : "未注册")}");
    }

    public void Tap(Action tapHotkey)
    {
        if (_registered) tapHotkey();
    }

    public void Dispose(Action unregister)
    {
        if (_registered) unregister();
        _registered = false;
    }
}
