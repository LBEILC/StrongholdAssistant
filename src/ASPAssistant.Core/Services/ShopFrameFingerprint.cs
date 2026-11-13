using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

// 只比较名称条，避开卡面入场和悬停光效；辅助区域另行判断资金和信息页变化。
internal sealed record ShopFrameFingerprint(byte[] Names, byte[] Context, double Left, double Right)
{
    public static ShopFrameFingerprint Read(byte[] png, GameShopTheme theme)
    {
        using var stream = new MemoryStream(png);
        using var source = new Bitmap(stream);
        return Read(source, theme);
    }

    public static ShopFrameFingerprint Read(Bitmap source, GameShopTheme theme)
    {
        double scale = Math.Min(source.Width / 1920.0, source.Height / 1080.0);
        // 覆盖 90～100 档缩放下的名称，不包含卡面、边框和动态店主。
        int bottom = theme == GameShopTheme.Terminal ? 36 : 44;
        int top = theme == GameShopTheme.Terminal ? 70 : 84;
        int left = Math.Max(0, (int)(source.Width - (source.Width / 2.0 + 600 * scale) * (100.0 / 90)));
        int right = (int)(source.Width / 2.0 + 720 * scale);
        var names = Sample(source, new Rectangle(left, source.Height - (int)(top * scale),
            right - left, Math.Max(1, (int)((top - bottom) * scale))), 768, 24);
        // 局部横向梯度保留字形，消除扫光对底色亮度的缓慢变化。
        for (int y = 0; y < 24; y++)
        {
            int previous = names[y * 768];
            for (int x = 0; x < 768; x++)
            {
                int index = y * 768 + x;
                int current = names[index];
                names[index] = (byte)Math.Abs(current - previous);
                previous = current;
            }
        }
        // 辅助区域发生变化时才重新核实资金和信息页；不以名称缓存替代状态机。
        var heading = Sample(source, new Rectangle(0, 0, source.Width, source.Height / 2), 384, 96);
        var economy = Sample(source, new Rectangle(0, source.Height - (int)(310 * scale),
            source.Width, Math.Max(1, (int)(310 * scale))), 384, 96);
        return new(names, [.. heading, .. economy], left / (double)source.Width, right / (double)source.Width);
    }

    public bool ChangedSlot(ShopFrameFingerprint current, ShopItem item)
    {
        var region = item.CardRegion.Width > 0 ? item.CardRegion : item.OcrRegion;
        int left = Math.Clamp((int)((region.X - Left) / (Right - Left) * 768), 0, 767);
        int right = Math.Clamp((int)((region.X + region.Width - Left) / (Right - Left) * 768), left + 1, 768);
        for (int y = 0; y < 24; y++)
            for (int x = left; x < right; x += 16)
            {
                int end = Math.Min(right, x + 16), difference = 0;
                for (int i = x; i < end; i++) difference += Math.Abs(Names[y * 768 + i] - current.Names[y * 768 + i]);
                if (difference > (end - x) * 10) return true;
            }
        return false;
    }

    private static byte[] Sample(Bitmap source, Rectangle region, int width, int height)
    {
        using var small = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(small))
            graphics.DrawImage(source, new Rectangle(0, 0, width, height), region, GraphicsUnit.Pixel);
        var data = small.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        small.UnlockBits(data);
        var gray = new byte[width * height];
        for (int i = 0; i < gray.Length; i++)
            gray[i] = (byte)((pixels[i * 4] * 114 + pixels[i * 4 + 1] * 587 + pixels[i * 4 + 2] * 299) / 1000);
        return gray;
    }

    public static bool SameNames(byte[] previous, byte[] current)
    {
        // 按局部块判断，避免单张卡换字被整条区域的平均值稀释。
        for (int start = 0; start < current.Length; start += 16)
        {
            int difference = 0;
            for (int i = start; i < start + 16; i++) difference += Math.Abs(previous[i] - current[i]);
            if (difference > 16 * 10) return false;
        }
        return true;
    }
}
