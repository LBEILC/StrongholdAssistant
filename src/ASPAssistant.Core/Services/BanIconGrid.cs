// 2026-11-15：按当前帧标题条的像素边界限制盟约图标搜索范围。
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Services;

/// <summary>One icon slot found on the ban screen, with its availability state.</summary>
public sealed record BanIconSlot(
    int Row,
    double CenterX,
    double CenterY,
    int Top,
    int Bottom,
    double GreenFraction)
{
    internal bool StateKnown { get; init; } = true;
}

/// <summary>
/// Finds the covenant icon grid on the "确认本局信息" screen from pixels alone — no OCR.
///
/// This is the second direction of the recognition: OCR names are bound to the icons found
/// here, and icons that receive no name are reported instead of silently disappearing.
///
/// Measured on 1920×1080 captures: icon rows are ~90 px tall bands of light pixels
/// (bright green discs and grey discs whose glyph/ring is still much lighter than the
/// background), columns sit on a 116 px pitch (0.0604 × width). Rows of plain text
/// (section headers, the ban list title) are rejected because their column gaps are not
/// consistent with a single pitch.
/// </summary>
public static class BanIconGrid
{
    private const int LumaThreshold = 60;

    /// <summary>Row band height limits, as fractions of the screenshot height.</summary>
    private const double BandMinHeightPercent = 0.05;
    private const double BandMaxHeightPercent = 0.13;

    /// <summary>A row counts as an icon row when at least this share of its scanning width is light.</summary>
    private const double BandMinLightShare = 0.05;

    /// <summary>Column gap consistency and size limits.</summary>
    private const double GapTolerance = 0.25;
    private const double MinPitchPercent = 0.04;

    private const int MinColumnsPerRow = 4;

    /// <summary>Half width of the green sample, as a fraction of the column pitch.</summary>
    private const double SampleHalfPitch = 0.30;

    /// <summary>
    /// Detects every icon slot whose vertical centre falls inside [minYPercent, maxYPercent].
    /// Returns an empty list when the image cannot be decoded or no grid is found.
    /// </summary>
    public static IReadOnlyList<BanIconSlot> Detect(
        byte[] png, int imgWidth, int imgHeight, double minYPercent = 0, double maxYPercent = 1)
    {
        using var pixels = BanScreenPixels.Load(png);
        if (pixels == null)
        {
            AppLogger.Warn("BanIconGrid", "could not decode screenshot");
            return [];
        }

        return Detect(pixels, minYPercent, maxYPercent);
    }

    /// <summary>
    /// 同一个分析里有核心/附加两块区域，复用一份已解码的像素可以省掉一次 PNG 解码
    /// （1920×1080 约 100 ms）。<see cref="BanScreenPixels"/> 读操作是只读的，可并行共享。
    /// </summary>
    public static IReadOnlyList<BanIconSlot> Detect(
        BanScreenPixels pixels, double minYPercent = 0, double maxYPercent = 1)
    {
        int width = pixels.Width;
        int height = pixels.Height;

        int bandTop = Math.Max(0, (int)(minYPercent * height));
        int bandBottom = Math.Min(height, (int)(maxYPercent * height));
        // 双行核心区有明亮的绿色选中背景和装饰字，必须只取图标的亮部。
        bool groupedCore = (double)width / height < 1.7 && minYPercent < .2
            && maxYPercent > .5 && maxYPercent < .65;
        int lumaThreshold = groupedCore ? 110 : LumaThreshold;
        // 开局页的角色光标会遮住核心标题条；双行核心区用紧随其后的附加标题测量同一面板宽度。
        var (scanLeft, scanRight) = groupedCore
            ? FindPanelBounds(pixels, bandBottom, Math.Min(height,
                bandBottom + (int)(80 * GameUiScale.InformationBaseScale(width, height))))
            : FindPanelBounds(pixels, bandTop, bandBottom);
        if (scanRight - scanLeft < 100)
            return [];

        var rows = new List<(int Top, int Bottom)>();
        // 保留原有亮点数量门槛，避免收窄搜索区域时改变图标行的上下边缘。
        int minLight = (int)Math.Max(20, width * 0.66 * BandMinLightShare);
        int minHeight = (int)(BandMinHeightPercent * height);
        int maxHeight = (int)(BandMaxHeightPercent * height);

        int rowStart = -1;
        for (int y = bandTop; y <= bandBottom; y++)
        {
            bool light = false;
            if (y < bandBottom)
            {
                int count = 0;
                for (int x = scanLeft; x < scanRight; x++)
                    if (pixels.Luma(x, y) > lumaThreshold) count++;
                light = count > minLight;
            }

            if (light && rowStart < 0)
            {
                rowStart = y;
            }
            else if (!light && rowStart >= 0)
            {
                int bandHeight = y - rowStart;
                if (bandHeight >= minHeight && bandHeight <= maxHeight)
                    rows.Add((rowStart, y));
                rowStart = -1;
            }
        }

        var slots = new List<BanIconSlot>();
        int rowIndex = 0;
        foreach (var (top, bottom) in rows)
        {
            var centers = DetectColumns(pixels, scanLeft, scanRight, top, bottom, width, lumaThreshold, groupedCore);
            if (centers.Count < MinColumnsPerRow)
                continue;

            double pitch = MedianPitch(centers);
            if (pitch <= 0)
                continue;

            int half = Math.Max(12, (int)(pitch * SampleHalfPitch));
            foreach (var centerX in centers)
            {
                double green = pixels.GreenFraction(
                    (int)centerX - half, top, (int)centerX + half, bottom);
                slots.Add(new BanIconSlot(
                    rowIndex, centerX, (top + bottom) / 2.0, top, bottom, green)
                {
                    // 灰白光标不是激活色；缺少足够绿色或灰色图标证据时留作未读出。
                    StateKnown = green >= BanIconBrightness.BannedThreshold
                        || (green < .05 && pixels.GrayIconFraction(
                            (int)centerX - half, top, (int)centerX + half, bottom) >= .04)
                });
            }
            rowIndex++;
        }

        return slots;
    }

