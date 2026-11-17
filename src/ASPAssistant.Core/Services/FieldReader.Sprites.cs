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
    private bool HasShortUnitEvidence(System.Drawing.Bitmap bitmap, FieldCell cell, FieldBadge badge)
    {
        // 黑猫低于原身体采样带；用既有官方模板确认占用，不放宽所有二阶的轮廓门槛。
        if (badge.Tier != 2) return false;
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        using var raw = Cv2.ImDecode(stream.ToArray(), ImreadModes.Color);
        using var image = new Mat();
        Cv2.Resize(raw, image, new Size(1920, 1080));
        var foot = _summonFeet[cell];
        var patch = CropSprite(image, foot.X, foot.Y, cell.Zone == "bench");
        if (patch is null) return false;
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
        var g = patch.Geometry!;
        int left = 2 * (int)Math.Ceiling(Math.Max(0, 2 * g.CellWidth - 90) / 2);
        int top = 2 * (int)Math.Ceiling(Math.Max(0, 2.25 * g.CellWidth - 180) / 2);
        int bottom = 2 * (int)Math.Ceiling(Math.Max(0, g.CellWidth - 24) / 2);
        var rect = new Rect((int)Math.Round(g.BadgeFootX) - 90 - left,
            (int)Math.Round(g.BadgeFootY) + (patch.Bench ? 0 : 24) - 180 - top,
            180 + left * 2, 204 + top + bottom);
        var visible = rect & new Rect(0, 0, image.Width, image.Height);
        using var crop = new Mat(image, visible);
        using var padded = new Mat();
        Cv2.CopyMakeBorder(crop, padded, visible.Top - rect.Top, rect.Bottom - visible.Bottom,
            visible.Left - rect.Left, rect.Right - visible.Right, BorderTypes.Constant, Scalar.All(0));
        return new(padded.ImEncode(".png"), left, top);
    }

    // 与冻结比对器相同的裁块；脚点坐标以1920×1080计。
    public static FieldSpritePatch? CropSprite(Mat image, double footX, double footY, bool bench)
    {
        int x = (int)Math.Round(footX), y = (int)Math.Round(footY) + (bench ? 0 : 24);
        var rect = new Rect(x - 90, y - 180, 180, 204);
        if (rect.X < 0 || rect.Y < 0 || rect.Right > image.Width || rect.Bottom > image.Height) return null;
        using var crop = new Mat(image, rect);
        using var center = new Mat(crop, new Rect(50, 40, 80, 136));
        using var small = new Mat();
        Cv2.Resize(center, small, new Size(20, 34));
        var pixels = new byte[20 * 34 * 3];
        Marshal.Copy(small.Data, pixels, 0, pixels.Length);
        return new(crop.ImEncode(".png"), pixels, bench);
    }
}
