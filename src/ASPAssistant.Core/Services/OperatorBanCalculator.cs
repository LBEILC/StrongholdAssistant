// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：StrongholdAssistant 从 BanScreenAnalyzer 抽出禁用推算，供手动修正与识别共用。
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public static class OperatorBanCalculator
{
    public static IReadOnlyList<string> Compute(IEnumerable<Operator> operators,
        IReadOnlySet<string> bannedCoreCovenants, IReadOnlySet<string> bannedAdditionalCovenants)
    {
        var autoBannedOperators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in operators.Where(o => o.Obtain is null))
        {
            if (string.IsNullOrWhiteSpace(op.Name))
                continue;

            // 2026-11-12：有核心盟约时只看核心；没有核心时才看附加，必须全部被禁。
            bool banned = op.CoreCovenants.Count > 0
                ? op.CoreCovenants.All(bannedCoreCovenants.Contains)
                : op.AdditionalCovenants.Count > 0 && op.AdditionalCovenants.All(bannedAdditionalCovenants.Contains);
            if (banned)
                autoBannedOperators.Add(op.Name);
        }

        return autoBannedOperators.ToList();
    }
}