    // 标题条灰底横贯面板；取区域内最长的灰底行，容许纹理造成的短缺口。
    // 核心和附加区域分别测量，不让右侧地图或首领面板参与图标检测。
    private static (int Left, int Right) FindPanelBounds(BanScreenPixels pixels, int top, int bottom)
    {
        int left = 0, right = 0;
        int maxGap = Math.Max(1, (int)(12 * GameUiScale.InformationBaseScale(pixels.Width, pixels.Height)));
        for (int y = top; y < bottom; y++)
        {
            int start = -1, lastGray = -1;
            for (int x = 0; x <= pixels.Width; x++)
            {
                bool gray = false;
                if (x < pixels.Width)
                {
                    var (r, g, b) = pixels.ColorAt(x, y);
                    gray = r is >= 50 and <= 80 && Math.Abs(r - g) < 5 && Math.Abs(r - b) < 5;
                }
                if (gray)
                {
                    if (start < 0) start = x;
                    lastGray = x;
                }
                if (start < 0 || (x < pixels.Width && x - lastGray <= maxGap)) continue;
                if (lastGray + 1 - start > right - left)
                    (left, right) = (start, lastGray + 1);
                start = -1;
            }
        }
        // 短灰线不是面板标题；缺少标题时由调用方沿用已有的不完整识别处理。
        return right - left >= pixels.Width * 0.3 ? (left, right) : (0, 0);
    }

    private static List<double> DetectColumns(
        BanScreenPixels pixels, int scanLeft, int scanRight, int top, int bottom, int width,
        int lumaThreshold, bool grouped)
    {
        int minSpan = Math.Max(10, (int)(0.012 * width));
        var spans = new List<(int Left, int Right)>();

        int runStart = -1;
        for (int x = scanLeft; x <= scanRight; x++)
        {
            bool light = false;
            if (x < scanRight)
            {
                int count = 0;
                for (int y = top; y < bottom; y++)
                    if (pixels.Luma(x, y) > lumaThreshold) count++;
                light = count > 3;
            }

            if (light && runStart < 0)
            {
                runStart = x;
            }
            else if (!light && runStart >= 0)
            {
                if (x - runStart > minSpan)
                    spans.Add((runStart, x));
                runStart = -1;
            }
        }

        var centers = spans.Select(s => (s.Left + s.Right) / 2.0).ToList();
        if (grouped) return IsRegular(centers, width, true) ? centers : [];
        if (spans.Count < MinColumnsPerRow) return [];

        // 光标可能把两枚图标连成一个亮区。只用正常宽度的亮区估计列距，
        // 再用多数中心确定网格相位；不让粘连亮区的中心带偏整排。
        double iconWidth = Median(spans.Select(s => (double)(s.Right - s.Left)).ToList());
        var clean = spans.Where(s => s.Right - s.Left >= iconWidth * .75
                && s.Right - s.Left <= iconWidth * 1.25)
            .Select(s => (s.Left + s.Right) / 2.0).ToList();
        if (clean.Count < MinColumnsPerRow) return [];
        // 剩下四列时，相邻距离的中位数也可能被缺列改变；比较一至三列的
        // 距离假设，以同一网格上的支持数优先、位置残差次之。
        var fits = new List<(double Pitch, double Anchor, int Count, double Error)>();
        for (int i = 0; i < clean.Count; i++)
        for (int j = i + 1; j < clean.Count; j++)
        for (int step = 1; step <= 3; step++)
        {
            double p = (clean[j] - clean[i]) / step;
            if (p < Math.Max(MinPitchPercent * width, iconWidth * 1.03) || p > width * .09) continue;
            var errors = clean.Select(c => Math.Abs(c - clean[i] - Math.Round((c - clean[i]) / p) * p)).ToList();
            fits.Add((p, clean[i], errors.Count(e => e <= p * .12), errors.Sum(e => Math.Min(e, p * .12))));
        }
        if (fits.Count == 0) return [];
        var fit = fits.OrderByDescending(f => f.Count).ThenBy(f => f.Error).First();
        double pitch = fit.Pitch;
        double tolerance = pitch * .12;
        double anchor = fit.Anchor;
        var aligned = clean.Where(c =>
            Math.Abs(c - anchor - Math.Round((c - anchor) / pitch) * pitch) <= tolerance).ToList();
        if (aligned.Count < MinColumnsPerRow || aligned.Count < centers.Count - 2) return [];
        double origin = Median(aligned.Select(c => c - Math.Round((c - anchor) / pitch) * pitch).ToList());
        // 只补已有亮区覆盖范围内的列；不把面板空白或另一组的间隔补成图标。
        int first = (int)Math.Ceiling((spans[0].Left + iconWidth * .25 - origin) / pitch);
        int last = (int)Math.Floor((spans[^1].Right - iconWidth * .25 - origin) / pitch);
        var grid = Enumerable.Range(first, Math.Max(0, last - first + 1))
            .Select(i => origin + i * pitch).ToList();
        return grid.Count <= centers.Count + 2 ? grid : [];
    }

