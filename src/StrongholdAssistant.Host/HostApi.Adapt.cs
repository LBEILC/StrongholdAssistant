using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using System.Text.Json;

namespace StrongholdAssistant.Host;

public sealed record AdaptCard(int Index, string? Id, string? Name, string? Desc, double? ClearRate, double? ReachedRound, int? Rank);
public sealed record AdaptAdvice(string Status, string? Reason, int Round, string? Family, string Basis,
    AdaptCard[] Cards, string[] Assumptions);

public sealed partial class HostApi
{
    private AdaptReading? _pendingAdapt;
    internal async Task<bool> ObserveAdaptFrameAsync(byte[] frame, int width, int height)
    {
        int generation = _matchReadingGeneration;
        int? previous = _observedRound ?? Match.Economy?.Round;
        var page = await _adaptReader.ReadAsync(frame, width, height,
            Match.Solo || Match.Mode?.Id.StartsWith("mode_single_") == true);
        if (page == null) return false;
        var turnPage = page;
        if (Match.Mode == null && page.Cards.Length == 6 && page.Cards.Skip(3).All(c => c == null))
            turnPage = await _adaptReader.ReadAsync(frame, width, height, true) ?? page;
        var money = await _collectionHud!.ReadCoveredMoney(frame, Theme(Settings.GameTheme));
        lock (_historyGate)
        {
            if (generation != _matchReadingGeneration || previous != (_observedRound ?? Match.Economy?.Round)) return true;
            CoverMatchHud(previous + 1, money);
            SetPhase("adapt", previous + 1, new { adapt = turnPage.Cards.Take(3).Select((c, index) =>
                new { index, id = c?.Id, name = c?.Name, desc = c?.Desc }).ToArray() });
            // 若直接在机变页启动，等休整 HUD 确认回合后再发布。
            if (previous == null) _pendingAdapt = page;
            else PublishAdaptAdvice(BuildAdaptAdvice(GetMatchState(), page, previous.Value + 1));
        }
        return true;
    }

    private AdaptReader _adaptReader = null!;
    private sealed record AdaptModel(string Version, bool Accepted, StrategyHead Clear, StrategyHead Round);
    private Lazy<AdaptModel?> _adaptModel = null!;

    private static AdaptModel? LoadAdaptModel(string directory)
    {
        string file = Path.Combine(directory, "decision", "adapt.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<AdaptModel>(File.ReadAllText(file), JsonOptions) : null;
    }

    internal AdaptAdvice BuildAdaptAdvice(MatchState s, AdaptReading page, int round)
    {
        string? mode = s.ModeId.Value;
        int[] levels = mode switch {
            "mode_multi_funny" => [1,2,3,4], "mode_multi_normal" => [5,6,7,8],
            "mode_multi_hard" => [9,10,11,12,13,14], "mode_multi_abyss" => [15,16,17,18,19,20],
            "mode_single_funny" => [0], _ when mode?.StartsWith("mode_single_") == true &&
                int.TryParse(mode.AsSpan(12), out var n) && n is >= 1 and <= 20 => [n], _ => [] };
        bool multi = levels.Length > 1;
        string basis = multi ? $"按单人规程等级 {levels[0]}～{levels[^1]} 平均估" :
            levels.Length == 0 ? "难度未知" : levels[0] == 0 ? "极速模拟" : $"规程等级{levels[0]}";
        var assumptions = new List<string> { "预计值按第三方复刻自带 bot 选牌后继续打完估计。",
            "悬赏卡组、部分权重、单人机密商店的槽位与非第11回合商店和战术卡池按推断模拟。" };
        if (multi) assumptions.Add("多人按同档单人估计，队友的选择与共享资源会影响结果。");
        var cards = page.Cards.Select((c,i) => new AdaptCard(i,c?.Id,c?.Name,c?.Desc,null,null,null)).ToArray();
        var model = _adaptModel.Value;
        string? reason = model is null || s.GameDataVersion.Value != model.Version ? "游戏数据版本和推荐模型对不上" :
            !model.Accepted ? "推荐还在调" : levels.Length == 0 ? "还没认出本局模式" :
            levels[0] == 0 ? "极速模拟没有机变" : cards.All(c => c.Id == null) ? "这类机变还认不出" : null;
        if (reason != null) return new("unavailable",reason,round,page.Family,basis,cards,assumptions.ToArray());

        var board = s.Units.Value?.Where(u => u.Zone.Value == "board").ToArray();
        foreach (var (c,i) in page.Cards.Select((c,i) => (c,i)))
        {
            if (c == null) continue;
            double clear = 0, reached = 0;
            foreach (int level in levels)
            {
                var x = new Dictionary<string,double> { ["difficulty"] = level/20d, ["round"] = round/15d,
                    ["hp"] = (s.Hp.Value ?? 0)/40d, ["funds"] = (s.Money.Value ?? 0)/30d,
                    ["center"] = (s.Level.Value ?? 0)/6d, ["count"] = (board?.Length ?? 0)/12d,
                    ["tiers"] = (board?.Sum(u => u.Tier.Value ?? 0) ?? 0)/40d, [page.Family] = 1,
                    [c.Id] = 1, ["cardTier"] = (c.Tier ?? 0)/6d,
                    [Difficulty("mode_single_" + level) + "/" + c.Id] = 1 };
                int cap = level == 0 ? 9 : level <= 4 ? 14 : 15;
                clear += 1/(1+Math.Exp(-model!.Clear.Score(x)));
                reached += Math.Clamp(cap+model.Round.Score(x),round,Math.Max(cap,round));
            }
            cards[i] = cards[i] with { ClearRate = clear/levels.Length, ReachedRound = reached/levels.Length };
        }
        int rank = 0;
        foreach (var c in cards.Where(c => c.Id != null).OrderByDescending(c => c.ClearRate).ThenByDescending(c => c.ReachedRound))
            cards[c.Index] = c with { Rank = ++rank };
        return new("ok",null,round,page.Family,basis,cards,assumptions.ToArray());
    }

    internal void PublishAdaptAdvice(AdaptAdvice advice)
    {
        if (Match.AdaptAdvice is { } old && old.Round == advice.Round && old.Family == advice.Family
            && old.Status == advice.Status && old.Basis == advice.Basis && old.Reason == advice.Reason
            && old.Cards.SequenceEqual(advice.Cards)) return;
        Match = Match with { AdaptAdvice = advice };
        Changed?.Invoke("match", Match);
    }
}
