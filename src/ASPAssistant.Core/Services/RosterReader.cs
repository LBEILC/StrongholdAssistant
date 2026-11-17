using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Services;

public sealed record RosterOperator(string Name, string? Skin, string? Skill, string? Module, int? Potential,
    string ReadAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Edited = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Prototype = null);
public sealed record Roster(string? ReadAt, RosterOperator[] Operators, string[] CustomSquad);
public sealed record RosterCard(RosterOperator Operator, bool Custom, int X, int Y, double SkinScore, double SkinMargin, string PotentialClass, double ModuleScore, string? SkinCandidate, string? ModuleCandidate);
public sealed record RosterReading(RosterCard[] Cards, int? SquadCount, double Milliseconds, string[] UnknownNames);
public sealed record RosterSort(string Mode, int X, int Y);

/// <summary>物资调配页两排完整卡片；输入归一到样图坐标，边缘残卡不参与识别。</summary>
public sealed partial class RosterReader
{
    private readonly IOcrEngine _ocr;
    private readonly Dictionary<string, JsonElement> _operators;
    private readonly Dictionary<string, Dictionary<string, double[]>> _templates;
    private readonly PrototypeCatalog _prototypes;
    private readonly Dictionary<string, byte[]> _prototypeBonds;
    public bool? HasPithStrategy { get; set; }
    public RosterReader(IOcrEngine ocr, string data)
    {
        _ocr = ocr;
        _prototypes = new(data);
        _prototypeBonds = Directory.GetFiles(Path.Combine(data, "prototype-bonds"), "*.png")
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p) + "Ship", File.ReadAllBytes);
        using var extra = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "codex_extra.json")));
        _operators = extra.RootElement.GetProperty("operators").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        _templates = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double[]>>>(File.ReadAllText(Path.Combine(data, "roster_templates.json")))!;
        foreach (var (name, group) in _templates)
            foreach (var key in group.Keys.ToArray()) group[key] = Normalize(name == "skins" ? SkinPixels(group[key]) : group[key]);
    }

    public async Task<RosterReading?> ReadAsync(byte[] frame)
    {
        var timer = Stopwatch.StartNew();
        using var stream = new MemoryStream(frame);
        using var original = new Bitmap(stream);
        using var image = RosterCoordinates.Normalize(original);
        using var png = new MemoryStream(); image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var bytes = png.ToArray();
        var header = string.Concat((await _ocr.RecognizeRegionAsync(bytes, 250, 10, 485, 90)).Select(l => l.Text.Replace(" ", "")));
        if (!header.Contains("自选编队") || !(header.Contains("选配助战") || header.Contains("物资调配"))) return null;
        var filter = string.Concat((await _ocr.RecognizeRegionAsync(bytes, image.Width - 230, 15, 230, 210)).Select(l => l.Text.Replace(" ", "")));
        if (filter.Contains("收起") && filter.Contains("ALL", StringComparison.OrdinalIgnoreCase))
            throw new IOException("先关掉右侧的盟约筛选");
        var countMatch = System.Text.RegularExpressions.Regex.Match(header, @"(\d+)/12");
        int? count = countMatch.Success ? int.Parse(countMatch.Groups[1].Value) : null;
        var cards = new List<RosterCard>();
        var unknown = new List<string>();
        var grid = LocateGrid(image).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var pending = new List<PrototypeCard>();
        foreach (var (sourceX, sourceY, scale) in grid)
        {
            // 先按画面测得的网格比例还原单卡；后续坐标仅描述卡内布局。
            const int x = 52, y = 0;
            using var cardImage = new Bitmap(260, 350);
            using (var graphics = Graphics.FromImage(cardImage))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, 260, 350),
                    (float)(sourceX - x * scale), sourceY, (float)(260 * scale), (float)(350 * scale), GraphicsUnit.Pixel);
            }
            using var cardPng = new MemoryStream(); cardImage.Save(cardPng, ImageFormat.Png);
            var cardBytes = cardPng.ToArray();
            var labels = await _ocr.RecognizeRegionAsync(cardBytes, x - 45, y + 248, 170, 34);
            string text = string.Concat(labels.Select(l => l.Text.Replace(" ", "")));
            if (!_operators.ContainsKey(text) && !_prototypes.IsPrototype(text))
                text = string.Concat((await _ocr.RecognizeLineAsync(cardBytes, x - 35, y + 249, 160, 30)).Select(l => l.Text.Replace(" ", "")));
            text = text.Replace("タ", "夕").Replace("傑", "杰");
            if (!_operators.ContainsKey(text) && !_prototypes.IsPrototype(text))
            {
                var suffixes = _operators.Keys.Where(n => n.Length >= 2 && text.EndsWith(n, StringComparison.Ordinal)).OrderByDescending(n => n.Length).ToArray();
                if (suffixes.Length > 0) text = suffixes[0];
            }
            bool realPith = false;
            if (text == "Pith")
            {
                var tier = await ReadPrototypeTier(cardBytes);
                var bonds = ReadPrototypeBonds(cardImage);
                // 原型的盟约仍属于被替代的棋子；协防／调和是两名真实 Pith 的盟约。
                if (bonds.Length > 0 && !bonds.Contains("emptyShip") && !bonds.Contains("maniShip"))
                {
                    pending.Add(new(text, sourceX, sourceY, Array.FindIndex(grid, g => g.X == sourceX && g.Y == sourceY), tier, bonds));
                    continue;
                }
                var identity = PithIdentity.Resolve(tier, HasPithStrategy);
                if (identity is null) { unknown.Add($"Pith ({sourceX},{sourceY})：阶数与策略不足以区分身份"); continue; }
                text = identity;
                realPith = true;
            }
            if (!realPith && _prototypes.IsPrototype(text))
            {
                pending.Add(new(text, sourceX, sourceY, Array.FindIndex(grid, g => g.X == sourceX && g.Y == sourceY),
                    await ReadPrototypeTier(cardBytes), ReadPrototypeBonds(cardImage)));
                continue;
            }
            if (!_operators.TryGetValue(text, out var op))
            {
                unknown.Add($"{text} ({sourceX},{sourceY})");
                AppLogger.Info("Roster", $"未匹配名字：{text} ({sourceX},{sourceY})");
                continue;
            }
            var skin = Match("skins", Options(op, "skins"), Vector(cardImage, new(x + 3, y + 30, 106, 110)));
            string? skill = null;
            for (int i = 0; i < Options(op, "skills").Length; i++)
            {
                // 选中格左框是高饱和青蓝色；首格从头像下方 21 像素开始。
                var color = cardImage.GetPixel(x + 127, y + 24 + i * 97);
                if (color.B > 140 && color.G > 100 && color.R < 90) skill = Options(op, "skills")[i];
            }
            var module = Match("modules", Options(op, "modules"), GlyphVector(cardImage, new(x + 62, y + 168, 39, 39)));
            string? moduleId = Options(op, "modules").Length == 0 ? "" : module.Score >= .65 && module.Margin >= .05 ? module.Id : null;
            // 无模组显示为斜线、空格或灰色空方框。
            if (NoModule(cardImage, x + 60, y + 166)) moduleId = "";
            var border = cardImage.GetPixel(x + 5, y);
            bool custom = border.R > 170 && border.G > 90 && border.B < 120;
            int? potential = ReadPotential(cardImage, x - 49, y + 223);
            string potentialClass = potential is int value ? $"潜能{value}" : "无法识别";
            cards.Add(new(new(text, skin.Score >= .75 && skin.Margin >= .10 ? skin.Id : null, skill,
                moduleId, potential, DateTimeOffset.UtcNow.ToString("O")), custom, sourceX, sourceY, skin.Score, skin.Margin, potentialClass, module.Score, skin.Id, module.Id));
        }
        if (pending.Count > 0) ResolvePrototypeCards(pending, cards, grid, (await ReadSortAsync(frame))?.Mode == "tier", unknown);
        return new(cards.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray(), count, timer.Elapsed.TotalMilliseconds, unknown.ToArray());
    }

    // 由排序文字和相连白底定位选中胶囊，按短边归一，保留完整画幅；找不到就不点击。
    public async Task<RosterSort?> ReadSortAsync(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var original = new Bitmap(stream);
        using var image = RosterCoordinates.Normalize(original);
        using var png = new MemoryStream(); image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        int regionLeft = image.Width - 670, regionRight = image.Width - 120;
        var labels = (await _ocr.RecognizeRegionAsync(png.ToArray(), regionLeft, 10, 550, 105))
            .Where(l => l.Text.Replace(" ", "") is "按盟约" or "按等阶").ToArray();
        if (labels.Length != 1) return null;
        var label = labels[0];
        bool tier = label.Text.Replace(" ", "") == "按等阶";
        bool White(int x, int y)
        {
            var c = image.GetPixel(x, y);
            return c.R >= 200 && c.G >= 200 && c.B >= 200;
        }
        int seedX = label.Box.X + label.Box.W / 2, seedY = label.Box.Y - 5;
        if (seedX < regionLeft || seedX >= regionRight || seedY is < 10 or >= 115) return null;
        // 重采样后 OCR 框可能上移几像素，在文字上沿附近找白底，避免种子落到胶囊外。
        int seedLimit = Math.Min(115, label.Box.Y + 6);
        while (seedY < seedLimit && !White(seedX, seedY)) seedY++;
        if (seedY == seedLimit) return null;
        var visited = new HashSet<Point>();
        var queue = new Queue<Point>();
        queue.Enqueue(new(seedX, seedY));
        int left = seedX, right = seedX, top = seedY, bottom = seedY;
        while (queue.TryDequeue(out var p))
        {
            if (p.X < regionLeft || p.X >= regionRight || p.Y is < 10 or >= 115 || !visited.Add(p) || !White(p.X, p.Y)) continue;
            left = Math.Min(left, p.X); right = Math.Max(right, p.X);
            top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
            queue.Enqueue(new(p.X - 1, p.Y)); queue.Enqueue(new(p.X + 1, p.Y));
            queue.Enqueue(new(p.X, p.Y - 1)); queue.Enqueue(new(p.X, p.Y + 1));
        }
        int width = right - left + 1, height = bottom - top + 1;
        if (width is < 150 or > 260 || height is < 45 or > 90
            || label.Box.X < left || label.Box.X + label.Box.W > right + 1) return null;
        // 两端图标到白色胶囊边缘的距离随控件高度缩放，兼容 PC 与模拟器布局。
        int offset = (int)Math.Round(height * .8);
        return new(tier ? "tier" : "covenant", tier ? left + offset : right + offset, (top + bottom) / 2);
    }

    public static int? ReadPotential(Bitmap image, int x, int y)
    {
        if (x < 0 || y < 0 || x + 30 > image.Width || y + 30 > image.Height) return null;
        static bool Blue(Color c) => c.B > 150 && c.G > 100 && c.B - c.R > 45;
        static bool White(Color c) => c.R > 155 && c.G > 155 && c.B > 155;
        // 避开交点，沿四条笔画的独立区段取样。第五笔与已有笔画重叠，另用最新笔的颜色区分满潜。
        (int X1, int Y1, int X2, int Y2)[] lines = [(17, 21, 25, 21), (10, 15, 17, 10), (22, 12, 20, 18), (10, 7, 13, 9)];
        int strokes = 0, lastBlue = 0;
        bool missing = false;
        foreach (var line in lines)
        {
            int bright = 0, blue = 0;
            for (int i = 0; i <= 8; i++)
            {
                var c = image.GetPixel(x + (int)Math.Round(line.X1 + (line.X2 - line.X1) * i / 8.0),
                    y + (int)Math.Round(line.Y1 + (line.Y2 - line.Y1) * i / 8.0));
                if (Blue(c)) blue++;
                if (White(c) || Blue(c)) bright++;
            }
            if (bright is > 1 and < 7) return null;
            if (bright < 7) missing = true;
            else
            {
                if (missing) return null;
                strokes++;
                lastBlue = blue;
            }
        }
        int lit = 0;
        for (int dy = 0; dy < 30; dy++) for (int dx = 0; dx < 30; dx++)
        {
            var c = image.GetPixel(x + dx, y + dy);
            if (White(c) || Blue(c)) lit++;
        }
        // 空位与亮色遮挡分开；非空却没有完整笔画、整块覆盖均不猜。
        if (strokes == 0) return lit < 8 ? 1 : null;
        if (lit > 500) return null;
        if (strokes == 4) return lastBlue >= 7 ? 5 : lastBlue <= 2 ? 6 : null;
        return strokes + 1;
    }

    private static string[] Options(JsonElement op, string field) => op.TryGetProperty(field, out var a)
        ? a.EnumerateArray().Select(v => v.GetProperty("id").GetString()!).ToArray() : [];
    private (string? Id, double Score, double Margin) Match(string group, string[] ids, double[] sample)
    {
        sample = Normalize(group == "skins" ? SkinPixels(sample) : sample);
        var scores = ids.Where(_templates[group].ContainsKey).Select(id => (Id: id,
            Score: _templates[group][id].Zip(sample, (a, b) => a * b).Sum())).OrderByDescending(p => p.Score).ToArray();
        return scores.Length == 0 ? (null, 0, 0) : (scores[0].Id, scores[0].Score, scores[0].Score - (scores.Length > 1 ? scores[1].Score : 0));
    }
    public static (int VisibleWidth, double ColumnSpacing) MeasureScroll(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var original = new Bitmap(stream);
        using var image = RosterCoordinates.Normalize(original);
        var columns = LocateGrid(image).Select(p => p.X).Distinct().Order().ToArray();
        if (columns.Length < 3)
        {
            var error = new IOException("无法测量物资调配页列间距。");
            error.Data["rosterColumns"] = columns;
            throw error;
        }
        // 分组标题会扩大个别间隔，取间隔的下中位数，避免把标题算成一列。
        var gaps = columns.Zip(columns.Skip(1), (a, b) => b - a).Order().ToArray();
        double spacing = gaps[(gaps.Length - 1) / 2];
        // 列距约 310×界面缩放（0.85～1.3）；超出说明认到的不是卡片列，按测不出处理（v1.2.5）。
        if (spacing is < 250 or > 420) throw new IOException("无法测量物资调配页列间距。");
        return (image.Width, spacing);
    }

    private static IEnumerable<(int X, int Y, double Scale)> LocateGrid(Bitmap image)
    {
        bool Border(int x, int y)
        {
            var c = image.GetPixel(x, y);
            // 助战卡青色边框在模拟器画面里蓝色只有 140 上下（v1.2.5 放宽到 120）。
            return c.R > 170 && c.G > 90 && (c.B > 150 || c.B < 120) || c.B > 120 && c.G > 80 && c.R < 100;
        }
        // 寻找卡面顶边，并以向下延续的左边框排除技能格、底边和画面内的亮线。
        var edges = new List<(int X, int Y)>();
        for (int y = 130; y < 700 + (image.Height - 1080) / 2; y++)
        {
            int start = -1;
            for (int x = 0; x < image.Width; x++)
            {
                if (Border(x, y)) { if (start < 0) start = x; }
                // 白框与蓝色角标交界经重采样会留下单像素暗缝，仍属于同一条顶边。
                else if (start >= 0 && !(x + 1 < image.Width && Border(x + 1, y)))
                {
                    int width = x - start;
                    // 助战（临时干员）卡是青色边框，亮度不到 .4，按边框颜色也算延续（v1.2.4）。
                    if (start >= 2 && width is >= 100 and <= 145 &&
                        new[] { .4, .9, 1.5 }.All(d => Enumerable.Range(start - 1, 4).Any(xx =>
                            image.GetPixel(xx, y + (int)(width * d)).GetBrightness() > .4 || Border(xx, y + (int)(width * d)))))
                        edges.Add((start, y));
                    start = -1;
                }
            }
        }
        var rows = new List<List<(int X, int Y)>>();
        foreach (var edge in edges)
        {
            if (rows.Count == 0 || edge.Y - rows[^1][^1].Y > 5) rows.Add([]);
            rows[^1].Add(edge);
        }
        // 一排里助战卡、角标多时可能只认出一两列，另一排借它的列位置；
        // 小图标、白色立绘也可能被当成一排：只取上下间距像两排卡的组合，
        // 先比两排对得上的列数（真卡上下同列，立绘白块对不上），再比列数（v1.2.5）。
        static List<int> Columns(IEnumerable<(int X, int Y)> points) => points.Select(p => p.X).Order()
            .Aggregate(new List<int>(), (xs, x) => { if (xs.Count == 0 || x - xs[^1] > 5) xs.Add(x); return xs; });
        var pair = (from upper in rows
                    from lower in rows
                    let gap = (lower[0].Y - upper[0].Y) / 351.0
                    where gap is >= .85 and <= 1.3
                    let top = Columns(upper)
                    let bottom = Columns(lower)
                    orderby top.Count(x => bottom.Any(b => Math.Abs(b - x) <= 5)) descending, top.Count + bottom.Count descending
                    select new[] { upper, lower }).FirstOrDefault();
        if (pair is null || pair.SelectMany(r => r).Select(p => p.X / 5).Distinct().Count() < 3) yield break;
        double scale = (pair[1][0].Y - pair[0][0].Y) / 351.0;
        // 两排共用列位置，借另一排补上助战角标遮住的顶边。
        var columns = Columns(pair.SelectMany(r => r));
        foreach (int column in columns)
        foreach (var row in pair)
            if (column >= 60 * scale && column + 205 * scale < image.Width)
                yield return (column, row[0].Y + 1, scale);
    }
    private static bool NoModule(Bitmap im, int x, int y)
    {
        int diagonal = 0, other = 0, bright = 0;
        for (int dy = 4; dy < 36; dy++) for (int dx = 4; dx < 36; dx++)
            if (im.GetPixel(x + dx, y + dy).GetBrightness() > .28) bright++;
        if (bright < 5) return true;
        for (int i = 5; i < 35; i++)
        {
            if (im.GetPixel(x + i, y + 39 - i).GetBrightness() > .28) diagonal++;
            if (im.GetPixel(x + i, y + i).GetBrightness() > .28) other++;
        }
        if (diagonal > 20 && other < 8) return true;
        // 空方框四边连续，内部没有模组图案；按亮像素包围盒适应卡片定位的少量偏移。
        int left = 40, top = 40, right = 0, bottom = 0;
        for (int dy = 4; dy < 38; dy++) for (int dx = 4; dx < 38; dx++)
            if (im.GetPixel(x + dx, y + dy).GetBrightness() > .28)
            { left = Math.Min(left, dx); top = Math.Min(top, dy); right = Math.Max(right, dx); bottom = Math.Max(bottom, dy); }
        if (right - left is < 24 or > 32 || bottom - top is < 24 or > 32 || Math.Abs(right - left - bottom + top) > 3) return false;
        int edge = 0, total = 0, inside = 0;
        for (int dy = top + 1; dy < bottom; dy++) for (int dx = left + 1; dx < right; dx++)
        {
            bool lit = im.GetPixel(x + dx, y + dy).GetBrightness() > .28;
            if (dx == left + 1 || dx == right - 1 || dy == top + 1 || dy == bottom - 1)
            { total++; if (lit) edge++; }
            if (dx >= left + 4 && dx <= right - 4 && dy >= top + 4 && dy <= bottom - 4 && lit) inside++;
        }
        return edge >= total * .9 && inside < 5;
    }
    // 24×24 特征对应卡面 (3,30,106,110)。两边留出重采样余量，顶部两角排除职业和自选角标。
    // 模板与样本先取相同像素再归一化，遮罩区域不参与均值、长度和相关性。
    private static double[] SkinPixels(double[] values) => values.Where((_, i) =>
        i % 24 is > 0 and < 23 && (i / 24 >= 4 || i % 24 is >= 7 and < 16)).ToArray();
    private static double[] GlyphVector(Bitmap image, Rectangle box)
    {
        int xmin = box.Right, ymin = box.Bottom, xmax = box.Left, ymax = box.Top;
        for (int y = box.Top; y < box.Bottom; y++) for (int x = box.Left; x < box.Right; x++)
            if (image.GetPixel(x, y).GetBrightness() > .32)
            { xmin = Math.Min(xmin, x); ymin = Math.Min(ymin, y); xmax = Math.Max(xmax, x); ymax = Math.Max(ymax, y); }
        return xmin > xmax ? new double[576] : Vector(image, Rectangle.FromLTRB(xmin, ymin, xmax + 1, ymax + 1));
    }
    public static double[] Vector(Bitmap image, Rectangle box)
    {
        using var small = new Bitmap(24, 24);
        using (var g = Graphics.FromImage(small))
        using (var attributes = new ImageAttributes())
        {
            // 大幅缩小时先滤波，避免细线和纹理在采样点混叠；镜像延拓避免图像边缘发黑。
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(image, new Rectangle(0, 0, 24, 24), box.X, box.Y, box.Width, box.Height, GraphicsUnit.Pixel, attributes);
        }
        var values = new double[576];
        for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
        { var c = small.GetPixel(x, y); values[y * 24 + x] = .2126 * c.R + .7152 * c.G + .0722 * c.B; }
        return values;
    }
    private static double[] Normalize(double[] values)
    {
        double mean = values.Average(); var result = values.Select(v => v - mean).ToArray();
        double length = Math.Sqrt(result.Sum(v => v * v));
        return length == 0 ? result : result.Select(v => v / length).ToArray();
    }
    public static bool SamePosition(byte[] first, byte[] second)
    {
        using var sa = new MemoryStream(first); using var sb = new MemoryStream(second);
        using var a = new Bitmap(sa); using var b = new Bitmap(sb);
        var av = Vector(a, new(a.Width / 10, a.Height / 5, a.Width * 8 / 10, a.Height * 3 / 5));
        var bv = Vector(b, new(b.Width / 10, b.Height / 5, b.Width * 8 / 10, b.Height * 3 / 5));
        return av.Zip(bv, (x, y) => Math.Abs(x - y)).Average() < 1.5;
    }
}
