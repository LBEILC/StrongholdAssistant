using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class BoardRecognitionTests
{
    [Theory]
    [InlineData("shop-ui100-2000x1125.png", "m01", BoardCamera.Expanded, .347, .227, .329, .532)]
    [InlineData("shop-terminal-ui100-2000x1125.png", "m01", BoardCamera.Expanded, .347, .227, .329, .532)]
    [InlineData("shop-ursus-ui100-2000x1125.png", "m01", BoardCamera.Expanded, .347, .227, .329, .532)]
    [InlineData("shop-closure-round1-1600x1024.png", "m01", BoardCamera.Expanded, .3375, .241, .316, .524)]
    [InlineData("board-desert_camo_expanded.bmp", "m06", BoardCamera.Expanded, .347, .222, .329, .539)]
    [InlineData("board-palace_arrows_expanded.bmp", "m12", BoardCamera.Expanded, .347, .222, .329, .539)]
    [InlineData("board-snow_collapsed.bmp", "m09", BoardCamera.Collapsed, .350, .283, .329, .667)]
    public void FindsMapAndProjectsToObservedBoard(string file, string map, BoardCamera camera,
        double topX, double topY, double bottomX, double bottomY)
    {
        var recognizer = new BoardRecognizer(Path.Combine(AppContext.BaseDirectory, "data"));
        var match = recognizer.Recognize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file)));
        Assert.NotNull(match);
        Assert.Equal(map, match.MapId); Assert.Equal(camera, match.Camera);
        var top = match.Projection.Project(0, 0); var bottom = match.Projection.Project(0, 4);
        Assert.InRange(Math.Abs(top.X - topX), 0, .012); Assert.InRange(Math.Abs(top.Y - topY), 0, .012);
        Assert.InRange(Math.Abs(bottom.X - bottomX), 0, .012); Assert.InRange(Math.Abs(bottom.Y - bottomY), 0, .012);
    }

    [Fact]
    public void IntroCameraDoesNotPublishPreparationBoard()
    {
        var recognizer = new BoardRecognizer(Path.Combine(AppContext.BaseDirectory, "data"));
        Assert.Null(recognizer.Recognize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "board-map_intro.bmp"))));
    }
}
