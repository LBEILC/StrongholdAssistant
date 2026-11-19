using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record CollectionMatch(string Id, string StartedAt, bool Ongoing, string? Mode,
    int Rounds, int Frames, int Answers, long Bytes);

// 单消费者队列：扫描只交付快照；图片编码、压缩和磁盘访问全部在工作线程完成。
internal sealed partial class CollectionStore
{
    private readonly string _root;
    public CollectionStore(string root) => _root = root;
    private readonly object _gate = new();
    private Task _work = Task.CompletedTask;
    private int _generation;
    private bool _enabled;
    private string? _active;
    private readonly Dictionary<string, Session> _sessions = [];
    internal sealed class Session(string id, string startedAt)
    {
        public string Id = id, StartedAt = startedAt;
        public string? Mode;
        public int Rounds, Frames, Answers, Sequence;
        public HashSet<string> Once = [];
        public HashSet<string> AnswerCells = [];
        public DateTimeOffset TimerAt = DateTimeOffset.MinValue;
        public long Bytes;
        public object? Context;
        public List<CollectedRound> RoundRows = [];
        public byte[]? RoundEndFrame;
        public bool WaitingForOpeningShop;
        public Dictionary<FieldCell, string> Confirmed = [];
        public IReadOnlyDictionary<int, string>? LastShop;
        public IReadOnlyDictionary<int, string>? LastOperators;
        public CollectedResult? Result;
        public (byte[] Frame, DateTimeOffset At)? Rest;
    }

    public void Enable(bool value)
    {
        lock (_gate)
        {
            if (_enabled == value) return;
            _enabled = value;
            _generation++;
            Queue(() =>
            {
                foreach (var s in _sessions.Values)
                {
                    new Batch(this, s, DateTimeOffset.UtcNow).FlushRound();
                    s.Rest = null;
                    s.LastShop = null;
                }
            }, quiet: true);
        }
    }

