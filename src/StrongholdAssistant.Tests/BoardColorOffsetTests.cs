using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class BoardColorOffsetTests
{
    // 玩家截图只从指定的绝对目录读取，不进入仓库和发布包。
    public sealed class DiagnosticTheoryAttribute : TheoryAttribute
    {
        public DiagnosticTheoryAttribute()
        {
            var directory = Environment.GetEnvironmentVariable("STRONGHOLD_BOARD_313");
            if (directory is null || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)
                || new[] { "0851021654075", "0851536555103", "0851284171431" }
                    .Any(id => !File.Exists(Path.Combine(directory, $"20261117-{id}.png"))))
                Skip = "未提供 313 棋盘样图绝对目录（STRONGHOLD_BOARD_313）";
        }
    }

    [DiagnosticTheory]
    [InlineData("0851021654075", true)]
    [InlineData("0851536555103", true)]
    [InlineData("0851284171431", false)]
    public void AcceptsSmallColorShiftButRejectsPlacementHighlight(string id, bool accepted)
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("STRONGHOLD_BOARD_313")!, $"20261117-{id}.png");
        var image = File.ReadAllBytes(path);
        var recognizer = new BoardRecognizer(Path.Combine(AppContext.BaseDirectory, "data"));
        var result = recognizer.RecognizeDetailed(image);
        if (accepted)
        {
            Assert.NotNull(result.Match);
            Assert.Equal("m05", result.Match.MapId);
            Assert.Equal(BoardCamera.Expanded, result.Match.Camera);
            Assert.True(result.Score > 11); // 诊断仍保留未经校色的分数。
            Assert.Null(result.Rejection);
            Assert.NotNull(recognizer.Recognize(image, "m05", BoardCamera.Expanded));
        }
        else
        {
            Assert.Null(result.Match);
            Assert.NotNull(result.Rejection);
            Assert.Null(recognizer.Recognize(image, "m05", BoardCamera.Collapsed));
        }
    }
}
