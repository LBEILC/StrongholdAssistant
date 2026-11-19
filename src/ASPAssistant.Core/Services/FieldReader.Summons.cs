using ASPAssistant.Core.Models;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

public sealed partial class FieldReader
{
    private SummonDefinition[] _summonDefinitions = [];
    private SpriteMatcher? _summonMatcher;
    private SpriteMatcher? _summonOperatorMatcher;
    private string _operatorPackage = "";
    private (string Name, int Tier)[] _operatorTiers = [];
    private readonly Dictionary<FieldCell, (double X, double Y)> _summonFeet = [];
    private readonly HashSet<FieldCell> _summonHints = [];
    public FieldSummon[] Summons { get; private set; } = [];

    private static Mat NormalizeSpriteImage(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        using var raw = Cv2.ImDecode(stream.ToArray(), ImreadModes.Color);
        var image = new Mat();
        Cv2.Resize(raw, image, new Size(1920, 1080));
        return image;
    }

    private void ReadSummons(Mat image, Dictionary<FieldCell, int> cells)
    {
        Summons = [];
        if (_summonMatcher is null) return;
        var found = new List<FieldSummon>();
        var names = _summonDefinitions.Select(s => s.CharId).ToArray();
        foreach (var (cell, tier) in cells.ToArray())
        {
            var g = _cellGeometry[cell];
            bool badge = _summonFeet.TryGetValue(cell, out var foot);
            if (!badge) foot = (g.FootX, g.FootY);
            // 无角标时先确认脚点上方确实有图像细节，纯色空白不运行身份比对。
            if (!badge)
            {
                var body = new Rect((int)foot.X - 35, (int)foot.Y - 110, 70, 90)
                    & new Rect(0, 0, image.Width, image.Height);
                if (body.Width == 0 || body.Height == 0) continue;
                using var region = new Mat(image, body);
                Cv2.MeanStdDev(region, out _, out Scalar deviation);
                if (Math.Max(deviation.Val0, Math.Max(deviation.Val1, deviation.Val2)) < 10) continue;
            }
            bool bench = cell.Zone == "bench";
            if (!CoversSprite(image, SpriteBounds(foot.X, foot.Y, bench))) continue;
            g = g with { BadgeFootX = foot.X, BadgeFootY = foot.Y };
            // 预筛、精排和干员复核共用同一裁块，无需在进程内来回编码 PNG。
            using var crop = TemplateCrop(image, g, bench, out int left, out int top);
            var quick = _summonMatcher.MatchAtFoot(crop, bench, g, left, top, 0, names, searchRadius: 8, quick: true);
            if (quick.Candidates.FirstOrDefault() is not { Score: >= .5 }) continue;
            var candidates = quick.Candidates.Take(4).Select(c => c.Name).ToArray();
            var match = _summonMatcher.MatchAtFoot(crop, bench, g, left, top, 0, candidates, searchRadius: 8);
            var ranked = match.Candidates.GroupBy(c => _summonDefinitions.Single(s => s.CharId == c.Name).Name)
                .Select(group => (Name: group.Key, Score: group.Max(c => c.Score)))
                .OrderByDescending(c => c.Score).ToArray();
            bool excluded = _summonHints.Contains(cell);
            if (ranked.Length == 0 || ranked[0].Score < (excluded ? .64 : badge ? .68 : .80)
                || ranked.Length > 1 && ranked[0].Score - ranked[1].Score < (excluded ? .06 : badge ? .12 : .16)) continue;
            if (tier > 0)
            {
                _summonOperatorMatcher ??= new(_operatorPackage);
                var operators = _summonOperatorMatcher.MatchAtFoot(crop, bench, g, left, top, tier,
                    _operatorTiers.Where(o => o.Tier == tier).Select(o => o.Name).ToArray());
                if (operators.Candidates.FirstOrDefault() is { } best && ranked[0].Score < best.Score + .12) continue;
            }
            cells[cell] = 0;
            found.Add(new(cell.Zone, cell.Col, cell.Row, ranked[0].Name, null));
        }
        Summons = found.ToArray();
    }
}
