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
    private int? _boardRound;
    private Size _boardSize;
    private bool _boardRoutesEnabled;
    private BoardRoutes? _boardRoutes;
    private string? _boardMode;
    private int _nonRestReadings;
    private byte[]? _latestBoardFrame;
    private string? _recognitionLogState, _queryLogState;
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
            var result = recognizer.RecognizeDetailed(frame, map, camera);
            LogBoardRecognition(result);
            return result.Match;
        };
    }

    internal void ObserveBoardFrame(byte[]? frame, EconomyReading? reading) => ObserveBoardFrame(frame, reading, true);

    internal void ObserveBoardFrame(byte[]? frame, EconomyReading? reading, bool shopVisible)
    {
        lock (_boardGate)
        {
            if (!_boardRoutesEnabled) return;
            if (frame is null)
            {
                ClearBoard();
                return;
            }
            if (reading is { IsRest: false })
            {
                if (++_nonRestReadings >= 3) ClearBoard();
                return;
            }
            if (reading is { IsRest: true }) _nonRestReadings = 0;
            // OCR 降频或暂时没读到回合，不把它当作离开休整期。
            if (reading is not { IsRest: true, Round: >= 1 and <= 15 }) return;
            using var stream = new MemoryStream(frame);
            using var image = Image.FromStream(stream);
            if (_boardRound != reading.Round || _boardSize != image.Size)
            {
                _boardCameras.Clear();
                _boardMap = null;
                _boardRound = reading.Round;
                _boardSize = image.Size;
            }
            var camera = shopVisible ? BoardCamera.Expanded : BoardCamera.Collapsed;
            if (!_boardCameras.TryGetValue(camera, out var board))
            {
                board = RecognizeBoard(frame, _boardMap, camera);
                if (board is not null)
                {
                    _boardMap = board.MapId;
                    _boardCameras[board.Camera] = board;
                }
            }
            BoardRoutes? routes = null;
            if (board is not null)
            {
                routes = _boardRouteStore.Find(board, reading.Round.Value, Match.Solo, Match.BossId, Match.Mode?.Id, out var reason);
                var query = $"查询 map={board.MapId} mode={Match.Mode?.Id ?? (Match.Solo ? "mode_single_20" : "mode_multi_normal")} round={reading.Round} solo={Match.Solo} boss={Match.BossId ?? "null"} routes={routes?.Routes.Count ?? 0} reason={reason ?? "成功"}";
                if (query != _queryLogState) { BoardLog(query); _queryLogState = query; }
            }
            PublishBoardRoutes(routes);
        }
    }

    internal void LogBoardRecognition(BoardRecognition result)
    {
        var state = $"{result.MapId}/{result.Camera}/{result.Rejection}/{result.Width}x{result.Height}";
        if (state == _recognitionLogState) return;
        var now = BoardClock();
        if (result.Match is null && _lastFailedBoardLog is { } last && now - last < TimeSpan.FromSeconds(10)) return;
        _recognitionLogState = state;
        if (result.Match is null) _lastFailedBoardLog = now;
        BoardLog($"识别 {(result.Match is null ? "失败" : "成功")} map={result.MapId} camera={result.Camera} score={result.Score:F3} gap={result.Gap?.ToString("F3") ?? "n/a"} reason={result.Rejection ?? "通过"} size={result.Width}x{result.Height}");
    }

    internal byte[]? GetDiagnosticBoardFrame()
    {
        lock (_boardGate) return _boardRoutesEnabled ? _latestBoardFrame : null;
    }

    private void ClearBoard()
    {
        _nonRestReadings = 0;
        _boardCameras.Clear();
        _boardMap = null;
        _boardRound = null;
        PublishBoardRoutes(null);
    }

    private void PublishBoardRoutes(BoardRoutes? next)
    {
        bool same = next is null && _boardRoutes is null;
        if (next is not null && _boardRoutes is { } previous)
            same = next.MapId == previous.MapId && next.Camera == previous.Camera && next.Round == previous.Round
                && next.Solo == previous.Solo && next.BossId == previous.BossId && _boardMode == Match.Mode?.Id
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
