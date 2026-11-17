// 颜色采样、距离截断与裁剪均值改编自 bingyunhe/asp-assistant-arknights BoardRecognizer.cs（Apache-2.0）。
// 2026-11-17：宽高屏共用场上画幅转换，投影还原到原截图；采样明确使用像素单位。
// 2026-11-17：普通回合允许有界的整体偏色，保留原始地图区分度和投影拟合。
// 2026-11-15：首领回合使用本局地图，比较两侧镜头并排除地形变化格。
// 2026-11-13：支持现有 PNG 截图；按画面拟合镜头的缩放与偏移，输出关卡地图 id 和投影。
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record BoardRecognition(BoardMatch? Match, string MapId, BoardCamera Camera,
    double Score, double? Gap, string? Rejection, int Width, int Height, double? LeftScore = null, double? RightScore = null);

public sealed class BoardRecognizer
{
    private readonly Dictionary<BoardCamera, ((double, double) Grid, (double, double) Screen)[]> _corners;
    private readonly Dictionary<string, Dictionary<BoardCamera, byte[][]>> _references;

    private readonly Dictionary<BoardCamera, ((double, double) Grid, (double, double) Screen)[]> _bossCorners;
    private readonly Dictionary<string, Dictionary<string, HashSet<(int Col, int Row)>>> _bossExcluded;

