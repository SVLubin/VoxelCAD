using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The names the 26 zones answer to on the command surface. Lenient about the
/// order of the parts, exact about everything else — an unknown direction is a typo
/// to correct, and guessing at one would aim the user's camera somewhere nobody
/// asked for.
/// </summary>
public sealed class ViewCubeDirectionsTests
{
    [Theory]
    [InlineData("front", 0, 0, 1)]
    [InlineData("back", 0, 0, -1)]
    [InlineData("left", -1, 0, 0)]
    [InlineData("right", 1, 0, 0)]
    [InlineData("top", 0, 1, 0)]
    [InlineData("bottom", 0, -1, 0)]
    [InlineData("top-front-right", 1, 1, 1)]
    [InlineData("bottom-back-left", -1, -1, -1)]
    [InlineData("top-front", 0, 1, 1)]
    [InlineData("front-right", 1, 0, 1)]
    public void ANameReachesItsZone(string name, int x, int y, int z)
    {
        Assert.True(ViewCubeDirections.TryParse(name, out Vector3I zone));
        Assert.Equal(new Vector3I(x, y, z), zone);
    }

    /// <summary>Any order parses; only one order comes back out.</summary>
    [Fact]
    public void ThePartsMayArriveInAnyOrder()
    {
        Assert.True(ViewCubeDirections.TryParse("right-front-top", out Vector3I zone));
        Assert.Equal(new Vector3I(1, 1, 1), zone);
        Assert.Equal("top-front-right", ViewCubeDirections.GetName(zone));
    }

    [Fact]
    public void CaseAndSurroundingSpaceDoNotMatter()
    {
        Assert.True(ViewCubeDirections.TryParse("  Top-Front  ", out Vector3I zone));
        Assert.Equal(new Vector3I(0, 1, 1), zone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sideways")]
    [InlineData("front-sideways")]
    [InlineData("top-bottom")]
    [InlineData("front-back")]
    [InlineData("top-top")]
    [InlineData("front-")]
    public void AnythingElseIsRefused(string name)
    {
        Assert.False(ViewCubeDirections.TryParse(name, out _));
    }

    /// <summary>Every generated name parses back to the zone it was generated from.</summary>
    [Fact]
    public void EveryNameRoundTrips()
    {
        foreach (string name in ViewCubeDirections.GetNames())
        {
            Assert.True(ViewCubeDirections.TryParse(name, out Vector3I zone));
            Assert.Equal(name, ViewCubeDirections.GetName(zone));
        }
    }

    /// <summary>Faces first, then edges, then corners: 6, 12, 8.</summary>
    [Fact]
    public void TheNamesAreListedSimplestFirst()
    {
        System.Collections.Generic.IReadOnlyList<string> names = ViewCubeDirections.GetNames();

        Assert.Equal(26, names.Count);

        for (var index = 0; index < names.Count; index++)
        {
            int expected = index < 6 ? 0 : index < 18 ? 1 : 2;

            Assert.Equal(expected, CountSeparators(names[index]));
        }
    }

    private static int CountSeparators(string name) => name.Split('-').Length - 1;
}
