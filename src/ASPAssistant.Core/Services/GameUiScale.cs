using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
        double scale = InformationBaseScale(pixels.Width, pixels.Height);
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
        double scale = Math.Min(bitmap.Width / 1920.0, bitmap.Height / 1080.0);
        var edges = new double[bitmap.Width];
        int delta = Math.Max(1, (int)(2 * scale));
        // 沿卡身高度累加每列的竖边强度。
        int top = (int)(bitmap.Height - 280 * scale), bottom = (int)(bitmap.Height - 130 * scale);
        if (bottom <= top) return 1;
        var bits = bitmap.LockBits(new(0, top, bitmap.Width, bottom - top), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bits.Height; y += 3)
            {
                Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                for (int x = delta; x < bitmap.Width - delta; x++)
                {
                    int a = (x - delta) * 4, b = (x + delta) * 4;
                    edges[x] += Math.Abs(row[a] - row[b]) + Math.Abs(row[a + 1] - row[b + 1])
                        + Math.Abs(row[a + 2] - row[b + 2]);
                }
            }
        }
        finally { bitmap.UnlockBits(bits); }
        int pitch = (int)(220 * scale);
        double best = 0;
        int left = (int)(bitmap.Width * .35), right = (int)(bitmap.Width * .86);
        for (int p = (int)(218 * scale); p <= (int)(246 * scale); p++)
        {
            double score = 0;
            // 六格中剩下的实卡可能并不相邻。把相隔整数个卡距的竖边都计入得分。
            for (int gap = 1; gap < 6; gap++)
                for (int x = left; x < right - gap * p; x++)
                    score += edges[x] * edges[x + gap * p];
            if (score <= best) continue;
            best = score;
            pitch = p;
        }
        return Math.Clamp(pitch / (220 * scale), 1, 100.0 / 90);
    }

    // 高画面的信息页按宽度缩放，顶部锚定；16:9 保持原来的高度换算。
    public static double InformationBaseScale(int width, int height)
        => Math.Min(width / 1920.0, height / 1080.0);

    public static OcrRegionDefinition InformationRegion(OcrRegionDefinition region, double k,
        int width = 1920, int height = 1080)
    {
        double left = Math.Max(0, .5 + (region.XPercent - .5) * k);
        double right = Math.Min(1, .5 + (region.XPercent + region.WidthPercent - .5) * k);
        double vertical = InformationBaseScale(width, height) * 1080 / height;
        double top = region.YPercent * k * vertical;
        double bottom = Math.Min(1, (region.YPercent + region.HeightPercent) * k * vertical);
        return region with { XPercent = left, YPercent = top, WidthPercent = right - left, HeightPercent = bottom - top };
    }
}
