namespace StrongholdAssistant.Host;

public readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public bool UsesHook => Modifiers == 0;
}

public static class HotkeyParser
{
    public static Hotkey? Parse(string? combo)
    {
        if (string.IsNullOrEmpty(combo)) return null;
        uint modifiers = 0;
        var remaining = combo;
        while (remaining.Length > 1 && remaining.IndexOf('+') is var separator && separator > 0)
        {
            var modifier = remaining[..separator].ToUpperInvariant() switch
            {
                "CTRL" => 2u, "ALT" => 1u, "SHIFT" => 4u, "WIN" => 8u, _ => 0u
            };
            if (modifier == 0 || (modifiers & modifier) != 0) return null;
            modifiers |= modifier;
            remaining = remaining[(separator + 1)..];
        }
        var key = remaining.ToUpperInvariant();
        uint vk;
        var function = key.Length >= 2 && key[0] == 'F'
            && int.TryParse(key[1..], out var number) && number is >= 1 and <= 24;
        if (function) vk = (uint)(111 + int.Parse(key[1..]));
        else if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) vk = key[0];
        else
        {
            vk = key switch
            {
                "TAB" => 9, "SPACE" or " " => 32, "ENTER" => 13,
                "BACKSPACE" => 8, "DELETE" => 46, "INSERT" => 45,
                "HOME" => 36, "END" => 35, "PAGEUP" => 33, "PAGEDOWN" => 34,
                "ARROWLEFT" => 37, "ARROWUP" => 38, "ARROWRIGHT" => 39, "ARROWDOWN" => 40,
                ";" or ":" => 0xBA, "=" or "+" => 0xBB, "," or "<" => 0xBC,
                "-" or "_" => 0xBD, "." or ">" => 0xBE, "/" or "?" => 0xBF,
                "`" or "~" => 0xC0, "[" or "{" => 0xDB, "\\" or "|" => 0xDC,
                "]" or "}" => 0xDD, "'" or "\"" => 0xDE,
                "!" => 0x31, "@" => 0x32, "#" => 0x33, "$" => 0x34, "%" => 0x35,
                "^" => 0x36, "&" => 0x37, "*" => 0x38, "(" => 0x39, ")" => 0x30,
                _ => 0
            };
        }
        if (vk == 0) return null;
        // KeyboardEvent.key 返回按下 Shift 后的符号，注册时还原到对应物理键。
        if (key.Length == 1 && ":+<_>?~{|}\"!@#$%^&*()".Contains(key)) modifiers |= 4;
        return new(modifiers, vk);
    }
}
