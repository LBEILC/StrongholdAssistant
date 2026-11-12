using System.Drawing;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>卡位从左到右编号 0～5；空位不会改变右侧卡片的编号。</summary>
public static class ShopLayout
{
    public static RectangleF Card(int slot, int width, int height, double k, GameShopTheme theme)
    {
        // 三套主题样图的 1080p、UI90 卡身边界，不包含顶部凸出的费用和阶级标签。
        var (top, bottom) = theme switch
        {
            GameShopTheme.Terminal => (815, 1065),
            GameShopTheme.Ursus => (815, 1068),
            _ => (808, 1070)
        };
        double baseScale = Math.Min(width / 1920.0, height / 1080.0);
        double scale = baseScale * k;
        // UI90 与 UI100 样图测得的锚点差：间距测量 k 略小于设置比例，
        // 卡身原点按两端样图校准，名称裁剪和 GameUiScale 的换算保持不变。
        double anchor = (k - 1) * baseScale;
        return new RectangleF((float)(width - width / 2.0 * k + (-599 + slot * 220) * scale - 72 * anchor),
            (float)(height - (1080 - top) * scale - 36 * anchor), (float)(210 * scale),
            (float)((bottom - top) * scale + 36 * anchor));
    }

    public static IReadOnlyList<ShopItem> Snap(IEnumerable<ShopItem> items,
        int width, int height, double k, GameShopTheme theme)
    {
        var result = new List<ShopItem>();
        foreach (var item in items)
        {
            var region = item.OcrRegion;
            var center = new PointF((float)((region.X + region.Width / 2) * width),
                (float)((region.Y + region.Height / 2) * height));
            for (int slot = 0; slot < 6; slot++)
            {
                var card = Card(slot, width, height, k, theme);
                if (!card.Contains(center)) continue;
                item.SlotIndex = slot;
                item.Id = $"{slot}:{item.Name}";
                item.CardRegion = (card.X / width, card.Y / height, card.Width / width, card.Height / height);
                if (!result.Any(i => i.Id == item.Id)) result.Add(item);
                break;
            }
        }
        return result.OrderBy(i => i.SlotIndex).ToArray();
    }

    /// <summary>两侧长直边的明暗差确认卡身仍在，不用 OCR 空结果判断收店。</summary>
    public static bool HasCards(Bitmap image, double k, GameShopTheme theme)
    {
        double scale = Math.Min(image.Width / 1920.0, image.Height / 1080.0) * k;
        int delta = Math.Max(2, (int)Math.Round(3 * scale));
        for (int slot = 0; slot < 6; slot++)
        {
            var card = Card(slot, image.Width, image.Height, k, theme);
            bool Edge(double x)
            {
                int cx = (int)Math.Round(x);
                if (cx - delta < 0 || cx + delta >= image.Width) return false;
                int hits = 0;
                for (int n = 0; n < 20; n++)
                {
                    int y = (int)(card.Top + card.Height * (.2 + n * .025));
                    var a = image.GetPixel(cx - delta, y);
                    var b = image.GetPixel(cx + delta, y);
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 24) hits++;
                }
                return hits >= 14;
            }
            if (Edge(card.Left) && Edge(card.Right)) return true;
        }
        return false;
    }
}
