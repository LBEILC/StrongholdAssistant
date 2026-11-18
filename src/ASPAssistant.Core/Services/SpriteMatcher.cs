using System.IO.Compression;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using ASPAssistant.Core.Models;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

public sealed record SpriteCandidate(string Name, double Score);
public sealed record SpriteMatch(IReadOnlyList<SpriteCandidate> Candidates, double? Gap)
{
    public bool CanAutoFill => Gap >= SpriteMatcher.AutoFillGap;
}

/// <summary>冻结的小人蒙版比对。单实例由扫描线程持有并释放，不负责候选来源或扫描调度。</summary>
public sealed partial class SpriteMatcher : IDisposable
{
    public const double AutoFillGap = 0.04592923876191826;
    private sealed record Template(string Name, string SkinId, int Tier, int Size, Mat Color, Mat Mask, Point2d? Origin, int Variant = 0)
    {
        public Size Dimensions { get; } = Color.Size();
        public SearchTemplate? Search { get; set; }
    }
    private readonly double _resolution;
    private readonly bool _hasOrigins;
    private readonly double _pixelsPerUnit;
    public bool UsesPhysicalScale => _pixelsPerUnit > 0;
    private readonly List<Template> _source = [];
    // 比对线程独立持有缩放图，避免其他格换尺度时释放正在使用的 Mat。
    private readonly ThreadLocal<Dictionary<string, (double? Unit, List<Template> Items)>> _scaledByThread = new(() => new(StringComparer.Ordinal), true);
    private readonly Dictionary<string, Template[]> _byName;

