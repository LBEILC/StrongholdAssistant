using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Moq;

namespace StrongholdAssistant.Tests.Services;

public class ShopFailureTests
{
    [Theory]
    [InlineData(false, "没认出商店画面。")]
    [InlineData(true, "商店卡片的名字没读出来。")]
    public async Task FailureStartsWithUserMessageThenOcrDetails(bool hasControl, string firstLine)
    {
        var ocr = new Mock<IOcrEngine>();
        var unreadable = new string('错', 300);
        List<OcrTextResult> texts = [new(unreadable, (740, 390, 220, 40))];
        if (hasControl) texts.Add(new("收起", (3500, 370, 160, 60)));
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(texts);
        var reader = new ShopReader(ocr.Object, ["山"]) { Theme = GameShopTheme.Closure };
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-tier4-gray.png"));
        Assert.Empty(await reader.ReadAsync(bytes, 1920, 1080, ["山"]));
        Assert.True(reader.HasVisibleCards);
        var lines = reader.Failure!.Split('\n');
        Assert.Equal(firstLine, lines[0]);
        Assert.Contains("OCR：" + unreadable[..240], lines[1]);
        Assert.DoesNotContain(unreadable, lines[1]);
        if (!hasControl) Assert.StartsWith("没找到「收起」文字", lines[1]);
    }
}