using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class SoloModeTests(EnemyRecognitionFixture fixture)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static Task<byte[]> Image(string name) => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", name));
    private static (int Width, int Height) Size(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    [Fact]
    public async Task BansRequireAllCoreOrAllAdditionalWhenThereIsNoCore()
    {
        var operators = await new JsonDataStore(Data).LoadOperatorsAsync();
        var banned = OperatorBanCalculator.Compute(operators,
            new HashSet<string> { "谢拉格", "阿戈尔", "叙拉古", "卡西米尔", "伊比利亚" },
            new HashSet<string> { "坚守", "奇迹", "不屈", "聚合", "奥术" });
        string[] expected = ["极光", "雪猎", "耶拉", "初雪", "灵知", "圣聆初雪", "凛御银灰", "角峰",
            "幽灵鲨", "深巡", "斯卡蒂", "歌蕾蒂娅", "归溟幽灵鲨", "乌尔比安", "浊心斯卡蒂", "克莱门莎", "水月", "安哲拉", "摆渡人", "遥"];
        Assert.All(expected, name => Assert.Contains(name, banned));
        Assert.All(new[] { "哈洛德", "锏", "复奏", "薇薇安娜", "隐德来希" }, name => Assert.DoesNotContain(name, banned));
    }

    public static IEnumerable<object[]> DetectionSamples()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.png"))
        {
            var name = Path.GetFileName(path);
            bool solo = name.StartsWith("solo-opening-") || name.StartsWith("mumu-solo-opening-");
            if (solo || new[] { "ban-sample-", "user-ban-", "info-ingame-", "info-ui100-", "mumu-solo-detail-top-" }.Any(name.StartsWith))
                yield return [name, solo];
        }
    }

    [Theory]
    [MemberData(nameof(DetectionSamples))]
    public async Task DetectsOnlySoloOpening(string name, bool expected)
    {
        var bytes = await Image(name);
        var (w, h) = Size(bytes);
        var (png, width, height) = ScreenshotNormalizer.EnsureAnalysable(bytes, w, h);
        var detector = new InformationScreenDetector(fixture.Engine, new GarrisonOcrStrategy());
        Assert.Equal(expected, await detector.IsSoloOpeningAsync(png, width, height));
    }

    [Fact]
    public async Task SoloOpeningClearsMatchAndDetailAutomaticallyStartsOnlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var api = await HostApi.CreateAsync(Data, directory);
        byte[] shot = await Image("solo-opening-level1-1920x1080.png");
        int captures = 0;
        await api.InitializeRecognitionAsync(Data, fixture.Engine, () => { captures++; return Task.FromResult<byte[]?>(shot); });
        using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
            new ShopReader(fixture.Engine, []), fixture.Engine, new ASPAssistant.Core.GameState.GameState(), () => []);
        scanner.AwaitingDetail = () => api.Match.AwaitingDetail;
        Task automatic = Task.CompletedTask;
        int openings = 0, details = 0;
        scanner.BanScreenDetected += solo => { openings++; automatic = api.HandleOpeningAsync(solo); };
        scanner.DetailScreenDetected += () => { details++; automatic = api.HandleDetailAsync(); };
        // 直接把样图交给扫描循环的检测步骤；不启动计时器或调用真实截屏。
        async Task Tick()
        {
            var (w, h) = Size(shot);
            await (Task)typeof(OcrScannerService).GetMethod("DetectBanScreenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(scanner, [shot, w, h])!;
            await automatic;
        }
        try
        {
            api.ApplyRecognizedMatch(api.Match with { BossId = "old", EnemyTypes = ["old"] }, ["初雪"]);
            api.SetCovenantIncomplete("kjerag", true);
            api.SetOperatorBanned("泡泡", true);
            await Tick(); await Tick(); await Tick();
            Assert.Equal(1, openings);
            Assert.Equal(1, captures);
            Assert.Equal("mode_single_1", api.Match.Mode?.Id);
            Assert.True(api.Match.Solo);
            Assert.True(api.Match.AwaitingDetail);
            Assert.Equal("none", api.Match.Source);
            Assert.Null(api.Match.BossId);
            Assert.Null(api.Match.RecognizedAt);
            Assert.Empty(api.Match.EnemyTypes);
            Assert.Empty(api.Match.BannedOperators);
            Assert.Empty(api.Match.BanGroups);
            Assert.All(api.Match.Covenants, c => Assert.False(c.Incomplete));
            var json = JsonSerializer.SerializeToElement(api.Match, HostApi.JsonOptions);
            Assert.True(json.GetProperty("solo").GetBoolean());
            Assert.True(json.GetProperty("awaitingDetail").GetBoolean());

            shot = await Image("mumu-solo-detail-top-1763x992.png");
            var recognized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scans = new List<ScanStatus>();
            api.Changed += (_, value) => {
                if (value is ScanStatus status) scans.Add(status);
                if (value is MatchInfo { Source: "recognized" }) recognized.TrySetResult();
            };
            await Tick();
            Assert.True(api.IsContinuous);
            Assert.False(api.Match.AwaitingDetail);
            await recognized.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await api.StopContinuousAsync();
            Assert.True(api.Match.Solo);
            Assert.Contains(scans, s => s.Busy && s.Continuous == true);
            Assert.Equal("mode_single_1", api.Match.Mode?.Id);
            Assert.Contains(scans, s => s.Continuous == false);
            await Tick();
            Assert.Equal(1, details);
            Assert.False(api.IsContinuous);

            // 沿用原有状态机：两帧实际对局画面重新允许下一局开局。
            shot = await Image("gameplay-markers-1920x1080.png");
            await Tick(); await Tick();
            shot = await Image("ban-sample-1920x1080.png");
            await Tick(); await Tick();
            Assert.Equal(2, openings);
            Assert.False(api.Match.Solo);
            Assert.False(api.Match.AwaitingDetail);
            Assert.Equal("recognized", api.Match.Source);
            Assert.Equal("mode_multi_hard", api.Match.Mode?.Id);
            Assert.True(captures >= 2);
        }
        finally { await api.StopContinuousAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RescanRejectsSoloOpeningAndSuccessfulManualReadConsumesWaiting()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var api = await HostApi.CreateAsync(Data, directory);
        byte[] shot = await Image("mumu-solo-opening-1763x992.png");
        await api.InitializeRecognitionAsync(Data, fixture.Engine, () => Task.FromResult<byte[]?>(shot));
        try
        {
            await api.HandleOpeningAsync(true);
            Assert.Equal("mode_single_16", api.Match.Mode?.Id);
            var error = await Assert.ThrowsAsync<RecognitionException>(() => api.InvokeAsync("rescan", []));
            Assert.Equal("这是单人模式的开局页：选好盟约进入对局后，点顶部回合数左边的放大镜打开对局详情再识别。", error.Message.Split('\n')[0]);
            Assert.True(api.Match.AwaitingDetail);
            shot = await Image("mumu-solo-detail-top-1763x992.png");
            await api.InvokeAsync("rescan", []);
            Assert.True(api.Match.Solo);
            Assert.False(api.Match.AwaitingDetail);
            await api.ResetMatchAsync();
            Assert.Null(api.Match.Mode);
            Assert.False(api.Match.Solo);
            Assert.False(api.Match.AwaitingDetail);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
