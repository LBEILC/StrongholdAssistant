using ASPAssistant.Core.Models;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Services;

public sealed partial class FieldReader
{
    public bool BossRound { get; set; }
    public string? KnownMap { get; set; }
    private readonly Dictionary<FieldCell, (double X, double Y)> _spriteFeet = [];
    private readonly Dictionary<FieldCell, FieldSpriteGeometry> _cellGeometry = [];
    private FieldFrameGeometry? _frameGeometry;
    private FieldBadge? ReadCovenantBadge(Pixels pixels, Mat image, int width, int height,
        double cx, double cy, double badgeScale)
    {
        var bounds = SpriteBounds(cx * 1920 / width + 35 * badgeScale, cy * 1080 / height + 112 * badgeScale, false);
        if (!CoversSprite(image, bounds)) return null;
        using var crop = new Mat(image, bounds);
        FieldBadge? best = null;
        foreach (var candidate in _occupancyModel.Value.LikelyOperators(crop))
        {
            int tier = _operatorTiers.FirstOrDefault(o => o.Name == candidate.Name).Tier;
            if (tier == 0) continue;
            var badge = ReadBadge(pixels, width, height, cx, cy, true, tier, candidate.Score >= .8 ? .49 : .66);
            if (badge is not null && (best is null || badge.Score > best.Score)) best = badge;
        }
        return best;
    }

    private bool HasModelBodyEvidence(Mat image, FieldCell cell, FieldBadge badge)
    {
        if (badge.Tier != 2 && !(badge.Tier == 1 && cell.Zone == "board")) return false;
        var foot = _summonFeet[cell];
        var bounds = SpriteBounds(foot.X, foot.Y, cell.Zone == "bench");
        if (!CoversSprite(image, bounds)) return false;
        using var crop = new Mat(image, bounds);
        var candidate = _occupancyModel.Value.LikelyOperators(crop).FirstOrDefault();
        // 背身摆渡人的披风中段缺少轮廓，要求一阶角标和身体模型同时确认。
        if (badge.Tier == 1) return candidate is { Name: "摆渡人", Score: >= .95 };
        // 光标特效会破坏黑猫的模板比对，完整裁块仍可由模型确认。
        if (candidate is { Name: "Miss.Christine", Score: >= .9 }) return true;
        var patch = CropSprite(image, foot.X, foot.Y, cell.Zone == "bench")!;
        patch = patch with { Geometry = _cellGeometry[cell] with { BadgeFootX = foot.X, BadgeFootY = foot.Y } };
        patch = patch with { TemplateRegion = CropTemplateSprite(image, patch) };
        _summonOperatorMatcher ??= new(_operatorPackage);
        var match = _summonOperatorMatcher.MatchCellWidth(patch, badge.Tier,
            _operatorTiers.Where(o => o.Tier == badge.Tier).Select(o => o.Name).ToArray());
        return match.Candidates.FirstOrDefault() is { Name: "Miss.Christine", Score: >= .45 }
            && match.Gap >= .02;
    }

    private Dictionary<FieldCell, FieldSpritePatch> ReadSprites(byte[] frame,
        IReadOnlyDictionary<FieldCell, int> cells)
    {
        using var raw = Cv2.ImDecode(frame, ImreadModes.Color);
        using var image = new Mat();
        Cv2.Resize(raw, image, new Size(1920, 1080));
        var result = new Dictionary<FieldCell, FieldSpritePatch>();
        foreach (var (cell, tier) in cells)
        {
            if (tier == 0) continue;
            bool bench = cell.Zone == "bench";
            var foot = _spriteFeet[cell];
            var patch = CropSprite(image, foot.X, foot.Y, bench);
            if (patch is not null)
            {
                patch = patch with { Geometry = _cellGeometry[cell] with { BadgeFootX = foot.X, BadgeFootY = foot.Y } };
                result[cell] = patch with { TemplateRegion = CropTemplateSprite(image, patch) };
            }
        }
        return result;
    }

    // 包中最大范围（含镜像/103%尺度）为横向1.53格、上方1.98格、下方0.58格。
    // 加上标定偏移和搜索余量；偶数扩边保持半尺寸采样与旧裁块一致。
    public static FieldSpriteRegion CropTemplateSprite(Mat image, FieldSpritePatch patch)
    {
        using var crop = TemplateCrop(image, patch.Geometry!, patch.Bench, out int left, out int top);
        return new(crop.ImEncode(".png"), left, top);
    }

    private static Mat TemplateCrop(Mat image, FieldSpriteGeometry g, bool bench, out int left, out int top)
    {
        left = 2 * (int)Math.Ceiling(Math.Max(0, 2 * g.CellWidth - 90) / 2);
        top = 2 * (int)Math.Ceiling(Math.Max(0, 2.25 * g.CellWidth - 180) / 2);
        int bottom = 2 * (int)Math.Ceiling(Math.Max(0, g.CellWidth - 24) / 2);
        var rect = new Rect((int)Math.Round(g.BadgeFootX) - 90 - left,
            (int)Math.Round(g.BadgeFootY) + (bench ? 0 : 24) - 180 - top,
            180 + left * 2, 204 + top + bottom);
        var visible = rect & new Rect(0, 0, image.Width, image.Height);
        using var crop = new Mat(image, visible);
        var padded = new Mat();
        Cv2.CopyMakeBorder(crop, padded, visible.Top - rect.Top, rect.Bottom - visible.Bottom,
            visible.Left - rect.Left, rect.Right - visible.Right, BorderTypes.Constant, Scalar.All(0));
        return padded;
    }

    private static Rect SpriteBounds(double x, double y, bool bench) =>
        new((int)Math.Round(x) - 90, (int)Math.Round(y) + (bench ? 0 : 24) - 180, 180, 204);

    private static bool CoversSprite(Mat image, Rect rect) =>
        rect.X >= 0 && rect.Y >= 0 && rect.Right <= image.Width && rect.Bottom <= image.Height;

    // 与冻结比对器相同的裁块；脚点坐标以1920×1080计。
    public static FieldSpritePatch? CropSprite(Mat image, double footX, double footY, bool bench)
    {
        var rect = SpriteBounds(footX, footY, bench);
        if (!CoversSprite(image, rect)) return null;
        using var crop = new Mat(image, rect);
        using var center = new Mat(crop, new Rect(50, 40, 80, 136));
        using var small = new Mat();
        Cv2.Resize(center, small, new Size(20, 34));
        var pixels = new byte[20 * 34 * 3];
        Marshal.Copy(small.Data, pixels, 0, pixels.Length);
        return new(crop.ImEncode(".png"), pixels, bench);
    }
}
