using ASPAssistant.Core.Services;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Tests.Services;

[Collection("Native OCR")]
public sealed class EconomyReaderTests : IDisposable
{
    private readonly MaaOcrEngine _ocr = new(Path.Combine(AppContext.BaseDirectory, "data"));

    [Theory]
    [InlineData("shop-terminal-ui100-2000x1125.png")]
    [InlineData("shop-ursus-ui100-2000x1125.png")]
    [InlineData("shop-ui100-2000x1125.png")]
    public async Task ReadsThreeShopThemes(string file)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var result = await new EconomyReader(_ocr, file.Contains("terminal") ? GameShopTheme.Terminal : file.Contains("ursus") ? GameShopTheme.Ursus : GameShopTheme.Closure).ReadAsync(bytes, 2000, 1125);
        Assert.Equal(new EconomyReading(1, 4, 1, 5, true), result);
    }

    [Theory]
    [InlineData("shop-terminal-tier4-gray.png", 0, 4, 12)]
    [InlineData("shop-tier6-glow-a.png", 11, 6, null)]
    public async Task ReadsHigherLevelsAndLeavesMissingRoundNull(string file, int money, int level, int? cost)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var result = await new EconomyReader(_ocr, file.Contains("terminal") ? GameShopTheme.Terminal : file.Contains("ursus") ? GameShopTheme.Ursus : GameShopTheme.Closure).ReadAsync(bytes, 1920, 1080);
        Assert.Equal(new EconomyReading(null, money, level, cost, false), result);
    }

    public void Dispose() => _ocr.Dispose();
}
