using Loop.Core.Actions;
using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class DisplayOrderTests
{
    [Fact]
    public void Sort_LeftToRightThenTopToBottom()
    {
        var primary = new Rect(0, 0, 1920, 1080);
        var below = new Rect(-348, 1080, 1920, 1080);  // this machine's layout: second screen below-left
        var right = new Rect(1920, 0, 2560, 1440);
        var stackedOnPrimaryLeft = new Rect(0, -1080, 1920, 1080);

        var sorted = DisplayOrder.Sort(new[] { right, primary, below, stackedOnPrimaryLeft }, r => r);

        Assert.Equal(new[] { below, stackedOnPrimaryLeft, primary, right }, sorted);
    }

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 0)] // wraps
    public void Next_WrapsAround(int current, int count, int expected) =>
        Assert.Equal(expected, DisplayOrder.Resolve(WindowAction.NextDisplay, current, count));

    [Theory]
    [InlineData(0, 3, 2)] // wraps
    [InlineData(2, 3, 1)]
    public void Previous_WrapsAround(int current, int count, int expected) =>
        Assert.Equal(expected, DisplayOrder.Resolve(WindowAction.PreviousDisplay, current, count));

    [Fact]
    public void SingleDisplay_DoesNothing()
    {
        Assert.Null(DisplayOrder.Resolve(WindowAction.NextDisplay, 0, 1));
        Assert.Null(DisplayOrder.Resolve(WindowAction.Display1, 0, 1));
    }

    [Fact]
    public void NumberedDisplay_TargetsThatDisplay()
    {
        Assert.Equal(0, DisplayOrder.Resolve(WindowAction.Display1, 1, 2));
        Assert.Equal(1, DisplayOrder.Resolve(WindowAction.Display2, 0, 2));
    }

    [Fact]
    public void NumberedDisplay_ThatDoesNotExist_DoesNothing() =>
        Assert.Null(DisplayOrder.Resolve(WindowAction.Display3, 0, 2));

    [Fact]
    public void NumberedDisplay_AlreadyOnIt_DoesNothing() =>
        Assert.Null(DisplayOrder.Resolve(WindowAction.Display2, 1, 2));

    [Fact]
    public void UnknownCurrentDisplay_DoesNothing() =>
        Assert.Null(DisplayOrder.Resolve(WindowAction.NextDisplay, -1, 2));

    [Fact]
    public void LayoutAction_IsNotADisplayMove() =>
        Assert.Null(DisplayOrder.Resolve(WindowAction.LeftHalf, 0, 2));

    [Fact]
    public void DisplayIndex_MapsAllNineKeys()
    {
        for (var i = 0; i < 9; i++)
            Assert.Equal(i, (WindowAction.Display1 + i).DisplayIndex());
        Assert.Null(WindowAction.NextDisplay.DisplayIndex());
    }
}