    public SpriteMatcher(string packagePath)
    {
        using var file = File.OpenRead(packagePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.UTF8);
        string version = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (version is not ("SPRT0001" or "SPRT0002" or "SPRT0003" or "SPRT0004"))
            throw new InvalidDataException("小人模板包版本不符。");
        // 旧包继续使用原有半尺寸口径，新包由生成器声明归一边长。
        _hasOrigins = version is "SPRT0003" or "SPRT0004";
        _resolution = version != "SPRT0001" ? reader.ReadUInt16() / 120.0 : .5;
        if (_resolution is not (.5 or 1)) throw new InvalidDataException("模板分辨率必须为60或120。");
        if (version == "SPRT0004")
        {
            _pixelsPerUnit = reader.ReadSingle();
            if (!double.IsFinite(_pixelsPerUnit) || _pixelsPerUnit <= 0)
                throw new InvalidDataException("骨骼单位比例必须为有限正数。");
        }
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
            string skinId = version != "SPRT0001" ? Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16())) : "default";
            int tier = reader.ReadUInt16(), width = reader.ReadUInt16(), height = reader.ReadUInt16();
            Point2d? origin = _hasOrigins ? new Point2d(reader.ReadSingle(), reader.ReadSingle()) : null;
            if (origin is { } p && (!double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                throw new InvalidDataException("模板原点必须为有限坐标。");
            var color = new Mat(height, width, MatType.CV_8UC3);
            var mask = new Mat(height, width, MatType.CV_8UC1);
            var pixels = new byte[width * height * 3]; reader.BaseStream.ReadExactly(pixels);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, color.Data, pixels.Length);
            pixels = new byte[width * height]; reader.BaseStream.ReadExactly(pixels);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, mask.Data, pixels.Length);
            _source.Add(new(name, skinId, tier, 0, color, mask, origin, i));
        }
        _byName = _source.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>按人工标定的脚点和格宽匹配，并排除前排遮挡轮廓。</summary>
    public SpriteMatch MatchCellWidth(FieldSpritePatch patch, int tier, IReadOnlyCollection<string> candidates,
        IReadOnlyDictionary<string, string?>? skins = null, int searchRadius = 3, bool quick = false)
    {
        if (!UsesPhysicalScale || patch.Geometry is not { } g)
            throw new ArgumentException("格宽匹配需要真实比例模板及本帧格子几何。");
        var region = patch.TemplateRegion;
        using var crop = Cv2.ImDecode(region?.Image ?? patch.Image, ImreadModes.Color);
        using var blocked = patch.OcclusionMask is null ? null : Cv2.ImDecode(patch.OcclusionMask, ImreadModes.Grayscale);
        return MatchAtFoot(crop, patch.Bench, g, region?.OffsetX ?? 0, region?.OffsetY ?? 0, tier,
            candidates, skins, blocked, searchRadius, quick);
    }

    internal SpriteMatch MatchAtFoot(Mat crop, bool bench, FieldSpriteGeometry g, int offsetX, int offsetY,
        int tier, IReadOnlyCollection<string> candidates, IReadOnlyDictionary<string, string?>? skins = null,
        Mat? blocked = null, int searchRadius = 3, bool quick = false)
    {
        double dx = g.CellWidth * (bench ? .05017159418908035 : .061382994597481755);
        double dy = g.CellWidth * (bench ? .03892515182781064 : .1774000064377288);
        var anchor = new Point((int)Math.Round(90 + g.BadgeFootX + dx - Math.Round(g.BadgeFootX)) + offsetX,
            (int)Math.Round((bench ? 180 : 156) + g.BadgeFootY + dy - Math.Round(g.BadgeFootY)) + offsetY);
        return MatchCropCore(crop, bench, tier, candidates, anchor, skins, g.CellWidth * .00271, blocked, searchRadius, quick);
    }

    /// <summary>离线裁块入口。默认180×204；自定义锚点为相对裁块的脚点（旧包为外框底部）。</summary>
    public SpriteMatch MatchCrop(Mat crop, bool bench, int tier, IReadOnlyCollection<string> candidates,
        Point? searchAnchor = null, IReadOnlyDictionary<string, string?>? skins = null,
        double? screenPixelsPerUnit = null, Mat? occlusion = null, int searchRadius = 3)
        => MatchCropCore(crop, bench, tier, candidates, searchAnchor, skins, screenPixelsPerUnit, occlusion, searchRadius, false);

    private SpriteMatch MatchCropCore(Mat crop, bool bench, int tier, IReadOnlyCollection<string> candidates,
        Point? searchAnchor, IReadOnlyDictionary<string, string?>? skins,
        double? screenPixelsPerUnit, Mat? occlusion, int searchRadius, bool quick)
    {
        using var timing = FieldRecognitionTiming.Measure("templates");
        if (crop.Empty() || crop.Type() != MatType.CV_8UC3)
            throw new ArgumentException("裁块必须为BGR图像。", nameof(crop));
        if (UsesPhysicalScale && (screenPixelsPerUnit is not { } unit || !double.IsFinite(unit) || unit <= 0))
            throw new ArgumentException("真实比例模板需要已标定的屏幕像素/骨骼单位。", nameof(screenPixelsPerUnit));
        using var half = new Mat();
        Cv2.Resize(crop, half, new Size((int)(crop.Width * _resolution), (int)(crop.Height * _resolution)));
        // 读取层的棋盘裁块向下移24像素，原始脚点在裁块内156；整备区为180。
        // 旧包保留冻结的外框底边口径，新包直接对齐骨骼原点。
        var anchor = searchAnchor ?? new Point(90, _hasOrigins ? (bench ? 180 : 156) : (bench ? 176 : 180));
        int bottom = (int)(anchor.Y * _resolution);
        int radius = (int)((UsesPhysicalScale ? searchRadius : 16) * _resolution);
        using var blocked = new Mat();
        if (occlusion is { } && !occlusion.Empty())
        {
            Cv2.Resize(occlusion, blocked, half.Size(), interpolation: InterpolationFlags.Nearest);
            // 搜索窗口内所有位移都排除前景，避免平移后把遮挡边缘重新算入。
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(radius * 4 + 1, radius * 4 + 1));
            Cv2.Dilate(blocked, blocked, kernel);
        }
        // 缩放缓存仍由扫描线程构建；各候选只读模板，并行时独占临时 Mat。
        var work = candidates.Distinct(StringComparer.Ordinal).Select(name => (Name: name,
            Templates: Templates(AppearanceName(name, skins), screenPixelsPerUnit), Skin: skins?.GetValueOrDefault(name))).ToArray();
        var search = UsesPhysicalScale && Avx2.IsSupported ? new SearchImage(half) : null;
        var ranked = new SpriteCandidate?[work.Length];
        Parallel.For(0, work.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
        {
            var (name, templates, skin) = work[index];
            using var scores = new Mat(); using var difference = new Mat();
            var values = new float[(radius * 2 + 1) * (radius * 2 + 1)];
            double best = -2, tiedScore = -2;
            Template? winner = null;
            Rect winnerRegion = default;
            using var winnerMask = new Mat();
            // 召唤物预筛每套皮肤仅取首尾外观和标定尺度；干员路径保持原样。
            var limits = quick ? templates.GroupBy(t => t.SkinId).ToDictionary(g => g.Key,
                g => (First: g.Min(t => t.Variant), Last: g.Max(t => t.Variant))) : null;
            foreach (var template in templates)
            {
                if (limits is not null && (template.Size != 100 || template.Variant != limits[template.SkinId].First
                    && template.Variant != limits[template.SkinId].Last)) continue;
                if (!AllowsSkin(template.SkinId, skin)) continue;
                if (template.Tier != tier || (!UsesPhysicalScale && (bench ? template.Size == 104 : template.Size == 168))) continue;
                var a = template.Color; var mask = template.Mask;
                int w = template.Dimensions.Width, h = template.Dimensions.Height;
                int center = (int)Math.Round(anchor.X * _resolution - (template.Origin?.X ?? w / 2.0));
                int top = template.Origin is { } origin ? (int)Math.Round(anchor.Y * _resolution - origin.Y) : bottom - h;
                int x0 = Math.Max(0, center - radius), x1 = Math.Min(half.Width, center + radius + w);
                int y0 = Math.Max(0, top - radius), y1 = Math.Min(half.Height, top + h + radius);
                if (x1 - x0 < w || y1 - y0 < h) continue;
                using var visible = new Mat();
                int visibleCount = 0;
                if (!blocked.Empty())
                {
                    using var excluded = new Mat(blocked, new Rect(x0, y0, w, h));
                    Cv2.BitwiseNot(excluded, visible);
                    Cv2.BitwiseAnd(mask, visible, visible);
                    visibleCount = Cv2.CountNonZero(visible);
                    if (visibleCount < 16) continue;
                    mask = visible;
                }
                using var roi = new Mat(half, new Rect(x0, y0, x1 - x0, y1 - y0));
                double value;
                // 三通道有符号像素乘积和必须能用 32 位整数精确表示。
                if (search is null || (long)w * h > int.MaxValue / (3 * 128 * 128))
                    value = ReferenceScore(roi, a, mask);
                else
                {
                    var shape = template.Search ??= new SearchTemplate(a, template.Mask);
                    if (!blocked.Empty() && visibleCount != shape.Count) shape = new SearchTemplate(a, mask);
                    var (location, corr) = shape.Best(search, x0, y0, roi.Width - w + 1, roi.Height - h + 1);
                    value = ScoreAt(roi, a, mask, location, corr);
                }
                // OpenCV 的单精度舍入可颠倒近似并列的外观。留出复算余量，最终只比较原分数。
                if (search is not null && Math.Abs(value - best) <= 1e-6)
                {
                    tiedScore = Math.Max(tiedScore, ReferenceScore(roi, a, mask));
                    continue;
                }
                if (value <= best) continue;
                best = value;
                tiedScore = -2;
                winner = template;
                winnerRegion = new(x0, y0, roi.Width, roi.Height);
                if (!blocked.Empty()) mask.CopyTo(winnerMask);
            }
            // 每名候选以胜出外观的原 OpenCV 分数参与排序、阈值和追踪。
            if (search is not null && winner is not null)
            {
                using var roi = new Mat(half, winnerRegion);
                best = Math.Max(tiedScore, ReferenceScore(roi, winner.Color, blocked.Empty() ? winner.Mask : winnerMask));
            }
            if (best > -2) ranked[index] = new(name, best);

            double ReferenceScore(Mat roi, Mat color, Mat mask)
            {
                Cv2.MatchTemplate(roi, color, scores, TemplateMatchModes.CCoeffNormed, mask);
                int count = scores.Rows * scores.Cols, bestIndex = 0;
                Marshal.Copy(scores.Data, values, 0, count);
                double corr = double.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    double value = float.IsFinite(values[i]) ? values[i] : -1;
                    if (value > corr) { corr = value; bestIndex = i; }
                }
                return ScoreAt(roi, color, mask, new(bestIndex % scores.Cols, bestIndex / scores.Cols), corr);
            }

            double ScoreAt(Mat roi, Mat color, Mat mask, Point location, double corr)
            {
                using var patch = new Mat(roi, new Rect(location, color.Size()));
                Cv2.Absdiff(patch, color, difference);
                var mean = Cv2.Mean(difference, mask);
                return .75 * corr + .25 * (1 - (mean.Val0 + mean.Val1 + mean.Val2) / (3 * 255));
            }
        });
        var sorted = ranked.OfType<SpriteCandidate>().OrderByDescending(r => r.Score).ThenBy(r => r.Name, StringComparer.Ordinal).ToArray();
        return new(sorted, sorted.Length > 1 ? sorted[0].Score - sorted[1].Score : null);
    }

    // 个人图块没有透明通道，以中心80×136区域作蒙版；半尺寸、位置±8与冻结比对口径一致。
    public static double MatchPersonal(FieldSpritePatch template, FieldSpritePatch target)
    {
        using var source = Cv2.ImDecode(template.Image, ImreadModes.Color);
        using var image = Cv2.ImDecode(target.Image, ImreadModes.Color);
        using var center = new Mat(source, new Rect(50, 40, 80, 136));
        using var half = new Mat();
        Cv2.Resize(image, half, new Size(90, 102));
        using var scaled = new Mat(); using var scores = new Mat(); using var difference = new Mat();
        double best = -1;
        var values = new float[17 * 17];
        foreach (double scale in new[] { .8, .9, 1, 1.1, 1.2 })
        {
            int w = (int)Math.Round(40 * scale), h = (int)Math.Round(68 * scale);
            Cv2.Resize(center, scaled, new Size(w, h));
            int x = (int)Math.Round(45 - w / 2.0), y = (int)Math.Round(54 - h / 2.0);
            int x0 = Math.Max(0, x - 8), y0 = Math.Max(0, y - 8);
            int x1 = Math.Min(half.Width, x + w + 8), y1 = Math.Min(half.Height, y + h + 8);
            using var roi = new Mat(half, new Rect(x0, y0, x1 - x0, y1 - y0));
            Cv2.MatchTemplate(roi, scaled, scores, TemplateMatchModes.CCoeffNormed);
            int count = scores.Rows * scores.Cols, bestIndex = 0;
            Marshal.Copy(scores.Data, values, 0, count);
            double corr = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                double value = float.IsFinite(values[i]) ? values[i] : -1;
                if (value > corr) { corr = value; bestIndex = i; }
            }
            using var patch = new Mat(roi, new Rect(bestIndex % scores.Cols, bestIndex / scores.Cols, w, h));
            Cv2.Absdiff(patch, scaled, difference);
            var mean = Cv2.Mean(difference);
            double color = 1 - (mean.Val0 + mean.Val1 + mean.Val2) / (3 * 255);
            best = Math.Max(best, .75 * corr + .25 * color);
        }
        return best;
    }

    private static readonly int[] PhysicalSizes = [97, 100, 103], LegacySizes = [104, 120, 136, 152, 168];

    private List<Template> Templates(string name, double? screenPixelsPerUnit = null)
    {
        using var timing = FieldRecognitionTiming.Measure("templateScale");
        var cache = _scaledByThread.Value!;
        if (cache.TryGetValue(name, out var cached) && cached.Unit == screenPixelsPerUnit) return cached.Items;
        // 格宽的亚像素变化仅在目标整数宽高改变时才会改变缩放图和原点。
        var result = cached.Items ?? [];
        int index = 0;
        using var mirrored = new Mat();
        using var mirroredMask = new Mat();
        foreach (var source in _byName.GetValueOrDefault(name) ?? [])
            for (int flip = 0; flip <= 1; flip++)
            {
                bool mirroredReady = false;
                int w = source.Dimensions.Width, h = source.Dimensions.Height;
                foreach (int size in UsesPhysicalScale ? PhysicalSizes : LegacySizes)
                {
                    double scale = UsesPhysicalScale
                        ? screenPixelsPerUnit!.Value * _resolution / _pixelsPerUnit * size / 100
                        : size * _resolution / Math.Max(w, h);
                    var target = new Size((int)Math.Round(w * scale), (int)Math.Round(h * scale));
                    int slot = index++;
                    if (slot < result.Count && result[slot].Dimensions == target) continue;
                    if (flip == 1 && !mirroredReady)
                    {
                        Cv2.Flip(source.Color, mirrored, FlipMode.Y);
                        Cv2.Flip(source.Mask, mirroredMask, FlipMode.Y);
                        mirroredReady = true;
                    }
                    var a = new Mat(); var b = new Mat();
                    Cv2.Resize(flip == 1 ? mirrored : source.Color, a, target);
                    Cv2.Resize(flip == 1 ? mirroredMask : source.Mask, b, target, interpolation: InterpolationFlags.Nearest);
                    Point2d? origin = source.Origin is { } p
                        ? new((flip == 1 ? w - p.X : p.X) * target.Width / w, p.Y * target.Height / h) : null;
                    var scaled = new Template(name, source.SkinId, source.Tier, size, a, b, origin, source.Variant);
                    if (slot == result.Count) result.Add(scaled);
                    else
                    {
                        result[slot].Color.Dispose(); result[slot].Mask.Dispose();
                        result[slot] = scaled;
                    }
                }
            }
        cache[name] = (screenPixelsPerUnit, result);
        return result;
    }

    public void Dispose()
    {
        foreach (var t in _source.Concat(_scaledByThread.Values.SelectMany(d => d.Values).SelectMany(v => v.Items)))
        { t.Color.Dispose(); t.Mask.Dispose(); }
        _scaledByThread.Dispose();
    }
}
