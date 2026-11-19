using System.Text;
using System.Text.Json;

namespace ASPAssistant.Core.Services;

public sealed record AdaptChoice(string Id, string Name, string Desc, int? Tier, string? Enemy = null);
public sealed record AdaptReading(string Family, AdaptChoice?[] Cards);

public sealed class AdaptReader(IOcrEngine ocr, JsonElement catalog, AdaptChoice[] tactics)
{
    private readonly AdaptChoice[] _items = catalog.GetProperty("equipment").EnumerateArray()
        .Where(e => e.TryGetProperty("itemId", out _))
        .Select(e => new AdaptChoice(e.GetProperty("itemId").GetString()!, e.GetProperty("name").GetString()!,
            e.GetProperty("normal").GetString()!, e.GetProperty("tier").GetInt32())).ToArray();
    private readonly AdaptChoice[] _bounties = catalog.GetProperty("bounties").EnumerateArray()
        .Select(e => new AdaptChoice(e.GetProperty("id").GetString()!, e.GetProperty("name").GetString()!,
            e.GetProperty("text").GetString()!, e.GetProperty("grade").Deserialize<int?>() ?? Math.Clamp(e.GetProperty("coin").GetInt32(), 1, 3),
            e.GetProperty("enemyName").GetString())).ToArray();

    public static bool TitleVisible(System.Drawing.Bitmap frame)
    {
        double s = Math.Min(frame.Width / 1920d, frame.Height / 1080d);
        int count = 0, dark = 0;
        for (int y = 48; y < 98; y += 8)
            for (int x = 248; x < 463; x += 10)
            {
                var p = frame.GetPixel((int)(x*s), (int)(y*s));
                if (p.G > 125 && p.B > 125 && p.G - p.R > 9) count++;
                if (p.G < 65 && p.R < 65 && p.B < 65) dark++;
            }
        return count >= 10 && dark >= 25;
    }

    private static string Series(string name) => name.TrimEnd('I', '!', '1', '|');

    private static string Normalize(string s) => string.Concat(s.Normalize(NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c))).Replace("“", "").Replace("”", "").Replace("\"", "")
        .Replace("·", "").Replace("•", "").Replace("・", "").Replace("異", "异");

    public async Task<AdaptReading?> ReadAsync(byte[] frame, int width, int height, bool solo)
    {
        double scale = Math.Min(width / 1920d, height / 1080d);
        async Task<string> Read(int x, int y, int w, int h)
        {
            var lines = await ocr.RecognizeRegionAsync(frame, (int)(x*scale), (int)(y*scale),
                (int)(w*scale), (int)(h*scale));
            return Normalize(string.Concat(lines.OrderBy(l => l.Box.Y / (int)Math.Max(1, 12*scale))
                .ThenBy(l => l.Box.X).Select(l => l.Text)));
        }
        var header = await ocr.RecognizeRegionAsync(frame, (int)(210*scale), (int)(25*scale),
            (int)(365*scale), (int)(85*scale));
        var title = header.OrderBy(l => l.Box.X).FirstOrDefault();
        string? family = Normalize(title?.Text ?? "") switch {
            "悬赏决策" => "bounty", "道具补给" => "supply", "机密商店" => "shop", "战术决策" => "tactic", _ => null };
        if (family is null) return null;
        var cards = new AdaptChoice?[solo ? 3 : 6];
        var pool = family switch { "bounty" => _bounties, "tactic" => tactics, _ => _items };
        // 三种主题的卡面围绕屏幕中心缩放；用标题左沿定位横向缩放。
        double titleX = title!.Box.X / scale;
        double span = titleX < 232 ? .94 : titleX > 256 ? 1.11 : 1;
        int X(int x) => (int)(960 + (x - 960) * span);
        int rowShift = 0;
        if (solo)
        {
            // 三选一没有实图可标定纵坐标。至少两个不同列的表内牌名共线才定位这一行。
            var anchors = new List<(int Column, int Y)>();
            for (int col = 0; col < 3; col++)
            {
                int x = X(476 + col*462), right = X(719 + col*462);
                var lines = await ocr.RecognizeRegionAsync(frame, (int)(x*scale), (int)(150*scale),
                    (int)((right-x)*scale), (int)(700*scale));
                foreach (var line in lines)
                {
                    string name = Normalize(line.Text);
                    // 效果正文会引用其他装备；牌名字号更大，忽略正文小字。
                    if (line.Box.H >= 27*scale && pool.Any(c => Normalize(c.Name) == name))
                        anchors.Add((col, (int)((line.Box.Y + line.Box.H/2)/scale)));
                }
            }
            var rows = anchors.Where(a => anchors.Any(b => b.Column != a.Column && Math.Abs(a.Y-b.Y) <= 24))
                .Select(a => a.Y).ToArray();
            if (rows.Length == 0 || rows.Max()-rows.Min() > 48) return new(family, cards);
            rowShift = (int)rows.Average() - 259;
        }
        for (int i = 0; i < cards.Length; i++)
        {
            int dx = i % 3 * 462, dy = i / 3 * 401 + rowShift;
            int nameX = X(476 + dx);
            int y = family == "bounty" ? 195 : 213;
            string name = await Read(nameX, y + dy, X(719 + dx) - nameX, 303 - y);
            // 档位的细竖线常丢笔；通用悬赏要求系列及效果里的敌名同时命中。
            bool generic = family == "bounty" && name.StartsWith("悬赏");
            var matches = pool.Where(c =>
                generic ? Series(Normalize(c.Name)) == Series(name) : Normalize(c.Name) == name).ToArray();
            if (family == "bounty" && matches.Length > 0)
            {
                // 同名、同档悬赏可以指向不同敌人，用效果文字核对具体目标。
                string body = await Read(X(338 + dx), 307 + dy, X(717 + dx) - X(338 + dx), 178);
                matches = matches.Where(c => c.Enemy is { Length: > 0 } e && body.Contains(Normalize(e))).ToArray();
            }
            if (matches.Length == 1) cards[i] = matches[0];
        }
        return new(family, cards);
    }
}
