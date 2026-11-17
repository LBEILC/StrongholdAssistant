namespace ASPAssistant.Core.Services;

/// <summary>卡面同名的两名 Pith；阶数优先，缺阶数时只使用已知的本局策略。</summary>
public static class PithIdentity
{
    public const string Gift = "Pith（优等生）";
    public static string? Resolve(int? tier, bool? hasStrategy) => tier switch
    {
        3 => Gift,
        5 => "Pith",
        null => hasStrategy switch { true => Gift, false => "Pith", null => null },
        _ => null
    };

    public static int? ReadTier(string text) => text.Trim().Normalize(System.Text.NormalizationForm.FormKC).Replace(" ", "") switch
    {
        "I" or "1" => 1, "II" or "2" => 2, "III" or "3" => 3,
        "IV" or "4" => 4, "V" or "5" => 5, "VI" or "6" => 6, _ => null
    };
}
