namespace Loop.Core.Input;

/// <summary>Win32 virtual-key codes used by Loop (kept here so Core stays platform-free).</summary>
public static class VirtualKeys
{
    public const int Enter = 0x0D;
    public const int Escape = 0x1B;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int C = 0x43;
    public const int M = 0x4D;
    public const int D1 = 0x31; // top-row digits 1..9 are 0x31..0x39
    public const int D9 = 0x39;
    public const int OpenBracket = 0xDB;  // VK_OEM_4  '['
    public const int CloseBracket = 0xDD; // VK_OEM_6  ']'

    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4; // Left Alt
    public const int RMenu = 0xA5; // Right Alt

    // Generic (side-less) codes; low-level hooks normally report the sided ones,
    // but injected input sometimes uses these.
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;

    /// <summary>
    /// An unassigned key code. Injecting it while Win/Alt are held stops Windows from treating
    /// their release as a lone tap (which would open Start or activate a menu bar).
    /// </summary>
    public const int MaskKey = 0xE8;

    /// <summary>Maps a modifier key code to its modifier flag, or <see cref="ModifierKeys.None"/>.</summary>
    public static ModifierKeys ToModifier(int vk) => vk switch
    {
        LControl or RControl or Control => ModifierKeys.Ctrl,
        LMenu or RMenu or Menu => ModifierKeys.Alt,
        LShift or RShift or Shift => ModifierKeys.Shift,
        LWin or RWin => ModifierKeys.Win,
        _ => ModifierKeys.None,
    };

    public static bool IsModifier(int vk) => ToModifier(vk) != ModifierKeys.None;
}
