namespace ASPAssistant.Core.Services;

// 使用截图开始时间判定，慢 OCR 在等待期结束后返回也不能重新显示旧标记。
public sealed class ShopRefreshWindow(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ShopStateStabilizer _stabilizer = new();
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    public int Generation { get; private set; }
    public DateTimeOffset CaptureStarted() => _time.GetUtcNow();
    public void Refresh()
    {
        Generation++;
        _stabilizer.Reset();
        _notBefore = _time.GetUtcNow().AddMilliseconds(500);
    }
    internal bool RemoveChanged(Func<ASPAssistant.Core.Models.ShopItem, bool> changed,
        DateTimeOffset capturedAt, out IReadOnlyList<ASPAssistant.Core.Models.ShopItem> items)
    {
        items = [];
        return Accept(capturedAt) && _stabilizer.RemoveChanged(changed, out items);
    }

    public bool Accept(DateTimeOffset captureStarted) => captureStarted >= _notBefore;
    public bool TryUpdate(IReadOnlyList<ASPAssistant.Core.Models.ShopItem> items, DateTimeOffset captureStarted,
        bool shopVisible, out IReadOnlyList<ASPAssistant.Core.Models.ShopItem> stable)
    {
        stable = [];
        return Accept(captureStarted) && _stabilizer.TryUpdate(items, out stable, shopVisible);
    }
}
