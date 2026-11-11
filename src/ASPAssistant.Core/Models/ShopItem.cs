// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
namespace ASPAssistant.Core.Models;

public class ShopItem
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Unique key for this card instance, used to identify it across the
    /// stabilizer and overlay pool. 使用固定卡位序号 + 名字，避免空位导致编号移动。
    /// </summary>
    public string Id { get; set; } = "";

    public int SlotIndex { get; set; }
    /// <summary>卡身矩形，相对于完整截图归一化；与名字文字框分开保存。</summary>
    public (double X, double Y, double Width, double Height) CardRegion { get; set; }

    public int Price { get; set; }
    public bool IsTracked { get; set; }
    public bool IsKey { get; set; }
    // 2026-11-11：目标盟约的可招募干员使用次级标记。
    public bool IsSecondary { get; set; }
    /// <summary>Detected name bounds, normalized to the full captured client image.</summary>
    public (double X, double Y, double Width, double Height) OcrRegion { get; set; }
}
