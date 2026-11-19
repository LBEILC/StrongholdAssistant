using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record StrategyEstimate(string StrategyId, int Rank, double ClearProbability, double ExpectedRound);
public sealed record StrategyRecommendation(StrategyEstimate[] Candidates, string[] Assumptions);

public sealed partial class HostApi
{
    private string? _gameDataVersion;
    private Lazy<StrategyModel?> _strategyModel = null!;
    private Lazy<StrategyModel?> _multiStrategyModel = null!;
    private sealed record StrategyNode(string? Key, double Threshold, int Left, int Right, double Value);
    private sealed record StrategyHead(double Bias, Dictionary<string, double>? Weights, StrategyNode[][]? Trees)
    {
        public double Score(Dictionary<string, double> features)
        {
            if (Weights is not null) return Bias + features.Sum(f => f.Value * Weights.GetValueOrDefault(f.Key));
            double sum = 0;
            foreach (var tree in Trees!)
            {
                var node = tree[0];
                while (node.Key is { } key)
                    node = tree[features.GetValueOrDefault(key) <= node.Threshold ? node.Left : node.Right];
                sum += node.Value;
            }
            return sum;
        }
    }

    private sealed record StrategyModel(string Version, Dictionary<string, int> InitialHp,
        Dictionary<string, string[]> TargetBonds, StrategyHead Clear, StrategyHead Round);

    private static StrategyModel? LoadStrategyModel(string path)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<StrategyModel>(File.ReadAllText(path), JsonOptions); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }

    private static string? StrategyBasis(string? mode) => mode switch
    {
        "mode_multi_funny" => "标准模拟",
        "mode_multi_normal" => "险境模拟",
        "mode_multi_hard" => "绝境模拟",
        "mode_multi_abyss" => "终极模拟",
        "mode_single_funny" => "极速模拟",
        _ when mode?.StartsWith("mode_single_", StringComparison.Ordinal) == true &&
            int.TryParse(mode.AsSpan(12), out int level) && level is >= 1 and <= 20 => $"规程等级{level}",
        _ => null
    };

    public StrategyRecommendation RecommendOpeningStrategies(MatchState state, string[] candidateIds)
    {
        var mode = state.ModeId.Value;
        if (mode is null || StrategyBasis(mode) is null) return new([], ["还没认出本局模式"]);
        bool multi = mode.StartsWith("mode_multi_", StringComparison.Ordinal);
        var model = (multi ? _multiStrategyModel : _strategyModel).Value;
        if (model is null) return new([], ["未能加载推荐模型，请检查助手的数据文件"]);
        if (_gameDataVersion is null || state.GameDataVersion.Value != _gameDataVersion || model.Version != _gameDataVersion)
            return new([], ["游戏数据版本和推荐模型对不上，等助手更新"]);

        var assumptions = new List<string> {
            "按复刻里的电脑玩家估计，它不擅长的策略可能被低估。",
            "第三方复刻里部分效果和游戏有差异，估计可能有偏差。"
        };
        if (multi) assumptions.Add("队友按复刻里的电脑玩家估计，真人队友的打法可能不同。目前多人推荐的把握不大。");
        if (candidateIds.Any(id => !model.InitialHp.ContainsKey(id))) return new([], ["游戏里的策略和推荐模型对不上，等助手更新"]);
        int n = multi || mode == "mode_single_funny" ? 0 : int.Parse(mode.AsSpan(12));
        var difficulty = Difficulty(mode)!;
        int cap = multi ? (difficulty == "FUNNY" ? 14 : 15) : n == 0 ? 9 : n <= 4 ? 14 : 15;
        var estimates = new List<StrategyEstimate>();
        foreach (var id in candidateIds.Distinct())
        {
            var features = new Dictionary<string, double> { ["level"] = n / 20.0,
                ["hp"] = (model.InitialHp[id] - 25) / 10.0, [mode] = 1, [difficulty] = 1,
                [id] = 1, [difficulty + "/" + id] = 1 };
            if (state.MapId.Value is { } map) features[map] = 1;
            if (state.BossId.Value is { } boss) { features[boss] = 1; features[boss + "/" + id] = 1; }
            foreach (var c in state.Covenants.Value ?? [])
                if (c.Incomplete.Value == true && c.Id.Value is { } bond) features["off/" + bond] = 1;
            if (model.TargetBonds.TryGetValue(id, out var targets) && targets.Any(b => features.ContainsKey("off/" + b)))
            {
                features["targetIncomplete"] = 1;
                features["targetIncomplete/" + id] = 1;
            }
            double clear = 1 / (1 + Math.Exp(-model.Clear.Score(features)));
            double round = Math.Clamp(cap + model.Round.Score(features), 1, cap);
            estimates.Add(new(id, 0, clear, round));
        }
        // TODO(design): 确认通关率优先、预计回合破同分的产品排序口径。
        return new(estimates.OrderByDescending(s => s.ClearProbability).ThenByDescending(s => s.ExpectedRound)
            .Select((s, i) => s with { Rank = i + 1 }).ToArray(), assumptions.ToArray());
    }
}
