using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;
using System.Text.Json;

namespace StrongholdAssistant.Tests;

public sealed class StrategyAdviceTests
{
    [Fact]
    public async Task Level16PushIncludesSortedPoolAndBothAveragesAndResetClearsIt()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName);
            var pushed = new TaskCompletionSource<StrategyAdvice>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.Changed += (kind, value) =>
            {
                if (kind == "match" && value is MatchInfo { StrategyAdvice: { Status: "ok" } advice })
                    pushed.TrySetResult(advice);
            };
            api.ReadInformation = () => Task.FromResult((api.Match with { Mode = new("mode_single_16", "规程等级16") },
                Array.Empty<string>()));
            await api.InvokeAsync("rescan", []);
            var result = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("规程等级16", result.Basis);
            Assert.Equal(60, result.Items.Select(i => i.StrategyId).Distinct().Count());
            Assert.Equal(60, result.Items.Length);
            Assert.Equal(result.Items.OrderByDescending(i => i.ClearRate), result.Items);
            Assert.Equal(result.Items.Average(i => i.ClearRate), result.Average.ClearRate);
            Assert.Equal(result.Items.Average(i => i.ReachedRound), result.Average.ReachedRound);

            await api.InvokeAsync("resetMatch", []);
            var match = JsonSerializer.SerializeToElement(await api.InvokeAsync("match", []), HostApi.JsonOptions);
            Assert.Equal(JsonValueKind.Null, match.GetProperty("strategyAdvice").ValueKind);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("mode_multi_funny", "标准模拟")]
    [InlineData("mode_multi_normal", "险境模拟")]
    [InlineData("mode_multi_hard", "绝境模拟")]
    [InlineData("mode_multi_abyss", "终极模拟")]
    public async Task MultiplayerPushExplainsWhyAdviceIsUnavailable(string mode, string label)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName);
            var pushed = new TaskCompletionSource<StrategyAdvice>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.Changed += (kind, value) =>
            {
                if (kind == "match" && value is MatchInfo { StrategyAdvice: { } advice })
                    pushed.TrySetResult(advice);
            };
            api.ReadInformation = () => Task.FromResult((api.Match with { Mode = new(mode, label) }, Array.Empty<string>()));
            await api.InvokeAsync("rescan", []);
            var result = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("unavailable", result.Status);
            Assert.Equal("多人模式的推荐还在训练，暂不提供", result.Reason);
            Assert.Equal(label, result.Basis);
            Assert.Empty(result.Items);
            Assert.Equal(new StrategyAdviceAverage(0, 0), result.Average);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task UnknownModeExplainsWhyAdviceIsUnavailable()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName);
            var result = api.BuildStrategyAdvice(new MatchState { GameDataVersion = api.GetMatchState().GameDataVersion });
            Assert.Equal("unavailable", result.Status);
            Assert.Equal("还没认出本局模式", result.Reason);
        }
        finally { directory.Delete(true); }
    }
}
