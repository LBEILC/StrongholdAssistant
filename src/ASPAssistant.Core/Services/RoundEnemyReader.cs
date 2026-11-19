using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace ASPAssistant.Core.Services;

/// <summary>用面板标题定位名单，并按标题实测大小换算名字区域。</summary>
public sealed class RoundEnemyReader(IOcrEngine ocr)
{
    private static readonly Mat Title = LoadTitle();
    private byte[]? _listPixels;
    public void Reset() => _listPixels = null;

    private static Mat LoadTitle()
    {
        using var stream = typeof(RoundEnemyReader).Assembly.GetManifestResourceStream("EnemyPanelTitle.png")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    }

    // 标题相对屏幕水平居中、顶部锚定。探针和整帧共用同一搜索区。
    private static Rectangle Region(int width, int height, int x, int y, int w, int h)
    {
        double s = Math.Min(width / 1920d, height / 1080d);
        return new((int)(width / 2d + (x - 960) * s), (int)(y * s), (int)(w * s), (int)(h * s));
    }

    public static Rectangle PanelStrip(int width, int height) => Region(width, height, 1090, 87, 220, 105);
    public static Rectangle PanelArea(int width, int height) => Region(width, height, 1090, 80, 700, 890);

    private static (OpenCvSharp.Point Origin, double Scale)? Locate(Mat gray, double screenScale)
    {
        // 90 到 100 档之间连续缩放；在小范围内同时比较图标和「敌人」两字。
        double best = .78;
        (OpenCvSharp.Point, double)? result = null;
        for (int setting = 90; setting <= 100; setting += 2)
        {
            double scale = screenScale * setting / 90;
            int w = (int)Math.Round(Title.Cols * scale), h = (int)Math.Round(Title.Rows * scale);
            if (w < 1 || h < 1 || w > gray.Cols || h > gray.Rows) continue;
            using var resized = new Mat();
            Cv2.Resize(Title, resized, new CvSize(w, h));
            using var scores = new Mat();
            Cv2.MatchTemplate(gray, resized, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out double score, out _, out var location);
            if (score <= best) continue;
            best = score;
            result = (location, scale);
        }
        return result;
    }

    public static bool PanelVisible(Bitmap strip, double s)
    {
        var bits = strip.LockBits(new(0, 0, strip.Width, strip.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = Mat.FromPixelData(bits.Height, bits.Width, MatType.CV_8UC4, bits.Scan0, bits.Stride);
            using var gray = new Mat();
            Cv2.CvtColor(bgra, gray, ColorConversionCodes.BGRA2GRAY);
            return Locate(gray, s) is not null;
        }
        finally { strip.UnlockBits(bits); }
    }

    public static bool PanelVisible(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var image = new Bitmap(stream);
        return PanelVisible(image);
    }

    public static bool PanelVisible(Bitmap image)
    {
        var area = PanelStrip(image.Width, image.Height);
        if (area.Width <= 0 || area.Height <= 0) return false;
        using var strip = image.Clone(area, PixelFormat.Format32bppArgb);
        return PanelVisible(strip, Math.Min(image.Width / 1920d, image.Height / 1080d));
    }

    public async Task<string[]?> ReadAsync(byte[] frame)
    {
        using var image = Cv2.ImDecode(frame, ImreadModes.Color);
        var strip = PanelStrip(image.Cols, image.Rows);
        using var header = new Mat(image, new Rect(strip.X, strip.Y, strip.Width, strip.Height));
        using var gray = new Mat();
        Cv2.CvtColor(header, gray, ColorConversionCodes.BGR2GRAY);
        if (Locate(gray, Math.Min(image.Cols / 1920d, image.Rows / 1080d)) is not { } title)
        {
            Reset();
            return null;
        }
        // 模板左上角在标准画面为 (1132,106)，名单在其右下方，留出字形边缘。
        double s = title.Scale;
        int left = strip.Left + title.Origin.X + (int)(122 * s);
        int top = strip.Top + title.Origin.Y + (int)(50 * s);
        int width = Math.Min((int)(409 * s), image.Cols - left);
        int height = Math.Min((int)(700 * s), image.Rows - top);
        using var area = new Mat(image, new Rect(left, top, width, height));
        using var green = area.ExtractChannel(1);
        // 底框是横贯面板的细亮线。比较行间增量，避免半透明面板内的亮背景提前截断名单。
        int gap = Math.Max(1, (int)Math.Round(2 * s));
        using var upper = green.RowRange(0, height - gap);
        using var lower = green.RowRange(gap, height);
        using var difference = new Mat();
        Cv2.Subtract(lower, upper, difference);
        using var edge = difference.Threshold(28, 255, ThresholdTypes.Binary);
        using var rows = new Mat();
        Cv2.Reduce(edge, rows, ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_32F);
        for (int y = (int)(5 * s); y < rows.Rows; y++)
        {
            if (rows.At<float>(y) > 255 * .83) { height = y + gap; break; }
        }
        using var list = new Mat(area, new Rect(0, 0, width, height));
        using var thumbnail = new Mat();
        using var listGreen = new Mat(green, new Rect(0, 0, width, height));
        Cv2.Resize(listGreen, thumbnail, new CvSize(80, 40), interpolation: InterpolationFlags.Area);
        var pixels = new byte[80 * 40];
        Marshal.Copy(thumbnail.Data, pixels, 0, pixels.Length);
        if (_listPixels is not null && _listPixels.Zip(pixels, (a, b) => Math.Abs(a - b)).Average() < 2)
            return null;
        using var names = new Mat();
        Cv2.InRange(list, Scalar.All(185), Scalar.All(255), names);
        var texts = await ocr.RecognizeRegionAsync(names.ImEncode(".png"), 0, 0, width, height);
        _listPixels = pixels;
        return texts.Where(t => t.Box.X < 25 * s && t.Box.H >= 14 * s && t.Box.H <= 40 * s)
            .OrderBy(t => t.Box.Y).Select(t => t.Text.Trim()).Where(t => t.Length > 0).Distinct().ToArray();
    }
}
