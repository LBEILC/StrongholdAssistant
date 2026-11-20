using ASPAssistant.Core.Services;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class EconomyReaderTests(ASPAssistant.Tests.Services.EnemyRecognitionFixture fixture)
{
    private readonly IOcrEngine _ocr = fixture.Engine;

    [Fact]
    public async Task ReadsClosure1600x1024()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,
            "TestData", "shop-closure-round1-1600x1024.png"));
        var result = await new EconomyReader(_ocr, GameShopTheme.Closure).ReadAsync(bytes, 1600, 1024);
        Assert.NotNull(result.UpgradeButton);
        Assert.Equal((1, 4, 1, 5, true), (result.Round, result.Money, result.Level, result.UpgradeCost, result.IsRest));
    }

    public sealed class CollapsedShopFrameAttribute : FactAttribute
    {
        public CollapsedShopFrameAttribute()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("STRONGHOLD_COLLAPSED_SHOP_FRAME")))
                Skip = "未提供收店资金为 2 的现场帧（STRONGHOLD_COLLAPSED_SHOP_FRAME）";
        }
    }

    [CollapsedShopFrame]
    public async Task ReadsTwoOnCollapsedClosureShop()
    {
        var bytes = await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("STRONGHOLD_COLLAPSED_SHOP_FRAME")!);
        var result = await new EconomyReader(_ocr, GameShopTheme.Closure).ReadAsync(bytes, 1920, 1080);
        Assert.Equal(2, result.Money);
    }

    [Theory]
    [InlineData("shop-terminal-ui100-2000x1125.png")]
    [InlineData("shop-ursus-ui100-2000x1125.png")]
    [InlineData("shop-ui100-2000x1125.png")]
    public async Task ReadsThreeShopThemes(string file)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var result = await new EconomyReader(_ocr, file.Contains("terminal") ? GameShopTheme.Terminal : file.Contains("ursus") ? GameShopTheme.Ursus : GameShopTheme.Closure).ReadAsync(bytes, 2000, 1125);
        Assert.NotNull(result.UpgradeButton);
        Assert.Equal((1, 4, 1, 5, true), (result.Round, result.Money, result.Level, result.UpgradeCost, result.IsRest));
    }

    [Theory]
    [InlineData("shop-terminal-tier4-gray.png", 0, 4, 12)]
    [InlineData("shop-tier6-glow-a.png", 11, 6, null)]
    public async Task ReadsHigherLevelsAndLeavesMissingRoundNull(string file, int money, int level, int? cost)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var reader = new EconomyReader(_ocr, file.Contains("terminal") ? GameShopTheme.Terminal : file.Contains("ursus") ? GameShopTheme.Ursus : GameShopTheme.Closure);
        var result = await reader.ReadAsync(bytes, 1920, 1080);
        Assert.NotNull(result.UpgradeButton);
        Assert.Equal(new EconomyReading(null, money, level, cost, false), result with { UpgradeButton = null });
        Assert.Equal(result, await reader.ReadAsync(bytes, 1920, 1080));
    }


}
