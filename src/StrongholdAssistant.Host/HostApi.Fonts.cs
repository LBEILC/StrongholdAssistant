using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private static string[] ListFonts()
    {
        var chinese = XmlLanguage.GetLanguage("zh-cn");
        var english = XmlLanguage.GetLanguage("en-us");
        return Fonts.SystemFontFamilies
            .Select(font => font.FamilyNames.TryGetValue(chinese, out var localName) ? localName
                : font.FamilyNames.TryGetValue(english, out var englishName) ? englishName : font.Source)
            .Where(name => !name.StartsWith('@'))
            .Distinct()
            .OrderBy(name => name, StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), false))
            .ToArray();
    }
}
