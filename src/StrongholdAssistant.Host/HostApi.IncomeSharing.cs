using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core;

namespace StrongholdAssistant.Host;

internal sealed record IncomeReport(string Device, string Mode, bool Solo, string? StrategyId,
    int Fingerprint, RoundMoney[] Samples);
internal sealed record IncomeTable(int Fingerprint, string UpdatedAt, IncomeTableRow[] Rows);
internal sealed record IncomeTableRow(string Mode, bool Solo, int Round, int Base);

public sealed partial class HostApi
{
    private static readonly HttpClient IncomeHttp = new();
    internal HttpClient IncomeClient { get; set; } = IncomeHttp;
    internal Task IncomeReportTask { get; private set; } = Task.CompletedTask;
    private Dictionary<string, int?> _sharedMoney = [];
    private readonly Dictionary<int, int> _roundSamples = [];

    // 与界面分享码一致：按目录顺序对 UTF-8 做 FNV-1a，取低 16 位。
    internal int DataFingerprint
    {
        get
        {
            var values = Catalog.GetProperty("covenants").EnumerateArray().Select(c => c.GetProperty("id").GetString())
                .Concat(Catalog.GetProperty("operators").EnumerateArray()
                    .Where(o => !o.TryGetProperty("diy", out var diy) || diy.ValueKind != JsonValueKind.True)
                    .Select(o => o.GetProperty("name").GetString()))
                .Concat(Catalog.GetProperty("equipment").EnumerateArray().Select(e => e.GetProperty("name").GetString()))
                .Concat(Catalog.GetProperty("strategies").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
            uint hash = 0x811c9dc5;
            foreach (byte b in Encoding.UTF8.GetBytes(string.Join("|", values))) hash = unchecked((hash ^ b) * 0x01000193);
            return (int)(hash & 0xffff);
        }
    }

    private string ShareId()
    {
        var path = Path.Combine(DataDirectory, "share-id.json");
        if (!File.Exists(path)) ResetShareId();
        return JsonSerializer.Deserialize<string>(File.ReadAllText(path))!;
    }

    public void ResetShareId() => SaveHistoryFile("share-id.json", Guid.NewGuid().ToString());

    private void CompleteHistory()
    {
        var local = _history.SingleOrDefault(r => r.Id == _activeRecord)?.Local;
        _activeRecord = null;
        if (local?.Mode is null || EconomyRule(local.StrategyId) is not null) return;
        var samples = _roundSamples.Where(p => p.Key is >= 1 and <= 15 && p.Value >= 0).OrderBy(p => p.Key)
            .Select(p => new RoundMoney(p.Key, p.Value)).ToArray();
        foreach (var sample in samples)
        {
            string key = $"{local.Mode.Id}|{local.Solo}|{sample.Round}";
            if (!_baseMoney.TryGetValue(key, out int old) || sample.StartMoney < old)
                _baseMoney[key] = sample.StartMoney;
        }
        if (samples.Length == 0) return;
        SaveHistoryFile("base-money.json", _baseMoney);
        if (Settings.ShareIncome)
        {
            var report = new IncomeReport(ShareId(), local.Mode.Id, local.Solo, local.StrategyId, DataFingerprint, samples);
            IncomeReportTask = ReportIncomeAsync(report);
        }
    }

    private async Task ReportIncomeAsync(IncomeReport report)
    {
        try
        {
            using var response = await IncomeClient.PostAsJsonAsync("https://sa.lubeiluchen.com/api/income", report, JsonOptions);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AppLogger.Warn("Income", $"资金上报失败：{ex.Message}");
        }
    }

    private void LoadIncomeTable()
    {
        var path = Path.Combine(DataDirectory, "income-table.json");
        if (!File.Exists(path)) return;
        try { UseIncomeTable(JsonSerializer.Deserialize<IncomeTable>(File.ReadAllText(path), JsonOptions)!); }
        catch (JsonException) { }
    }

    private bool UseIncomeTable(IncomeTable table)
    {
        if (table is null || table.Fingerprint != DataFingerprint || table.Rows is null
            || table.Rows.Any(r => r is null || string.IsNullOrEmpty(r.Mode) || r.Round is < 1 or > 30 || r.Base is < 0 or > 200)) return false;
        _sharedMoney = table.Rows.GroupBy(r => $"{r.Mode}|{r.Solo}|{r.Round}")
            .ToDictionary(g => g.Key, g => (int?)g.Min(r => r.Base));
        return true;
    }

    public async Task DownloadIncomeTableAsync()
    {
        try
        {
            var table = await IncomeClient.GetFromJsonAsync<IncomeTable>("https://sa.lubeiluchen.com/api/income-table", JsonOptions);
            lock (_historyGate)
            {
                if (!UseIncomeTable(table!)) return;
                SaveHistoryFile("income-table.json", table);
                if (Match.Economy is { } economy) PublishIncome(economy);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            // 下载失败保留启动时读取的上一份资金表。
        }
    }
}
