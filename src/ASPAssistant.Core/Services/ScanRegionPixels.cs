using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Services;

// 只缓存实际 OCR 输入区域；逐行复制时跳过原图的行尾间隔。
internal sealed class ScanRegionPixels
{
    private Rectangle _region;
    private byte[] _previous = [], _current = [];

    public bool Unchanged(Bitmap bitmap, Rectangle region)
    {
        int rowBytes = region.Width * 4;
        bool comparable = region == _region;
        if (!comparable)
        {
            _region = region;
            _current = new byte[rowBytes * region.Height];
            _previous = new byte[_current.Length];
        }
        var bits = bitmap.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < region.Height; row++)
                Marshal.Copy(bits.Scan0 + row * bits.Stride, _current, row * rowBytes, rowBytes);
        }
        finally { bitmap.UnlockBits(bits); }
        bool same = comparable && _current.AsSpan().SequenceEqual(_previous);
        (_previous, _current) = (_current, _previous);
        return same;
    }
}