    public BoardRecognizer(string directory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "garrison_board_refs.json")));
        static BoardCamera Camera(string s) => s == "expanded" ? BoardCamera.Expanded : BoardCamera.Collapsed;
        _corners = doc.RootElement.GetProperty("cameras").EnumerateObject().ToDictionary(p => Camera(p.Name),
            p => p.Value.EnumerateArray().Select(v =>
                ((v[0][0].GetDouble(), v[0][1].GetDouble()), (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray());
        _bossCorners = doc.RootElement.GetProperty("bossCameras").EnumerateObject().ToDictionary(p => Camera(p.Name),
            p => p.Value.EnumerateArray().Select(v =>
                ((v[0][0].GetDouble(), v[0][1].GetDouble()), (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray());
        _bossExcluded = doc.RootElement.GetProperty("bossTerrain").EnumerateObject().ToDictionary(p => p.Name,
            p => new[] { "left", "right" }.ToDictionary(side => side, side => ChangedCells(p.Value, side)));
        _references = doc.RootElement.GetProperty("maps").EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.EnumerateObject().ToDictionary(c => Camera(c.Name), c => c.Value.EnumerateArray()
                .Select(v => v.EnumerateArray().Select(n => n.GetByte()).ToArray()).ToArray()));
    }

    public BoardMatch? Recognize(byte[] image) => Recognize(image, null, null);

    // 已知地图后只拟合目标镜头，舍弃偏差最大的三成采样，避免覆盖线拉偏投影。
    public BoardMatch? Recognize(byte[] image, string? mapId, BoardCamera? targetCamera)
        => RecognizeDetailed(image, mapId, targetCamera).Match;

    public BoardRecognition RecognizeDetailed(byte[] image, string? mapId = null, BoardCamera? targetCamera = null, bool bossRound = false, string? playerSide = null)
    {
        using var stream = new MemoryStream(image);
        using var original = new Bitmap(stream);
        var (boardFrame, viewport) = FieldReader.NormalizeBoardViewport(image, original.Width, original.Height);
        using var boardStream = new MemoryStream(boardFrame);
        using var source = new Bitmap(boardStream);
        // 与上游 400 像素宽的颜色样本保持相同采样面积，避免分辨率影响边界颜色。
        using var bitmap = new Bitmap(400, (int)Math.Round(source.Height * 400.0 / source.Width), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap)) g.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        var pixels = new Pixels(bitmap);
        bool boss = bossRound && mapId is not null;
        var candidates = new List<Fit>();
        foreach (var side in boss ? (playerSide is null ? new string?[] { "left", "right" } : [playerSide]) : [null])
        foreach (var camera in _corners.Keys.Where(c => targetCamera is null || c == targetCamera))
        {
            var corners = side == "right" ? _bossCorners[camera] : _corners[camera];
            var excluded = boss ? _bossExcluded[mapId!][side!] : null;
            Fit? best = null;
            // 以宽度保持 3D 场景比例；其余偏移与尺度直接从棋盘颜色样本拟合。
            if (boss)
            {
                // 首领镜头保留基础标定尺度，只在普通细调步长内平移。
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        Evaluate(1, x * .005 / 3, y * .005 / 3);
            }
            else
            {
                for (int s = -1; s <= 2; s++)
                    for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                            Evaluate(1 + s * .08, x * .025, y * .03);
                foreach (var step in new[] { .02, .005 })
                {
                    var center = best!;
                    for (int s = -1; s <= 1; s++)
                        for (int x = -1; x <= 1; x++)
                            for (int y = -1; y <= 1; y++)
                                Evaluate(center.Scale + s * step, center.X + x * step / 3, center.Y + y * step / 3);
                }
            }
            void Evaluate(double scale, double dx, double dy)
            {
                var projection = new BoardProjection(corners.Select(p => (p.Grid,
                    ((p.Screen.Item1 - .5) * scale + .5 + dx,
                     (p.Screen.Item2 - .5) * scale * 225 / pixels.Height + .5 + dy))).ToArray());
                var samples = Sample(pixels, projection, excluded);
                foreach (var (map, refs) in _references.Where(p => mapId is null || p.Key == mapId))
                {
                    // m01 上游只有展开镜头样本；两镜头采同一格，沿用上游的跨镜头颜色复用。
                    var reference = refs.GetValueOrDefault(camera) ?? refs.Values.First();
                    var (full, trimmed) = Distance(samples, reference, excluded);
                    var fit = new Fit(map, camera, projection, full, trimmed, scale, dx, dy, side);
                    candidates.Add(fit);
                    if (best is null || Rank(fit) < Rank(best)) best = fit;
                }
            }
        }
        double Rank(Fit fit) => mapId is null ? fit.Full : fit.Trimmed;
        var winner = candidates.MinBy(Rank)!;
        double? gap = mapId is null ? candidates.Where(f => f.Map != winner.Map).Min(f => f.Full) - winner.Full : null;
        bool colorMatch = winner.Trimmed <= (boss ? 40 : 11);
        if (!boss && !colorMatch && !(gap < 5))
        {
            var references = _references[winner.Map];
            var reference = references.GetValueOrDefault(winner.Camera) ?? references.Values.First();
            colorMatch = MatchesWithColorOffset(Sample(pixels, winner.Projection, null), reference);
        }
        string? rejection = !colorMatch ? (boss ? "score>40" : "score>11") : gap < 5 ? "gap<5" : null;
        var projection = winner.Projection;
        if (viewport.Width != 1 || viewport.Height != 1)
            projection = new BoardProjection(new[] { (0d, 0d), (7d, 0d), (7d, 5d), (0d, 5d) }.Select(p =>
            {
                var point = winner.Projection.Project(p.Item1, p.Item2);
                return (p, (viewport.X + point.X * viewport.Width, viewport.Y + point.Y * viewport.Height));
            }).ToArray());
        BoardMatch? match = rejection is null ? new(winner.Map, winner.Camera, projection, winner.Trimmed, winner.Side) : null;
        return new(match, winner.Map, winner.Camera, winner.Trimmed, gap, rejection, original.Width, original.Height,
            boss ? candidates.Where(f => f.Side == "left").Min(f => (double?)f.Trimmed) : null,
            boss ? candidates.Where(f => f.Side == "right").Min(f => (double?)f.Trimmed) : null);
    }

    private sealed record Fit(string Map, BoardCamera Camera, BoardProjection Projection,
        double Full, double Trimmed, double Scale, double X, double Y, string? Side);

    private static byte[][] Sample(Pixels px, BoardProjection projection, HashSet<(int Col, int Row)>? excluded)
    {
        var samples = new List<byte[]>();
        for (int r = 0; r <= 4; r++)
            for (int c = -1; c <= 7; c++)
                foreach (var (sx, sy) in new[] { (.32, .72), (.72, .32) })
                {
                    if (excluded?.Contains((c, r)) == true) { samples.Add([]); continue; }
                    var (x, y) = projection.Project(c + sx, r + sy);
                    var (x2, _) = projection.Project(c + sx + .1, r + sy);
                    samples.Add(px.Mean((int)(x * px.Width), (int)(y * px.Height), Math.Max(2, (int)(Math.Abs(x2 - x) * px.Width))));
                }
        return samples.ToArray();
    }

    private static (double Full, double Trimmed) Distance(byte[][] a, byte[][] b, HashSet<(int Col, int Row)>? excluded)
    {
        var distances = a.Select((p, i) => (p, i))
            .Where(v => excluded?.Contains((v.i / 2 % 9 - 1, v.i / 18)) != true)
            .Select(v => { var (p, i) = v; return Math.Min(45, Math.Sqrt(
            Math.Pow(p[0] - b[i][0], 2) + Math.Pow(p[1] - b[i][1], 2) + Math.Pow(p[2] - b[i][2], 2))); }).ToArray();
        double full = distances.Average();
        Array.Sort(distances);
        return (full, distances.Take((int)(distances.Length * .7)).Average());
    }

    private static bool MatchesWithColorOffset(byte[][] samples, byte[][] reference)
    {
        // 用全部采样的通道中位差估计整体偏色，不用少数被干员或面板覆盖的格子校色。
        var offset = Enumerable.Range(0, 3).Select(channel => samples
            .Select((sample, i) => sample[channel] - reference[i][channel])
            .Order().ElementAt(samples.Length / 2)).ToArray();
        // RGB 向量最多补偿 20，不能把拖人高亮、暗幕等大幅色变校成正常棋盘。
        if (offset.Sum(value => value * value) > 20 * 20) return false;
        var distances = samples.Select((sample, i) => Math.Min(45, Math.Sqrt(
            Enumerable.Range(0, 3).Sum(channel => Math.Pow(
                sample[channel] - reference[i][channel] - offset[channel], 2))))).Order();
        // 只复核颜色；地图排名、gap、投影和对外诊断分数仍用未经校色的结果。
        return distances.Take((int)(samples.Length * .7)).Average() <= 11;
    }

    // 数据来自地图文件；行 -1..4、列 -1..7，位 0 表示可走，位 1 表示有预置物（含隐藏）。
    public static HashSet<(int Col, int Row)> ChangedCells(JsonElement terrain, string side)
    {
        var normal = terrain.GetProperty("normal");
        var boss = terrain.GetProperty(side);
        var changed = new HashSet<(int, int)>();
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 9; c++)
                if (normal[r][c].GetInt32() != boss[r][c].GetInt32()
                    || ((normal[r][c].GetInt32() | boss[r][c].GetInt32()) & 2) != 0)
                    changed.Add((c - 1, r - 1));
        return changed;
    }

    private sealed class Pixels
    {
        private readonly byte[] _bytes;
        private readonly int _stride;
        public int Width { get; }
        public int Height { get; }
        public Pixels(Bitmap bitmap)
        {
            Width = bitmap.Width; Height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            _stride = data.Stride;
            _bytes = new byte[_stride * Height];
            Marshal.Copy(data.Scan0, _bytes, 0, _bytes.Length);
            bitmap.UnlockBits(data);
        }
        public byte[] Mean(int x, int y, int radius)
        {
            int r = 0, g = 0, b = 0, n = 0;
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(Height - 1, y + radius); yy++)
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(Width - 1, x + radius); xx++)
                {
                    int i = yy * _stride + xx * 3;
                    b += _bytes[i]; g += _bytes[i + 1]; r += _bytes[i + 2]; n++;
                }
            return n == 0 ? [0, 0, 0] : [(byte)(r / n), (byte)(g / n), (byte)(b / n)];
        }
    }
}
