using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;

namespace ASPAssistant.Core.Services;

public sealed record FieldBadge(double X, double Y, int Tier, double Score, double Size = 40);
public sealed record FieldOverlayGeometry(BoardProjection Projection,
    IReadOnlyDictionary<FieldCell, (double X, double Y)> Anchors,
    IReadOnlyDictionary<FieldCell, FieldBadge> Badges);

/// <summary>按格读取角标与身体证据；低矮干员通过既有小人模板补证占用。</summary>
public sealed partial class FieldReader : IDisposable
{
    private readonly IOcrEngine _ocr;
    private readonly FieldEquipmentReader _equipment;
    private readonly string[] _names;
    private readonly Lazy<SpriteModelMatcher> _occupancyModel;
    public bool? HasPithStrategy { get; set; }
    private readonly List<(int Tier, int Size, double[] Values, double[] Fine, double[] Lower, double[] LowerFine, double[] Rim)> _templates = [];
    private readonly BoardRecognizer _board;
    private readonly GarrisonBoardData _maps;
    private readonly Dictionary<(int, int, bool), BoardMatch> _boards = new();
    private readonly Dictionary<bool, BoardProjection> _projections;
    private readonly Dictionary<bool, BoardProjection> _bossProjections;
    private int? _round;
    private string? _lastMap;
    private Dictionary<int, ShopItem> _shopCards = new();
    private (int, int, bool)? _frameKey;
    public GameShopTheme Theme { get; set; }
    public FieldOverlayGeometry? OverlayGeometry { get; private set; }

