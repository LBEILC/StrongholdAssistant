using OpenCvSharp;

namespace ASPAssistant.Core.Services;

internal static class CollectionFrameImage
{
    public static byte[] RestoreAspect(byte[] frame, int rawWidth, int rawHeight, out Size size)
    {
        using var stored = Cv2.ImDecode(frame, ImreadModes.Color);
        size = stored.Size();
        // 采集和截屏的等比缩放会对高度取整，允许不足一个像素的误差。
        if (rawWidth <= 0 || rawHeight <= 0 || Math.Abs(size.Height - size.Width * (double)rawHeight / rawWidth) < 1)
            return frame;

        size = new Size(rawWidth, rawHeight);
        using var restored = new Mat();
        Cv2.Resize(stored, restored, size, 0, 0, InterpolationFlags.Cubic);
        return restored.ImEncode(".png");
    }
}
