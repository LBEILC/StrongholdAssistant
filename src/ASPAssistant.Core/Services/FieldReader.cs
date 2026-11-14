using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record FieldBadge(double X, double Y, int Tier, double Score);

/// <summary>角标决定占用；名字只读拖动详情。模板匹配不使用小人身份特征。</summary>
public sealed class FieldReader
{
    private readonly IOcrEngine _ocr;
    private readonly string[] _names;
    private readonly List<(int Tier, int Size, double[] Values, double[] Fine)> _templates = [];
    private readonly BoardRecognizer _board;
    private readonly Dictionary<(int, int, bool), BoardMatch> _boards = new();
    private readonly Dictionary<bool, BoardProjection> _projections;
    private int? _round;
    private Dictionary<int, ShopItem> _shopCards = new();
    private (int, int, bool)? _frameKey;
    public GameShopTheme Theme { get; set; }

    public FieldReader(string directory, IOcrEngine ocr, IEnumerable<string> names)
    {
        _ocr = ocr; _names = names.OrderByDescending(n => n.Length).ToArray();
        _board = new(directory);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "garrison_board_refs.json")));
        _projections = doc.RootElement.GetProperty("cameras").EnumerateObject().ToDictionary(p => p.Name == "expanded",
            p => new BoardProjection(p.Value.EnumerateArray().Select(v =>
                ((v[0][0].GetDouble(), v[0][1].GetDouble()), (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray()));
        for (int tier = 1; tier <= 6; tier++)
        {
            using var image = new Bitmap(Path.Combine(directory, "field", $"tier-{tier}.png"));
            foreach (int size in new[] { 34, 40, 44 })
            {
                using var scaled = new Bitmap(size, size);
                using (var g = Graphics.FromImage(scaled)) { g.Clear(Color.Black); g.DrawImage(image, 0, 0, size, size); }
                var px = new Pixels(scaled);
                var values = Sample(px, 0, 0, size);
                Normalize(values);
                var fine = Sample(px, 0, 0, size, 20); Normalize(fine);
                _templates.Add((tier, size, values, fine));
            }
        }
    }

    public void Reset() { _boards.Clear(); _round = null; _shopCards.Clear(); }

    // 就绪控件可能露在外部窗口旁；棋盘中大面积连续的中性纯色不是地形纹理。
    internal static (bool Obscured, bool Detail, bool ConfirmationCandidate) InspectSurface(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var bitmap = new Bitmap(stream);
        var low = new int[75, 150]; var high = new int[75, 150];
        for (int row = 0; row < 75; row++) for (int col = 0; col < 150; col++)
        {
            var c = bitmap.GetPixel((400 + col * 8) * bitmap.Width / 1920, (200 + row * 8) * bitmap.Height / 1080);
            low[row, col] = Math.Min(c.R, Math.Min(c.G, c.B));
            high[row, col] = Math.Max(c.R, Math.Max(c.G, c.B));
        }
        int flat = 0;
        for (int row = 2; row < 73; row++) for (int col = 2; col < 148; col++)
        {
            int min = 255, max = 0;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
            {
                min = Math.Min(min, low[row + dy, col + dx]);
                max = Math.Max(max, high[row + dy, col + dx]);
            }
            if (min > 15 && max - min < 12) flat++;
        }
        int red = 0;
        for (int y = 680; y < 740; y += 8) for (int x = 1000; x < 1600; x += 8)
        {
            var c = bitmap.GetPixel(x * bitmap.Width / 1920, y * bitmap.Height / 1080);
            if (c.R > 60 && c.R > c.G * 1.2 && c.R > c.B * 1.2) red++;
        }
        // 就绪确认弹窗的横向红色确认区也会遮暗角标，露出的就绪文字不能代表无弹窗。
        return (flat > 71 * 146 * .025,
            HasDetailPanel(bitmap, Math.Min(bitmap.Width / 1920d, bitmap.Height / 1080d)), red > 600 * .75);
    }

    public async Task<FieldObservation> ReadAsync(byte[] frame, DateTimeOffset at, EconomyReading? economy,
        bool mainScreen, bool shopVisible, IReadOnlyList<ShopItem> shop, RectangleF? cursorRegion = null)
    {
        if (economy is not { IsRest: true }) return new(at, false, null, economy?.Round, new Dictionary<FieldCell, int>());
        if (_round != economy.Round) { _boards.Clear(); _round = economy.Round; }
        using var stream = new MemoryStream(frame);
        using var bitmap = new Bitmap(stream);
        double scale = Math.Min(bitmap.Width / 1920d, bitmap.Height / 1080d);
        bool panel = HasDetailPanel(bitmap, scale);
        bool dragging = panel && HasDragPreview(bitmap, scale);
        string? drag = null;
        if (dragging)
        {
            var texts = await _ocr.RecognizeRegionAsync(frame, 0, (int)(285 * scale), (int)(230 * scale), (int)(65 * scale));
            string text = string.Join("", texts.Select(t => t.Text.Replace(" ", "")));
            drag = _names.FirstOrDefault(n => text.Contains(n, StringComparison.Ordinal));
        }
        if (dragging || !mainScreen)
            return new(at, true, null, economy.Round, new Dictionary<FieldCell, int>(), Money: economy.Money, DragOperator: drag, MainScreen: false);
        var key = (bitmap.Width, bitmap.Height, shopVisible);
        if (_frameKey != key) { _boards.Clear(); _frameKey = key; }
        if (!_boards.TryGetValue(key, out var board))
        {
            // 空卡较多时商店长边会漏检；镜头由棋盘本身判定，不能直接把“没检测到卡”当收店。
            board = _board.Recognize(frame);
            if (board is not null) _boards[key] = board;
        }
        var hidden = shop.Select(i => new RectangleF((float)i.CardRegion.X, (float)i.CardRegion.Y,
            (float)i.CardRegion.Width, (float)i.CardRegion.Height)).ToList();
        if (panel) hidden.Add(new(0, .17f, .33f, .75f));
        if (cursorRegion is { } cursor) hidden.Add(cursor);
        var cells = ReadCells(bitmap, board, shopVisible, hidden);
        Dictionary<int, string>? cards = null;
        if (shopVisible)
        {
            var current = shop.GroupBy(i => i.SlotIndex).ToDictionary(g => g.Key, g => g.First());
            foreach (var (slot, old) in _shopCards)
                if (!current.ContainsKey(slot) && CardVisible(bitmap, old, Theme)) current[slot] = old;
            _shopCards = current;
            cards = current.Where(p => _names.Contains(p.Value.Name)).ToDictionary(p => p.Key, p => p.Value.Name);
        }
        else _shopCards.Clear();
        return new(at, true, board?.MapId, economy.Round, cells,
            cards, economy.Money);
    }

    private static bool HasDetailPanel(Bitmap bitmap, double scale)
    {
        int step = Math.Max(1, (int)(4 * scale));
        for (int y = (int)(440 * scale); y < 640 * scale; y += step)
        {
            int blue = 0;
            for (int x = 0; x < 632 * scale; x += step)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.B > 120 && c.G > 65 && c.R < 90) blue++;
            }
            // 生命条横贯面板；城区的蓝色装饰仅占局部，不能累加整片区域当生命条。
            if (blue * step > 360 * scale) return true;
        }
        return false;
    }

    private static bool HasDragPreview(Bitmap bitmap, double scale)
    {
        int green = 0, step = Math.Max(1, (int)(8 * scale));
        for (int y = (int)(240 * scale); y < 840 * scale; y += step)
            for (int x = (int)(600 * scale); x < 1760 * scale; x += step)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.G > 150 && c.G > c.R * 1.35 && c.G > c.B * 1.3) green++;
            }
        return green > 112;
    }

    private static bool CardVisible(Bitmap bitmap, ShopItem item, GameShopTheme theme)
    {
        var r = item.CardRegion;
        int delta = Math.Max(2, bitmap.Width / 640);
        bool Edge(double x)
        {
            int cx = (int)Math.Round(x * bitmap.Width), hits = 0;
            if (cx - delta < 0 || cx + delta >= bitmap.Width) return false;
            for (int n = 0; n < 20; n++)
            {
                int y = Math.Clamp((int)((r.Y + r.Height * (.2 + n * .025)) * bitmap.Height), 0, bitmap.Height - 1);
                var a = bitmap.GetPixel(cx - delta, y); var b = bitmap.GetPixel(cx + delta, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 24) hits++;
            }
            return hits >= 14;
        }
        // 名字漏读不等于买走；可露希尔和乌萨斯沿用卡片长边判断。
        if (theme != GameShopTheme.Terminal) return Edge(r.X) && Edge(r.X + r.Width);
        // 终端的空槽仍有外框；确认购买会遮住名字，但卡面仍亮于 NO INFO 空槽。
        int bright = 0;
        for (int row = 0; row < 10; row++) for (int col = 0; col < 10; col++)
        {
            int x = Math.Clamp((int)((r.X + r.Width * (.1 + col * .08)) * bitmap.Width), 0, bitmap.Width - 1);
            int y = Math.Clamp((int)((r.Y + r.Height * (.15 + row * .055)) * bitmap.Height), 0, bitmap.Height - 1);
            var c = bitmap.GetPixel(x, y);
            if ((c.R * 77 + c.G * 150 + c.B * 29) / 256 > 70) bright++;
        }
        return bright >= 20;
    }

    public Dictionary<FieldCell, int> ReadCells(Bitmap bitmap, BoardMatch? board, bool shopVisible,
        IReadOnlyList<RectangleF>? hidden = null)
    {
        var badges = ReadBadges(bitmap);
        var cells = new Dictionary<FieldCell, int>();
        bool expanded = board?.Camera == BoardCamera.Expanded || board is null && shopVisible;
        // 就绪主画面的镜头可确定格坐标；地图颜色不明确时 mapId 仍保持未知。
        var projection = board?.Projection ?? _projections[expanded];
        var anchors = new Dictionary<FieldCell, (double X, double Y)>();
        double width = bitmap.Width, height = bitmap.Height;
        for (int row = 0; row <= 4; row++)
            for (int col = 0; col <= 6; col++)
            {
                var p = projection.Project(col + .5, row + .5);
                var p2 = projection.Project(col + 1.5, row + .5);
                double tile = (p2.X - p.X) * width;
                anchors[new("board", col, row)] = (p.X * width - tile * .35, p.Y * height - 112 * width / 1920);
            }
        // 整备区与棋盘共享镜头；用投影中的基准点校正整条木板的位置。
        var origin = projection.Project(0, 0);
        double baseX = expanded ? .346875 : .35;
        double baseY = expanded ? .222222222 : .283333333;
        double dx = (origin.X - baseX) * width, dy = (origin.Y - baseY) * height;
        for (int col = 1; col <= 10; col++)
            anchors[new("bench", col, 0)] = ((expanded ? 239 + (col - 1) * 122.8 : 170 + (col - 1) * 149.5) * width / 1920 + dx,
                (expanded ? 609 : 798) * height / 1080 + dy);
        foreach (var (cell, p) in anchors)
        {
            // 商店和屏幕外格子没有观测，保留追踪器中的旧值。
            if (p.X < 20 || p.X > width - 55 || p.Y < 180 * height / 1080 || p.Y + 55 > height * (shopVisible ? .72 : .94)) continue;
            cells[cell] = 0;
        }
        foreach (var badge in badges)
        {
            var near = anchors.MinBy(p => Math.Pow(p.Value.X - badge.X, 2) + Math.Pow(p.Value.Y - badge.Y, 2));
            if (Math.Abs(near.Value.X - badge.X) < width * .035 && Math.Abs(near.Value.Y - badge.Y) < height * .045)
                cells[near.Key] = badge.Tier;
        }
        foreach (var (cell, p) in anchors)
        {
            var area = new RectangleF((float)((p.X - 22) / width), (float)((p.Y - 22) / height),
                (float)(90 / width), (float)(155 / height));
            if (hidden?.Any(r => r.IntersectsWith(area)) == true) cells.Remove(cell);
        }
        return cells;
    }

    public IReadOnlyList<FieldBadge> ReadBadges(Bitmap bitmap)
    {
        var px = new Pixels(bitmap);
        var found = new List<FieldBadge>();
        foreach (int size in new[] { 34, 40, 44 })
        {
            var templates = _templates.Where(t => t.Size == size).ToArray();
            int step = size == 40 ? 2 : 3;
            for (int y = 70; y < bitmap.Height * .82 - size; y += step)
                for (int x = bitmap.Width / 10; x < bitmap.Width * .90 - size; x += step)
                {
                    // 角标外框比内部底色亮，先用左右两边排除绝大多数地板像素。
                    int mid = size / 2;
                    if (px.Gray(x + 2, y + mid) < px.Gray(x + 7, y + mid) + 18
                        || px.Gray(x + size - 3, y + mid) < px.Gray(x + size - 8, y + mid) + 18) continue;
                    var values = Sample(px, x, y, size);
                    Normalize(values);
                    double coarse = -1;
                    foreach (var t in templates) coarse = Math.Max(coarse, Dot(t.Values, values));
                    if (coarse < .60) continue;
                    for (int yy = y - 1; yy <= y + 1; yy++)
                        for (int xx = x - 1; xx <= x + 1; xx++)
                        {
                            var fine = Sample(px, xx, yy, size, 20); Normalize(fine);
                            double score = -1; int tier = 0;
                            foreach (var t in templates)
                            {
                                double value = Dot(t.Fine, fine);
                                if (value > score) { score = value; tier = t.Tier; }
                            }
                            if (score >= .75) found.Add(new(xx + mid, yy + mid, tier, score));
                        }
                }
        }
        var result = new List<FieldBadge>();
        foreach (var b in found.OrderByDescending(b => b.Score))
        {
            if (result.Any(p => Math.Abs(p.X - b.X) < 25 && Math.Abs(p.Y - b.Y) < 25)) continue;
            // 装备悬浮在格子底部，角标右下方的大段空间仍是地板；干员在此处有头部/身体边缘。
            int edges = 0;
            for (int y = (int)b.Y + 55; y < Math.Min(bitmap.Height - 2, b.Y + 75); y += 2)
                for (int x = (int)b.X + 18; x < Math.Min(bitmap.Width - 2, b.X + 65); x += 2)
                    if (Math.Abs(px.Gray(x, y) - px.Gray(x + 2, y)) > 32) edges++;
            if (edges >= 22) result.Add(b);
        }
        return result;
    }

    private static double[] Sample(Pixels p, int x, int y, int size, int count = 10)
    {
        var values = new double[count * count];
        for (int r = 0; r < count; r++) for (int c = 0; c < count; c++)
            values[r * count + c] = p.Gray(x + (int)((c + .5) * size / count), y + (int)((r + .5) * size / count));
        return values;
    }
    private static void Normalize(double[] a)
    {
        double mean = 0, sum = 0;
        for (int i = 0; i < a.Length; i++) mean += a[i];
        mean /= a.Length;
        for (int i = 0; i < a.Length; i++) { a[i] -= mean; sum += a[i] * a[i]; }
        double norm = Math.Sqrt(sum);
        if (norm > 0) for (int i = 0; i < a.Length; i++) a[i] /= norm;
    }
    private static double Dot(double[] a, double[] b)
    { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    private sealed class Pixels
    {
        private readonly byte[] _bytes;
        private readonly int _stride;
        public Pixels(Bitmap source)
        {
            using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bitmap)) g.DrawImageUnscaled(source, 0, 0);
            var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            _stride = data.Stride; _bytes = new byte[_stride * bitmap.Height];
            Marshal.Copy(data.Scan0, _bytes, 0, _bytes.Length); bitmap.UnlockBits(data);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int Gray(int x, int y)
        { int i = y * _stride + x * 3; return (_bytes[i] * 29 + _bytes[i + 1] * 150 + _bytes[i + 2] * 77) >> 8; }
    }
}
