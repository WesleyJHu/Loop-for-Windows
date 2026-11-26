using Loop.Core.Actions;
using static Loop.Core.Actions.Direction;

namespace Loop.Core.Tests;

public class ActionResolverTests
{
    [Fact]
    public void NoDirections_ResolvesToNothing() =>
        Assert.Null(ActionResolver.Resolve(None));

    [Theory]
    [InlineData(Left, WindowAction.LeftHalf)]
    [InlineData(Right, WindowAction.RightHalf)]
    [InlineData(Up, WindowAction.TopHalf)]
    [InlineData(Down, WindowAction.BottomHalf)]
    public void SingleDirection_GivesHalf(Direction held, WindowAction expected) =>
        Assert.Equal(expected, ActionResolver.Resolve(held));

    [Theory]
    [InlineData(Up | Left, WindowAction.TopLeft)]
    [InlineData(Up | Right, WindowAction.TopRight)]
    [InlineData(Down | Left, WindowAction.BottomLeft)]
    [InlineData(Down | Right, WindowAction.BottomRight)]
    public void PerpendicularDirections_GiveCorner(Direction held, WindowAction expected) =>
        Assert.Equal(expected, ActionResolver.Resolve(held));

    [Fact]
    public void LeftAndRight_GiveFullWidth() =>
        Assert.Equal(WindowAction.MaximizeWidth, ActionResolver.Resolve(Left | Right));

    [Fact]
    public void UpAndDown_GiveFullHeight() =>
        Assert.Equal(WindowAction.MaximizeHeight, ActionResolver.Resolve(Up | Down));

    [Fact]
    public void AllFour_GiveMaximize() =>
        Assert.Equal(WindowAction.Maximize, ActionResolver.Resolve(Left | Right | Up | Down));

    [Theory]
    // Full width + one vertical direction = that vertical half (which already spans the width).
    [InlineData(Left | Right | Up, WindowAction.TopHalf)]
    [InlineData(Left | Right | Down, WindowAction.BottomHalf)]
    // Full height + one horizontal direction = that horizontal half.
    [InlineData(Up | Down | Left, WindowAction.LeftHalf)]
    [InlineData(Up | Down | Right, WindowAction.RightHalf)]
    public void ThreeDirections_CollapseToHalf(Direction held, WindowAction expected) =>
        Assert.Equal(expected, ActionResolver.Resolve(held));

    [Fact]
    public void EveryCombination_Resolves()
    {
        // All 15 non-empty combinations of the 4 directions must map to some action.
        for (var mask = 1; mask < 16; mask++)
            Assert.NotNull(ActionResolver.Resolve((Direction)mask));
    }
}