    public FieldReader(string directory, IOcrEngine ocr, IEnumerable<string> names)
    {
        _occupancyModel = new(() => new(Path.Combine(directory, "sprite-model.onnx"), Path.Combine(directory, "sprite-model-classes.json")));
        _summonDefinitions = SummonCatalog.Load(directory);
        _summonMatcher = new(Path.Combine(directory, "summon-templates.bin.gz"));
        _operatorPackage = Path.Combine(directory, "sprite-templates.bin.gz");
        using (var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "operators.json"))))
            _operatorTiers = catalog.RootElement.GetProperty("operators").EnumerateArray()
                .Select(o => (o.GetProperty("name").GetString()!, o.GetProperty("tier").GetInt32())).ToArray();
        _equipment = new(directory, ocr);
        _ocr = ocr; _names = names.OrderByDescending(n => n.Length).ToArray();
        _board = new(directory);
        using var maps = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "garrison_maps.json")));
        _maps = GarrisonBoardStore.Parse(maps.RootElement);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "garrison_board_refs.json")));
        _projections = doc.RootElement.GetProperty("cameras").EnumerateObject().ToDictionary(p => p.Name == "expanded",
            p => new BoardProjection(p.Value.EnumerateArray().Select(v =>
                ((v[0][0].GetDouble(), v[0][1].GetDouble()), (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray()));
        _bossProjections = doc.RootElement.GetProperty("bossCameras").EnumerateObject().ToDictionary(p => p.Name == "expanded",
            p => new BoardProjection(p.Value.EnumerateArray().Select(v =>
                ((v[0][0].GetDouble(), v[0][1].GetDouble()), (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray()));
        for (int tier = 1; tier <= 6; tier++)
        {
            using var image = new Bitmap(Path.Combine(directory, "field", $"tier-{tier}.png"));
            // IV/VI 原图是暗框图标；复用共用亮框，将原字形还原到场上角标比例。
            if (tier is 4 or 6)
            {
                using var glyph = (Bitmap)image.Clone();
                using var border = new Bitmap(Path.Combine(directory, "field", "tier-3.png"));
                using var g = Graphics.FromImage(image);
                g.DrawImage(border, new Rectangle(0, 0, border.Width, border.Height), new Rectangle(0, 0, border.Width, border.Height), GraphicsUnit.Pixel);
                g.DrawImage(glyph, new Rectangle(5, 5, 30, 30), new Rectangle(8, 8, 24, 24), GraphicsUnit.Pixel);
            }
            foreach (int size in new[] { 34, 40, 44 })
            {
                using var scaled = new Bitmap(size, size);
                using (var g = Graphics.FromImage(scaled)) { g.Clear(Color.Black); g.DrawImage(image, new Rectangle(0, 0, size, size), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel); }
                var px = new Pixels(scaled);
                var values = Sample(px, 0, 0, size);
                Normalize(values);
                var fine = Sample(px, 0, 0, size, 20); Normalize(fine);
                var lower = Sample(px, 0, 0, size, 10, 5); Normalize(lower);
                var lowerFine = Sample(px, 0, 0, size, 20, 10); Normalize(lowerFine);
                var rim = lowerFine[100..]; Normalize(rim);
                _templates.Add((tier, size, values, fine, lower, lowerFine, rim));
            }
        }
    }

    public void Dispose()
    {
        if (_occupancyModel.IsValueCreated) _occupancyModel.Value.Dispose();
        _summonMatcher?.Dispose();
        _summonOperatorMatcher?.Dispose();
    }
    public void Reset() { _boards.Clear(); _round = null; _lastMap = null; KnownMap = null; _shopCards.Clear(); Summons = []; }

    // 就绪控件可能露在外部窗口旁；棋盘中大面积连续的中性纯色不是地形纹理。
    internal static (bool Obscured, bool Detail, bool ConfirmationCandidate) InspectSurface(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var bitmap = new Bitmap(stream);
        return InspectSurface(bitmap);
    }

    internal static (bool Obscured, bool Detail, bool ConfirmationCandidate) InspectSurface(Bitmap bitmap)
    {
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
        bool mainScreen, bool shopVisible, IReadOnlyList<ShopItem> shop, RectangleF? cursorRegion = null, bool readShop = true)
    {
        using var timing = FieldRecognitionTiming.Measure("reader");
        OverlayGeometry = null;
        _frameGeometry = null;
        if (economy is not { IsRest: true }) return new(at, false, null, economy?.Round, new Dictionary<FieldCell, int>());
        if (_round != economy.Round) { _boards.Clear(); _round = economy.Round; }
        using var stream = new MemoryStream(frame);
        using var bitmap = new Bitmap(stream);
        double scale = Math.Min(bitmap.Width / 1920d, bitmap.Height / 1080d);
        bool panel = HasDetailPanel(bitmap, scale);
        bool dragging = panel && HasDragPreview(bitmap, scale);
        string? drag = null;
        if (panel)
        {
            var texts = await _ocr.RecognizeRegionAsync(frame, 0, (int)(285 * scale), (int)(230 * scale), (int)(65 * scale));
            string text = string.Join("", texts.Select(t => t.Text.Replace(" ", "")));
            drag = _names.FirstOrDefault(n => text.Contains(n, StringComparison.Ordinal));
            if (drag == "Pith")
            {
                var tiers = await _ocr.RecognizeRegionAsync(frame, (int)(12 * scale), (int)(340 * scale), (int)(48 * scale), (int)(48 * scale));
                var values = tiers.Select(t => PithIdentity.ReadTier(t.Text)).OfType<int>().Distinct().ToArray();
                drag = PithIdentity.Resolve(values.Length == 1 ? values[0] : null, HasPithStrategy);
            }
        }
        // 商店预览也有同款详情；只有拖动或已选中己方单位的“出售”控件才读取装备。
        bool selected = dragging;
        if (drag is not null && !dragging)
        {
            var controls = await _ocr.RecognizeRegionAsync(frame, (int)(650 * scale), (int)(280 * scale),
                (int)(1080 * scale), (int)(500 * scale));
            selected = controls.Any(t => t.Text.Replace(" ", "") == "出售");
        }
        var equipment = drag is not null && selected ? await _equipment.ReadAsync(frame, drag) : null;
        if (dragging || !mainScreen)
            return new(at, true, null, economy.Round, new Dictionary<FieldCell, int>(), Money: economy.Money, DragOperator: dragging ? drag : null, MainScreen: false)
                { PanelOperator = drag, Equipment = equipment };
        // 棋盘按短边等比、居中显示；只转换棋盘，详情和商店控件仍在原画面读取。
        var (boardFrame, viewport) = NormalizeBoardViewport(frame, bitmap.Width, bitmap.Height);
        using var boardStream = ReferenceEquals(boardFrame, frame) ? null : new MemoryStream(boardFrame);
        using var normalizedBitmap = boardStream is null ? null : new Bitmap(boardStream);
        var boardBitmap = normalizedBitmap ?? bitmap;
        var key = (bitmap.Width, bitmap.Height, shopVisible);
        if (_frameKey != key) { _boards.Clear(); _frameKey = key; }
        KnownMap = _lastMap ?? KnownMap;
        _boards.TryGetValue(key, out var board);
        BoardMatch? cameraOnly = null;
        if (BossRound && KnownMap is not null)
            board = _board.RecognizeDetailed(boardFrame, KnownMap, bossRound: true).Match;
        else if (board is null)
        {
            // 空卡较多时商店长边会漏检；镜头由棋盘本身判定，不能直接把“没检测到卡”当收店。
            var ordinary = _board.RecognizeDetailed(boardFrame, KnownMap);
            board = ordinary.Match;
            if (board is null && BossRound && (ordinary.Camera == BoardCamera.Expanded) != shopVisible)
            {
                // 首领地形可能让普通地图分数失效。两次拟合的镜头一致且两侧有区分度时，只借用投影。
                var boss = _board.RecognizeDetailed(boardFrame, ordinary.MapId, bossRound: true);
                if (boss.Camera == ordinary.Camera && Math.Abs(boss.LeftScore!.Value - boss.RightScore!.Value) >= 5)
                    cameraOnly = boss.Match;
            }
            if (board is not null) _boards[key] = board;
        }
        _lastMap = board?.MapId ?? KnownMap;
        var hidden = shop.Select(i => new RectangleF((float)i.CardRegion.X, (float)i.CardRegion.Y,
            (float)i.CardRegion.Width, (float)i.CardRegion.Height)).ToList();
        if (panel) hidden.Add(new(0, .17f, .33f, .75f));
        if (cursorRegion is { } cursor) hidden.Add(cursor);
        if (viewport.Width != 1 || viewport.Height != 1)
            hidden = hidden.Select(r => new RectangleF((r.X - viewport.X) / viewport.Width,
                (r.Y - viewport.Y) / viewport.Height, r.Width / viewport.Width, r.Height / viewport.Height)).ToList();
        var cells = ReadCells(boardBitmap, board, shopVisible, hidden, cameraOnly);
        if ((viewport.Width != 1 || viewport.Height != 1) && OverlayGeometry is { } overlay)
            OverlayGeometry = RestoreBoardViewport(overlay, viewport);
        if (panel) OverlayGeometry = null;
        Dictionary<int, string>? cards = null;
        if (readShop && shopVisible)
        {
            var current = shop.GroupBy(i => i.SlotIndex).ToDictionary(g => g.Key, g => g.First());
            foreach (var (slot, old) in _shopCards)
                if (!current.ContainsKey(slot) && CardVisible(bitmap, old, Theme)) current[slot] = old;
            _shopCards = current;
            cards = current.Where(p => _names.Contains(p.Value.Name)).ToDictionary(p => p.Key, p => p.Value.Name);
        }
        else if (readShop) _shopCards.Clear();
        return new(at, true, _lastMap, economy.Round, cells,
            cards, economy.Money) { PanelOperator = drag, Equipment = equipment,
                Sprites = ReadSprites(boardFrame, cells), Geometry = _frameGeometry, Summons = Summons };
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
        IReadOnlyList<RectangleF>? hidden = null) => ReadCells(bitmap, board, shopVisible, hidden, null);

    private Dictionary<FieldCell, int> ReadCells(Bitmap bitmap, BoardMatch? board, bool shopVisible,
        IReadOnlyList<RectangleF>? hidden, BoardMatch? cameraOnly)
    {
        var px = new Pixels(bitmap);
        _spriteFeet.Clear();
        _summonFeet.Clear();
        _summonHints.Clear();
        _cellGeometry.Clear();
        var cells = new Dictionary<FieldCell, int>();
        var camera = board ?? cameraOnly;
        bool expanded = camera?.Camera == BoardCamera.Expanded || camera is null && shopVisible;
        // 就绪主画面的镜头可确定格坐标；本帧颜色不明确时仍用本局已知地图过滤。
        var projection = camera?.Projection ?? _projections[expanded];
        var anchors = new Dictionary<FieldCell, (double X, double Y)>();
        var mapId = board?.MapId ?? _lastMap ?? KnownMap;
        var map = mapId is null ? null : _maps.Find(mapId);
        double width = bitmap.Width, height = bitmap.Height;
        // 最下面的第 4 行只放装备，不生成干员观测或身份裁块。
        for (int row = 0; row < 4; row++)
            for (int col = 0; col <= 6; col++)
            {
                if (map is not null && !GarrisonMap.CanDeploy(map.At(new(col, row)), melee: false)) continue;
                var p = projection.Project(col + .5, row + .5);
                var p2 = projection.Project(col + 1.5, row + .5);
                // 棋盘坐标会镜像，角色阶数角标仍在角色左上方。
                double tile = Math.Abs(p2.X - p.X) * width;
                var left = projection.Project(col, row + .5);
                var right = projection.Project(col + 1, row + .5);
                _cellGeometry[new("board", col, row)] = new(Math.Abs(right.X - left.X) * 1920,
                    p.X * 1920, p.Y * 1080, 0, 0);
                double offsetX = expanded ? camera?.PlayerSide switch
                {
                    "left" => 7 * width / 1920,
                    "right" => (-42 - row * 4) * width / 1920,
                    _ => -tile * .4
                } : camera?.PlayerSide == "right" ? (-18 - row * 5) * width / 1920 : -tile * .35;
                double offsetY = expanded ? 110 : 116 - row * 2;
                if (!expanded && camera?.PlayerSide == "right") offsetY = 98 + row * 3;
                anchors[new("board", col, row)] = (p.X * width + offsetX, p.Y * height - offsetY * width / 1920);
            }
        // 整备区与棋盘共享镜头；用投影中的基准点校正整条木板的位置。
        var origin = projection.Project(0, 0);
        // 整备区不随棋盘镜像；取相应镜头相对自身标定的平移与缩放。
        var baselineProjection = (camera?.PlayerSide == "right" ? _bossProjections : _projections)[expanded];
        var baseline = baselineProjection.Project(0, 0);
        var end = projection.Project(6, 0);
        var baselineEnd = baselineProjection.Project(6, 0);
        double zoom = Math.Abs((end.X - origin.X) / (baselineEnd.X - baseline.X));
        for (int col = 1; col <= 10; col++)
        {
            double x = expanded ? 254 + (col - 1) * 121.2 : 170 + (col - 1) * 149.5;
            if (expanded) x += camera?.PlayerSide switch { "left" => 59, "right" => 11, _ => 0 };
            double y = expanded ? 609 : 798;
            anchors[new("bench", col, 0)] = (origin.X * width + (x / 1920 - baseline.X) * width * zoom,
                origin.Y * height + (y / 1080 - baseline.Y) * height * zoom);
            var anchor = anchors[new("bench", col, 0)];
            // anchors与ReadBadge的输出均为角标中心，沿用中心到脚点的固定偏移。
            _cellGeometry[new("bench", col, 0)] = new((expanded ? 121.2 : 149.5) * zoom,
                anchor.X * 1920 / width + 35, anchor.Y * 1080 / height + 132, 0, 0);
        }
        using var resized = bitmap.Width == 1920 && bitmap.Height == 1080 ? null : new Bitmap(bitmap, 1920, 1080);
        var cameraFrame = resized ?? bitmap;
        var landmarks = ReadBadges(cameraFrame);
        double badgeScale = BadgeScale(cameraFrame, landmarks, zoom);
        var bodyPixels = resized is null ? px : new Pixels(cameraFrame);
        var topVisible = anchors.Where(a => a.Key.Zone == "board" && a.Key.Row == 0 && a.Value.Y >= 150 * height / 1080)
            .Select(a => a.Key).ToHashSet();
        foreach (var (cell, p) in anchors)
            anchors[cell] = (p.X - 35 * (badgeScale - 1) * width / 1920,
                p.Y - (cell.Zone == "bench" ? 132 : 112) * (badgeScale - 1) * height / 1080);
        var fit = FitFieldCamera(bitmap, landmarks, anchors);
        if (fit != (1d, 0d, 0d))
        {
            (double X, double Y) Transform(double x, double y) =>
                ((x - .5) * fit.Scale + .5 + fit.X, (y - .5) * fit.Scale + .5 + fit.Y);
            projection = new BoardProjection(new[] { (0d, 0d), (7d, 0d), (7d, 5d), (0d, 5d) }.Select(c =>
            {
                var p = projection.Project(c.Item1, c.Item2);
                return (c, Transform(p.X, p.Y));
            }).ToArray());
            foreach (var (cell, anchor) in anchors)
            {
                var p = Transform(anchor.X / width, anchor.Y / height);
                anchors[cell] = (p.X * width, p.Y * height);
                var geometry = _cellGeometry[cell];
                var foot = Transform(geometry.FootX / 1920, geometry.FootY / 1080);
                _cellGeometry[cell] = geometry with { FootX = foot.X * 1920, FootY = foot.Y * 1080,
                    CellWidth = geometry.CellWidth * fit.Scale };
            }
        }
        _frameGeometry = new(expanded ? BoardCamera.Expanded : BoardCamera.Collapsed,
            camera?.PlayerSide, BossRound, board is not null,
            new[] { (0d, 0d), (7d, 0d), (7d, 5d), (0d, 5d) }.Select(c =>
            { var p = projection.Project(c.Item1, c.Item2); return new FieldScreenPoint(p.X * 1920, p.Y * 1080); }).ToArray(),
            _cellGeometry.Where(p => p.Key.Zone == "bench").ToDictionary(p => p.Key, p => new FieldScreenPoint(p.Value.FootX, p.Value.FootY)));
        foreach (var (cell, p) in anchors)
        {
            // 商店和屏幕外格子没有观测，保留追踪器中的旧值。
            // 展开镜头的首排角标可能位于盟约栏下沿，实际可见部分由 ReadBadge 检查（342）；按可见角标校正过的首排格不受上界限制（341）。
            double minY = expanded && cell is { Zone: "board", Row: 0 } ? 140 : 150;
            if (p.X < 20 || p.X > width - 55 || (p.Y < minY * height / 1080 && !topVisible.Contains(cell)) || p.Y + 55 > height * (shopVisible ? .72 : .94)) continue;
            cells[cell] = 0;
        }
        using var spriteImage = NormalizeSpriteImage(bitmap);
        var matchedBadges = new Dictionary<FieldCell, FieldBadge>();
        foreach (var cell in cells.Keys.ToArray())
        {
            var p = anchors[cell];
            var badge = ReadBadge(px, bitmap.Width, bitmap.Height, p.X, p.Y, cell.Zone == "board");
            // 首排被盟约栏遮住时，用可见的身体缩小阶数候选，再回到原图验证角标。
            if (badge is null && cell is { Zone: "board", Row: 0 } && p.Y < 180 * width / 1920)
                badge = ReadCovenantBadge(px, spriteImage, bitmap.Width, bitmap.Height, p.X, p.Y, badgeScale);
            if (badge is null) continue;
            double badgeX = badge.X * 1920 / width, badgeY = badge.Y * 1080 / height;
            // 大角标相对人物向左上延伸；整备区多出的 20 像素来自落脚点标定。
            var foot = (badgeX + 35 * badgeScale,
                badgeY + 112 * badgeScale + (cell.Zone == "bench" ? 20 : 0));
            _summonFeet[cell] = foot;
            if (badge.X + 90 * width / 1920 < width && badge.Y + 150 * width / 1920 < height && (cell.Zone == "bench" ? HasSummonCount(px, badge, width / 1920)
                : HasSummonTriangle(px, badge, width / 1920))) { _summonHints.Add(cell); continue; }
            // 全片仍有无法凭令牌分类的过渡/遮挡候选，保留原人体证据门槛。
            var bodyOrigin = badge with { X = badgeX + 35 * (badgeScale - 1), Y = badgeY + 112 * (badgeScale - 1) };
            if (!HasUnitEvidence(bodyPixels, bodyOrigin, 1920, 1080, cell.Zone == "board")
                && !HasShortUnitEvidence(spriteImage, cell, badge)) continue;
            cells[cell] = badge.Tier;
            _spriteFeet[cell] = foot;
            matchedBadges[cell] = badge with { X = badge.X / width, Y = badge.Y / height, Size = badge.Size / height };
        }
        foreach (var (cell, p) in anchors)
        {
            var area = new RectangleF((float)((p.X - 22) / width), (float)((p.Y - 22) / height),
                (float)(90 / width), (float)(155 / height));
            if (hidden?.Any(r => r.IntersectsWith(area)) == true) cells.Remove(cell);
        }
        OverlayGeometry = new(projection, anchors.ToDictionary(p => p.Key, p => (p.Value.X / width, p.Value.Y / height)), matchedBadges);
        ReadSummons(spriteImage, cells);
        foreach (var summon in Summons) matchedBadges.Remove(new(summon.Zone, summon.Col, summon.Row));
        return cells;
    }

    // 每格分别检查平地和高台的固定位置；角标不再按最近距离分配到其他格。
    private FieldBadge? ReadBadge(Pixels px, int width, int height, double cx, double cy, bool board, int? supportedTier = null, double minimum = .73)
    {
        double scale = width / 1920d;
        // 展开商店时第一排角标与盟约栏重叠，只比较角标下半部。
        bool lower = board && cy < 180 * scale;
        FieldBadge? best = null;
        foreach (int size in new[] { 34, 40, 44 })
        {
            var templates = _templates.Where(t => t.Size == size && (supportedTier is null || t.Tier == supportedTier)).ToArray();
            int actualSize = Math.Max(10, (int)Math.Round(size * scale));
            int radius = (int)Math.Ceiling((board ? 9 : 12) * scale);
            int horizontal = (int)Math.Ceiling(12 * scale);
            // 横向扩展按完整的两像素步长进行，保留原搜索窗的粗采样位置。
            horizontal += (horizontal - radius) % 2;
            foreach (int lift in board ? new[] { 0, 26 } : new[] { 0 })
            {
                int left = (int)Math.Round(cx) - actualSize / 2;
                int top = (int)Math.Round(cy - lift * scale) - actualSize / 2;
                for (int y = Math.Max(0, top - radius); y <= Math.Min(height - actualSize - 1, top + radius); y += 2)
                    for (int x = Math.Max(0, left - horizontal); x <= Math.Min(width - actualSize - 1, left + horizontal); x += 2)
                    {
                        // 位置已受约束，亮框预筛允许比整屏搜索更低的对比度。
                        int mid = actualSize / 2;
                        int outer = Math.Max(1, (int)Math.Round(2 * scale));
                        int inner = Math.Max(outer + 1, (int)Math.Round(7 * scale));
                        if (px.Gray(x + outer, y + mid) < px.Gray(x + inner, y + mid) + 6
                            && px.Gray(x + actualSize - outer - 1, y + mid) < px.Gray(x + actualSize - inner - 1, y + mid) + 6) continue;
                        var values = Sample(px, x, y, actualSize, 10, lower ? 5 : 0); Normalize(values);
                        if (templates.Max(t => Dot(lower ? t.Lower : t.Values, values)) < (supportedTier is null ? .60 : .40)) continue;
                        for (int yy = y; yy <= Math.Min(y + 1, height - actualSize - 1); yy++)
                            for (int xx = x; xx <= Math.Min(x + 1, width - actualSize - 1); xx++)
                            {
                                var fine = Sample(px, xx, yy, actualSize, 20, lower ? 10 : 0); Normalize(fine);
                                foreach (var t in templates)
                                {
                                    double score = Dot(lower ? t.LowerFine : t.Fine, fine);
                                    if (best is not null && score <= best.Score) continue;
                                    if (supportedTier is not null)
                                    {
                                        // 盟约文字可能盖住数字；只在身体模型支持该阶且下框仍清晰时采纳。
                                        if (score < minimum) continue;
                                        // 下半图为 10×20，最后五行保留底边与两个下角。
                                        var rim = fine[100..]; Normalize(rim);
                                        if (Dot(t.Rim, rim) < .72) continue;
                                    }
                                    else if (score < .73)
                                    {
                                        // 一阶细竖线的外框会被邻格小人挡住一角；两侧亮边均清晰时允许轻微缺损。
                                        if (score < .71 || !board || lower || t.Tier != 1
                                            || px.Gray(xx + outer, yy + mid) - px.Gray(xx + inner, yy + mid) < 30
                                            || px.Gray(xx + actualSize - outer - 1, yy + mid) - px.Gray(xx + actualSize - inner - 1, yy + mid) < 30) continue;
                                    }
                                    best = new FieldBadge(xx + actualSize / 2, yy + actualSize / 2, t.Tier, score, actualSize);
                                }
                            }
                    }
            }
        }
        return best;
    }

    // 整备区召唤物的数量标签：角标右侧为暗色胶囊，内部有亮色 X 与数字。
    private static bool HasSummonCount(Pixels px, FieldBadge badge, double scale)
    {
        int Gray(double x, double y) => px.Gray((int)(badge.X + x * scale), (int)(badge.Y + y * scale));
        int border = 0, ink = 0;
        for (int x = 30; x <= 74; x += 2)
        {
            if (Gray(x, -17) > Gray(x, -9) + 18 && Gray(x, 17) > Gray(x, 9) + 18) border++;
            for (int y = -7; y <= 7; y += 2) if (Gray(x, y) > 150) ink++;
        }
        return border >= 19 && ink >= 12;
    }

    private static bool HasUnitEvidence(Pixels px, FieldBadge badge, double width, double height, bool board)
    {
        double scale = width / 1920;
        int Edges(int top)
        {
            int edges = 0;
            for (int row = top; row < top + 20; row += 2)
                for (int col = 18; col < 65; col += 2)
                {
                    int x = (int)(badge.X + col * scale), y = (int)(badge.Y + row * scale);
                    int x2 = x + Math.Max(1, (int)Math.Round(2 * scale));
                    if (x2 < width && y < height && Math.Abs(px.Gray(x, y) - px.Gray(x2, y)) > 32) edges++;
                }
            return edges;
        }
        int body = Edges(55);
        // 棋盘人物的密集轮廓可能偏上（如黑键），检查相邻的上下两段身体，沿用原门槛。
        if (body < 22)
        {
            // 宽帽的中段可能是整块纯色；上缘和下身都清晰才按人物计。
            return board && badge.Tier <= 3 && body >= 8 && Edges(15) >= 40 && Edges(75) >= 22 && Edges(95) >= 45;
        }
        if (badge.Tier is not (4 or 6)) return true;
        if (Edges(15) < (board ? 12 : 22)) return false;
        int bottom = Edges(75);
        int upper = Edges(35);
        if (!board) return body + Math.Max(upper, bottom) >= 130;
        // 增益光效使轮廓分散在三段；上下段都清晰时也能确认身体连续。
        return body + Math.Max(upper, bottom) >= 130
            || Math.Min(upper, bottom) >= 40 && body + upper + bottom >= 180;
    }

    // 906 秒既有测试裁片的倒三角灰度描述子（16×16），不含角标或鸟形身份。
    // 原画来自用户录屏，归上海鹰角网络所有；避免把身体稀疏本身当成装备证据。
    private static readonly double[] SummonTriangle = TriangleDescriptor();
    private static double[] TriangleDescriptor()
    {
        var values = Convert.FromBase64String("gIaHjpKRjZFgMkmMiYaFhpqen6CkM3iDkJSQf3FumZx8ZGlpdyiAR3l6dWtGWXWfo2BjZGpuSXh2eXl5V4R7nJweZl5gZmJxdWNyeGJ1mJecnBVfQGAvZGwqc3FgVZ2dl5qYrFyDaGBBV0JKYJiYmJWVlZhnUIJeXWQ7Q2WWlZSRkJGUUpZdUURFU1yQkZCQkJCPj5BNblhKWVhdjo6PkISEhoeGh5tdWVwSf4uIiYiVkY6Ki4uQp1pae42LjY6OiYuJiouKimtdVjuKiYmKiomIiYmJiIiKOVCIiYeIh4aGh4eHhIODgH1kgoKCg4SGgnl0ZlxXVE9PT1BPUVdZYA==")
            .Select(v => (double)v).ToArray();
        Normalize(values);
        return values;
    }

    private static bool HasSummonTriangle(Pixels px, FieldBadge badge, double scale)
    {
        // 当前有画面证据的是四阶夜莺幻影；其余召唤物不能凭同阶或身高直接排除。
        if (badge.Tier != 4) return false;
        int edges = 0;
        for (int y = 55; y < 95; y += 2)
            for (int x = 18; x < 65; x += 2)
                if (Math.Abs(px.Gray((int)(badge.X + x * scale), (int)(badge.Y + y * scale))
                    - px.Gray((int)(badge.X + (x + 2) * scale), (int)(badge.Y + y * scale))) > 32) edges++;
        if (edges >= 130) return false;
        // 倒三角会绕竖轴转动；蓝色鸟形提供第二条证据，允许侧面令牌的相关分数降低。
        int blue = 0;
        for (int y = 42; y < 90; y += 2)
            for (int x = 15; x < 70; x += 2)
                if (px.IsBlue((int)(badge.X + x * scale), (int)(badge.Y + y * scale))) blue++;
        double threshold = blue >= 30 ? .25 : .40;
        foreach (int size in new[] { 46, 50, 54, 58, 62 })
            for (int y = 68; y <= 86; y += 2)
                for (int x = 5; x <= 24; x += 2)
                {
                    var values = Sample(px, (int)(badge.X + x * scale), (int)(badge.Y + y * scale),
                        (int)(size * scale), 16);
                    Normalize(values);
                    if (Dot(values, SummonTriangle) >= threshold) return true;
                }
        return false;
    }
    // 兼容无棋盘投影的离线截图诊断；产品 ReadCells/ReadAsync 仅按格读取。
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
                            if (score >= .75) found.Add(new(xx + mid, yy + mid, tier, score, size));
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

    private static double[] Sample(Pixels p, int x, int y, int size, int count = 10, int firstRow = 0)
    {
        var values = new double[(count - firstRow) * count];
        for (int r = firstRow; r < count; r++) for (int c = 0; c < count; c++)
            values[(r - firstRow) * count + c] = p.Gray(x + (int)((c + .5) * size / count), y + (int)((r + .5) * size / count));
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
            using (var g = Graphics.FromImage(bitmap)) g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            _stride = data.Stride; _bytes = new byte[_stride * bitmap.Height];
            Marshal.Copy(data.Scan0, _bytes, 0, _bytes.Length); bitmap.UnlockBits(data);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int Gray(int x, int y)
        { int i = y * _stride + x * 3; return (_bytes[i] * 29 + _bytes[i + 1] * 150 + _bytes[i + 2] * 77) >> 8; }
        public bool IsBlue(int x, int y)
        { int i = y * _stride + x * 3; return _bytes[i] > 90 && _bytes[i] > _bytes[i + 2] * 1.4 && _bytes[i + 1] > _bytes[i + 2] * 1.2; }
    }
}
