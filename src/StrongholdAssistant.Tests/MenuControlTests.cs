using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ASPAssistant.Core.Control;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Cli;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public class MenuControlTests(ASPAssistant.Tests.Services.EnemyRecognitionFixture fixture)
{
    [Fact]
    public void MenuCommandsUseActionBatchAndExecutionOptions()
    {
        var start = Command.Parse(["start", "7", "--mode", "dryRun", "--wait"]);
        Assert.Equal("actions", start.Path);
        Assert.True(start.Wait);
        var body = JsonSerializer.SerializeToElement(start.Body);
        Assert.Equal("dryRun", body.GetProperty("mode").GetString());
        Assert.Equal(new StartSoloAction(7), body.GetProperty("actions")[0].Deserialize<GameAction>(Host.HostApi.JsonOptions));
        var leave = JsonSerializer.SerializeToElement(Command.Parse(["leave"]).Body);
        Assert.IsType<LeaveResultAction>(leave.GetProperty("actions")[0].Deserialize<GameAction>());
        Assert.Throws<ArgumentException>(() => Command.Parse(["start", "0"]));
    }

    [Fact]
    public async Task DifficultyUsesSelectedLevelAndScrollsTowardHigherRows()
    {
        using var bmp = new Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        bmp.Save(stream, ImageFormat.Png);
        var frame = new ControlFrame(stream.ToArray(), bmp.Width, bmp.Height, null, null, false);
        var ocr = new DifficultyOcr();
        using var compiler = new ActionCompiler(ocr, Path.Combine(AppContext.BaseDirectory, "data"));
        Assert.Equal(new MenuPage("difficulty", 1), await MenuPages.IdentifyAsync(ocr, frame.Image));
        Assert.Equal(new TapStep(1670d / 1920, 985d / 1080),
            Assert.Single(compiler.Compile(new StartSoloAction(1), frame).Plan!.Steps));
        // 未选中的行字暗不算锁（397 实机：规程 12 已解锁但字发暗）。
        Assert.Equal("选择规程等级 2", compiler.Compile(new StartSoloAction(2), frame).Plan!.Text);
        var drag = Assert.IsType<DragStep>(Assert.Single(compiler.Compile(new StartSoloAction(7), frame).Plan!.Steps));
        Assert.True(drag.Y2 > drag.Y1);
        Assert.False(compiler.Verify(new StartSoloAction(7), frame, frame).Ok);
    }

    [Fact]
    public async Task OpeningLevelDoesNotIncludeDigitsFromOtherOcrBoxes()
    {
        var image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "solo-opening-level1-1920x1080.png"));
        Assert.Equal(new MenuPage("opening", 1), await MenuPages.IdentifyAsync(fixture.Engine, image));
    }
    private sealed class DifficultyOcr : IOcrEngine
    {
        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] image, int x, int y, int w, int h) =>
            Task.FromResult<IReadOnlyList<OcrTextResult>>([
                new("请选择模拟难度", (1520, 30, 240, 50)),
                new("规程等级1", (36, 1014, 150, 40)),
                new("规程等级2", (1470, 273, 250, 65)),
                new("规程等级1", (1468, 454, 250, 65)),
                new("开始模拟", (1570, 950, 200, 70))
            ]);
        public Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(byte[] image, int x, int y, int w, int h) =>
            throw new NotSupportedException();
        public Task<string?> FindCandidateInRegionAsync(byte[] image, int x, int y, int w, int h, IReadOnlyList<string> c) =>
            throw new NotSupportedException();
    }
}
