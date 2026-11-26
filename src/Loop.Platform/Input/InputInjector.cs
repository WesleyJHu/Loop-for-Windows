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

    /// <summary>Injects mouse button presses (Down) and releases, in order, at the cursor. False if Windows refused any.</summary>
    public static bool SendMouseButtons(IReadOnlyList<(MouseButton Button, bool Down)> events)
    {
        if (events.Count == 0) return true;
        var inputs = new INPUT[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var (button, down) = events[i];
            var (flags, data) = button switch
            {
                MouseButton.Left => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
                MouseButton.Right => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
                MouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
                MouseButton.X1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON1_DATA),
                _ => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON2_DATA),
            };
            inputs[i] = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = Signature } },
            };
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
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
