using ASPAssistant.Core.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed record RosterScanStatus(bool Running, int Read,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);

public sealed partial class HostApi
{
    private readonly object _rosterGate = new();
    private Roster? _roster;
    private RosterReader? _rosterReader;
    private Func<RosterGameControl>? _rosterControl;
    private Func<bool, Task> _pauseRoster = _ => Task.CompletedTask;
    private CancellationTokenSource? _rosterStop;
    private Task _rosterTask = Task.CompletedTask;
    private string _rosterReason = "user";
    private DateTimeOffset? _rosterStarted;
    public bool IsRosterScanning => _rosterStop is not null;

    public void InitializeRoster(string data, IOcrEngine ocr, GameConnectionService connection, Func<bool, Task> pause)
    {
        _rosterReader = new(ocr, data);
        _rosterControl = () => new(connection);
        _pauseRoster = pause;
    }
    public Roster ReadRoster()
    {
        lock (_rosterGate)
        {
            var path = Path.Combine(DataDirectory, "roster.json");
            return _roster ??= UserJsonFile.Read(path, text => JsonSerializer.Deserialize<Roster>(text, JsonOptions))
                ?? new(null, [], []);
        }
    }
    private void SaveRoster(Roster value)
    {
        _roster = value;
        Directory.CreateDirectory(DataDirectory);
        UserJsonFile.Write(Path.Combine(DataDirectory, "roster.json"), JsonSerializer.Serialize(value, JsonOptions));

    }
    public Roster SetRosterOperator(string name, JsonElement patch)
    {
        var op = Catalog.GetProperty("operators").EnumerateArray().FirstOrDefault(o => o.GetProperty("name").GetString() == name);
        if (op.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("干员不存在。");
        Roster result;
        lock (_rosterGate)
        {
            var roster = ReadRoster();
            var old = roster.Operators.FirstOrDefault(o => o.Name == name) ?? new(name, null, null, null, null, "");
            var edited = (old.Edited ?? []).ToHashSet();
            foreach (var field in patch.EnumerateObject())
            {
                if (field.Name is not ("skin" or "skill" or "module" or "potential")) throw new ArgumentException("不支持的干员配置项。");
                if (field.Name == "potential")
                {
                    int? value = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.GetInt32();
                    if (value is < 1 or > 6) throw new ArgumentException("潜能必须是 1～6。");
                    old = old with { Potential = value };
                }
                else
                {
                    string? value = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.GetString();
                    string options = field.Name == "skin" ? "skins" : field.Name == "skill" ? "skills" : "modules";
                    if (value is not null && !(field.Name == "module" && value == "")
                        && (!op.TryGetProperty(options, out var values) || !values.EnumerateArray().Any(v => v.GetProperty("id").GetString() == value)))
                        throw new ArgumentException("配置项不属于这名干员。");
                    old = field.Name switch { "skin" => old with { Skin = value }, "skill" => old with { Skill = value }, _ => old with { Module = value } };
                }
                edited.Add(field.Name);
            }
            old = old with { Edited = edited.ToArray(), ReadAt = DateTimeOffset.UtcNow.ToString("O") };
            SaveRoster(roster with { Operators = roster.Operators.Where(o => o.Name != name).Append(old).ToArray() });
            result = _roster!;
        }
        Changed?.Invoke("roster", result);
        return result;
    }
    internal void MergeRoster(RosterReading reading, HashSet<string> seen, List<string> squad)
    {
        lock (_rosterGate)
        {
            var roster = ReadRoster(); var ops = roster.Operators.ToList();
            foreach (var card in reading.Cards)
            {
                seen.Add(card.Operator.Name);
                int index = ops.FindIndex(o => o.Name == card.Operator.Name);
                if (index < 0) ops.Add(card.Operator); else ops[index] = card.Operator;
                if (card.Custom && !squad.Contains(card.Operator.Name)) squad.Add(card.Operator.Name);
            }
            SaveRoster(roster with { Operators = ops.ToArray(), CustomSquad = roster.CustomSquad.Concat(squad).Distinct().ToArray() });
        }
        Changed?.Invoke("roster", ReadRoster());
        Changed?.Invoke("rosterScan", new RosterScanStatus(true, seen.Count));
    }
    public async Task StartRosterScanAsync()
    {
        if (IsRosterScanning) return;
        if (_rosterReader is null) throw new InvalidOperationException("识别尚未就绪，请稍候。");
        if (Connection.GameElevated == true && !ProcessElevation.Current)
            throw new InvalidOperationException("游戏是以管理员身份运行的，助手也要以管理员身份运行才能滑动列表。");
        var stop = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rosterTask = completion.Task;
        _rosterStarted = null;
        _rosterReason = "user";
        _rosterStop = stop;
        RosterGameControl? control = null;
        bool acquired = false;
        byte[]? first = null;
        try
        {
            await _scanGate.WaitAsync(stop.Token); acquired = true;
            await _pauseRoster(true);
            stop.Token.ThrowIfCancellationRequested();
            control = await Task.Run(_rosterControl!);
            first = await Task.Run(control.Capture);
            _rosterReader.HasPithStrategy = Match.StrategyId is { } strategyId ? strategyId == "band_pith" : null;
            var reading = await _rosterReader.ReadAsync(first);
            stop.Token.ThrowIfCancellationRequested();
            if (reading is null)
                throw new InvalidOperationException("先在游戏里打开卫戍协议的「物资调配」页，再点读取。");
            Changed?.Invoke("rosterScan", new RosterScanStatus(true, 0));
            _ = Task.Run(async () =>
            {
                try { await RunRosterAsync(control, first, stop); }
                finally { completion.TrySetResult(); }
            });
        }
        catch (Exception ex)
        {
            bool canceled = ex is OperationCanceledException && stop.IsCancellationRequested;
            string reason = _rosterReason;
            control?.Dispose();
            lock (_rosterGate) { _rosterStop = null; stop.Dispose(); }
            if (acquired) _scanGate.Release();
            await _pauseRoster(false);
            completion.TrySetResult();
            if (canceled)
            {
                Changed?.Invoke("rosterScan", new RosterScanStatus(false, 0, reason,
                    reason == "input" ? "检测到鼠标或键盘操作，读取停止。" : "已停止读取。"));
                return;
            }
            ReportRecognitionFailure(first, ex.Message, null, ["物资调配：检查首页；排序和列尚未读取"]);
            throw;
        }
    }
    public Task StopRosterScanAsync()
    {
        lock (_rosterGate) { _rosterReason = "user"; _rosterStop?.Cancel(); return _rosterTask; }
    }
    public void NotifyRosterInput()
    {
        lock (_rosterGate)
        {
            if (!IsRosterScanning || Connection.Source is "mumu" or "ldplayer"
                || _rosterStarted is { } started && DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1)) return;
            _rosterReason = "input"; _rosterStop?.Cancel();
        }
    }
    internal static async Task<RosterSort?> RecheckRosterSortAsync(RosterSort? sort,
        Func<Task<RosterSort?>> captureAndRead, bool requireTier, CancellationToken token)
    {
        for (int attempt = 0; attempt < 3 && (requireTier ? sort?.Mode != "tier" : sort is null); attempt++)
        {
            await Task.Delay(500, token);
            sort = await captureAndRead();
        }
        return sort;
    }

