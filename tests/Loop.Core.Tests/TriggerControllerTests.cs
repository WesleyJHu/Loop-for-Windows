using Loop.Core.Actions;
using Loop.Core.Input;
using VK = Loop.Core.Input.VirtualKeys;

namespace Loop.Core.Tests;

public class TriggerControllerTests
{
    private const ModifierKeys CtrlWinAlt = ModifierKeys.Ctrl | ModifierKeys.Win | ModifierKeys.Alt;

    private static TriggerController NewController(ModifierKeys trigger = CtrlWinAlt) =>
        new(trigger, KeyBindings.Default);

    private static KeyDecision Down(TriggerController c, int vk) => c.Process(new KeyEvent(vk, IsDown: true));
    private static KeyDecision Up(TriggerController c, int vk) => c.Process(new KeyEvent(vk, IsDown: false));

    private static void HoldTrigger(TriggerController c)
    {
        Down(c, VK.LControl);
        Down(c, VK.LWin);
        Down(c, VK.LMenu);
    }

    private static void ReleaseTrigger(TriggerController c)
    {
        Up(c, VK.LMenu);
        Up(c, VK.LWin);
        Up(c, VK.LControl);
    }

    [Fact]
    public void ArrowWithoutTrigger_PassesThrough()
    {
        var c = NewController();
        Assert.Equal(KeyDecision.PassThrough, Down(c, VK.Left));
        Assert.Equal(KeyDecision.PassThrough, Up(c, VK.Left));
    }

    [Fact]
    public void ArrowWithPartialTrigger_PassesThrough()
    {
        var c = NewController();
        Down(c, VK.LControl);
        Down(c, VK.LWin); // Ctrl+Win+Left is the Windows virtual-desktop switch; must not be eaten
        Assert.Equal(KeyDecision.PassThrough, Down(c, VK.Left));
    }

    [Fact]
    public void TriggerPlusArrow_SwallowsAndAppliesHalf()
    {
        var c = NewController();
        HoldTrigger(c);

        var d = Down(c, VK.Left);

        Assert.True(d.Swallow);
        Assert.Equal(WindowAction.LeftHalf, d.Action);
        Assert.True(Up(c, VK.Left).Swallow);
    }

    [Theory]
    [InlineData(VK.Left, WindowAction.LeftHalf)]
    [InlineData(VK.Right, WindowAction.RightHalf)]
    [InlineData(VK.Up, WindowAction.TopHalf)]
    [InlineData(VK.Down, WindowAction.BottomHalf)]
    [InlineData(VK.Enter, WindowAction.Maximize)]
    [InlineData(VK.C, WindowAction.Center)]
    [InlineData(VK.M, WindowAction.Minimize)]
    public void DefaultBindings(int vk, WindowAction expected)
    {
        var c = NewController();
        HoldTrigger(c);
        Assert.Equal(expected, Down(c, vk).Action);
    }

    [Fact]
    public void ModifierEvents_AreNeverSwallowed()
    {
        var c = NewController();
        Assert.False(Down(c, VK.LControl).Swallow);
        Assert.False(Down(c, VK.LWin).Swallow);
        Assert.False(Down(c, VK.LMenu).Swallow);
        Down(c, VK.Left);
        Assert.False(Up(c, VK.LMenu).Swallow);
        Assert.False(Up(c, VK.LWin).Swallow);
        Assert.False(Up(c, VK.LControl).Swallow);
    }

    [Fact]
    public void RightSideModifiers_AlsoActivateTrigger()
    {
        var c = NewController();
        Down(c, VK.RControl);
        Down(c, VK.RWin);
        Down(c, VK.RMenu);
        Assert.Equal(WindowAction.RightHalf, Down(c, VK.Right).Action);
    }

