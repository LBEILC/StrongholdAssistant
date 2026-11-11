// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：StrongholdAssistant 从 BanScreenAnalyzer 抽出禁用推算，供手动修正与识别共用。
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public static class OperatorBanCalculator
{
    public static IReadOnlyList<string> Compute(IEnumerable<Operator> operators,
        IReadOnlySet<string> bannedCoreCovenants, IReadOnlySet<string> bannedAdditionalCovenants)
    {
        var bannedCovenants = bannedCoreCovenants
            .Concat(bannedAdditionalCovenants)
            .ToHashSet(StringComparer.Ordinal);

        var autoBannedOperators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in operators)
        {
            if (string.IsNullOrWhiteSpace(op.Name))
                continue;

            var allCovenants = op.CoreCovenants
                .Concat(op.AdditionalCovenants)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // 规则一：干员只有一个盟约，且该盟约被禁用。
            bool rule1 = allCovenants.Count == 1 && bannedCovenants.Contains(allCovenants[0]);

            // 规则二：至少一个核心盟约和一个附加盟约同时被禁用。
            bool hasBannedCore = op.CoreCovenants.Any(c => bannedCoreCovenants.Contains(c));
            bool hasBannedAdditional = op.AdditionalCovenants.Any(c => bannedAdditionalCovenants.Contains(c));
            bool rule2 = hasBannedCore && hasBannedAdditional;

            if (rule1 || rule2)
                autoBannedOperators.Add(op.Name);
        }

        return autoBannedOperators.ToList();
    }
}
