using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F2", 6u, 0x71u)]
    [InlineData("alt+q", 1u, 0x51u)]
    [InlineData("Alt+S", 1u, 0x53u)]
    [InlineData("F1", 0u, 0x70u)]
    [InlineData("f24", 0u, 0x87u)]
    [InlineData("Ctrl+Alt+Shift+Win+9", 15u, 0x39u)]
    [InlineData("Ctrl+/", 2u, 0xBFu)]
    [InlineData("Ctrl+Shift++", 6u, 0xBBu)]
    public void ParsesSupportedKeys(string combo, uint modifiers, uint key)
        => Assert.Equal(new Hotkey(modifiers, key), HotkeyParser.Parse(combo));

    [Theory]
    [InlineData("Q")]
    [InlineData("+")]
    [InlineData("Ctrl+Unknown")]
    [InlineData("F25")]
    [InlineData("Ctrl+Ctrl+Q")]
    [InlineData("Alt+")]
    public void RejectsInvalidCombinations(string combo) => Assert.Null(HotkeyParser.Parse(combo));
}
