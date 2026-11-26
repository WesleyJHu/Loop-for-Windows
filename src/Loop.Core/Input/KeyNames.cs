using System.Globalization;

namespace Loop.Core.Input;

/// <summary>Human-readable names for virtual-key codes, used in the settings file and UI.</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> NameByVk = Build();
    private static readonly Dictionary<string, int> VkByName =
        NameByVk.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> Build()
    {
        var d = new Dictionary<int, string>();
        for (var c = 'A'; c <= 'Z'; c++) d[c] = c.ToString();
        for (var c = '0'; c <= '9'; c++) d[c] = c.ToString();
        for (var i = 1; i <= 24; i++) d[0x6F + i] = $"F{i}";
        for (var i = 0; i <= 9; i++) d[0x60 + i] = $"Numpad{i}";

        d[0x08] = "Backspace";
        d[0x09] = "Tab";
        d[0x0D] = "Enter";
        d[0x1B] = "Escape";
        d[0x20] = "Space";
        d[0x21] = "PageUp";
        d[0x22] = "PageDown";
        d[0x23] = "End";
        d[0x24] = "Home";
        d[0x25] = "Left";
        d[0x26] = "Up";
        d[0x27] = "Right";
        d[0x28] = "Down";
        d[0x2D] = "Insert";
        d[0x2E] = "Delete";
        d[0x6A] = "Numpad*";
        d[0x6B] = "Numpad+";
        d[0x6D] = "Numpad-";
        d[0x6E] = "Numpad.";
        d[0x6F] = "Numpad/";
        d[0xBA] = ";";
        d[0xBB] = "=";
        d[0xBC] = ",";
        d[0xBD] = "-";
        d[0xBE] = ".";
        d[0xBF] = "/";
        d[0xC0] = "`";
        d[0xDB] = "[";
        d[0xDC] = "\\";
        d[0xDD] = "]";
        d[0xDE] = "'";

        d[VirtualKeys.LControl] = "LeftCtrl";
        d[VirtualKeys.RControl] = "RightCtrl";
        d[VirtualKeys.LMenu] = "LeftAlt";
        d[VirtualKeys.RMenu] = "RightAlt";
        d[VirtualKeys.LShift] = "LeftShift";
        d[VirtualKeys.RShift] = "RightShift";
        d[VirtualKeys.LWin] = "LeftWin";
        d[VirtualKeys.RWin] = "RightWin";
        return d;
    }

    /// <summary>The name of a key, e.g. "Left", "C", "]", or "VK 0xE8" for unnamed codes.</summary>
    public static string GetName(int vk) =>
        NameByVk.TryGetValue(vk, out var name) ? name : $"VK 0x{vk:X2}";

    /// <summary>Parses a key name (case-insensitive) or a "VK 0xNN" code.</summary>
    public static bool TryParse(string? text, out int vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (VkByName.TryGetValue(s, out vk)) return true;

        const string prefix = "VK 0x";
        if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(s.AsSpan(prefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) &&
            vk is > 0 and < 0xFF)
            return true;

        vk = 0;
        return false;
    }

    /// <summary>A trigger as text, e.g. "Ctrl+Win+Alt".</summary>
    public static string Format(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Win)) parts.Add("Win");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        return parts.Count == 0 ? "(none)" : string.Join("+", parts);
    }
}
