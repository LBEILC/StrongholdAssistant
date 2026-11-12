using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class IncomeSharingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
    private static void Open(HostApi api) => api.ApplyRecognizedMatch(api.Match with { Mode = new("mode_multi_hard", "联机困难") }, [], opening: true);
    private static void Read(HostApi api, int round, int money) => api.ObserveEconomy(new(round, money, 1, 5, true));

    private sealed class FakeHttp : HttpMessageHandler
    {
        public IncomeTable Table { get; set; } = new(0x981d, "2026-11-12T00:00:00Z", []);
        public List<IncomeReport> Reports { get; } = [];
        public bool Fail { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("sa.lubeiluchen.com", request.RequestUri!.Host);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/api/income", request.RequestUri.AbsolutePath);
                Reports.Add((await request.Content!.ReadFromJsonAsync<IncomeReport>(HostApi.JsonOptions, cancellationToken))!);
            }
            else Assert.Equal("/api/income-table", request.RequestUri.AbsolutePath);
            if (Fail) throw new HttpRequestException("测试网络失败");
            return request.Method == HttpMethod.Post ? new(HttpStatusCode.NoContent)
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(Table, options: HostApi.JsonOptions) };
        }
    }

    [Fact]
    public async Task ManualStrategyUpdatesPredictionHistoryAndExcludesWholeMatchFromLearning()
    {
        var api = await CreateApi();
        Open(api);
        Read(api, 1, 4);
        Read(api, 2, 6);
        Open(api);
        Read(api, 1, 5);
        int events = 0;
        api.Changed += (name, _) => { if (name == "match") events++; };
        var match = Assert.IsType<MatchInfo>(await api.InvokeAsync("setMatchStrategy", [JsonSerializer.SerializeToElement("band_cannot")]));
        Assert.Equal("carry", match.Economy!.Rule);
        Assert.Equal(13, match.Economy.Next);
        Assert.Contains(api.History(), h => h.Local!.StrategyId == "band_cannot");
        api.ApplyRecognizedMatch(api.Match with { StrategyId = null }, []);
        Assert.Equal("band_cannot", api.Match.StrategyId);
        api.SetMatchStrategy(null);
        Assert.Null(api.Match.Economy!.Rule);
        Assert.Equal(7, api.Match.Economy.Next);
        Assert.True(events >= 2);
        Assert.Equal("策略不存在。", Assert.Throws<ArgumentException>(() => api.SetMatchStrategy("invalid")).Message);
        Read(api, 2, 0);
        api.SetMatchStrategy("band_fund");
        Open(api);
        Assert.Null(api.Match.StrategyId);
        Read(api, 1, 4);
        Assert.Equal(6, api.Match.Economy!.Next);
        Assert.Equal("own", api.Match.Economy.Basis);
    }

    [Fact]
    public async Task SharedTableUsesMinimumCachesAndRejectsWrongFingerprint()
    {
        var api = await CreateApi();
        using var handler = new FakeHttp();
        using var http = new HttpClient(handler);
        api.IncomeClient = http;
        Assert.Equal(0x981d, api.DataFingerprint);
        Open(api);
        Read(api, 1, 4);
        Read(api, 2, 8);
        Open(api);
        handler.Table = handler.Table with { Rows = [new("mode_multi_hard", false, 1, 5), new("mode_multi_hard", false, 2, 6)] };
        await api.DownloadIncomeTableAsync();
        Read(api, 1, 4);
        Assert.Equal(6, api.Match.Economy!.Next);
        Assert.Equal("shared", api.Match.Economy.Basis);
        Read(api, 2, 8);
        Assert.Equal(2, api.Match.Economy.Extra);
        api = await CreateApi();
        api.IncomeClient = http;
        handler.Fail = true;
        await api.DownloadIncomeTableAsync();
        Open(api);
        Read(api, 1, 4);
        Assert.Equal(6, api.Match.Economy!.Next);
        handler.Fail = false;
        handler.Table = handler.Table with { Fingerprint = 0, Rows = [new("mode_multi_hard", false, 2, 1)] };
        await api.DownloadIncomeTableAsync();
        Assert.Equal(6, api.Match.Economy.Next);
        handler.Table = handler.Table with { Fingerprint = api.DataFingerprint, Rows = [new("mode_multi_hard", false, 1, 4), new("mode_multi_hard", false, 2, 10)] };
        await api.DownloadIncomeTableAsync();
        Assert.Equal(8, api.Match.Economy.Next);
        Assert.Equal("own", api.Match.Economy.Basis);
        await File.WriteAllTextAsync(Path.Combine(_directory, "income-table.json"), JsonSerializer.Serialize(handler.Table with { Fingerprint = 0 }, HostApi.JsonOptions));
        api = await CreateApi();
        api.ApplyRecognizedMatch(api.Match with { Mode = new("mode_single_1", "规程等级1") }, []);
        Read(api, 1, 4);
        Assert.Equal("none", api.Match.Economy!.Basis);
    }

    [Fact]
    public async Task ReportSubtractsIncomeSkipsEconomicOrUnknownModeAndResetsDevice()
    {
        var api = await CreateApi();
        using var handler = new FakeHttp();
        using var http = new HttpClient(handler);
        api.IncomeClient = http;
        api.SaveSettings(api.Settings with { ShareIncome = true });
        Open(api);
        Read(api, 1, 4);
        var income = api.AddIncome("精打细算玩偶", true).Economy!.Incomes.Single();
        Read(api, 2, 10);
        api.RemoveIncome(income.Id);
        Open(api);
        await api.IncomeReportTask;
        var report = Assert.Single(handler.Reports);
        Assert.Equal(new[] { new RoundMoney(1, 4), new RoundMoney(2, 8) }, report.Samples);
        Assert.Equal(0x981d, report.Fingerprint);
        Assert.True(Guid.TryParse(report.Device, out _));
        Read(api, 1, 0);
        api.SetMatchStrategy("band_fund");
        Open(api);
        await api.IncomeReportTask;
        Assert.Single(handler.Reports);
        api.ApplyRecognizedMatch(api.Match with { Mode = null }, []);
        Read(api, 1, 4);
        Open(api);
        Assert.Single(handler.Reports);
        await api.InvokeAsync("resetShareId", []);
        Read(api, 1, 5);
        handler.Fail = true;
        Open(api);
        await api.IncomeReportTask;
        Assert.Equal(2, handler.Reports.Count);
        Assert.NotEqual(report.Device, handler.Reports[1].Device);
        Assert.Equal(handler.Reports[1].Device, JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(Path.Combine(_directory, "share-id.json"))));
        api.SaveSettings(api.Settings with { ShareIncome = false });
        Read(api, 1, 5);
        Open(api);
        Assert.Equal(2, handler.Reports.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
