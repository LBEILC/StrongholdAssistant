using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

public sealed partial class RosterReader
{
    private sealed record PrototypeCard(string Name, int X, int Y, int Index, int? Tier, string[] Bonds);

    private string[] ReadPrototypeBonds(Bitmap card)
    {
        using var png = new MemoryStream(); card.Save(png, ImageFormat.Png);
        using var source = Cv2.ImDecode(png.ToArray(), ImreadModes.Color);
        using var strip = new Mat(source, new Rect(45, 282, 215, 52));
        using var green = new Mat(); Cv2.ExtractChannel(strip, green, 1);
        // 游戏卡下方是青色圆底、黑色盟约图案；资源是透明底白色图案。
        Cv2.BitwiseNot(green, green);
        var matches = new List<(string Name, double Score, int X)>();
        foreach (var (name, bytes) in _prototypeBonds)
        {
            using var icon = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
            using var alpha = new Mat(); Cv2.ExtractChannel(icon, alpha, 3);
            double best = 0; int bestX = 0;
            foreach (int size in new[] { 24, 28, 32, 36 })
            {
                using var resized = new Mat(); Cv2.Resize(alpha, resized, new OpenCvSharp.Size(size, size));
                using var score = new Mat(); Cv2.MatchTemplate(green, resized, score, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(score, out _, out var max, out _, out var location);
                if (max > best) { best = max; bestX = location.X; }
            }
            matches.Add((name, best, bestX));
        }
        var accepted = new List<string>();
        foreach (var best in matches.OrderByDescending(m => m.Score))
        {
            if (best.Score < .65) break;
            var rivals = matches.Where(m => m.Name != best.Name && Math.Abs(m.X - best.X) < 18).ToArray();
            if (rivals.All(m => best.Score - m.Score >= .08)) accepted.Add(best.Name);
        }
        return accepted.ToArray();
    }

    private async Task<int?> ReadPrototypeTier(byte[] card)
    {
        var text = string.Concat((await _ocr.RecognizeRegionAsync(card, 0, 6, 52, 80)).Select(l => l.Text))
            .Trim().Normalize(System.Text.NormalizationForm.FormKC).Replace(" ", "");
        return PithIdentity.ReadTier(text);
    }

    private void ResolvePrototypeCards(List<PrototypeCard> pending, List<RosterCard> cards,
        (int X, int Y, double Scale)[] grid, bool tierSort, List<string> unknown)
    {
        foreach (var card in pending)
        {
            var positions = tierSort ? cards.Select(c => (Card: c, Slot: _prototypes.Slots.FirstOrDefault(s => s.Name == c.Operator.Name)))
                .Where(p => p.Slot is not null)
                .Select(p => (p.Slot!.Tier, Order: p.Slot.Order + card.Index - Array.FindIndex(grid, g => g.X == p.Card.X && g.Y == p.Card.Y)))
                .Where(p => card.Tier is null || p.Tier == card.Tier)
                .GroupBy(p => p).Where(g => g.Count() >= 2).Select(g => g.Key).ToArray() : [];
            var resolved = positions.Select(p => _prototypes.Resolve(card.Name, p.Tier, p.Order, card.Bonds))
                .OfType<PrototypeSlot>().DistinctBy(s => s.Name).ToArray();
            var slot = resolved.Length == 1 ? resolved[0] : positions.Length == 0
                ? _prototypes.Resolve(card.Name, card.Tier, null, card.Bonds) : null;
            if (slot is null)
            {
                unknown.Add($"{card.Name} ({card.X},{card.Y})：原型干员无法唯一反推；阶数={card.Tier}，盟约={string.Join(',', card.Bonds)}");
                continue;
            }
            cards.Add(new(new(slot.Name, null, null, null, null, DateTimeOffset.UtcNow.ToString("O"), Prototype: slot.Prototype),
                false, card.X, card.Y, 0, 0, "无法识别", 0, null, null));
        }
    }
}
