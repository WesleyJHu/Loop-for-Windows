using System.Runtime.InteropServices;
using Loop.Core.Input;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Input;

internal static class InputInjector
{
    /// <summary>Tag placed in dwExtraInfo so our own hook ignores what we inject ("LOOP").</summary>
    public static readonly UIntPtr Signature = (UIntPtr)0x4C4F4F50u;

    /// <summary>
    /// Taps an unassigned key so Windows does not treat the upcoming Win/Alt release as a lone
    /// tap (which would open the Start menu or focus an app's menu bar).
    /// </summary>
    public static void SendMaskKey()
    {
        var inputs = new[]
        {
            Key(VirtualKeys.MaskKey, keyUp: false),
            Key(VirtualKeys.MaskKey, keyUp: true),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(int vk, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                dwExtraInfo = Signature,
            },
        },
    };
}
