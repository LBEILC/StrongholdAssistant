using ASPAssistant.Core.Models;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

public sealed partial class SpriteMatcher
{
    public FieldSpritePatch CropModelInput(FieldSpritePatch target, int tier, string[] candidates,
        IReadOnlyDictionary<string, string?>? skins = null)
    {
        if (!UsesPhysicalScale || target.Geometry is not { } g) return target;
        var templates = _source.Where(t => t.Tier == tier && candidates.Any(n => AppearanceName(n, skins) == t.Name
            && AllowsSkin(t.SkinId, skins?.GetValueOrDefault(n)))).ToArray();
        if (templates.Length == 0) return target;
        using var image = Cv2.ImDecode(target.TemplateRegion?.Image ?? target.Image, ImreadModes.Color);
        using var blocked = new Mat(image.Size(), MatType.CV_8UC1, Scalar.All(255));
        double scale = g.CellWidth * .00271 / _pixelsPerUnit;
        double x = 90 + (target.TemplateRegion?.OffsetX ?? 0) + g.CellWidth * (target.Bench ? .05017159418908035 : .061382994597481755);
        double y = (target.Bench ? 180 : 156) + (target.TemplateRegion?.OffsetY ?? 0) + g.CellWidth * (target.Bench ? .03892515182781064 : .1774000064377288);
        // 格宽左右各留8%，最高候选上下各留8%格宽及3像素的定位余量。
        double margin = .08 * g.CellWidth + 3;
        double top = y - templates.Max(t => t.Origin!.Value.Y) * scale - margin;
        double bottom = y + templates.Max(t => t.Color.Height - t.Origin!.Value.Y) * scale + margin;
        var rect = new Rect((int)Math.Floor(x - .58 * g.CellWidth), (int)Math.Floor(top),
            (int)Math.Ceiling(1.16 * g.CellWidth), (int)Math.Ceiling(bottom - top)) & new Rect(0, 0, image.Width, image.Height);
        if (rect.Width > 0 && rect.Height > 0) Cv2.Rectangle(blocked, rect, Scalar.All(0), -1);
        if (target.OcclusionMask is { } bytes)
        {
            using var front = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
            Cv2.BitwiseOr(blocked, front, blocked);
        }
        return target with { OcclusionMask = blocked.ImEncode(".png") };
    }

}
