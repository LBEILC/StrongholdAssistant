using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public class HighResolutionRecognitionTests(ShopReaderFixture fixture, EnemyRecognitionFixture recognition)
{
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public async Task EnlargedSamplesMatchOriginalThroughCapturePath(int width, int height)
    {
        var engine = recognition.Engine;
        var original = File.ReadAllBytes(fixture.Sample("info-ingame-1920x1080.png"));
        var scaled = Capture("info-ingame-1920x1080.png", width, height);
        async Task<MatchInfo> Read(byte[] png)
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"),
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            await api.InitializeRecognitionAsync(Path.Combine(AppContext.BaseDirectory, "data"), engine, () => Task.FromResult<byte[]?>(png));
            return (MatchInfo)(await api.InvokeAsync("rescan", []))!;
        }
        var baseline = await Read(original);
        var enlarged = await Read(scaled);
        Assert.Equal(JsonSerializer.Serialize(baseline with { RecognizedAt = null }),
            JsonSerializer.Serialize(enlarged with { RecognizedAt = null }));
        fixture.SelectTheme("shop-terminal-tier1-color.png");
        var shop = await fixture.Reader.ReadAsync(File.ReadAllBytes(fixture.Sample("shop-terminal-tier1-color.png")), 1920, 1080, fixture.Names);
        var resizedShop = await fixture.Reader.ReadAsync(Capture("shop-terminal-tier1-color.png", width, height), 1920, 1080, fixture.Names);
        Assert.NotEmpty(shop);
        Assert.Equal(shop.Select(i => (i.Id, i.Name)), resizedShop.Select(i => (i.Id, i.Name)));
    }

    private byte[] Capture(string file, int width, int height)
    {
        using var source = new Bitmap(fixture.Sample(file));
        using var enlarged = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
        }
        return new ScreenCaptureService().EncodeCapture(enlarged);
    }
}
