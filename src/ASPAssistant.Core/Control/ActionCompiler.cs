using ASPAssistant.Core;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Control;

public sealed partial class ActionCompiler : IActionCompiler, IDisposable
{
    private readonly IOcrEngine _ocr;
    private readonly string _data;
    private readonly string[] _operators;
    private readonly string[] _cards;
    private readonly GarrisonBoardData _maps;
    private readonly HashSet<string> _melee;
    private readonly ASPAssistant.Core.Data.BoardRouteStore _routes;
    private readonly Dictionary<string, string> _strategies;
    private readonly bool _ownsEngine;

    public ActionCompiler() : this(Path.Combine(PackageLayout.Root, "data")) { }
    private ActionCompiler(string data) : this(new MaaOcrEngine(data), data) => _ownsEngine = true;
    public ActionCompiler(IOcrEngine ocr, string dataDirectory)
    {
        _ocr = ocr;
        _data = dataDirectory;
        JsonElement[] Rows(string file, string key)
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_data, file + ".json")));
            return doc.RootElement.GetProperty(key).EnumerateArray().Select(x => x.Clone()).ToArray();
        }
        _operators = Rows("operators", "operators").Select(x => x.GetProperty("name").GetString()!).ToArray();
        _cards = _operators.Concat(Rows("equipment", "equipment").Select(x => x.GetProperty("name").GetString()!)).ToArray();
        _strategies = Rows("strategies", "strategies").ToDictionary(x => x.GetProperty("id").GetString()!,
            x => x.GetProperty("initiator").GetString()!);
        using var profiles = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_data, "operator_profiles.json")));
        _melee = profiles.RootElement.GetProperty("operators").EnumerateObject()
            .Where(p => p.Value.GetProperty("position").GetString() == "近战").Select(p => p.Name).ToHashSet();
        using var maps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_data, "garrison_maps.json")));
        _maps = GarrisonBoardStore.Parse(maps.RootElement);
        _routes = new(_data);
    }

    public void Dispose() { if (_ownsEngine) ((MaaOcrEngine)_ocr).Dispose(); }

    private sealed class View(ControlFrame frame)
    {
        public ControlFrame Frame => frame;
        public int W => frame.Width;
        public int H => frame.Height;
        public double S => Math.Min(W / 1920d, H / 1080d);
        public double K = 1;
        public GameShopTheme Theme;
        public bool Board, Shop;
        public bool? Ready, Frozen;
        public string Heading = "";
        public EconomyReading? Economy;
        public int? Remaining, Free;
        public Dictionary<int, string> Cards = [];
        public FieldOverlayGeometry? Geometry;
        public FieldObservation? Observation;
        public CustomPickReading? Custom;
        public int? Money => Economy?.Money ?? frame.State?.Money.Value;
        public TapStep Hud(double right, double y, bool bottom = true) =>
            new(1 - right * S * K / W, bottom ? 1 - y * S * K / H : y * S * K / H);
        public TapStep Center(double x, double y) =>
            new(.5 + (x - 960) * S / W, .5 + (y - 540) * S / H);
        public FieldUnit? Unit(FieldCell cell) => frame.Field?.Units.FirstOrDefault(u => u.Cell == cell);
        public TapStep? Position(FieldCell cell, bool start)
        {
            if (Geometry is null || !Geometry.Anchors.TryGetValue(cell, out var anchor)) return null;
            if (cell.Zone == "bench")
                return new(anchor.X + 35d / 1920, anchor.Y + (start ? 108d : 132d) / 1080);
            var p = Geometry.Projection.Project(cell.Col + .5, cell.Row + (start ? .56 : .5));
            return new(p.X, p.Y);
        }
    }

    private static string Join(IEnumerable<OcrTextResult> lines) =>
        string.Concat(lines.Select(l => l.Text.Replace(" ", "")));

    private IReadOnlyList<OcrTextResult> Region(View v, int x, int y, int w, int h) =>
        _ocr.RecognizeRegionAsync(v.Frame.Image, x, y, w, h).GetAwaiter().GetResult();

    private static bool Opening(GameAction a) => a is PickStrategyAction or PickAdaptAction or PickCustomAction;

    private View Read(GameAction action, ControlFrame frame)
    {
        var v = new View(frame);
        byte[] image = frame.Image;
        using var stream = new MemoryStream(image);
        using var bmp = new Bitmap(stream);
        if (Opening(action))
        {
            v.Heading = Join(Region(v, 0, 0, v.W, (int)(125 * v.S)));
            if (action is PickCustomAction)
                v.Custom = new CustomPickReader(_ocr, _operators).ReadAsync(image, v.W, v.H).GetAwaiter().GetResult();
            return v;
        }

        var surface = FieldReader.InspectSurface(bmp);
        if (surface.Obscured || surface.Detail) return v;
        if (surface.ConfirmationCandidate && Join(Region(v, (int)(650 * v.S), (int)(400 * v.S),
            (int)(620 * v.S), (int)(150 * v.S))).Contains("确认准备")) return v;
        var board = new BoardSurfaceReader(_ocr);
        bool main = board.IsMainScreenAsync(image, v.W, v.H).GetAwaiter().GetResult();
        string ready = board.RawText.Replace(" ", "");
        v.Ready = ready.Contains("取消") || ready.Contains("已就绪") ? true :
            ready.Contains("就绪") ? false : null;
        v.Board = main || v.Ready == true;
        if (!v.Board && ready is "不足" or "资金不足")
            v.Board = new EconomyReader(_ocr).ReadFieldAsync(image, v.W, v.H, false).GetAwaiter().GetResult().IsRest;
        if (!v.Board) return v;
        v.Theme = new GameThemeDetector(_ocr).ReadAsync(image, v.W, v.H).GetAwaiter().GetResult()?.Theme ?? GameShopTheme.Terminal;
        v.K = GameUiScale.Shop(bmp);
        v.Shop = ShopLayout.HasCards(bmp, v.K, v.Theme);
        if (action is BuyAction or RefreshAction or UpgradeAction or SellAction or FreezeAction)
        {
            v.Economy = new EconomyReader(_ocr, v.Theme).ReadAsync(image, v.W, v.H, v.K).GetAwaiter().GetResult();
            v.Shop |= v.Economy.UpgradeButton is not null;
        }
        if (action is BuyAction or RefreshAction && v.Shop)
        {
            var reader = new ShopReader(_ocr, _cards) { Theme = v.Theme, ReadUntracked = true };
            reader.ReadAsync(image, v.W, v.H, [], bmp).GetAwaiter().GetResult();
            v.Cards = reader.AllItems.GroupBy(c => c.SlotIndex).Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single().Name);
        }
        if (action is FreezeAction or RefreshAction or MoveAction)
        {
            string buttons = Join(Region(v, (int)(v.W - 475 * v.S * v.K), (int)(v.H - 520 * v.S * v.K),
                (int)(475 * v.S * v.K), (int)(230 * v.S * v.K)));
            v.Frozen = buttons.Contains("解冻") ? true : buttons.Contains("冻结") ? false : frame.State?.Frozen.Value;
            v.Remaining = Counter(buttons, @"剩余可放置角色[：:]?(\d+)");
            v.Free = Counter(buttons, @"免费[×xX]?(\d+)");
        }
        if (action is MoveAction or FaceAction or SellAction)
        {
            var mode = frame.State?.ModeId.Value;
            var round = frame.Field?.Round ?? frame.State?.Round.Value;
            using var field = new FieldReader(_data, _ocr, _operators)
            {
                Theme = v.Theme,
                KnownMap = frame.Field?.MapId ?? frame.State?.MapId.Value,
                BossRound = round.HasValue && _routes.IsBossRound(round.Value, mode?.Contains("single") == true, mode)
            };
            v.Observation = field.ReadAsync(image, DateTimeOffset.UnixEpoch, new(round, null, null, null, true),
                true, v.Shop, [], readShop: false).GetAwaiter().GetResult();
            v.Geometry = field.OverlayGeometry;
        }
        return v;
    }

    private static int? Counter(string text, string pattern) =>
        Regex.Match(text, pattern) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
    private static MatchCard? StateCard(View v, int slot) => v.Frame.State is not { } state ? null :
        state.Shop.ElementAtOrDefault(slot); // 364 起商店是按卡位 0～5 的六格数组
    private static string? Card(View v, int slot)
    {
        if (v.Shop) return v.Cards.GetValueOrDefault(slot);
        var state = StateCard(v, slot);
        if (state?.Bought.Value == true) return null;
        return state?.Card.Value?.Name.Value;
    }
    private static string Cell(FieldCell c) => c.Zone == "bench" ? $"整备区第 {c.Col} 格" : PlacementCells.Code(new(c.Col, c.Row));
    private static string? Direction(string? f) => f switch
    { "left" => "左", "right" => "右", "up" => "上", "down" => "下", _ => null };
    private static DragStep Drag(TapStep a, TapStep b) => new(a.X, a.Y, b.X, b.Y, 100, 640, 300);
    private static CompileResult WrongScreen() => new(null, "screen", "当前画面不能执行这个动作");
    private static CompileResult Mismatch(string why) => new(null, "mismatch", why);

    public CompileResult Compile(GameAction action, ControlFrame now)
    {
        var v = Read(action, now);
        List<InputStep> steps = [];
        static bool Inside(double x, double y) => x is >= 0 and <= 1 && y is >= 0 and <= 1;
        CompileResult Plan(string text) => steps.Any(s => s switch
        {
            TapStep t => !Inside(t.X, t.Y),
            DragStep d => !Inside(d.X1, d.Y1) || !Inside(d.X2, d.Y2),
            _ => false
        }) ? Mismatch("目标在画面外") : new(new(action, text, steps, steps.Count == 0 ? 0 : 650), null, null);
        if (Opening(action)) return CompileOpening(action, v, steps, Plan);
        if (!v.Board || v.Ready == true && action is not ReadyAction) return WrongScreen();
        void Button(string key, TapStep point) => steps.Add(now.Keyboard ? new KeyStep(key) : point);
        void OpenShop()
        {
            if (v.Shop) return;
            Button("A", v.Hud(121, 121));
            steps.Add(new WaitStep(400));
        }
        bool Source(FieldCell cell, string? expect) => v.Unit(cell) is { } u &&
            (expect is null || u.Operator == expect) && v.Observation?.Cells.GetValueOrDefault(cell) > 0 &&
            v.Position(cell, true) is not null;
        bool CanPlace(MoveAction move)
        {
            if (v.Frame.Field?.Summons?.Any(s => new FieldCell(s.Zone, s.Col, s.Row) == move.To) == true) return false;
            if (move.To.Zone != "board") return true;
            if (move.From.Zone == "bench" && v.Remaining == 0) return false;
            string? mapId = v.Observation?.MapId ?? now.Field?.MapId;
            if (mapId is null || _maps.Find(mapId) is not { } map) return true;
            bool melee = v.Unit(move.From)?.Operator is { } op && _melee.Contains(op);
            return GarrisonMap.CanDeploy(map.At(new(move.To.Col, move.To.Row)), melee);
        }
        void Aim(TapStep at, FieldCell cell, string facing)
        {
            // 落格后选择菱形方向；单独转身按同一方向菜单推断，待实机。
            var projection = v.Geometry!.Projection;
            int dc = facing == "left" ? -1 : facing == "right" ? 1 : 0;
            int dr = facing == "up" ? -1 : facing == "down" ? 1 : 0;
            var start = projection.Project(cell.Col + .5, cell.Row + .5);
            var end = projection.Project(cell.Col + .5 + dc, cell.Row + .5 + dr);
            double dx = (end.X - start.X) * v.W, dy = (end.Y - start.Y) * v.H;
            double factor = (dc == 0 ? 110 : 115) * v.S / Math.Sqrt(dx * dx + dy * dy);
            steps.Add(new WaitStep(300));
            steps.Add(Drag(at, new(at.X + dx * factor / v.W, at.Y + dy * factor / v.H)));
        }
        switch (action)
        {
            case ReadyAction a:
                if (v.Ready is null) return Mismatch("读不到准备状态");
                if (v.Ready != a.On) Button("C", v.Hud(120, 155, false));
                return Plan(a.On ? "准备就绪" : "取消准备");
            case FreezeAction a:
                if (v.Frozen is null) return Mismatch("读不到冻结状态");
                if (v.Frozen != a.On) { OpenShop(); Button("S", v.Hud(320, 352)); }
                return Plan(a.On ? "冻结商店" : "解冻商店");
            case RefreshAction:
                OpenShop(); Button("D", v.Hud(120, 352)); return Plan("刷新商店");
            case UpgradeAction:
                OpenShop();
                var box = v.Economy?.UpgradeButton;
                if (!now.Keyboard && box is null && v.Shop) return Mismatch("读不到升级卡的位置");
                // 待实机：收起时只能从宿主快照估计升级卡在最左商品左侧的位置。
                int first = Enumerable.Range(0, 6).FirstOrDefault(i => StateCard(v, i)?.Card.Value is not null, -1);
                if (!now.Keyboard && box is null && first < 0) return Mismatch("读不到展开后的升级卡位");
                var firstCard = ShopLayout.Card(first, v.W, v.H, v.K, v.Theme);
                Button("G", box is { } b ? new(b.X + b.Width / 2, b.Y + b.Height / 2) :
                    new((firstCard.Left - 85 * v.S * v.K) / v.W, 1 - 140 * v.S * v.K / v.H));
                return Plan("升级商店");
            case BuyAction a:
                if (a.Slot is < 0 or > 5 || Card(v, a.Slot) is not { } name || a.Expect is not null && a.Expect != name)
                    return Mismatch("卡位为空或卡名不符");
                OpenShop();
                var rect = ShopLayout.Card(a.Slot, v.W, v.H, v.K, v.Theme);
                var tap = new TapStep((rect.Left + rect.Width / 2) / v.W, (rect.Top + rect.Height * .64) / v.H);
                steps.Add(tap); steps.Add(new WaitStep(350)); steps.Add(tap);
                return Plan($"买第 {a.Slot + 1} 张卡（{name}）");
            case SellAction a:
                if (!Source(a.From, a.Expect)) return Mismatch("出售格没有目标干员");
                steps.Add(Drag(v.Position(a.From, true)!, v.Hud(118, 140)));
                return Plan($"卖出{Cell(a.From)}的{v.Unit(a.From)!.Operator ?? "干员"}");
            case MoveAction a:
                if (!Source(a.From, a.Expect) || v.Position(a.To, false) is not { } target ||
                    a.Facing is not null && Direction(a.Facing) is null || !CanPlace(a))
                    return Mismatch("起点、目标格或可上场人数不符");
                steps.Add(Drag(v.Position(a.From, true)!, target));
                if (a.To.Zone == "board") Aim(target, a.To, a.Facing ?? "right");
                return Plan($"把 {v.Unit(a.From)!.Operator ?? "干员"} 从{Cell(a.From)}拖到{Cell(a.To)}" +
                    (a.To.Zone == "board" ? $"，朝{Direction(a.Facing ?? "right")}" : ""));
            case FaceAction a:
                if (a.At.Zone != "board" || !Source(a.At, null) || Direction(a.Facing) is null)
                    return Mismatch("朝向或目标格不符");
                var at = v.Position(a.At, true)!;
                steps.Add(at); Aim(at, a.At, a.Facing); return Plan($"把{Cell(a.At)}的干员转向{Direction(a.Facing)}");
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
}
