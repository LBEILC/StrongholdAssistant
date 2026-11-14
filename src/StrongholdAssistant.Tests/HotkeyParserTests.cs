using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Mouse4", 0u, 0x05u)]
    [InlineData("mouse5", 0u, 0x06u)]
    [InlineData("Ctrl+Mouse4", 2u, 0x05u)]
    public void MouseKeysUseRawInput(string combo, uint modifiers, uint key)
    {
        var hotkey = HotkeyParser.Parse(combo);
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
        Assert.True(hotkey!.Value.UsesHook);
    }

    [Theory]
    [InlineData("Mouse4", 0x05u, 0u)]
    [InlineData("Ctrl+Mouse5", 0x06u, 2u)]
    public void RoutesMouseKeysWithMatchingModifiers(string combo, uint key, uint modifiers)
    {
        var hotkey = HotkeyParser.Parse(combo);
        var router = new KeyboardRouter();
        Assert.Equal(new KeyboardActions(true, true), router.Observe(key, true, modifiers, 1, 1, 2, hotkey, hotkey));
        router.Observe(key, false, modifiers, 1, 1, 2, hotkey, hotkey);
        Assert.Equal(default, router.Observe(key, true, modifiers ^ 2u, 1, 1, 2, hotkey, hotkey));
    }

    [Theory]
    [InlineData("Ctrl+Shift+F2", 6u, 0x71u)]
    [InlineData("alt+q", 1u, 0x51u)]
    [InlineData("Alt+S", 1u, 0x53u)]
    [InlineData("F1", 0u, 0x70u)]
    [InlineData("f24", 0u, 0x87u)]
    [InlineData("Ctrl+Alt+Shift+Win+9", 15u, 0x39u)]
    [InlineData("Ctrl+/", 2u, 0xBFu)]
    [InlineData("Ctrl+Shift++", 6u, 0xBBu)]
    [InlineData("Tab", 0u, 9u)]
    [InlineData("D", 0u, 0x44u)]
    [InlineData("`", 0u, 0xC0u)]
    public void ParsesSupportedKeys(string combo, uint modifiers, uint key)
        => Assert.Equal(new Hotkey(modifiers, key), HotkeyParser.Parse(combo));

    [Theory]
    [InlineData("Esc")]
    [InlineData("Escape")]
    [InlineData("Ctrl+Unknown")]
    [InlineData("F25")]
    [InlineData("Ctrl+Ctrl+Q")]
    [InlineData("Alt+")]
    public void RejectsInvalidCombinations(string combo) => Assert.Null(HotkeyParser.Parse(combo));

    [Fact]
    public void RoutesSingleKeysByForegroundAndDoesNotRepeat()
    {
        var toggle = HotkeyParser.Parse("Tab");
        var refresh = HotkeyParser.Parse("D");
        Assert.True(toggle!.Value.UsesHook);
        Assert.True(HotkeyParser.Parse("F2")!.Value.UsesHook);
        Assert.False(HotkeyParser.Parse("Alt+S")!.Value.UsesHook);
        var router = new KeyboardRouter();
        Assert.Equal(new KeyboardActions(true, false), router.Observe(9, true, 0, 1, 1, 2, toggle, refresh));
        Assert.Equal(default, router.Observe(9, true, 0, 1, 1, 2, toggle, refresh));
        router.Observe(9, false, 0, 1, 1, 2, toggle, refresh);
        Assert.True(router.Observe(9, true, 0, 2, 1, 2, toggle, refresh).Toggle);
        router.Observe(9, false, 0, 2, 1, 2, toggle, refresh);
        Assert.Equal(default, router.Observe(9, true, 0, 3, 1, 2, toggle, refresh));
        Assert.Equal(default, router.Observe(0x44, true, 0, 2, 1, 2, toggle, refresh));
        router.Observe(0x44, false, 0, 2, 1, 2, toggle, refresh);
        Assert.True(router.Observe(0x44, true, 0, 1, 1, 2, toggle, refresh).Refresh);
        router.Observe(0x44, false, 0, 1, 1, 2, toggle, refresh);
        Assert.False(router.Observe(0x44, true, 0, 1, 1, 2, toggle, null).Refresh);
        Assert.False(router.Observe(0x53, true, 1, 1, 1, 2, HotkeyParser.Parse("Alt+S"), null).Toggle);
    }
}
