using System.Drawing;

namespace ASPAssistant.Core.Services;

internal static class ScanHudPixels
{
    // 模式选择页右侧的青色开始按钮、金色标准模拟和绿色入门协议图标。
    internal static bool ModeMenu(Bitmap image)
    {
        Color At(double x, double y) => image.GetPixel((int)(image.Width * x), (int)(image.Height * y));
        static bool Green(Color c) => c.G > 120 && c.G > c.R + 65 && c.B > c.R + 50;
        var gold = At(.75, .65);
        return Green(At(.85, .90)) && Green(At(.96, .94)) && Green(At(.75, .82))
            && gold.R > 160 && gold.G > 95 && gold.B < gold.G / 2;
    }

    // 顶栏金色敌人图标及左侧青底回合数同时可见时，取数字字形做签名。
    // 图标会浮动，比较颜色面积；回合数用固定在面板上的暗色笔画。
    internal static byte[]? CombatRound(Bitmap image, out bool hasRoundBadge, double zoom = 1)
    {
        double scale = Math.Min(image.Width / 1920d, image.Height / 1080d) * zoom;
        int X(int x) => (int)(image.Width / 2d + x * scale);
        int Y(int y) => (int)(y * scale);
        int gold = 0, cyan = 0;
        for (int y = 10; y < 65; y += 3)
        for (int x = -80; x < -15; x += 3)
        {
            var c = image.GetPixel(X(x), Y(y));
            if (c.R > 160 && c.G > 95 && c.B < c.G * .6 && c.R >= c.G) gold++;
        }

        var glyph = new byte[20 * 12];
        int i = 0, ink = 0;
        for (int y = 31; y < 55; y += 2)
        for (int x = -212; x < -172; x += 2)
        {
            var c = image.GetPixel(X(x), Y(y));
            if (c.G > 130 && c.B > 130 && c.R < c.G * .55 && c.R < c.B * .55) cyan++;
            if (c.G < 75) ink++;
            glyph[i++] = c.G;
        }
        hasRoundBadge = cyan > glyph.Length / 2 && ink >= 8;
        return hasRoundBadge && gold >= 35 ? glyph : null;
    }

    internal static bool SameRound(byte[] current, byte[] previous)
    {
        int changes = 0;
        for (int i = 0; i < current.Length; i++)
            if (Math.Abs(current[i] - previous[i]) > 45 && ++changes > 2) return false;
        return true;
    }
}
