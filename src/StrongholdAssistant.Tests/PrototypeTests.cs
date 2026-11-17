using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Moq;
using OpenCvSharp;

namespace StrongholdAssistant.Tests;

public sealed class PrototypeTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void RulesUseExactBackupsAndAmbiguousCardsStayUnknown()
    {
        var catalog = new PrototypeCatalog(Data);
        Assert.Equal(13, catalog.Prototypes.Length);
        Assert.Equal(67, catalog.Slots.Count(s => s.Prototype is not null));
        Assert.Equal("贝洛内", catalog.Resolve("Sharp", 5, 13, ["siracusaShip", "raidShip"])?.Name);
        Assert.Null(catalog.Resolve("Sharp", 5, null, []));
        Assert.Null(catalog.Resolve("Sharp", 5, 13, ["sargonShip"]));
        Assert.Equal("领主·Sharp", catalog.Slots.Single(s => s.Name == "棘刺").Prototype);
        Assert.DoesNotContain(catalog.Slots, s => s.Name == "歌蕾蒂娅" && s.Prototype is not null);
    }

    [Fact]
    public async Task TwoSharpCardsResolveIndependentlyAndRoundTripPrototype()
    {
        using var original = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "roster-left-end-1920x1080.jpg"));
        using var canvas = new Bitmap(original);
        using (var g = Graphics.FromImage(canvas))
        {
            DrawBonds(g, 269, 208, "siracusa", "raid");
            DrawBonds(g, 269, 559, "sargon", "skillful");
        }
        using var png = new MemoryStream(); canvas.Save(png, ImageFormat.Png);
        var ocr = new Mock<IOcrEngine>();
        var names = new Queue<string>(["Sharp", "Sharp", "风絮", "乌有", "余", "仇白", "Mechanist", "老鲤", "赤刃明霄陈", "斩业星熊"]);
        ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int w, int h) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                y == 248 ? [new(names.Dequeue(), (x, y, w, h))] : x == 250 ? [new("物资调配 自选编队 12/12", (x, y, w, h))]
                : w == 52 ? [new("V", (x, y, w, h))] : []));
        var result = await new RosterReader(ocr.Object, Data).ReadAsync(png.ToArray());
        Assert.NotNull(result);
        Assert.Empty(result.UnknownNames);
        Assert.Equal(new[] { "贝洛内", "佩佩" }, result.Cards.Take(2).Select(c => c.Operator.Name));
        Assert.All(result.Cards.Take(2), c => Assert.Equal("Sharp", c.Operator.Prototype));
        // 第七张沿用真实截图的盟约图标，只替换 OCR 名字，验证游戏圆底和发光不会阻碍图标比对。
        Assert.Equal("Mechanist", result.Cards.Single(c => c.Operator.Name == "黍").Operator.Prototype);
        var op = result.Cards[0].Operator;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Assert.Equal(op, JsonSerializer.Deserialize<RosterOperator>(JsonSerializer.Serialize(op, options), options));
    }

    [Fact]
    public async Task RealPithCardsUseTierInsteadOfPrototypeBranch()
    {
        using var canvas = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "roster-left-end-1920x1080.jpg"));
        using (var g = Graphics.FromImage(canvas))
        {
            DrawBonds(g, 269, 208, "mani");
            DrawBonds(g, 269, 559, "empty");
        }
        using var png = new MemoryStream(); canvas.Save(png, ImageFormat.Png);
        var names = new Queue<string>(["Pith", "Pith", "风絮", "乌有", "余", "仇白", "黍", "老鲤", "赤刃明霄陈", "斩业星熊"]);
        var tiers = new Queue<string>(["III", "V"]);
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int w, int h) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                y == 248 ? [new(names.Dequeue(), (x, y, w, h))] : x == 250 ? [new("物资调配 自选编队 12/12", (x, y, w, h))]
                : w == 52 ? [new(tiers.Dequeue(), (x, y, w, h))] : []));
        var result = await new RosterReader(ocr.Object, Data).ReadAsync(png.ToArray());
        Assert.NotNull(result);
        Assert.Empty(result.UnknownNames);
        Assert.Equal(new[] { "Pith（优等生）", "Pith" }, result.Cards.Take(2).Select(c => c.Operator.Name));
        Assert.All(result.Cards.Take(2), c => Assert.Null(c.Operator.Prototype));
    }

    private static void DrawBonds(Graphics g, int x, int y, params string[] names)
    {
        g.FillRectangle(Brushes.Black, x + 40, y + 281, 165, 54);
        for (int i = 0; i < names.Length; i++)
        {
            using var icon = new Bitmap(Path.Combine(Data, "prototype-bonds", names[i] + ".png"));
            using var black = new Bitmap(28, 28);
            using (var draw = Graphics.FromImage(black)) draw.DrawImage(icon, 0, 0, 28, 28);
            int cx = x + 54 + i * 48, cy = y + 290;
            using var fill = new SolidBrush(Color.FromArgb(0, 220, 180));
            g.FillEllipse(fill, cx - 3, cy - 3, 34, 34);
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } }));
            g.DrawImage(black, new Rectangle(cx, cy, 28, 28), 0, 0, 28, 28, GraphicsUnit.Pixel, attributes);
            attributes.Dispose();
        }
    }

    [Fact]
    public async Task TierSortedNeighborsDisambiguateSharedCovenant()
    {
        using var canvas = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "roster-left-end-1920x1080.jpg"));
        using (var g = Graphics.FromImage(canvas))
        {
            DrawBonds(g, 580, 208, "siracusa", "raid");
            // 灵巧同时属于佩佩与隐德来希；按相邻原干员的排序位置消除歧义。
            DrawBonds(g, 891, 559, "skillful");
        }
        using var png = new MemoryStream(); canvas.Save(png, ImageFormat.Png);
        var names = new Queue<string>(["薇薇安娜", "铃兰", "Sharp", "维娜·维多利亚", "迷迭香", "Sharp", "塞雷娅", "溯光星源", "老鲤", "Mon3tr"]);
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int w, int h) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                y == 248 ? [new(names.Dequeue(), (x, y, w, h))] : x == 250 ? [new("物资调配 自选编队", (x, y, w, h))]
                : w == 52 ? [new("V", (x, y, w, h))] : w == 550 ? [new("按等阶", (1540, 42, 80, 24))] : []));
        var result = await new RosterReader(ocr.Object, Data).ReadAsync(png.ToArray());
        Assert.NotNull(result);
        Assert.Empty(result.UnknownNames);
        Assert.Equal("贝洛内", result.Cards[2].Operator.Name);
        Assert.Equal("隐德来希", result.Cards[5].Operator.Name);
        Assert.Equal("Sharp", result.Cards[5].Operator.Prototype);
    }

    [Fact]
    public void PrototypeTemplateKeepsOriginalIdentityAndUnknownRosterIsPending()
    {
        using var matcher = new SpriteMatcher(Path.Combine(Data, "sprite-templates.bin.gz"));
        using var model = new SpriteModelMatcher(Path.Combine(Data, "sprite-model.onnx"), Path.Combine(Data, "sprite-model-classes.json"));
        var catalog = new PrototypeCatalog(Data);
        var patch = SharpPatch();
        var appearances = SpriteMatcher.RosterAppearances([new("贝洛内", "irrelevant", null, null, null, "", Prototype: "Sharp")]);
        var ranked = matcher.MatchCellWidth(patch, 5, ["贝洛内", "黍"], appearances).Candidates;
        Assert.Equal("贝洛内", ranked[0].Name);
        Assert.True(ranked[0].Score > .8);
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity([new() { Name = "贝洛内", Tier = 5 }, new() { Name = "黍", Tier = 5 }],
            (p, tier, names) => matcher.MatchCellWidth(p, tier, names, appearances), model);
        tracker.ConfigurePrototypes(n => n == "贝洛内", (_, _, _) => []);
        var cell = new FieldCell("bench", 1, 0);
        var observation = new FieldObservation(DateTimeOffset.UtcNow, true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = 5 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [cell] = patch } };
        tracker.Observe(observation, true, true);
        Assert.Equal("贝洛内", Assert.Single(tracker.State!.Units).Operator);
        var unknown = new FieldTracker();
        unknown.ConfigureIdentity([new() { Name = "贝洛内", Tier = 5 }, new() { Name = "佩佩", Tier = 5 }, new() { Name = "黍", Tier = 5 }],
            (p, tier, names) => matcher.MatchCellWidth(p, tier, names), model);
        unknown.ConfigurePrototypes(_ => false, (p, tier, names) => catalog.MatchUnknown(matcher, p, tier, names));
        unknown.ConfigureCandidatePool(["佩佩"], []);
        unknown.Observe(observation, true, true);
        var unit = Assert.Single(unknown.State!.Units);
        Assert.Equal("pending", unit.Status);
        Assert.Equal("贝洛内", Assert.Single(unit.Candidates).Operator);
        // 确认一格的名字不能把相同 Sharp 外观的另一格也确认为这个名字。
        unknown.SetOperator(cell, "贝洛内", observation.At);
        var second = new FieldCell("bench", 2, 0);
        var two = observation with { At = observation.At.AddSeconds(1),
            Cells = new Dictionary<FieldCell, int> { [cell] = 5, [second] = 5 },
            Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [cell] = patch, [second] = patch } };
        unknown.Observe(two);
        unknown.Observe(two with { At = two.At.AddMilliseconds(100) });
        Assert.Equal("pending", unknown.State!.Units.Single(u => u.Cell == second).Status);
    }

    private static FieldSpritePatch SharpPatch()
    {
        using var file = File.OpenRead(Path.Combine(Data, "sprite-templates.bin.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.UTF8);
        Assert.Equal("SPRT0004", Encoding.ASCII.GetString(reader.ReadBytes(8)));
        reader.ReadUInt16(); double scale = .271 / reader.ReadSingle(); int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
            reader.ReadBytes(reader.ReadUInt16());
            int tier = reader.ReadUInt16(), width = reader.ReadUInt16(), height = reader.ReadUInt16();
            float ox = reader.ReadSingle(), oy = reader.ReadSingle();
            byte[] color = reader.ReadBytes(width * height * 3), mask = reader.ReadBytes(width * height);
            if (name != "Sharp" || tier != 5) continue;
            using var rgb = Mat.FromPixelData(height, width, MatType.CV_8UC3, color);
            using var alpha = Mat.FromPixelData(height, width, MatType.CV_8UC1, mask);
            using var resized = new Mat(); using var visible = new Mat();
            Cv2.Resize(rgb, resized, new OpenCvSharp.Size((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
            Cv2.Resize(alpha, visible, resized.Size(), interpolation: InterpolationFlags.Nearest);
            using var image = new Mat(204, 180, MatType.CV_8UC3, Scalar.All(45));
            using var roi = new Mat(image, new Rect((int)Math.Round(95 - ox * scale), (int)Math.Round(184 - oy * scale), resized.Width, resized.Height));
            resized.CopyTo(roi, visible);
            return FieldReader.CropSprite(image, 90, 180, true)! with { Geometry = new(100, 90, 180, 90, 180) };
        }
        throw new InvalidDataException("缺少 Sharp 五阶模板");
    }
}