    [Fact]
    public void ExtraModifier_DisablesTrigger()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.LShift); // Ctrl+Win+Alt+Shift is the Office key and is reserved for other bindings
        Assert.Equal(KeyDecision.PassThrough, Down(c, VK.Left));
    }

    [Fact]
    public void UnboundKey_PassesThroughWhileTriggerHeld()
    {
        var c = NewController();
        HoldTrigger(c);
        Assert.Equal(KeyDecision.PassThrough, Down(c, 0x41 /* A */));
    }

    [Fact]
    public void HoldingSecondDirection_ProducesCorner()
    {
        var c = NewController();
        HoldTrigger(c);

        Assert.Equal(WindowAction.LeftHalf, Down(c, VK.Left).Action);
        Assert.Equal(WindowAction.TopLeft, Down(c, VK.Up).Action);
    }

    [Fact]
    public void FirstDirection_StartsGesture_SecondRefinesIt()
    {
        var c = NewController();
        HoldTrigger(c);

        Assert.True(Down(c, VK.Left).NewGesture);
        Assert.False(Down(c, VK.Right).NewGesture);
    }

    [Fact]
    public void ReleasingAllDirections_EndsGesture()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        Up(c, VK.Left);

        Assert.True(Down(c, VK.Right).NewGesture);
    }

    [Fact]
    public void ReleasingOneOfTwoDirections_ContinuesGesture()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        Down(c, VK.Up);
        Up(c, VK.Up);

        var d = Down(c, VK.Down);
        Assert.Equal(WindowAction.BottomLeft, d.Action);
        Assert.False(d.NewGesture);
    }

    [Fact]
    public void ActionKeys_AlwaysStartGesture()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        Assert.True(Down(c, VK.C).NewGesture);
    }

    [Fact]
    public void ReleasedDirection_NoLongerCombines()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        Up(c, VK.Left);

        Assert.Equal(WindowAction.TopHalf, Down(c, VK.Up).Action);
    }

    [Fact]
    public void OppositeDirections_StretchAxis()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        Assert.Equal(WindowAction.MaximizeWidth, Down(c, VK.Right).Action);

        Up(c, VK.Left);
        Up(c, VK.Right);
        Down(c, VK.Up);
        Assert.Equal(WindowAction.MaximizeHeight, Down(c, VK.Down).Action);
    }

    [Fact]
    public void AutoRepeat_IsSwallowedWithoutRepeatingAction()
    {
        var c = NewController();
        HoldTrigger(c);
        Assert.NotNull(Down(c, VK.Left).Action);

        for (var i = 0; i < 5; i++)
        {
            var repeat = Down(c, VK.Left);
            Assert.True(repeat.Swallow);
            Assert.Null(repeat.Action);
        }
    }

    [Fact]
    public void KeyUpAfterTriggerReleased_IsStillSwallowed()
    {
        // The app never saw the key-down, so it must not see an orphan key-up either.
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        ReleaseTrigger(c);

        Assert.True(Up(c, VK.Left).Swallow);
        Assert.Equal(KeyDecision.PassThrough, Down(c, VK.Left)); // and afterwards arrows work normally
    }

    [Fact]
    public void ReleasingTrigger_ForgetsHeldDirections()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        ReleaseTrigger(c);
        HoldTrigger(c); // Left is still physically down, but belongs to the previous activation

        Assert.Equal(WindowAction.TopHalf, Down(c, VK.Up).Action);
    }

    [Fact]
    public void Disabled_PassesEverythingThrough()
    {
        var c = NewController();
        c.Enabled = false;
        HoldTrigger(c);
        Assert.Equal(KeyDecision.PassThrough, Down(c, VK.Left));
    }

    [Fact]
    public void DisablingMidPress_StillSwallowsPendingKeyUp()
    {
        var c = NewController();
        HoldTrigger(c);
        Down(c, VK.Left);
        c.Enabled = false;
        Assert.True(Up(c, VK.Left).Swallow);
    }

    [Fact]
    public void WinOrAltTrigger_RequestsMaskKey()
    {
        var c = NewController();
        HoldTrigger(c);
        Assert.True(Down(c, VK.Left).SendMaskKey);
    }

    [Fact]
    public void CtrlShiftTrigger_DoesNotRequestMaskKey()
    {
        var c = NewController(ModifierKeys.Ctrl | ModifierKeys.Shift);
        Down(c, VK.LControl);
        Down(c, VK.LShift);
        var d = Down(c, VK.Left);
        Assert.Equal(WindowAction.LeftHalf, d.Action);
        Assert.False(d.SendMaskKey);
    }

    [Fact]
    public void SyncModifiers_ClearsStuckModifiers()
    {
        // e.g. Ctrl+Alt+Del: the key-ups were delivered to the secure desktop, not to us.
        var c = NewController();
        HoldTrigger(c);
        c.SyncModifiers(Array.Empty<int>());

        Assert.False(c.IsTriggerActive);
        Assert.Equal(KeyDecision.PassThrough, Down(c, VK.Left));
    }

    [Fact]
    public void SyncModifiers_CanActivateTrigger()
    {
        var c = NewController();
        c.SyncModifiers(new[] { VK.LControl, VK.RWin, VK.LMenu });
        Assert.True(c.IsTriggerActive);
        Assert.Equal(WindowAction.BottomHalf, Down(c, VK.Down).Action);
    }

    [Fact]
    public void SyncModifiers_IgnoresNonModifierKeys()
    {
        var c = NewController(ModifierKeys.Ctrl);
        c.SyncModifiers(new[] { VK.LControl, VK.Left });
        Assert.Equal(ModifierKeys.Ctrl, c.CurrentModifiers);
    }

    [Fact]
    public void EmptyTrigger_IsRejected() =>
        Assert.Throws<ArgumentException>(() => new TriggerController(ModifierKeys.None, KeyBindings.Default));
}
