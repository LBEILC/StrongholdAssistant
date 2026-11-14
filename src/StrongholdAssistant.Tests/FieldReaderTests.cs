using System.Drawing;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class FieldReaderTests(EnemyRecognitionFixture fixture)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
    private async Task<FieldReader> Reader() => new(Data, fixture.Engine,
        (await new JsonDataStore(Data).LoadOperatorsAsync()).Select(o => o.Name));

    [Theory]
    [InlineData(47, "bench:9:0,bench:10:0")]
    [InlineData(57, "board:4:2,board:5:3")]
    [InlineData(345, "bench:9:0,bench:10:0")]
    [InlineData(350, "board:5:3,board:6:3")]
    public async Task RecordedOccupancyAndTiersMatchAnswer(int second, string expected)
    {
        var reader = await Reader(); var bytes = File.ReadAllBytes(Sample($"field-{second}.jpg"));
        using var bitmap = new Bitmap(Sample($"field-{second}.jpg"));
        var board = new BoardRecognizer(Data).Recognize(bytes);
        Assert.NotNull(board);
        var cells = reader.ReadCells(bitmap, board, true).Where(p => p.Value > 0).ToArray();
        Assert.Equal(expected.Split(',').Order(), cells.Select(p => $"{p.Key.Zone}:{p.Key.Col}:{p.Key.Row}").Order());
        Assert.All(cells, p => Assert.Equal(1, p.Value));
    }

    [Fact]
    public async Task PreparationHasElevenOperatorsAndEquipmentDoesNotCount()
    {
        var reader = await Reader();
        using var prep = new Bitmap(Sample("prep-ready-1920x1080.png"));
        var cells = reader.ReadCells(prep, null, false).Where(p => p.Value > 0).ToArray();
        Assert.Equal(11, cells.Length);
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 2, 3, 3, 3, 5, 5 }, cells.Select(p => p.Value).Order());
        using var shop = new Bitmap(Sample("shop-closure-li-user-1665x1012.jpg"));
        Assert.Equal(new[] { 1, 1, 3, 5 }, reader.ReadBadges(shop).Select(b => b.Tier).Order());
    }

    [Fact]
    public async Task DragPanelReadsMousseDespiteHiddenReadyControl()
    {
        var reader = await Reader();
        var o = await reader.ReadAsync(File.ReadAllBytes(Sample("field-55.5.jpg")), DateTimeOffset.UtcNow,
            new(1, 0, 1, 0, true), false, false, []);
        Assert.Equal("慕斯", o.DragOperator); Assert.Empty(o.Cells);
    }
}
