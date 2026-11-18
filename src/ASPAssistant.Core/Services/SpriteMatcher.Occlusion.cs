using ASPAssistant.Core.Models;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

public sealed partial class SpriteMatcher
{
    // 遮挡率以个人模板使用的中心80×136区域为分母，不依赖后排候选名字。

    public FieldSpritePatch WithOcclusion(FieldSpritePatch target,
        IEnumerable<(FieldUnit Unit, FieldSpritePatch Patch)> foreground,
        IReadOnlyDictionary<string, string?>? skins = null)
    {
        if (!UsesPhysicalScale || target.Geometry is not { } g || target.Bench) return target;
        var region = target.TemplateRegion;
        using var image = Cv2.ImDecode(region?.Image ?? target.Image, ImreadModes.Color);
        using var blocked = new Mat(image.Size(), MatType.CV_8UC1, Scalar.All(0));
        double left = Math.Round(g.BadgeFootX) - 90 - (region?.OffsetX ?? 0);
        double top = Math.Round(g.BadgeFootY) - 156 - (region?.OffsetY ?? 0);
        foreach (var (unit, patch) in foreground)
        {
            if (patch.Bench || patch.Geometry is not { } f
                || f.BadgeFootY <= g.BadgeFootY + .25 * g.CellWidth) continue;
            double footX = f.BadgeFootX + f.CellWidth * .061382994597481755 - left;
            double footY = f.BadgeFootY + f.CellWidth * .1774000064377288 - top;
            if (Math.Abs(f.BadgeFootX - g.BadgeFootX) > 2 * (g.CellWidth + f.CellWidth)
                || footY - 2 * f.CellWidth > image.Height) continue;
            if (unit.Operator is null)
            {
                // 未知前排用固定身形；不借后排答案或全阶巨大轮廓扩大遮挡。
                Cv2.Ellipse(blocked, new Point((int)footX, (int)(footY - .65 * f.CellWidth)),
                    new Size((int)(.3 * f.CellWidth), (int)(.65 * f.CellWidth)), 0, 0, 360, Scalar.All(255), -1);
                continue;
            }
            string? skin = skins?.GetValueOrDefault(unit.Operator);
            foreach (var template in Templates(AppearanceName(unit.Operator, skins), f.CellWidth * .00271)
                .Where(t => t.Size == 100 && t.Tier == unit.Tier
                    && AllowsSkin(t.SkinId, skin)))
            {
                // 当前身份的正背面、待机帧及镜像取并集，保留透明轮廓。
                using var mask = new Mat();
                Cv2.Resize(template.Mask, mask, new Size((int)Math.Round(template.Mask.Width / _resolution),
                    (int)Math.Round(template.Mask.Height / _resolution)), interpolation: InterpolationFlags.Nearest);
                var origin = template.Origin!.Value;
                var rect = new Rect((int)Math.Round(footX - origin.X / _resolution),
                    (int)Math.Round(footY - origin.Y / _resolution), mask.Width, mask.Height);
                var clipped = rect & new Rect(0, 0, image.Width, image.Height);
                if (clipped.Width <= 0 || clipped.Height <= 0) continue;
                using var source = new Mat(mask, new Rect(clipped.X - rect.X, clipped.Y - rect.Y, clipped.Width, clipped.Height));
                using var destination = new Mat(blocked, clipped);
                Cv2.BitwiseOr(source, destination, destination);
            }
        }
        using var center = new Mat(blocked, new Rect(50 + (region?.OffsetX ?? 0), 40 + (region?.OffsetY ?? 0), 80, 136));
        double ratio = Cv2.CountNonZero(center) / (80d * 136);
        return target with { OcclusionMask = Cv2.CountNonZero(blocked) == 0 ? null : blocked.ImEncode(".png"), OcclusionRatio = ratio };
    }
}
