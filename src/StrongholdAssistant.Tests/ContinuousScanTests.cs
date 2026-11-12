using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class ContinuousScanTests
{
    private static Task<HostApi> CreateApi() => HostApi.CreateAsync(
        Path.Combine(AppContext.BaseDirectory, "data"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreeFailuresStopWithSummary(bool succeedFirst)
    {
        var api = await CreateApi();
        int captures = 0;
        Func<Task<byte[]?>> capture = () => Task.FromResult<byte[]?>(++captures == 1 && succeedFirst ? [1] : null);
        api.ReadInformation = async () => await capture() is null
            ? throw new RecognitionException("未找到信息页。")
            : (api.Match, new[] { "阿米娅" });
        var events = new List<ScanStatus>();
        var ended = new TaskCompletionSource<ScanStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Changed += (_, value) =>
        {
            if (value is not ScanStatus scan) return;
            events.Add(scan);
            if (scan.Continuous == false) ended.SetResult(scan);
        };

        await api.InvokeAsync("scanContinuous", [JsonSerializer.SerializeToElement(true)]);
        var final = await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(succeedFirst ? 4 : 3, captures);
        Assert.False(api.IsContinuous);
        Assert.Equal(succeedFirst, final.Ok);
        Assert.Equal(succeedFirst, api.Match.BansScanned);
        Assert.Equal(succeedFirst
            ? "连续 3 次没找到本局信息页，持续识别已结束。本次新识别到 1 名禁用干员，共 1 名。"
            : "没找到本局信息页，持续识别已结束。请先打开开局信息页或对局详情再点「持续识别」。", final.Message);
        Assert.Equal(captures * 2 + 1, events.Count);
        Assert.All(events.SkipLast(1), scan => { Assert.True(scan.Continuous); Assert.Null(scan.Message); });
        Assert.Equal(Enumerable.Range(0, captures * 2).Select(i => i % 2 == 0), events.SkipLast(1).Select(s => s.Busy));
    }

    [Fact]
    public async Task StopWaitsForCurrentCaptureAndMergesPages()
    {
        var api = await CreateApi();
        int captures = 0;
        var secondCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task<byte[]>> capture = async () =>
        {
            if (++captures == 2)
            {
                secondCapture.SetResult();
                await releaseCapture.Task;
            }
            return [(byte)captures];
        };
        api.ReadInformation = async () => (api.Match, (await capture())[0] == 1
            ? new[] { "阿米娅" } : new[] { "阿米娅", "芬" });
        var scans = new List<ScanStatus>();
        var matches = new List<MatchInfo>();
        api.Changed += (_, value) =>
        {
            if (value is ScanStatus scan) scans.Add(scan);
            if (value is MatchInfo match) matches.Add(match);
        };
        await api.InvokeAsync("scanContinuous", [JsonSerializer.SerializeToElement(true)]);
        await secondCapture.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await api.InvokeAsync("scanContinuous", [JsonSerializer.SerializeToElement(true)]);
        var error = await Assert.ThrowsAsync<RecognitionException>(() => api.InvokeAsync("rescan", []));
        Assert.Equal("正在持续识别，请先停止。", error.Message);
        var stopping = api.InvokeAsync("scanContinuous", [JsonSerializer.SerializeToElement(false)]);
        Assert.False(stopping.IsCompleted);
        releaseCapture.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, captures);
        Assert.Equal(new[] { "阿米娅", "芬" }.Order(), api.Match.BannedOperators.Order());
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.True(match.BansScanned));
        Assert.Equal(new ScanStatus(false, "持续识别已停止，本次新识别到 2 名禁用干员，共 2 名。", true, false), scans.Last());
        Assert.Single(scans, s => s.Continuous == false);
        Assert.False(api.IsContinuous);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BansScannedSurvivesRescanAndClearsForNewMatch(bool solo)
    {
        var api = await CreateApi();
        api.ReadOpeningMode = () => Task.FromResult<ASPAssistant.Core.Models.GameModeInfo?>(null);
        api.ReadInformation = () => Task.FromResult((api.Match, Array.Empty<string>()));
        await api.ScanContinuousAsync(true);
        await api.StopContinuousAsync();
        Assert.True(api.Match.BansScanned);

        // 手动重扫即便读到不同盟约或首领，也不改变来源标记。
        api.ReadInformation = () => Task.FromResult((api.Match with { BossId = "next" }, Array.Empty<string>()));
        await api.InvokeAsync("rescan", []);
        Assert.True(api.Match.BansScanned);
        await api.HandleOpeningAsync(false);
        Assert.True(api.Match.BansScanned);

        api.ReadInformation = () => Task.FromResult((api.Match with { BossId = "new" }, Array.Empty<string>()));
        await api.HandleOpeningAsync(solo);
        Assert.False(api.Match.BansScanned);
        await api.ScanContinuousAsync(true);
        await api.StopContinuousAsync();
        Assert.True(api.Match.BansScanned);
        await api.ResetMatchAsync();
        Assert.False(api.Match.BansScanned);
        await api.InvokeAsync("rescan", []);
        Assert.False(api.Match.BansScanned);
    }
}
