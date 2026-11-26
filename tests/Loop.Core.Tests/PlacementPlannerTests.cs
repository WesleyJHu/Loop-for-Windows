using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class PlacementPlannerTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1080);

    [Fact]
    public void OuterRectInsideMonitor_IsOneMove()
    {
        var inside = new Rect(100, 100, 500, 500);
        Assert.Equal(new[] { new PlacementStep(inside, false) }, PlacementPlanner.Plan(inside, Primary));
    }

    [Fact]
    public void RightHalf_MovesWithinMonitorThenGrowsInPlace()
    {
        var outer = new Rect(953, 0, 974, 1087); // right and bottom borders poke 7px out

        Assert.Equal(new[]
        {
            new PlacementStep(new Rect(953, 0, 967, 1080), SizeOnly: false),
            new PlacementStep(outer, SizeOnly: true),
        }, PlacementPlanner.Plan(outer, Primary));
    }

    [Fact]
    public void LeftHalf_KeepsLeftOverhangInTheMove()
    {
        var outer = new Rect(-7, 0, 974, 1087);

        Assert.Equal(new[]
        {
            new PlacementStep(new Rect(-7, 0, 974, 1080), SizeOnly: false),
            new PlacementStep(outer, SizeOnly: true),
        }, PlacementPlanner.Plan(outer, Primary));
    }

    [Fact]
    public void OnlyLeftOrTopOverhang_IsOneMove()
    {
        var outer = new Rect(-7, 0, 500, 500);
        Assert.Equal(new[] { new PlacementStep(outer, false) }, PlacementPlanner.Plan(outer, Primary));
    }

    [Theory]
    [InlineData(-7, 0, 974, 1087)]
    [InlineData(953, 533, 974, 554)]
    [InlineData(-7, 0, 1934, 1087)]
    public void MoveStep_NeverExtendsPastRightOrBottomEdge(int x, int y, int w, int h)
    {
        var outer = new Rect(x, y, w, h);
        var steps = PlacementPlanner.Plan(outer, Primary);

        Assert.Equal(2, steps.Count);
        Assert.False(steps[0].SizeOnly);
        Assert.True(steps[0].Rect.Right <= Primary.Right && steps[0].Rect.Bottom <= Primary.Bottom);
        Assert.Equal((outer.Left, outer.Top), (steps[0].Rect.Left, steps[0].Rect.Top));
        Assert.Equal(new PlacementStep(outer, SizeOnly: true), steps[1]);
    }

    [Fact]
    public void OuterRectEntirelyOffMonitor_IsOneMove()
    {
        var outer = new Rect(3000, 3000, 100, 100);
        Assert.Equal(new[] { new PlacementStep(outer, false) }, PlacementPlanner.Plan(outer, Primary));
    }

    [Fact]
    public void SecondaryMonitorBelow_UsesItsOwnBounds()
    {
        var below = new Rect(-348, 1080, 1920, 1080);
        var outer = new Rect(-355, 1080, 974, 1087); // left half of the lower monitor
        var steps = PlacementPlanner.Plan(outer, below);

        Assert.Equal(new Rect(-355, 1080, 974, 1080), steps[0].Rect);
    }
}

public class RectTests
{
    [Fact]
    public void Intersect_Overlapping() =>
        Assert.Equal(new Rect(5, 5, 5, 5), new Rect(0, 0, 10, 10).Intersect(new Rect(5, 5, 10, 10)));

    [Fact]
    public void Intersect_Disjoint_IsEmpty() =>
        Assert.True(new Rect(0, 0, 10, 10).Intersect(new Rect(20, 20, 5, 5)).IsEmpty);

    [Fact]
    public void Intersect_Touching_IsEmpty() =>
        Assert.True(new Rect(0, 0, 10, 10).Intersect(new Rect(10, 0, 10, 10)).IsEmpty);

    [Fact]
    public void Contains()
    {
        var r = new Rect(0, 0, 100, 100);
        Assert.True(r.Contains(new Rect(0, 0, 100, 100)));
        Assert.True(r.Contains(new Rect(10, 10, 5, 5)));
        Assert.False(r.Contains(new Rect(-1, 0, 10, 10)));
        Assert.False(r.Contains(new Rect(95, 95, 10, 10)));
    }
}