    private Task Queue(Action action, bool quiet = false)
    {
        lock (_gate)
        {
            return _work = _work.ContinueWith(_ =>
            {
                try { action(); }
                // 本地文件和图片编码是边界；失败跳过，不重试、不进入产品日志。
                catch (Exception e) when (quiet && e is (IOException or UnauthorizedAccessException or JsonException
                    or OpenCVException or System.Runtime.InteropServices.ExternalException)) { }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    public Task DrainAsync() { lock (_gate) return _work; }

    public void End(string? id)
    {
        lock (_gate)
        {
            if (_active == id) _active = null;
            if (!_enabled || id is null) return;
            int generation = _generation;
            Queue(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                if (_sessions.TryGetValue(id, out var session))
                {
                    var batch = new Batch(this, session, DateTimeOffset.UtcNow);
                    batch.FlushRest();
                    batch.FlushRound();
                    SaveMeta(session);
                }
            }, quiet: true);
        }
    }

    public void Add(string id, string startedAt, string? mode, DateTimeOffset at, object context,
        Action<Batch> write)
    {
        lock (_gate)
        {
            if (!_enabled) return;
            int generation = _generation;
            _active = id;
            Queue(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                bool created = !_sessions.TryGetValue(id, out var session);
                if (created)
                {
                    session = new(id, startedAt);
                    _sessions[id] = session;
                    Directory.CreateDirectory(Path.Combine(_root, id));

                }
                session!.Mode = mode; session.Context = context;
                var batch = new Batch(this, session, at);
                write(batch);
                batch.Save();
                SaveMeta(session);
                foreach (var old in created ? Read().Skip(3) : [])
                {
                    Directory.Delete(Path.Combine(_root, old.Id), true);
                    _sessions.Remove(old.Id);
                }
            }, quiet: true);
        }
    }

    private void SaveMeta(Session s)
    {
        var directory = Path.Combine(_root, s.Id);
        File.WriteAllText(Path.Combine(directory, "meta.json"), JsonSerializer.Serialize(new
        {
            format = "stronghold-field-collection", version = 2,
            match = new CollectionMatch(s.Id, s.StartedAt, false, s.Mode, s.Rounds, s.Frames, s.Answers, s.Bytes),
            context = s.Context
        }, HostApi.JsonOptions), Encoding.UTF8);
        s.Bytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
    }

    private CollectionMatch[] Read()
    {
        if (!Directory.Exists(_root)) return [];
        return Directory.EnumerateDirectories(_root).Select(directory =>
        {
            var file = Path.Combine(directory, "meta.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var match = doc.RootElement.GetProperty("match").Deserialize<CollectionMatch>(HostApi.JsonOptions)!;
            return match with { Ongoing = match.Id == _active,
                Bytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) };
        }).OfType<CollectionMatch>().OrderByDescending(m => m.StartedAt).ToArray();
    }

    public async Task<CollectionMatch[]> ListAsync()
    {
        CollectionMatch[] matches = [];
        await Queue(() => matches = Read().Take(3).ToArray());
        return matches;
    }

    public Task ClearAsync()
    {
        lock (_gate)
        {
            _generation++;
            return Queue(() =>
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
                _sessions.Clear();
            });
        }
    }

    public Task ExportAsync(string id, string path)
    {
        // id 只能指向已列出的局，不能用用户输入拼任意路径。
        return Queue(() =>
        {
            if (!Read().Any(m => m.Id == id)) throw new ArgumentException("采集记录不存在。");
            if (_sessions.TryGetValue(id, out var session))
                new Batch(this, session, DateTimeOffset.UtcNow).FlushRound();
            using var destination = new FileStream(path, FileMode.Create);
            using var zip = new ZipArchive(destination, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(_root, id), "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, Path.GetRelativePath(Path.Combine(_root, id), file).Replace('\\', '/'), CompressionLevel.Fastest);
            using var writer = new StreamWriter(zip.CreateEntry("说明.txt").Open(), Encoding.UTF8);
            // TODO(design): 确认采集包说明文字；当前按任务规定说明用途和不公开。
            writer.Write("本包包含本局游戏窗口截图、识别结果和玩家确认的名单，用于改进场上干员识别，不会公开。仅在本机导出，不会自动上传。");
            using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, id, "meta.json")));
            if (meta.RootElement.GetProperty("version").GetInt32() == 2)
                writer.WriteLine("""

                    采集格式 v2
                    rounds.jsonl 每行一个回合。round、mode、map、boss、enemies、strategy、covenants、banned 分别为回合号、模式、地图、首领、本回合敌人、策略、盟约及禁用。
                    start 和 end 是休整期首末次观察到的棋盘读数，startAt 和 endAt 是对应帧的采集时间。hp 为顶栏耐久，money 为资金，level 为商店等级。
                    hpChange = 下一回合 start.hp − 本回合 end.hp。负值表示顶栏耐久减少。联防阶段可能切换到全队耐久，请结合截图判断。leaks 为画面明确显示的漏怪数。跳回合、缺读数和最后一回合均不计算差值。
                    openingShop 按固定卡位记录开店时可见的干员和装备，0～5 从左到右；缺号是空位或未识别。首帧无卡名时可用持续读到相同资金期间第一次可读的商店，openingShopAt 为实际采集时间。refreshes 和 purchases 仅统计商店变化、资金减少支持的刷新次数和买入干员，可能漏记。upgraded 表示观察到商店等级上升。
                    units 为休整末的场上和备战区阵容。zone、col、row 是区域及格坐标，operator、tier、elite、equipment 为干员、阶数、精二及装备。confirmed 为玩家修改或整盘确认过的干员；其余是自动观察。只看见占用时保留格坐标和阶数。
                    restStart、restEnd、enemyPanel 为本回合休整首帧、末帧、实际出现的敌人面板，均为 1280 像素宽的 JPEG。没有看到的面板不补图。
                    result.json 在看到结算证据时生成。reachedRound 为到达回合，cleared 为是否通关，finalHp 为结算时可见的顶栏耐久，trophies、score、rank 分别为本局获得奖杯、评分、名次。null 表示未取得对应证据。
                    """);
        });
    }

    internal sealed partial class Batch
    {
        private readonly CollectionStore _owner;
        private readonly Session _session;
        private readonly DateTimeOffset _at;
        private readonly List<string> _lines = [];
        internal Batch(CollectionStore owner, Session session, DateTimeOffset at)
        { _owner = owner; _session = session; _at = at; }
        public void Log(string kind, object data) => _lines.Add(JsonSerializer.Serialize(new { at = _at, kind, data }, HostApi.JsonOptions));
        public void Round(int? round) => _session.Rounds = Math.Max(_session.Rounds, round ?? 0);
        public void Answers(IEnumerable<string> cells)
        { _session.AnswerCells.UnionWith(cells); _session.Answers = _session.AnswerCells.Count; }
        private string FileName(string reason, string extension, DateTimeOffset? capturedAt = null)
            => $"{Math.Max(0, ((capturedAt ?? _at) - DateTimeOffset.Parse(_session.StartedAt)).TotalSeconds).ToString("0000.0", CultureInfo.InvariantCulture)}-{++_session.Sequence:D6}-{reason}.{extension}";
        public string? Image(byte[] bytes, string reason, string folder = "frames", bool once = false, DateTimeOffset? capturedAt = null)
        {
            if (once && !_session.Once.Add(reason)) return null;
            string directory = Path.Combine(_owner._root, _session.Id, folder);
            Directory.CreateDirectory(directory);
            string name = FileName(reason, folder == "raw" ? "png" : "jpg", capturedAt);
            using var image = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (folder == "raw") File.WriteAllBytes(Path.Combine(directory, name), image.ImEncode(".png"));
            else
            {
                using var scaled = new Mat();
                if (folder == "frames") Cv2.Resize(image, scaled, new Size(1920, (int)Math.Round(image.Height * 1920d / image.Width)));
                File.WriteAllBytes(Path.Combine(directory, name), (folder == "frames" ? scaled : image)
                    .ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 90)));
            }
            if (folder == "frames") _session.Frames++;
            return folder + "/" + name;
        }
        public void Observe(byte[] frame, bool rest, bool main)
        {
            if (rest && main) _session.Rest = (frame, _at);
            else if (!rest) FlushRest();
        }
        internal void FlushRest()
        {
            if (_session.Rest is { } last)
            {
                var previous = new Batch(_owner, _session, last.At);
                previous.Image(last.Frame, "rest");
                _session.Rest = null;
            }
        }
        public void Timer(byte[] frame)
        {
            int interval = _session.Bytes > 40 * 1024 * 1024 ? 600 : 120;
            if ((_at - _session.TimerAt).TotalSeconds < interval) return;
            Image(frame, "timer"); _session.TimerAt = _at;
        }
        internal void Save()
        {
            if (_lines.Count == 0) return;
            using var file = new FileStream(Path.Combine(_owner._root, _session.Id, "log.jsonl.gz"), FileMode.Append);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
            foreach (var line in _lines) writer.WriteLine(line);
        }
    }
}
