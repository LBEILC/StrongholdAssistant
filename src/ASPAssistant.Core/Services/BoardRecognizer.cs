// 颜色采样、距离截断与裁剪均值改编自 bingyunhe/asp-assistant-arknights BoardRecognizer.cs（Apache-2.0）。
// 2026-11-13：支持现有 PNG 截图；按画面拟合镜头的缩放与偏移，输出关卡地图 id 和投影。
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record BoardRecognition(BoardMatch? Match, string MapId, BoardCamera Camera,
    double Score, double? Gap, string? Rejection, int Width, int Height);

public sealed class BoardRecognizer
{
    private readonly Dictionary<BoardCamera, ((double, double) Grid, (double, double) Screen)[]> _corners;
    private readonly Dictionary<string, Dictionary<BoardCamera, byte[][]>> _references;

    public BoardRecognizer(string directory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "garrison_board_refs.json")));
        static BoardCamera Camera(string s) => s == "expanded" ? BoardCamera.Expanded : BoardCamera.Collapsed;
        _corners = doc.RootElement.GetProperty("cameras").EnumerateObject().ToDictionary(p => Camera(p.Name),
            p => p.Value.EnumerateArray().Select(v =>
                ((v[0][0].GetDouble(), v[0][1].GetDouble()), (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray());
        _references = doc.RootElement.GetProperty("maps").EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.EnumerateObject().ToDictionary(c => Camera(c.Name), c => c.Value.EnumerateArray()
                .Select(v => v.EnumerateArray().Select(n => n.GetByte()).ToArray()).ToArray()));
    }

    public BoardMatch? Recognize(byte[] image) => Recognize(image, null, null);

    // 已知地图后只拟合目标镜头，舍弃偏差最大的三成采样，避免覆盖线拉偏投影。
    public BoardMatch? Recognize(byte[] image, string? mapId, BoardCamera? targetCamera)
        => RecognizeDetailed(image, mapId, targetCamera).Match;

    public BoardRecognition RecognizeDetailed(byte[] image, string? mapId = null, BoardCamera? targetCamera = null)
    {
        using var stream = new MemoryStream(image);
        using var source = new Bitmap(stream);
        // 与上游 400 像素宽的颜色样本保持相同采样面积，避免分辨率影响边界颜色。
        using var bitmap = new Bitmap(400, (int)Math.Round(source.Height * 400.0 / source.Width), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap)) g.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
        var pixels = new Pixels(bitmap);
        var candidates = new List<Fit>();
        foreach (var camera in _corners.Keys.Where(c => targetCamera is null || c == targetCamera))
        {
            Fit? best = null;
            // 以宽度保持 3D 场景比例；其余偏移与尺度直接从棋盘颜色样本拟合。
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
            void Evaluate(double scale, double dx, double dy)
            {
                var projection = new BoardProjection(_corners[camera].Select(p => (p.Grid,
                    ((p.Screen.Item1 - .5) * scale + .5 + dx,
                     (p.Screen.Item2 - .5) * scale * 225 / pixels.Height + .5 + dy))).ToArray());
                var samples = Sample(pixels, projection);
                foreach (var (map, refs) in _references.Where(p => mapId is null || p.Key == mapId))
                {
                    // m01 上游只有展开镜头样本；两镜头采同一格，沿用上游的跨镜头颜色复用。
                    var reference = refs.GetValueOrDefault(camera) ?? refs.Values.First();
                    var (full, trimmed) = Distance(samples, reference);
                    var fit = new Fit(map, camera, projection, full, trimmed, scale, dx, dy);
                    candidates.Add(fit);
                    if (best is null || Rank(fit) < Rank(best)) best = fit;
                }
            }
        }
        double Rank(Fit fit) => mapId is null ? fit.Full : fit.Trimmed;
        var winner = candidates.MinBy(Rank)!;
        double? gap = mapId is null ? candidates.Where(f => f.Map != winner.Map).Min(f => f.Full) - winner.Full : null;
        string? rejection = winner.Trimmed > 11 ? "score>11" : gap < 5 ? "gap<5" : null;
        BoardMatch? match = rejection is null ? new(winner.Map, winner.Camera, winner.Projection, winner.Trimmed) : null;
        return new(match, winner.Map, winner.Camera, winner.Trimmed, gap, rejection, source.Width, source.Height);
    }

    private sealed record Fit(string Map, BoardCamera Camera, BoardProjection Projection,
        double Full, double Trimmed, double Scale, double X, double Y);

    private static byte[][] Sample(Pixels px, BoardProjection projection)
    {
        var samples = new List<byte[]>();
        for (int r = 0; r <= 4; r++)
            for (int c = -1; c <= 7; c++)
                foreach (var (sx, sy) in new[] { (.32, .72), (.72, .32) })
                {
                    var (x, y) = projection.Project(c + sx, r + sy);
                    var (x2, _) = projection.Project(c + sx + .1, r + sy);
                    samples.Add(px.Mean((int)(x * px.Width), (int)(y * px.Height), Math.Max(2, (int)(Math.Abs(x2 - x) * px.Width))));
                }
        return samples.ToArray();
    }

    private static (double Full, double Trimmed) Distance(byte[][] a, byte[][] b)
    {
        var distances = a.Select((p, i) => Math.Min(45, Math.Sqrt(
            Math.Pow(p[0] - b[i][0], 2) + Math.Pow(p[1] - b[i][1], 2) + Math.Pow(p[2] - b[i][2], 2)))).ToArray();
        double full = distances.Average();
        Array.Sort(distances);
        return (full, distances.Take((int)(distances.Length * .7)).Average());
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
