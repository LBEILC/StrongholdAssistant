using System.Drawing;
using ASPAssistant.Core.GameModes;

namespace ASPAssistant.Core.Services;

/// <summary>从截图测量游戏 UI 相对于 90 档的连续缩放。</summary>
public static class GameUiScale
{
    public static double Information(byte[] screenshot)
    {
        using var pixels = BanScreenPixels.Load(screenshot);
        return Information(pixels);
    }

    public static double Information(BanScreenPixels? pixels)
    {
        if (pixels is null) return 1;
        double scale = pixels.Height / 1080.0;
        bool Gray(int x, int y)
        {
            var (r, g, b) = pixels.ColorAt(x, y);
            return r is >= 50 and <= 80 && Math.Abs(r - g) < 5 && Math.Abs(r - b) < 5;
        }
        // 标题条中段没有文字。先找其灰底所在行，再取左边缘的中位数，避开纹理和字形。
        // 滚动后的“本局禁用干员”标题条具有相同左边缘。
        var lefts = new List<int>();
        for (int y = (int)(145 * scale); y < (int)(260 * scale); y++)
        {
            int gray = 0;
            for (int x = (int)(pixels.Width * .4); x < (int)(pixels.Width * .5); x += 3)
                if (Gray(x, y)) gray++;
            if (gray <= pixels.Width * .1 / 3 * .85) continue;
            int run = 0;
            for (int x = 0; x < (int)(pixels.Width * .15); x++)
            {
                run = Gray(x, y) ? run + 1 : 0;
                if (run < 20 * scale) continue;
                lefts.Add(x - run + 1);
                break;
            }
        }
        if (lefts.Count == 0) return 1;
        lefts.Sort();
        return Math.Clamp((pixels.Width / 2.0 - lefts[lefts.Count / 2]) / (852 * scale), 1, 100.0 / 90);
    }

    public static double Shop(Bitmap bitmap)
    {
        double scale = bitmap.Height / 1080.0;
        var edges = new double[bitmap.Width];
        int delta = Math.Max(1, (int)(2 * scale));
        // 卡片两侧的竖线在整段高度上重复，按间距做相关性比较，不依赖卡面或出售状态。
        for (int x = delta; x < bitmap.Width - delta; x++)
        for (int y = (int)(800 * scale); y < (int)(950 * scale); y += 3)
        {
            var a = bitmap.GetPixel(x - delta, y);
            var b = bitmap.GetPixel(x + delta, y);
            edges[x] += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }
        int pitch = (int)(220 * scale);
        double best = 0;
        for (int p = (int)(218 * scale); p <= (int)(246 * scale); p++)
        {
            double score = 0;
            for (int x = (int)(bitmap.Width * .35); x < (int)(bitmap.Width * .86) - p; x++)
                score += edges[x] * edges[x + p];
            if (score <= best) continue;
            best = score;
            pitch = p;
        }
        return Math.Clamp(pitch / (220 * scale), 1, 100.0 / 90);
    }

    public static OcrRegionDefinition InformationRegion(OcrRegionDefinition region, double k)
    {
        double left = Math.Max(0, .5 + (region.XPercent - .5) * k);
        double right = Math.Min(1, .5 + (region.XPercent + region.WidthPercent - .5) * k);
        double top = region.YPercent * k;
        double bottom = Math.Min(1, (region.YPercent + region.HeightPercent) * k);
        return region with { XPercent = left, YPercent = top, WidthPercent = right - left, HeightPercent = bottom - top };
    }
}
