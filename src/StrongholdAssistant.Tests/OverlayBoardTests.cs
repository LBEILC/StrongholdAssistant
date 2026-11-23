using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using Shapes = System.Windows.Shapes;

namespace StrongholdAssistant.Tests;

public sealed class OverlayBoardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void OldSettingsDefaultBothLayersAndPersistIndependentSwitches()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{}");
        var store = new HostSettingsStore(path);
        var settings = store.LoadSettings();
        Assert.Equal(new OverlayLayerSettings(true, true), settings.OverlayLayers);
        store.SaveSettings(settings with { OverlayLayers = new(false, true) });
        Assert.Equal(new OverlayLayerSettings(false, true), store.LoadSettings().OverlayLayers);
        store.SaveSettings(settings with { OverlayLayers = new(true, false) });
        Assert.Equal(new OverlayLayerSettings(true, false), store.LoadSettings().OverlayLayers);
    }

    [Fact]
    public async Task BoardUsesFieldMapAndEconomyRoundWithRoutesDisabledAndDeduplicates()
    {
        var api = await HostApi.CreateAsync(Data, _directory);
        Assert.Null(await api.InvokeAsync("board", []));
        var tracker = (FieldTracker)typeof(HostApi).GetField("_fieldTracker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
        tracker.Observe(new(DateTimeOffset.UtcNow, true, "m01", 1, new Dictionary<FieldCell, int>()));
        Assert.Null(api.Board()!.Round);
        Assert.Empty(api.Board()!.Routes);
        Assert.Empty(api.Board()!.BossSpots);
        var pushed = new List<BoardView?>();
        api.Changed += (name, data) => { if (name == "board") pushed.Add((BoardView?)data); };
        api.ObserveEconomy(new(6, 0, 1, 0, true));
        api.ObserveEconomy(new(6, 0, 1, 0, true));
        Assert.False(api.BoardRoutesEnabled);
        var board = Assert.IsType<BoardView>(await api.InvokeAsync("board", []));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        var map = json.RootElement.GetProperty("maps").GetProperty("m01");
        Assert.Equal(map.GetProperty("levels").GetProperty("06").GetProperty("routes").GetArrayLength(), board.Routes.Count);
        Assert.Equal(6, board.Round);
        Assert.False(board.BossRound);
        Assert.Equal(5, board.Walkable.Length); Assert.All(board.Walkable, row => Assert.Equal(12, row.Length));
        Assert.Null(board.Side);
        int count = pushed.Count;
        api.SaveSettings(api.Settings with { OverlayLayers = new(false, true) });
        Assert.Equal(count, pushed.Count);
        api.ApplyRecognizedMatch(api.Match with { BossId = json.RootElement.GetProperty("bossIds").GetProperty("boss_2").GetString(), Economy = api.Match.Economy! with { Round = 14 } }, []);
        api.ObserveEconomy(new(14, 0, 1, 0, true));
        api.ObserveEconomy(new(14, 0, 1, 0, true));
        tracker.Observe(new(DateTimeOffset.UtcNow, true, "m01", 14, new Dictionary<FieldCell, int>()));
        api.SaveSettings(api.Settings with { Routes = api.Settings.Routes with { Side = "right" } });
        board = api.Board()!;
        Assert.True(board.BossRound); Assert.Equal("right", board.Side);
        Assert.Equal(6, board.BossWalkable.Length); Assert.All(board.BossWalkable, row => Assert.Equal(17, row.Length));
        Assert.NotEmpty(board.Routes);
    }

    [Fact]
    public void RecomputingKeepsOldPlanWhileSwitchesAndMissingSurfaceClearGraphics() => OnSta(() =>
    {
        var projection = new BoardProjection([((0, 0), (.2, .2)), ((7, 0), (.8, .2)), ((7, 5), (.8, .7)), ((0, 5), (.2, .7))]);
        var cell = new FieldCell("board", 1, 1);
        var field = new FieldState("m01", 6, "now", [new("board", 1, 1, 1, null, null) { Status = "pending" }], []);
        var geometry = new FieldOverlayGeometry(projection, new Dictionary<FieldCell, (double, double)> { [cell] = (.3, .3) }, new Dictionary<FieldCell, FieldBadge>());
        var pending = new PendingLayer();
        var placement = new PlacementLayer();
        var plan = new OverlayPlacementPlan("now", [new("芬", cell, new("board", 2, 2), "right")]);
        pending.Update(field, geometry, true, 1920, 1080, false);
        placement.Update(plan, field, projection, true, 1920, 1080);
        Assert.Single(pending.Children.Cast<UIElement>()); Assert.NotEmpty(placement.Children.Cast<UIElement>());
        pending.Update(field, geometry, false, 1920, 1080);
        Assert.Empty(pending.Children.Cast<UIElement>()); Assert.NotEmpty(placement.Children.Cast<UIElement>());
        placement.Update(plan, field, projection, false, 1920, 1080);
        Assert.Empty(placement.Children.Cast<UIElement>());
        placement.Update(plan with { BasedOn = "old" }, field, projection, true, 1920, 1080);
        Assert.NotEmpty(placement.Children.Cast<UIElement>());
        pending.Update(field with { Units = [field.Units[0] with { Status = "sure" }] }, geometry, true, 1920, 1080);
        Assert.Empty(pending.Children.Cast<UIElement>());
        pending.Update(field, null, true, 1920, 1080);
        placement.Update(plan, field, null, true, 1920, 1080);
        Assert.Empty(pending.Children.Cast<UIElement>()); Assert.Empty(placement.Children.Cast<UIElement>());
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1920, 0)]
    [InlineData(0, 1080)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    [InlineData(double.NaN, 1080)]
    [InlineData(1920, double.NaN)]
    [InlineData(double.PositiveInfinity, 1080)]
    [InlineData(1920, double.PositiveInfinity)]
    public void InvalidViewportSkipsAllLayersAndValidSizeRestoresDrawing(double width, double height) => OnSta(() =>
    {
        var projection = new BoardProjection([((0, 0), (.2, .2)), ((7, 0), (.8, .2)), ((7, 5), (.8, .7)), ((0, 5), (.2, .7))]);
        var cell = new FieldCell("board", 1, 1);
        var field = new FieldState("m01", 6, "now", [new("board", 1, 1, 1, null, null) { Status = "pending" }], []);
        var geometry = new FieldOverlayGeometry(projection, new Dictionary<FieldCell, (double, double)> { [cell] = (.3, .3) }, new Dictionary<FieldCell, FieldBadge>());
        var plan = new OverlayPlacementPlan("now", [new("芬", cell, new("board", 2, 2), "right")]);
        var data = new BoardRoutes("m01", 6, true, null,
            [new("fly", false, false, 2, [[new(1, 1), new(4, 1)]], null, false)],
            [], BoardCamera.Expanded, projection);
        var settings = new RouteOverlaySettings(Enabled: true);
        var pending = new PendingLayer();
        var placement = new PlacementLayer();
        var shop = new ShopMarkerLayer();
        var routes = new RouteLayer();
        void Update(double w, double h)
        {
            pending.Update(field, geometry, true, w, h, false);
            placement.Update(plan, field, projection, true, w, h);
            shop.Update([new ShopItem { Id = "tracked", IsTracked = true, CardRegion = (.5, .75, .11, .23) }], w, h, false);
            routes.Update(data, settings, w, h);
        }
        void AssertEmpty()
        {
            Assert.Empty(pending.Children.Cast<UIElement>());
            Assert.Empty(placement.Children.Cast<UIElement>());
            Assert.Empty(shop.Children.Cast<UIElement>());
            Assert.Empty(routes.Children.Cast<UIElement>());
        }
        // 首次布局前不画；已有内容失去尺寸时清空，恢复后重新绘制。
        Update(width, height);
        AssertEmpty();
        Update(1920, 1080);
        Assert.NotEmpty(pending.Children.Cast<UIElement>());
        Assert.NotEmpty(placement.Children.Cast<UIElement>());
        Assert.NotEmpty(shop.Children.Cast<UIElement>());
        Assert.NotEmpty(routes.Children.Cast<UIElement>());
        routes.Resize(width, height);
        Assert.Empty(routes.Children.Cast<UIElement>());
        routes.Resize(1920, 1080);
        Assert.NotEmpty(routes.Children.Cast<UIElement>());
        Update(width, height);
        AssertEmpty();
        routes.Update(null, settings, 0, 0);
    });

    [Fact]
    public void FieldUpdatesBeforeWindowLayoutDoNotThrow() => OnSta(() =>
    {
        var window = new OverlayWindow();
        try
        {
            Assert.Equal(0, window.ActualHeight);
            var cell = new FieldCell("board", 1, 1);
            var projection = new BoardProjection([((0, 0), (.2, .2)), ((7, 0), (.8, .2)), ((7, 5), (.8, .7)), ((0, 5), (.2, .7))]);
            var field = new FieldState("m01", 6, "now", [new("board", 1, 1, 1, null, null) { Status = "pending" }], []);
            var geometry = new FieldOverlayGeometry(projection, new Dictionary<FieldCell, (double, double)> { [cell] = (.3, .3) }, new Dictionary<FieldCell, FieldBadge>());
            window.UpdateField(field, geometry);
            window.UpdatePlacement(new("now", [new("芬", cell, new("board", 2, 2), "right")]));
            Assert.False(window.HasOverlayContent);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void RetreatAndRefillOnOneCellStaySeparate() => OnSta(() =>
    {
        var projection = new BoardProjection([((0, 0), (.2, .2)), ((7, 0), (.8, .2)), ((7, 5), (.8, .7)), ((0, 5), (.2, .7))]);
        var cell = new FieldCell("board", 2, 2);
        var field = new FieldState("m01", 6, "now",
            [new("board", 2, 2, 1, "芬", null), new("bench", 3, 0, 1, "Mon3tr", null)], []);
        // 一个下场（芬 → 整备区）+ 一个顶到他那一格（Mon3tr → 芬那格）：换人最常见的样子。
        var plan = new OverlayPlacementPlan("now",
            [new("Mon3tr", new("bench", 3, 0), cell, "up"), new("芬", cell, new("bench", 1, 0), null)]);
        var placement = new PlacementLayer();
        placement.Update(plan, field, projection, true, 1920, 1080);
        var children = placement.Children.Cast<UIElement>().ToList();
        var polygons = children.OfType<Shapes.Polygon>().ToList();
        var labels = children.OfType<Border>().ToList();
        Assert.Equal(2, labels.Count);
        static Rect Bounds(Shapes.Polygon polygon) => new(polygon.Points.Min(p => p.X), polygon.Points.Min(p => p.Y),
            polygon.Points.Max(p => p.X) - polygon.Points.Min(p => p.X), polygon.Points.Max(p => p.Y) - polygon.Points.Min(p => p.Y));
        var byWidth = polygons.OrderByDescending(p => Bounds(p).Width).ToList();
        var arrival = Bounds(byWidth[0]);
        var tag = Bounds(byWidth[1]);
        // 上场占满整格，下场缩到左上角那一小块，两者不再互相盖住。
        Assert.True(arrival.Width > tag.Width * 1.5);
        Assert.True(tag.Width < arrival.Width * .6 && tag.Height < arrival.Height * .6);
        Assert.InRange(tag.X - arrival.X, -2, 2);
        Assert.InRange(tag.Y - arrival.Y, -2, 2);
        // 上场元素排在下场之后，z 序上不会被盖住。
        Assert.True(children.IndexOf(byWidth[0]) > children.IndexOf(byWidth[1]));
        // 两个标签上下分开，不是同一个位置。
        Assert.True(Math.Abs(Canvas.GetTop(labels[0]) - Canvas.GetTop(labels[1])) > 10);
    });

    [Fact]
    public void DepartureOnlyStillUsesTheWholeCell() => OnSta(() =>
    {
        var projection = new BoardProjection([((0, 0), (.2, .2)), ((7, 0), (.8, .2)), ((7, 5), (.8, .7)), ((0, 5), (.2, .7))]);
        var cell = new FieldCell("board", 2, 2);
        var field = new FieldState("m01", 6, "now", [new("board", 2, 2, 1, "芬", null)], []);
        var plan = new OverlayPlacementPlan("now", [new("芬", cell, new("bench", 1, 0), null)]);
        var placement = new PlacementLayer();
        placement.Update(plan, field, projection, true, 1920, 1080);
        var children = placement.Children.Cast<UIElement>().ToList();
        // 没有别人顶这一格时，下场仍是满格虚线框 + 一个「下场」标签。
        Assert.Equal(2, children.Count);
        var polygon = Assert.Single(children.OfType<Shapes.Polygon>());
        var label = Assert.Single(children.OfType<Border>());
        Assert.EndsWith("下场", ((TextBlock)label.Child).Text);
        Assert.True(polygon.Points.Max(p => p.X) - polygon.Points.Min(p => p.X) > 20);
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
