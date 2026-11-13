using System.Drawing;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class ShopDiagnosticTests(ShopReaderFixture fixture)
{
    // 用户诊断图只保留商店像素，去除其余画面；画面归上海鹰角网络所有。
    [Theory]
    [InlineData("shop-wide-ui100.png", GameShopTheme.Closure, "驱逐者瓦斯|泡泡|鸿雪|阿米娅|烈夏|维伊")]
    [InlineData("shop-terminal-frozen-1610.png", GameShopTheme.Terminal, "炎国短刀|矩|瑰盐|归溟幽灵鲨|幽灵鲨")]
    public async Task DiagnosticCardsRemainVisibleAndReadNames(string name, GameShopTheme theme, string expected)
    {
        fixture.Reader.Theme = theme;
        using var image = new Bitmap(fixture.Sample(name));
        var items = await fixture.Reader.ReadAsync(File.ReadAllBytes(fixture.Sample(name)), image.Width, image.Height, fixture.Names);
        Assert.True(fixture.Reader.HasVisibleCards);
        Assert.Equal(expected.Split('|'), items.Select(i => i.Name));
        Assert.All(items, item => Assert.InRange((item.OcrRegion.X + item.OcrRegion.Width / 2 - item.CardRegion.X) / item.CardRegion.Width, 0, 1));
    }
}
