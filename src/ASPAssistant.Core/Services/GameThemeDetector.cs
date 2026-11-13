using System.Drawing;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record GameThemeReading(GameShopTheme Theme, double Score);

/// <summary>先查顶栏的主题色，再以回合及阶段文字确认游戏 HUD。</summary>
public sealed class GameThemeDetector(IOcrEngine ocr)
{
    public async Task<GameThemeReading?> ReadAsync(byte[] png, int width, int height)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        // 顶栏以水平中心、顶边为锚；包住 UI90～100 的装饰，不依赖商店是否展开。
        double scale = Math.Min(width / 1920.0, height / 1080.0);
        int left = (int)(width / 2.0 - 330 * scale), topHeight = (int)(85 * scale);
        int blue = 0, green = 0, orange = 0, count = 0;
        int step = Math.Max(1, (int)(3 * scale));
        for (int x = left; x < width - left; x += step)
        for (int y = 0; y < topHeight; y += step)
        {
            var c = bitmap.GetPixel(x, y);
            count++;
            if (c.B > c.R + 35 && c.B > c.G + 20 && c.B > 90) blue++;
            if (c.G > c.R + 25 && c.B > c.R + 15 && c.G > 75) green++;
            if (c.R > c.G * 1.35 && c.G > c.B * 1.3 && c.R > 90) orange++;
        }
        // 可露希尔也有青色文字、乌萨斯也有浅绿数字，先看各自独有的装饰色。
        var reading = blue > count * .015 ? new GameThemeReading(GameShopTheme.Closure, (double)blue / count)
            : orange > count * .015 ? new GameThemeReading(GameShopTheme.Ursus, (double)orange / count)
            : green > count * .025 ? new GameThemeReading(GameShopTheme.Terminal, (double)green / count) : null;
        if (reading is null) return null;
        var texts = await ocr.RecognizeRegionAsync(png, left, 0, width - left * 2, topHeight);
        string heading = string.Concat(texts.Select(t => t.Text.Replace(" ", "")));
        if (heading.Contains("结算") || heading.Contains("加载")) return null;
        return heading.Contains("回合") && (heading.Contains("休息") || heading.Contains("休整")
            || heading.Contains("战斗") || heading.Contains("作战")) ? reading : null;
    }
}

/// <summary>未知帧打断连续计数，但不清除已经确认的主题。</summary>
public sealed class GameThemeTracker
{
    public GameShopTheme? Current { get; private set; }
    private GameShopTheme? _candidate;
    private int _frames;

    public bool Observe(GameThemeReading? reading)
    {
        if (reading is null) { _candidate = null; _frames = 0; return false; }
        if (_candidate != reading.Theme) { _candidate = reading.Theme; _frames = 0; }
        _frames = Math.Min(3, _frames + 1);
        if (_frames < 3 || Current == reading.Theme) return false;
        Current = reading.Theme;
        return true;
    }
}
