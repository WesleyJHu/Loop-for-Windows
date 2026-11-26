using Loop.Core.Actions;
using Loop.Core.Input;
using Loop.Core.Windows;

namespace Loop.Core.Tests;

public class KeyBindingsTests
{
    [Fact]
    public void BindingAModifier_Throws() =>
        Assert.Throws<ArgumentException>(() => new KeyBindings(
            new Dictionary<int, Direction> { [VirtualKeys.LShift] = Direction.Left },
            new Dictionary<int, WindowAction>()));

    [Fact]
    public void BindingAKeyTwice_Throws() =>
        Assert.Throws<ArgumentException>(() => new KeyBindings(
            new Dictionary<int, Direction> { [VirtualKeys.Left] = Direction.Left },
            new Dictionary<int, WindowAction> { [VirtualKeys.Left] = WindowAction.Center }));

    [Fact]
    public void Defaults_CoverAllDirections()
    {
        var directions = KeyBindings.Default.DirectionKeys.Values.Aggregate(Direction.None, (a, b) => a | b);
        Assert.Equal(Direction.Left | Direction.Right | Direction.Up | Direction.Down, directions);
    }
}

public class WindowFilterTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("progman")]
    public void ShellWindows_AreExcluded(string className) =>
        Assert.True(WindowFilter.IsShellWindowClass(className));

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("CabinetWClass")]
    [InlineData("")]
    [InlineData(null)]
    public void RegularWindows_AreAllowed(string? className) =>
        Assert.False(WindowFilter.IsShellWindowClass(className));
}
