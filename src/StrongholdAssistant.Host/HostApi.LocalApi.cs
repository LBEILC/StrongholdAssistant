using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using ASPAssistant.Core.Control;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private LocalHttpServer? _localServer;
    private readonly Dictionary<string, JobEntry> _jobs = [];
    private string? _latestJob;
    private bool _localApiClosed;
    private string ApiProfile => new DirectoryInfo(DataDirectory) is { Parent.Name: "profiles" } dir ? dir.Name : "default";
    private object ControlApiStatus() => new { address = _localServer?.Address, profile = ApiProfile };

    internal void ConfigureLocalApi()
    {
        if (Settings.Control.Api && !_localApiClosed)
            _localServer ??= new LocalHttpServer(DataDirectory, ApiProfile, DispatchLocalAsync);
        else
        {
            _localServer?.Dispose();
            _localServer = null;
        }
    }

    internal void CloseLocalApi()
    {
        _localApiClosed = true;
        ConfigureLocalApi();
    }

    internal static GameAction[] PlacementMoves(PlacementResult? plan, FieldState? field)
    {
        if (plan is null) throw new InvalidOperationException("还没有站位推荐");
        if (plan.BasedOn != field?.UpdatedAt) throw new InvalidOperationException("场上变了，等重新算完再摆");
        var actions = PlacementActions.From(plan, field!).ToArray();
        if (actions.Length == 0) throw new InvalidOperationException("现在就是推荐站位，不用挪");
        return actions;
    }

    private sealed class JobEntry(ControlJob job)
    {
        public ControlJob Latest = job;
        public TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal void RecordControlJob(ControlJob job)
    {
        lock (_jobs)
        {
            if (!_jobs.TryGetValue(job.Id, out var entry))
            {
                if (job.Mode != "dryRun") BeginFieldActions();
                if (_jobs.Count == 20) _jobs.Remove(_jobs.Values.MinBy(x => x.Latest.StartedAt)!.Latest.Id);
                _jobs[job.Id] = entry = new(job);
            }
            _latestJob = job.Id;
            if (entry.Latest.State != "done" && job.State == "done" && job.Mode != "dryRun") CompleteFieldActions(job);
            entry.Latest = job;
            if (job.EndedAt != null) entry.Finished.TrySetResult();
        }
    }

    private async Task<ControlJob?> ReadJobAsync(string id, string? wait, CancellationToken token)
    {
        int seconds = 0;
        if (wait != null && (!int.TryParse(wait, out seconds) || seconds < 0))
            throw new LocalApiError(400, "等待秒数无效");
        JobEntry entry;
        lock (_jobs)
        {
            if (id == "current" && _latestJob == null) return null;
            if (!_jobs.TryGetValue(id == "current" ? _latestJob! : id, out entry!))
                throw new LocalApiError(404, "找不到这批操作");
        }
        if (seconds > 0)
        {
            try { await entry.Finished.Task.WaitAsync(TimeSpan.FromSeconds(Math.Min(seconds, 120)), token); }
            catch (TimeoutException) { }
        }
        lock (_jobs) return entry.Latest;
    }

    private Task<object?> DispatchLocalAsync(string method, Uri uri, byte[] body, CancellationToken token)
    {
        Task<object?> Route()
        {
            token.ThrowIfCancellationRequested();
            return RouteLocalAsync(method, uri, body, token, RunActions);
        }
        return DialogOwner?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()
            ? dispatcher.InvokeAsync(Route, System.Windows.Threading.DispatcherPriority.Normal, token).Task.Unwrap()
            : Route();
    }

    internal async Task<object?> RouteLocalAsync(string method, Uri uri, byte[] body, CancellationToken token,
        Func<GameAction[], string?, string, ControlJob> run)
    {
        var path = uri.AbsolutePath;
        var control = Settings.Control;
        switch ((method, path))
        {
            case ("GET", "/v1/info"):
                var connected = Connection.State == "connected";
                var game = connected ? _controlCapture?.Connection : null;
                return new { profile = ApiProfile, version = typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0],
                    source = game?.State.Source, window = game?.State.Source == "pc" ? (long?)game.State.Window : null,
                    serial = game?.ConnectedDevice?.Serial, control = new { control.Enabled, control.External, control.Mode } };
            case ("GET", "/v1/schedule"): return GetSchedule();
            case ("GET", "/v1/turn"): return await ReadTurnAsync(uri, token);
            case ("GET", "/v1/page"): return await ReadMenuPageAsync();
            case ("GET", "/v1/state"): return GetMatchState();
            case ("GET", "/v1/field"):
                return (object?)await WaitForFieldAsync(_matchRest?.Economy.Round, FirstFieldWait, token)
                    ?? new { source = "unknown", reason = FieldTimeout };
            case ("POST", "/v1/field/recognize"): return await RecognizeFieldAsync();
            case ("GET", "/v1/placement"): return Placement;
            case ("GET", "/v1/screenshot"):
                if (Connection.State != "connected" || _controlCapture is null) throw new InvalidOperationException("还没连上游戏");
                var capture = _controlCapture.Connection.IsAdb ? _controlCapture.Connection.CaptureAdbScreen(int.MaxValue)
                    : _controlCapture.CaptureWindowScreen();
                if (capture == null) throw new InvalidOperationException("游戏截图失败");
                using (var input = new MemoryStream(capture))
                using (var frame = Image.FromStream(input))
                using (var output = new MemoryStream())
                {
                    frame.Save(output, ImageFormat.Png);
                    return output.ToArray();
                }
            case ("POST", "/v1/stop"): StopControl("api"); return _control?.Current;
            case ("POST", "/v1/actions"):
            case ("POST", "/v1/placement/run"):
                if (!control.Enabled || !control.External) throw new LocalApiError(403, "助手里没有允许外部程序操作");
                if (_control?.Current?.State is "running" or "waitingConfirm") throw new LocalApiError(409, "上一批操作还没做完");
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new LocalApiError(400, "请求体应为对象");
                    string? mode = null;
                    if (root.TryGetProperty("mode", out var m))
                    {
                        if (m.ValueKind != JsonValueKind.String || m.GetString() is not ("dryRun" or "confirm" or "auto"))
                            throw new LocalApiError(400, "执行模式无效");
                        mode = m.GetString();
                    }
                    if (path.EndsWith("/run")) return run(PlacementMoves(Placement, Field), mode, "api");
                    if (!root.TryGetProperty("actions", out var a) || a.ValueKind != JsonValueKind.Array)
                        throw new LocalApiError(400, "请提供 actions 动作数组");
                    var actions = a.EnumerateArray().Select(ReadAction).ToArray();
                    // 策略确认后可能已经跳到自选，后台扫描仍在处理上一张策略帧。
                    var openingPage = actions is [PickCustomAction] && _phase == "strategy" && _controlCapture != null
                        ? (await ReadMenuPageAsync()).Page : null;
                    lock (_fieldGate)
                    {
                        var state = GetMatchState();
                        var projected = new ActionState(state, Field?.Round == state.Round.Value ? Field : null,
                            _matchRest?.Economy.RemainingDeploy);
                        for (int i = 0; i < actions.Length; i++)
                        {
                            if (actions[i] is PickStrategyAction pick &&
                                _openingStrategies.FirstOrDefault(s => s.Name == pick.StrategyId) is { } named)
                                actions[i] = new PickStrategyAction(named.Id);
                            if (projected.Apply(actions[i], openingPage ?? _phase, true) is { } problem)
                                throw new LocalApiError(422, $"第 {i + 1} 个动作：{problem}", i);
                        }
                    }
                    return run(actions, mode, "api");
                }
        }
        if (method == "GET" && path.StartsWith("/v1/jobs/", StringComparison.Ordinal))
            return await ReadJobAsync(path[9..], System.Web.HttpUtility.ParseQueryString(uri.Query)["wait"], token);
        throw new LocalApiError(404, "找不到这个接口");
    }

    private async Task<MenuPage> ReadMenuPageAsync()
    {
        if (Connection.State != "connected" || _controlCapture is null) throw new InvalidOperationException("还没连上游戏");
        var connection = _controlCapture.Connection;
        var bytes = connection.IsAdb ? connection.CaptureAdbScreen(int.MaxValue) : _controlCapture.CaptureWindowScreen();
        if (bytes == null) throw new InvalidOperationException("游戏截图失败");
        using var ocr = new ASPAssistant.Core.Services.MaaOcrEngine(Path.Combine(ASPAssistant.Core.PackageLayout.Root, "data"));
        return await MenuPages.IdentifyAsync(ocr, bytes);
    }

    private static (Type, string[]) MenuContract(string? type) => type switch
    {
        "startSolo" => (typeof(StartSoloAction), ["level"]),
        "leaveResult" => (typeof(LeaveResultAction), []),
        _ => throw new LocalApiError(400, "未知动作类型")
    };

    // 构造参数缺失时反序列化会补零，不能把残缺的动作当作第一张卡或第一个选项。
    private static GameAction ReadAction(JsonElement action)
    {
        if (action.ValueKind != JsonValueKind.Object || !action.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
            throw new LocalApiError(400, "动作缺少 type");
        (Type Type, string[] Required) contract = t.GetString() switch
        {
            "buy" => (typeof(BuyAction), ["slot"]),
            "sell" => (typeof(SellAction), ["from"]),
            "move" => (typeof(MoveAction), ["from", "to"]),
            "face" => (typeof(FaceAction), ["at", "facing"]),
            "freeze" => (typeof(FreezeAction), ["on"]),
            "pickStrategy" => (typeof(PickStrategyAction), ["strategyId"]),
            "pickAdapt" => (typeof(PickAdaptAction), ["index"]),
            "pickCustom" => (typeof(PickCustomAction), ["tier", "operators"]),
            "refresh" => (typeof(RefreshAction), []),
            "ready" => (typeof(ReadyAction), []),
            "upgrade" => (typeof(UpgradeAction), []),
            _ => MenuContract(t.GetString())
        };
        foreach (var name in contract.Required)
        {
            if (!action.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                throw new LocalApiError(400, $"动作缺少字段 {name}");
            if (name is "at" or "from" or "to" && (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("zone", out _) || !value.TryGetProperty("row", out _) || !value.TryGetProperty("col", out _)))
                throw new LocalApiError(400, "格子需要 zone、col 和 row");
        }
        // 外部 JSON 的 type 可以出现在任意位置。
        return (GameAction)action.Deserialize(contract.Type, JsonOptions)!;
    }
}