    internal void CompleteRosterScan(string reason, HashSet<string> seen, List<string> squad)
    {
        lock (_rosterGate)
        {
            var roster = ReadRoster();
            SaveRoster(roster with { ReadAt = DateTimeOffset.UtcNow.ToString("O"),
                Operators = reason == "end" ? seen.Select(n => roster.Operators.Single(o => o.Name == n)).ToArray() : roster.Operators,
                CustomSquad = reason == "end" ? squad.ToArray() : roster.CustomSquad });
        }
        Changed?.Invoke("roster", ReadRoster());
    }

    private async Task RunRosterAsync(RosterGameControl control, byte[] frame, CancellationTokenSource stop)
    {
        string reason = "end"; string? error = null;
        var seen = new HashSet<string>(); var squad = new List<string>();
        string step = "读取排序";
        RosterSort? sort = null;
        RosterReading? reading = null;
        void SaveFailure(string message, object? measuredColumns = null) => ReportRecognitionFailure(frame, message, null,
            [$"物资调配：{step}", $"排序：{JsonSerializer.Serialize(sort, JsonOptions)}",
             $"测量所得列：{JsonSerializer.Serialize(measuredColumns)}",
             $"已识别卡片列：{JsonSerializer.Serialize(reading?.Cards.Select(c => c.X).Distinct().Order().ToArray())}",
             $"未匹配名字：{JsonSerializer.Serialize(reading?.UnknownNames)}"]);
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            sort = await _rosterReader!.ReadSortAsync(frame);
            async Task<RosterSort?> CaptureSort()
            {
                frame = control.Capture();
                sort = null;
                sort = await _rosterReader.ReadSortAsync(frame);
                return sort;
            }
            sort = await RecheckRosterSortAsync(sort, CaptureSort, false, stop.Token);
            if (sort?.Mode == "covenant")
            {
                step = "切换并确认等阶排序";
                await control.SelectTierSortAsync(sort, stop.Token);
                sort = null;
                sort = await RecheckRosterSortAsync(sort, CaptureSort, true, stop.Token);
            }
            stop.Token.ThrowIfCancellationRequested();
            if (sort?.Mode != "tier") throw new IOException("无法确认物资调配页已按等阶排序。");
            // 两次不动才认定到头；回左端时同样逐屏确认页面。
            foreach (bool left in new[] { false, true })
            {
                int still = 0;
                var scroll = new RosterScroll();
                while (true)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (control.Lost) { reason = "lost"; break; }
                    step = "读取卡片";
                    reading = null;
                    _rosterReader!.HasPithStrategy = Match.StrategyId is { } strategyId ? strategyId == "band_pith" : null;
                    reading = await _rosterReader.ReadAsync(frame);
                    if (reading is null)
                    {
                        reason = "left";
                        SaveFailure("已离开物资调配页，读取停止。");
                        break;
                    }
                    if (left) MergeRoster(reading, seen, squad);
                    if (still >= 2) break;
                    var previous = frame;
                    lock (_rosterGate) { _rosterStarted ??= DateTimeOffset.UtcNow; }
                    step = "测量列间距";
                    int distance = scroll.NextDrag(frame, reading);
                    step = "拖动列表";
                    await control.DragAsync(left, distance, stop.Token);
                    step = "拖动后截图";
                    frame = control.Capture();
                    still = RosterReader.SamePosition(previous, frame) ? still + 1 : 0;
                }
                if (reason != "end") break;
            }
        }
        catch (OperationCanceledException) { reason = _rosterReason; }
        catch (Exception ex)
        {
            reason = control.Lost ? "lost" : "failed"; error = ex.Message;
            // 原来不记，玩家只看到「读取失败。」，诊断包里也查不到原因（v1.0.4 玩家诊断包）
            ASPAssistant.Core.AppLogger.Error("Roster", "物资调配读取失败", ex);
            SaveFailure(ex.Message, ex.Data["rosterColumns"]);
        }
        finally
        {
            control.Dispose();
            try
            {
                CompleteRosterScan(reason, seen, squad);
            }
            catch (Exception ex) { reason = "failed"; error = ex.Message; }
            lock (_rosterGate) { _rosterStop = null; stop.Dispose(); }
            _scanGate.Release();
            await _pauseRoster(false);
            string message = reason switch { "end" => "物资调配列表读取完成。", "user" => "已停止读取。", "left" => "已离开物资调配页，读取停止。", "input" => "检测到鼠标或键盘操作，读取停止。", "lost" => "游戏窗口丢失，读取停止。", _ => "读取失败。\n" + error };
            Changed?.Invoke("rosterScan", new RosterScanStatus(false, seen.Count, reason, message));
        }
    }
}