    /// <summary>
    /// A real icon row has evenly spaced columns; text lines and HUD elements produce
    /// irregular gaps and are rejected here.
    /// </summary>
    private static bool IsRegular(List<double> centers, int width, bool grouped)
    {
        if (centers.Count < MinColumnsPerRow)
            return false;

        var gaps = new List<double>();
        for (int i = 1; i < centers.Count; i++)
            gaps.Add(centers[i] - centers[i - 1]);

        double median = Median(gaps);
        if (median < MinPitchPercent * width)
            return false;

        // 高画面开局页的已激活、未激活两组之间留有一段空白。
        var irregular = gaps.Where(g => Math.Abs(g - median) > median * GapTolerance).ToList();
        return irregular.Count == 0 || (grouped && irregular.Count == 1 && irregular[0] > median);
    }

    private static double MedianPitch(List<double> centers)
    {
        if (centers.Count < 2)
            return 0;

        var gaps = new List<double>();
        for (int i = 1; i < centers.Count; i++)
            gaps.Add(centers[i] - centers[i - 1]);
        return Median(gaps);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}

/// <summary>
/// Decoded screenshot pixels shared by the icon grid and the brightness sampling.
/// </summary>
public sealed class BanScreenPixels : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly BitmapData _data;
    private readonly byte[] _buffer;

    public int Width { get; }
    public int Height { get; }

    private BanScreenPixels(Bitmap bitmap, BitmapData data, byte[] buffer)
    {
        _bitmap = bitmap;
        _data = data;
        _buffer = buffer;
        Width = data.Width;
        Height = data.Height;
    }

    /// <summary>Decodes the PNG; returns <c>null</c> when the bytes are not a readable image.</summary>
    public static BanScreenPixels? Load(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            var bitmap = new Bitmap(stream);
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buffer = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            return new BanScreenPixels(bitmap, data, buffer);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BanScreenPixels", $"decode failed: {ex.Message}");
            return null;
        }
    }

    public int Luma(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return 0;

        int offset = y * _data.Stride + x * 4;
        int b = _buffer[offset];
        int g = _buffer[offset + 1];
        int r = _buffer[offset + 2];
        return (r * 299 + g * 587 + b * 114) / 1000;
    }

    internal (int R, int G, int B) ColorAt(int x, int y)
    {
        int offset = y * _data.Stride + x * 4;
        return (_buffer[offset + 2], _buffer[offset + 1], _buffer[offset]);
    }

    /// <summary>Fraction of green-dominant pixels in the given rectangle.</summary>
    public double GreenFraction(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(Width, x1);
        y1 = Math.Min(Height, y1);
        if (x1 <= x0 || y1 <= y0)
            return 0;

        int total = 0;
        int green = 0;
        // 16:10 诊断图的激活色偏青绿（例如 1,235,187），灰色禁用图标仍无绿色优势。
        int blueRatio = (double)Width / Height < 1.7 ? 11 : 13;
        for (int y = y0; y < y1; y++)
        {
            int row = y * _data.Stride;
            for (int x = x0; x < x1; x++)
            {
                int offset = row + x * 4;
                int b = _buffer[offset];
                int g = _buffer[offset + 1];
                int r = _buffer[offset + 2];
                total++;
                if (g > 90 && g * 10 > r * 13 && g * 10 > b * blueRatio)
                    green++;
            }
        }

        return total == 0 ? 0 : (double)green / total;
    }

    // 灰色图标的中亮度轮廓，排除背景暗部和光标的纯白亮部。
    internal double GrayIconFraction(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(Width, x1);
        y1 = Math.Min(Height, y1);
        int gray = 0, total = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            var (r, g, b) = ColorAt(x, y);
            total++;
            if (r is >= 80 and <= 200 && Math.Abs(r - g) < 20 && Math.Abs(r - b) < 20) gray++;
        }
        return total == 0 ? 0 : (double)gray / total;
    }

    public void Dispose()
    {
        try
        {
            _bitmap.UnlockBits(_data);
        }
        finally
        {
            _bitmap.Dispose();
        }
    }
}
