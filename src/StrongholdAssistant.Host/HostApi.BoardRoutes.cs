using System.Drawing;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private readonly object _boardGate = new();
    private BoardRouteStore _boardRouteStore = null!;
    internal Func<byte[], string?, BoardCamera?, BoardMatch?> RecognizeBoard { get; set; } = null!;
    private readonly Dictionary<BoardCamera, BoardMatch> _boardCameras = new();
    private string? _boardMap;
    private string? _boardPlayerSide;
    private int? _boardRound;
    private int? _acceptedBoardRound;
    private readonly RoundJumpConfirmation _boardJump = new();
    private string? _roundRejectionState;
    private Size _boardSize;
    private bool _boardRoutesEnabled;
    private BoardRoutes? _boardRoutes;
    private string? _boardMode;
    private int _nonRestReadings;
    private byte[]? _latestBoardFrame;
    private string? _recognitionLogState, _queryLogState, _filterLogState;
    private bool? _boardMainScreen;
    private DateTimeOffset? _lastFailedBoardLog;
    internal Action<string> BoardLog { get; set; } = message => AppLogger.Info("Board", message);
    internal Func<DateTimeOffset> BoardClock { get; set; } = () => DateTimeOffset.UtcNow;
    public event Action<BoardRoutes?>? BoardRoutesChanged;
    public event Action<bool>? RouteHotkeyChanged;

    internal void PublishRouteHotkey(bool down)
    {
        BoardLog($"快捷键 PublishRouteHotkey down={down}");
        RouteHotkeyChanged?.Invoke(down);
    }

    public bool BoardRoutesEnabled
    {
        get { lock (_boardGate) return _boardRoutesEnabled; }
        set
        {
            lock (_boardGate)
            {
                if (_boardRoutesEnabled == value) return;
                _boardRoutesEnabled = value;
                BoardLog($"BoardRoutesEnabled={value}");
                if (!value) _latestBoardFrame = null;
                if (!value) ClearBoard();
            }
        }
    }

    public void InitializeBoardRoutes(string directory)
    {
        _boardRouteStore = new BoardRouteStore(directory);
        var recognizer = new BoardRecognizer(directory);
        RecognizeBoard = (frame, map, camera) =>
        {
            _latestBoardFrame = frame;
            var result = recognizer.RecognizeDetailed(frame, map, camera,
                _boardRound is { } round && _boardRouteStore.IsBossRound(round, Match.Solo, Match.Mode?.Id), _boardPlayerSide);
            LogBoardRecognition(result);
            return result.Match;
        };
    }

    internal void ObserveBoardFrame(byte[]? frame, EconomyReading? reading) => ObserveBoardFrame(frame, reading, true);

    internal void ObserveBoardFrame(byte[]? frame, EconomyReading? reading, bool shopVisible, bool mainScreen = true)
    {
        lock (_boardGate)
        {
            if (!_boardRoutesEnabled) return;
            if (frame is null)
            {
                _boardJump.Reset();
                ClearBoard();
                return;
            }
            if (_boardMainScreen != mainScreen)
            {
                _boardMainScreen = mainScreen;
                BoardLog($"休整期主画面={mainScreen} reason={(mainScreen ? "就绪控件可见" : "有弹窗或就绪控件不可见")}");
            }
            if (!mainScreen)
            {
                if (reading is { IsRest: false }) _boardJump.Reset();
                PublishBoardRoutes(null);
                return;
            }
            if (reading is { IsRest: false })
            {
                _boardJump.Reset();
                if (++_nonRestReadings >= 3) ClearBoard();
                return;
            }
            if (reading is { IsRest: true }) _nonRestReadings = 0;
            // OCR 降频或暂时没读到回合，不把它当作离开休整期。
            if (reading is not { IsRest: true }) return;
            int? round = reading.IsFinalRound ? _boardRouteStore.LastRound(Match.Solo, Match.Mode?.Id) : reading.Round;
            if (round is not (>= 1 and <= 15)) { _boardJump.Reset(); return; }
            if (!_boardJump.Accept(_acceptedBoardRound, round.Value, BoardClock()))
            {
                string rejection = $"拒绝回合 round={round} confirmed={_acceptedBoardRound} reason=同局只接受当前或下一回合";
                if (rejection != _roundRejectionState) { BoardLog(rejection); _roundRejectionState = rejection; }
                return;
            }
            _roundRejectionState = null;
            _acceptedBoardRound = round;
            using var stream = new MemoryStream(frame);
            using var image = Image.FromStream(stream);
            if (_boardRound != round || _boardSize != image.Size)
            {
                _boardCameras.Clear();
                _boardRound = round;
                _boardSize = image.Size;
            }
            var camera = shopVisible ? BoardCamera.Expanded : BoardCamera.Collapsed;
            if (!_boardCameras.TryGetValue(camera, out var board))
            {
                board = RecognizeBoard(frame, _boardMap, camera);
                if (board is not null)
                {
                    _boardMap = board.MapId;
                    if (_boardPlayerSide is null && board.Camera == BoardCamera.Expanded && board.PlayerSide is not null)
                    {
                        _boardPlayerSide = board.PlayerSide;
                        PublishBoardView();
                        // 收起镜头可能先独立判过侧边；展开确认后按本局侧边重新识别。
                        _boardCameras.Clear();
                    }
                    _boardCameras[board.Camera] = board;
                }
            }
            BoardRoutes? routes = null;
            if (board is not null)
            {
                routes = _boardRouteStore.Find(board, round.Value, Match.Solo, Match.BossId, Match.Mode?.Id, out var reason);
                var query = $"查询 map={board.MapId} mode={Match.Mode?.Id ?? (Match.Solo ? "mode_single_20" : "mode_multi_normal")} round={round} solo={Match.Solo} boss={Match.BossId ?? "null"} routes={routes?.Routes.Count ?? 0} reason={reason ?? "成功"}";
                if (query != _queryLogState) { BoardLog(query); _queryLogState = query; }
                if (routes is not null) routes = FilterBoardRoutes(routes);
            }
            PublishBoardRoutes(routes);
        }
    }

    internal BoardRoutes FilterBoardRoutes(BoardRoutes routes)
    {
        var match = Match;
        var possible = _placementAdapter.RoundStreams(routes.Round, match.Mode?.Id, match.Solo, match.EnemyTypes, match.BossId);
        var selected = possible;
        string reason = possible.Count == 0 ? "缺怪组数据，不过滤" : "本局可能怪组";
        if (possible.Count > 0 && match.RoundEnemies is { } known && known.Round == routes.Round && known.Enemies.Length > 0)
        {
            var matching = possible.Where(s => known.Enemies.All(e => s.Spawns.Any(p => p.Profile.Name == e.Name))).ToArray();
            if (matching.Length > 0) { selected = matching; reason = "本回合名单匹配"; }
            else reason = "名单未匹配，回到全部可能怪组";
        }
        // SimSpawn.Flying 来自现有敌人资料的 motion == FLY，不能用怪组的 Flying 代替逐只判断。
        bool fly = selected.SelectMany(s => s.Spawns).Any(e => e.Flying);
        bool walk = selected.SelectMany(s => s.Spawns).Any(e => !e.Flying);
        var kept = possible.Count == 0 ? routes.Routes : routes.Routes.Where(r => r.Boss || (r.Motion == "fly" ? fly : walk)).ToArray();
        var removed = routes.Routes.Except(kept).Select(r => r.Motion).Distinct().ToArray();
        string log = $"过滤 round={routes.Round} possible={possible.Count} kept={selected.Count} removed={(removed.Length == 0 ? "无" : string.Join(",", removed))} reason={reason}";
        if (log != _filterLogState) { BoardLog(log); _filterLogState = log; }
        return removed.Length == 0 ? routes : routes with { Routes = kept };
    }

    private void RefreshBoardRoutes()
    {
        lock (_boardGate)
        {
            if (!_boardRoutesEnabled || _boardRoutes is not { } previous) return;
            // 已确认的回合可能先由经济扫描更新；沿用当前投影立即推送，下一帧仍按原流程校准棋盘。
            int round = Match.Economy?.Round == previous.Round + 1 ? previous.Round + 1 : previous.Round;
            var board = new BoardMatch(previous.MapId, previous.Camera, previous.Projection, 0, previous.PlayerSide);
            var routes = _boardRouteStore.Find(board, round, Match.Solo, Match.BossId, Match.Mode?.Id);
            PublishBoardRoutes(routes is null ? null : FilterBoardRoutes(routes));
        }
    }

    internal void LogBoardRecognition(BoardRecognition result)
    {
        var state = $"{result.MapId}/{result.Camera}/{result.Rejection}/{result.Width}x{result.Height}";
        if (state == _recognitionLogState) return;
        var now = BoardClock();
        if (result.Match is null && _lastFailedBoardLog is { } last && now - last < TimeSpan.FromSeconds(10)) return;
        _recognitionLogState = state;
        if (result.Match is null)
        {
            _lastFailedBoardLog = now;
            if (_latestBoardFrame is { } frame)
            {
                try { _diagnostics.SaveBoardFailure(frame, _boardRound, result, Match.Solo, Match.BossId, now); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
                {
                    AppLogger.Error("Diagnostics", "保存棋盘失败现场失败", ex);
                }
            }
        }
        BoardLog($"识别 {(result.Match is null ? "失败" : "成功")} map={result.MapId} camera={result.Camera} score={result.Score:F3} side={result.Match?.PlayerSide ?? "null"} left={result.LeftScore?.ToString("F3") ?? "n/a"} right={result.RightScore?.ToString("F3") ?? "n/a"} gap={result.Gap?.ToString("F3") ?? "n/a"} reason={result.Rejection ?? "通过"} size={result.Width}x{result.Height}");
    }

    internal byte[]? GetDiagnosticBoardFrame()
    {
        lock (_boardGate) return _boardRoutesEnabled ? _latestBoardFrame : null;
    }

    private void ClearBoard()
    {
        _nonRestReadings = 0;
        _boardCameras.Clear();
        _boardRound = null;
        PublishBoardRoutes(null);
    }

    private void ResetBoardRound()
    {
        lock (_boardGate)
        {
            _boardMap = null;
            _boardPlayerSide = null;
            _acceptedBoardRound = null;
            _diagnostics.ResetBoardFailures();
            _boardJump.Reset();
            _roundRejectionState = null;
            ClearBoard();
        }
    }

    private void PublishBoardRoutes(BoardRoutes? next)
    {
        bool same = next is null && _boardRoutes is null;
        if (next is not null && _boardRoutes is { } previous)
            same = next.MapId == previous.MapId && next.Camera == previous.Camera && next.Round == previous.Round
                && next.PlayerSide == previous.PlayerSide && next.Solo == previous.Solo && next.BossId == previous.BossId && _boardMode == Match.Mode?.Id
                && next.Routes.SequenceEqual(previous.Routes) && next.BossSpots.SequenceEqual(previous.BossSpots)
                && new[] { (0d, 0d), (6d, 4d) }.All(p =>
                {
                    var a = next.Projection.Project(p.Item1, p.Item2);
                    var b = previous.Projection.Project(p.Item1, p.Item2);
                    // 小于约一像素的采样抖动不触发重绘；比例变化仍更新投影。
                    return Math.Abs(a.X - b.X) < .001 && Math.Abs(a.Y - b.Y) < .001;
                });
        if (same) return;
        _boardRoutes = next;
        _boardMode = Match.Mode?.Id;
        BoardLog(next is null ? "覆盖层结果=null" : $"覆盖层结果=路线 map={next.MapId} camera={next.Camera} round={next.Round} routes={next.Routes.Count}");
        BoardRoutesChanged?.Invoke(next);
    }
}
