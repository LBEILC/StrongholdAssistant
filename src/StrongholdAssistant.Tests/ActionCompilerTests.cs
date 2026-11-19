using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Control;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public class ActionCompilerTests(EnemyRecognitionFixture fixture)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static ControlFrame Load(string path)
    {
        byte[] image = File.ReadAllBytes(path);
        using var stream = new MemoryStream(image);
        using var bmp = new Bitmap(stream);
        return new(image, bmp.Width, bmp.Height, null, null, false);
    }
    private static ControlFrame Sample(string name) => Load(Path.Combine(AppContext.BaseDirectory, "TestData", name));
    private static ControlFrame EmulateSize(ControlFrame source)
    {
        using var stream = new MemoryStream(source.Image);
        using var original = new Bitmap(stream);
        using var resized = new Bitmap(original, 1763, 992);
        using var output = new MemoryStream();
        resized.Save(output, ImageFormat.Png);
        return source with { Image = output.ToArray(), Width = 1763, Height = 992 };
    }
    private static void Preview(string label, ControlFrame frame, CompiledAction plan)
    {
        if (Environment.GetEnvironmentVariable("CONTROL_PREVIEWS") is not { } folder) return;
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, label + ".png"), ControlPreview.Render(frame, plan));
    }

    [Theory]
    [InlineData("field-47.jpg", false, 1700, 681, 1490, 682, 640, 808, 1695, 124, 2)]
    [InlineData("shop-ui100-2000x1125.png", false, 1745, 660, 1500, 660, 512, 810, 1755, 135, 2)]
    [InlineData("shop-closure-round1-1600x1024.png", false, 1415, 692, 1230, 692, 533, 801, 1410, 100, 2)]
    [InlineData("field-47.jpg", true, 1560, 625, 1360, 625, 584, 738, 1560, 111, 2)]
    public void ButtonsAndCardLandInsideMeasuredBoxes(string file, bool resized,
        int rx, int ry, int fx, int fy, int ux, int uy, int cx, int cy, int slot)
    {
        var frame = Sample(file);
        if (resized) frame = EmulateSize(frame);
        string label = resized ? "1763x992" : Path.GetFileNameWithoutExtension(file);
        using var compiler = new ActionCompiler(fixture.Engine, Data);
        void Check(GameAction action, string key, Rectangle box)
        {
            var r = compiler.Compile(action, frame);
            Assert.True(r.Plan is not null, r.Problem);
            var tap = Assert.IsType<TapStep>(Assert.Single(r.Plan.Steps));
            Assert.True(box.Contains((int)(tap.X * frame.Width), (int)(tap.Y * frame.Height)),
                $"{file} {key} 点 ({tap.X * frame.Width:F0},{tap.Y * frame.Height:F0})，预期 {box}");
            Preview(label + "-" + key, frame, r.Plan);
            var keyboard = compiler.Compile(action, frame with { Keyboard = true });
            Assert.Equal(new KeyStep(key), Assert.Single(keyboard.Plan!.Steps));
            Preview(label + "-key-" + key, frame, keyboard.Plan);
        }
        double s = frame.Width / 1920d;
        Rectangle Box(int x, int y, int w, int h) => new(x, y, (int)(w * s), (int)(h * s));
        Check(new RefreshAction(), "D", Box(rx, ry, 205, 88));
        Check(new FreezeAction(true), "S", Box(fx, fy, 203, 88));
        Check(new UpgradeAction(), "G", Box(ux, uy, 170, 270));
        Check(new ReadyAction(), "C", Box(cx, cy, 198, 63));
        var purchase = compiler.Compile(new BuyAction(slot), frame);
        Assert.True(purchase.Plan is not null, purchase.Problem);
        using var stream = new MemoryStream(frame.Image);
        using var bmp = new Bitmap(stream);
        var card = ShopLayout.Card(slot, frame.Width, frame.Height, GameUiScale.Shop(bmp), GameShopTheme.Closure);
        var clicks = purchase.Plan.Steps.OfType<TapStep>().ToArray();
        Assert.Equal(2, clicks.Length);
        Assert.All(clicks, t => Assert.True(card.Contains((float)(t.X * frame.Width), (float)(t.Y * frame.Height))));
        Preview(label + "-buy", frame, purchase.Plan);
        Assert.Equal("mismatch", compiler.Compile(new BuyAction(slot, "不相符的名字"), frame).Reason);
    }

    [Theory]
    [InlineData("strategy-selection.jpg")]
    [InlineData("mumu-solo-opening-1763x992.png")]
    public void OpeningRejectsBuy(string name)
    {
        using var compiler = new ActionCompiler(fixture.Engine, Data);
        Assert.Equal("screen", compiler.Compile(new BuyAction(2), Sample(name)).Reason);
    }

    private ControlFrame RecognizeField(ControlFrame frame)
    {
        using var reader = new FieldReader(Data, fixture.Engine, []);
        using var stream = new MemoryStream(frame.Image);
        using var image = new Bitmap(stream);
        bool shop = ShopLayout.HasCards(image, GameUiScale.Shop(image), GameShopTheme.Closure);
        var obs = reader.ReadAsync(frame.Image, DateTimeOffset.UnixEpoch, new(1, null, null, null, true), true, shop, []).GetAwaiter().GetResult();
        var units = obs.Cells.Where(c => c.Value > 0).Select(c => new FieldUnit(c.Key.Zone, c.Key.Col, c.Key.Row, c.Value, null, null)).ToArray();
        return frame with { Field = new(obs.MapId, 1, "", units, []) };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragStartsOnBenchOperator(bool resized)
    {
        var image = Sample("field-47.jpg");
        var frame = RecognizeField(resized ? EmulateSize(image) : image);
        using var compiler = new ActionCompiler(fixture.Engine, Data);
        var bench = new FieldCell("bench", 10, 0);
        var move = new MoveAction(bench, new("board", 4, 2), "right");
        var r = compiler.Compile(move, frame);
        Assert.True(r.Plan is not null, r.Problem);
        var d = Assert.IsType<DragStep>(r.Plan.Steps[0]);
        Assert.InRange(d.X1 * 1920, 1330, 1420);
        Assert.InRange(d.Y1 * 1080, 655, 752);
        string label = resized ? "1763x992" : "field-47";
        Preview(label + "-bench10-move", frame, r.Plan);
        var sell = compiler.Compile(new SellAction(bench), frame);
        Preview(label + "-bench10-sell", frame, sell.Plan!);
    }

    [Theory]
    [InlineData("shop-ui100-2000x1125.png")]
    [InlineData("shop-closure-round1-1600x1024.png")]
    public void EmptyBoardAndAlreadyReachedTargets(string file)
    {
        var frame = Sample(file) with { Field = new(null, 1, "", [new("bench", 9, 0, 1, "慕斯", null)], []) };
        using var compiler = new ActionCompiler(fixture.Engine, Data);
        var stale = new MoveAction(new("bench", 9, 0), new("board", 5, 3), Expect: "慕斯");
        Assert.Equal("mismatch", compiler.Compile(stale, frame).Reason);
        Assert.Empty(compiler.Compile(new FreezeAction(false), frame).Plan!.Steps);
        Assert.Empty(compiler.Compile(new ReadyAction(false), frame).Plan!.Steps);
    }

    [Fact]
    public void CustomSelectionKeepsRequestedCardsAlreadyHighlighted()
    {
        var frame = Sample("custom-picks-solo-selecting-2v2.png");
        using var compiler = new ActionCompiler(fixture.Engine, Data);
        var action = new PickCustomAction(5, ["结城理", "凯尔希·思衡托"]);
        var r = compiler.Compile(action, frame);
        Assert.True(r.Plan is not null, r.Problem);
        var confirm = Assert.IsType<TapStep>(Assert.Single(r.Plan.Steps));
        Assert.True(confirm.X > .8 && confirm.Y > .85);
        Preview("custom-picks-solo-confirm", frame, r.Plan);
        Assert.True(compiler.Verify(action, frame, Sample("custom-picks-solo-final-2v2.png")).Ok);
    }

    [Theory]
    [InlineData("buy")]
    [InlineData("move")]
    [InlineData("refresh")]
    [InlineData("ready")]
    public void RecordedTransitionHasDirection(string name)
    {
        string? root = Environment.GetEnvironmentVariable("CONTROL_PAIRS");
        if (root is null || !File.Exists(Path.Combine(root, name + "-before.png")) || !File.Exists(Path.Combine(root, name + "-after.png"))) return;
        var before = Load(Path.Combine(root, name + "-before.png"));
        var after = Load(Path.Combine(root, name + "-after.png"));
        GameAction action = name switch
        {
            "buy" => new BuyAction(5, "灰喉"),
            "move" => new MoveAction(new("bench", 9, 0), new("board", 5, 3), "right"),
            "refresh" => new RefreshAction(),
            _ => new ReadyAction()
        };
        if (action is MoveAction) { before = RecognizeField(before); after = RecognizeField(after); }
        using var compiler = new ActionCompiler(fixture.Engine, Data);
        Assert.True(compiler.Verify(action, before, after).Ok, $"{name} 正向");
        Assert.False(compiler.Verify(action, after, before).Ok, $"{name} 倒序");
        Assert.False(compiler.Verify(action, before, before).Ok, $"{name} 同帧");
    }

    [Theory]
    [InlineData("""{"type":"buy","slot":2,"expect":"深靛"}""")]
    [InlineData("""{"type":"sell","from":{"zone":"bench","col":3,"row":0}}""")]
    [InlineData("""{"type":"refresh"}""")]
    [InlineData("""{"type":"freeze","on":false}""")]
    [InlineData("""{"type":"upgrade"}""")]
    [InlineData("""{"type":"move","from":{"zone":"bench","col":1,"row":0},"to":{"zone":"board","col":0,"row":1},"facing":"left","expect":"深靛"}""")]
    [InlineData("""{"type":"face","at":{"zone":"board","col":1,"row":2},"facing":"up"}""")]
    [InlineData("""{"type":"ready","on":true}""")]
    [InlineData("""{"type":"pickStrategy","strategyId":"band_mech"}""")]
    [InlineData("""{"type":"pickAdapt","index":0}""")]
    [InlineData("""{"type":"pickCustom","tier":5,"operators":["深靛"]}""")]
    public void JsonMatchesWireAction(string json)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var action = JsonSerializer.Deserialize<GameAction>(json, options);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonSerializer.SerializeToNode(action, options)));
        Assert.Equal(new ReadyAction(), JsonSerializer.Deserialize<GameAction>("""{"type":"ready"}""", options));
    }
}
