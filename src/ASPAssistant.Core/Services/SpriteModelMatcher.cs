using System.Text.Json;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace ASPAssistant.Core.Services;

/// <summary>小人模型概率与逐名字模板最高分融合；归一化已包含在 ONNX 图内。</summary>
public sealed class SpriteModelMatcher : IDisposable
{
    public const double ModelWeight = .1;
    // 两局跨局预测分别校准为 .033005 / .096211，取较高值向上取整。
    public const double AutoFillGap = .09622;
    private readonly ThreadLocal<Net> _nets;
    private readonly double _modelWeight;
    private readonly Dictionary<string, int> _indices;

    public SpriteModelMatcher(string modelPath, string classesPath, double modelWeight = ModelWeight)
    {
        _modelWeight = modelWeight;
        _indices = JsonSerializer.Deserialize<string[]>(File.ReadAllText(classesPath))!
            .Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);
        // 读成字节再交给 OpenCV：按路径加载时用 ANSI 编码传路径，装在中文目录、系统又不是中文区域时会崩（v1.1.1 玩家日志）
        var model = File.ReadAllBytes(modelPath);
        _nets = new(() =>
        {
            var net = CvDnn.ReadNetFromOnnx(model) ?? throw new InvalidDataException("小人模型无法加载。");
            net.SetPreferableBackend(Backend.OPENCV);
            net.SetPreferableTarget(Target.CPU);
            return net;
        }, true);
    }

    public float[] Predict(Mat crop)
    {
        using var timing = FieldRecognitionTiming.Measure("model");
        if (crop.Empty() || crop.Width != 180 || crop.Height != 204 || crop.Type() != MatType.CV_8UC3)
            throw new ArgumentException("小人模型输入必须为 180×204 BGR 图块。", nameof(crop));
        using var blob = CvDnn.BlobFromImage(crop, 1d / 255, new Size(160, 192), swapRB: true, crop: false);
        var net = _nets.Value!;
        net.SetInput(blob);
        using var prediction = net.Forward();
        prediction.GetArray(out float[] values);
        return values;
    }

    internal IEnumerable<SpriteCandidate> LikelyOperators(Mat crop)
    {
        var logits = Predict(crop);
        double max = logits.Max();
        double total = logits.Sum(v => Math.Exp(v - max));
        return _indices.Select(p => new SpriteCandidate(p.Key, Math.Exp(logits[p.Value] - max) / total))
            .Where(p => p.Score >= .2).OrderByDescending(p => p.Score).Take(2);
    }

    public SpriteCandidate[] Fuse(Mat crop, IReadOnlyList<SpriteCandidate> templates)
    {
        if (templates.Count == 0) return [];
        return Fuse(Predict(crop), templates);
    }

    public string[] Shortlist(float[] logits, IEnumerable<string> names, int count)
        => names.Distinct().OrderByDescending(n => logits[_indices[n]]).Take(count).ToArray();

    internal string[] TemplateCandidates(float[] logits, string[] eligible, int count = 4)
    {
        var first = Shortlist(logits, eligible, count);
        // 被浮签、箭头盖住时，模型会把这些同阶外观排出前四；补比模板仍受本局候选池限制。
        return first.Concat(eligible.Where(name => name switch
        {
            "凯瑟琳" or "罗比菈塔" => first.Contains("风絮"),
            "慕斯" => first.Contains("陨星"),
            "砾" => first.Contains("瑰盐"),
            "地灵" => first.Contains("拜松") || first.Contains("慕斯"),
            _ => false
        })).Distinct().ToArray();
    }

    internal bool Confident(float[] logits, string[] eligible, string winner)
        => eligible.Length > 1 && eligible.Sum(n => Math.Exp((double)logits[_indices[n]] - logits[_indices[winner]])) <= 1 / .99;

    internal bool StronglySupports(float[] logits, SpriteCandidate[] templates, string name)
    {
        // 以本次实际参与融合的名字为分母，与 Fuse 的概率口径一致。
        double winner = logits[_indices[name]];
        return templates.Sum(c => Math.Exp(logits[_indices[c.Name]] - winner)) <= 1 / .9;
    }

    public SpriteCandidate[] Fuse(float[] logits, IReadOnlyList<SpriteCandidate> templates)
    {
        if (templates.Count == 0) return [];
        double max = templates.Max(c => (double)logits[_indices[c.Name]]);
        var weights = templates.Select(c => Math.Exp(logits[_indices[c.Name]] - max)).ToArray();
        double sum = weights.Sum();
        return templates.Select((c, i) => new SpriteCandidate(c.Name,
            (1 - _modelWeight) * c.Score + _modelWeight * weights[i] / sum))
            .OrderByDescending(c => c.Score).ToArray();
    }

    // 采集读取本次推理值；不额外运行模型。
    public double[] Probabilities(float[] logits, IReadOnlyList<SpriteCandidate> templates)
    {
        if (templates.Count == 0) return [];
        double max = templates.Max(c => (double)logits[_indices[c.Name]]);
        var weights = templates.Select(c => Math.Exp(logits[_indices[c.Name]] - max)).ToArray();
        double sum = weights.Sum();
        return weights.Select(w => w / sum).ToArray();
    }

    public void Dispose() { foreach (var net in _nets.Values) net.Dispose(); _nets.Dispose(); }
}
