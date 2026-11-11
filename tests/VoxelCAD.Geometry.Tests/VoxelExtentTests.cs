using Godot;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// The box a set of cells occupies. The half-open convention is the whole of it:
/// a cell spans from its own coordinate to the next, so a single cell is a
/// one-unit box rather than a point — and its middle is therefore half a cell
/// away from the coordinate that names it.
/// </summary>
public sealed class VoxelExtentTests
{
    [Fact]
    public void NoCellsHaveNoBox()
    {
        Assert.True(VoxelExtent.Measure(new Vector3I[0]).IsEmpty);
    }

    [Fact]
    public void OneCellSpansToTheNextCoordinate()
    {
        VoxelExtent extent = VoxelExtent.Measure(new[] { new Vector3I(2, 3, 4) });

        Assert.False(extent.IsEmpty);
        Assert.Equal(new Vector3(2f, 3f, 4f), extent.Corner);
        Assert.Equal(new Vector3(3f, 4f, 5f), extent.FarCorner);
        Assert.Equal(Vector3I.One, extent.Size);
    }

    [Fact]
    public void TheBoxCoversEveryCell()
    {
        VoxelExtent extent = VoxelExtent.Measure(new[]
        {
            new Vector3I(-2, 0, 5),
            new Vector3I(4, 1, -3),
            new Vector3I(0, 7, 0)
        });

        Assert.Equal(new Vector3(-2f, 0f, -3f), extent.Corner);
        Assert.Equal(new Vector3(5f, 8f, 6f), extent.FarCorner);
        Assert.Equal(new Vector3I(-2, 0, -3), extent.Min);
        Assert.Equal(new Vector3I(4, 7, 5), extent.Max);
    }

    /// <summary>
    /// An odd run of cells is centred on a boundary between two of them and an
    /// even run through the middle of one, so the answer lands on the half-cell
    /// lattice either way — which is why the exact form is the doubled one.
    /// </summary>
    [Fact]
    public void TheMiddleFallsOnTheHalfCellLattice()
    {
        VoxelExtent odd = VoxelExtent.Measure(new[]
        {
            new Vector3I(0, 0, 0), new Vector3I(2, 0, 0)
        });

        Assert.Equal(new Vector3I(3, 1, 1), odd.CenterDoubled);
        Assert.Equal(new Vector3(1.5f, 0.5f, 0.5f), odd.Center);

        VoxelExtent even = VoxelExtent.Measure(new[]
        {
            new Vector3I(0, 0, 0), new Vector3I(3, 0, 0)
        });

        Assert.Equal(new Vector3I(4, 1, 1), even.CenterDoubled);
        Assert.Equal(new Vector3(2f, 0.5f, 0.5f), even.Center);
    }

    [Fact]
    public void AnEmptyBoxHasNoMiddle()
    {
        Assert.Equal(Vector3.Zero, VoxelExtent.Measure(new Vector3I[0]).Center);
    }
}
