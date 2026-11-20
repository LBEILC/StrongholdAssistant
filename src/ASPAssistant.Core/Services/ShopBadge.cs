using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

internal static class ShopBadge
{
    // 从资金或价格牌的金色边框定位，在二值图上识别数字，避免读入半透明背景。
    public static async Task<int?> ReadAsync(IOcrEngine ocr, Bitmap bitmap, Rectangle area, bool money)
    {
        double s = Math.Min(bitmap.Width / 1920d, bitmap.Height / 1080d);
        using var crop = bitmap.Clone(area, PixelFormat.Format24bppRgb);
        using var encoded = new MemoryStream();
        crop.Save(encoded, ImageFormat.Png);
        using var image = Cv2.ImDecode(encoded.ToArray(), ImreadModes.Color);
        using var hsv = new Mat();
        using var ink = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(12, 105, 85), new Scalar(38, 255, 255), ink);
        Rect Border()
        {
            Cv2.FindContours(ink, out OpenCvSharp.Point[][] contours, out _,
                RetrievalModes.List, ContourApproximationModes.ApproxSimple);
            return contours.Select(Cv2.BoundingRect).Where(b => money
                ? b.Width > 96 * s && b.Width < 158 * s && b.Width > 2.5 * b.Height && b.Width < 3.7 * b.Height
                : b.Left > 0 && b.Top > 0 && b.Right < ink.Width && b.Bottom < ink.Height &&
                  b.Width > 34 * s && b.Width < 68 * s && b.Height > 23 * s && b.Height < 48 * s &&
                  b.Width > 1.1 * b.Height && b.Width < 2 * b.Height).OrderByDescending(b => b.Width).FirstOrDefault();
        }
        var box = Border();
        // 资金不足时商品和价签一起变灰，白字与灰边仍在原位。
        if (!money && box.Width == 0)
        {
            Cv2.InRange(hsv, new Scalar(0, 0, 145), new Scalar(180, 42, 255), ink);
            box = Border();
        }
        if (box.Width == 0) return null;
        var digits = new Rect(box.X + (int)(box.Width * (money ? .44 : .24)), box.Y + (int)(box.Height * .16),
            (int)(box.Width * (money ? .39 : .52)), (int)(box.Height * .69));
        // 折扣商品的数字是青色，边框仍是金色；牌内按亮度取字。
        if (!money)
        {
            Cv2.CvtColor(image, ink, ColorConversionCodes.BGR2GRAY);
            using var number = new Mat(ink, digits);
            Cv2.Threshold(number, number, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        }
        using var mask = new Mat(ink, digits);
        var text = await ocr.RecognizeLineAsync(mask.ImEncode(".png"), 0, 0, mask.Width, mask.Height);
        return int.TryParse(string.Concat(text.Select(t => t.Text)), out int value) && value is >= 0 and <= 999 ? value : null;
    }
}
