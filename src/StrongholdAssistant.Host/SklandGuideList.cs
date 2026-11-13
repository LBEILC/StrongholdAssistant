using System.Globalization;
using System.Text.Json;

namespace StrongholdAssistant.Host;

public sealed record SklandGuideQuery(string Section, string? Sort = null, string? Keyword = null,
    string? Core = null, string[]? Extras = null, string? StrategyId = null, string? PageToken = null);
public sealed record SklandGuideStats(double Likes, double Collections, double Views);
public sealed record SklandGuideItem(string Id, string Url, string Title, string Author, string EditedAt,
    string PhaseName, SklandGuideStats Stats, bool Collected, SklandGuide? Guide);
public sealed record SklandGuidePage(SklandGuideItem[] Items, string? PageToken, bool LoginRequired);
internal sealed record SklandGuideRequest(string Method, Dictionary<string, object> Parameters);

public static partial class SklandParser
{
    internal static SklandGuideRequest GuideQuery(SklandGuideQuery query, JsonElement resources, JsonElement catalog)
    {
        var method = query.Section switch
        {
            "all" => "getGuideList", "collected" => "getMyCollectedList", "published" => "getMyGuideList",
            _ => throw new ArgumentException("森空岛攻略分段无效。")
        };
        if (query.Sort is not (null or "recommended" or "latest")) throw new ArgumentException("森空岛攻略排序无效。");
        var parameters = new Dictionary<string, object>
        {
            ["seasonId"] = Text(resources, "seasonId"), ["pageSize"] = 10, ["pageToken"] = query.PageToken ?? ""
        };
        if (query.Section != "published") parameters["sortType"] = query.Section == "all" && query.Sort == "latest" ? 2 : 1;
        var keyword = query.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword)) parameters["titleKeyword"] = string.Concat(keyword.EnumerateRunes().Take(40));
        if (query.Section == "all")
        {
            string? Bond(string? id)
            {
                var local = Elements(catalog, "covenants").FirstOrDefault(c => Option(c, "id") == id);
                var name = Option(local, "name");
                return name is null ? null : Option(Elements(resources, "factions").FirstOrDefault(f => Option(f, "name") == name), "id");
            }
            if (Bond(query.Core) is { } core) parameters["seasonBondId"] = core;
            var extras = (query.Extras ?? []).Select(Bond).OfType<string>().Distinct().ToArray();
            if (extras.Length > 0) parameters["regularBondIds"] = extras;
            if (query.StrategyId is { } strategy && Elements(catalog, "strategies").Any(s => Option(s, "id") == strategy)
                && Strings(resources, "strategyIds").Contains(strategy)) parameters["strategyId"] = strategy;
        }
        return new(method, parameters);
    }

    internal static SklandGuidePage ParseGuidePage(string json, string? requestedToken, bool loginRequired, bool decoded = true)
        => ParseResponse(() =>
        {
            using var document = JsonDocument.Parse(json);
            var data = decoded ? document.RootElement : ReadData(document.RootElement, "森空岛攻略列表读不出来，稍后再试。");
            var items = data.GetProperty("list").EnumerateArray().Select(item =>
            {
                var content = item.GetProperty("guide");
                var id = Text(content, decoded ? "id" : "guideId");
                var url = NormalizeGuideUrl("https://game.skland.com/arknights/stronghold-guide/detail?guideId=" + id);
                var author = Option(Optional(item, "poster"), "nickname") ?? "";
                SklandGuide? guide = null;
                var invalid = decoded ? Optional(item, "isInvalid").ValueKind == JsonValueKind.True
                    : Optional(item, "invalid").GetInt32() != 0;
                if (!invalid && Option(content, "versionId") is not null)
                {
                    // 外部列表可能裁剪正文；卡片仍展示，但不能导入不完整的攻略。
                    try { guide = ParseGuideContent(content, author, url, decoded); }
                    catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException) { }
                }
                var stats = Optional(item, decoded ? "stats" : "rts");
                var timestamp = Number(content, decoded ? "latestEditAt" : "latestEditAtTs");
                var editedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(decoded ? timestamp : timestamp * 1000))
                    .UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                return new SklandGuideItem(id, url, Option(content, "title") ?? "", author, editedAt,
                    Option(Optional(item, decoded ? "season" : "seasonTag"), "phaseName") ?? "",
                    new(Number(stats, "liked"), Number(stats, "collected"), Number(stats, "viewed")),
                    !loginRequired && Optional(Optional(item, decoded ? "relation" : "rel"), "collected").ValueKind == JsonValueKind.True, guide);
            }).ToArray();
            var token = Option(data, "pageToken");
            return new SklandGuidePage(items, !loginRequired && Optional(data, "hasMore").ValueKind == JsonValueKind.True
                && token != requestedToken ? token : null, loginRequired);
        }, "森空岛攻略列表的数据结构已变化，暂时无法读取。");

    private static double Number(JsonElement value, string name)
    {
        var number = Optional(value, name);
        return number.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? 0
            : number.ValueKind == JsonValueKind.Number ? number.GetDouble()
            : double.Parse(number.GetString()!, CultureInfo.InvariantCulture);
    }
}

// 排队与空闲寿命共用一把锁；时钟和关闭动作可在测试中替换。
internal sealed class SklandGuideQueue(Action close, TimeProvider clock, SynchronizationContext? context = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITimer? _timer;
    private DateTimeOffset _lastCall;
    private int _pending;
    internal static readonly TimeSpan IdleTime = TimeSpan.FromMinutes(5);

    public async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        Interlocked.Increment(ref _pending);
        _lastCall = clock.GetUtcNow();
        await _gate.WaitAsync();
        _timer?.Dispose();
        _timer = null;
        try { return await call(); }
        finally
        {
            Interlocked.Decrement(ref _pending);
            var remaining = IdleTime - (clock.GetUtcNow() - _lastCall);
            _timer = clock.CreateTimer(_ =>
            {
                if (context is null) _ = CloseIfIdleAsync();
                else context.Post(async _ => await CloseIfIdleAsync(), null);
            }, null, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    internal async Task CloseIfIdleAsync()
    {
        await _gate.WaitAsync();
        try { if (_pending == 0 && clock.GetUtcNow() - _lastCall >= IdleTime) Close(); }
        finally { _gate.Release(); }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try { Close(); }
        finally { _gate.Release(); }
    }

    private void Close() { _timer?.Dispose(); _timer = null; close(); }
}
