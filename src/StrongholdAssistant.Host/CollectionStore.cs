using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenCvSharp;

namespace StrongholdAssistant.Host;

public sealed record CollectionMatch(string Id, string StartedAt, bool Ongoing, string? Mode,
    int Rounds, int Frames, int Answers, long Bytes);

// 单消费者队列：扫描只交付快照；图片编码、压缩和磁盘访问全部在工作线程完成。
internal sealed class CollectionStore
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
        public (byte[] Frame, DateTimeOffset At)? Rest;
    }

    public void Enable(bool value)
    {
        lock (_gate)
        {
            if (_enabled == value) return;
            _enabled = value;
            _generation++;
            Queue(() => { foreach (var s in _sessions.Values) s.Rest = null; });
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
                if (_sessions.TryGetValue(id, out var session) && session.Rest is not null)
                {
                    new Batch(this, session, DateTimeOffset.UtcNow).FlushRest();
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
            format = "stronghold-field-collection", version = 1,
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
            using var destination = new FileStream(path, FileMode.Create);
            using var zip = new ZipArchive(destination, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(_root, id), "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, Path.GetRelativePath(Path.Combine(_root, id), file).Replace('\\', '/'), CompressionLevel.Fastest);
            using var writer = new StreamWriter(zip.CreateEntry("说明.txt").Open(), Encoding.UTF8);
            // TODO(design): 确认采集包说明文字；当前按任务规定说明用途和不公开。
            writer.Write("本包包含本局游戏窗口截图、识别结果和玩家确认的名单，用于改进场上干员识别，不会公开。仅在本机导出，不会自动上传。");
        });
    }

    internal sealed class Batch
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
