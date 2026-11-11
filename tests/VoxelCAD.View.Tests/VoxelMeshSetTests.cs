using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The split that decides how many instances an element's geometry becomes. The
/// meshes themselves cannot be built without an engine, so what is held here is
/// the rule; that it comes out right on screen was measured by driving — a red
/// pane and a blue pane at alpha 0.5 read R/B 0.68 from one side and 1.20 from
/// the other, in one element and in two alike.
/// </summary>
public class VoxelMeshSetTests
{
    /// <summary>
    /// Anything the eye can see through is sorted by the engine, and the engine
    /// sorts instances — so each such material needs one of its own.
    /// </summary>
    [Fact]
    public void ATransparentMaterialNeedsAnInstanceOfItsOwn()
    {
        Assert.True(VoxelMeshSet.NeedsOwnInstance(0f));
        Assert.True(VoxelMeshSet.NeedsOwnInstance(0.5f));
        Assert.True(VoxelMeshSet.NeedsOwnInstance(0.999f));
    }

    /// <summary>
    /// And an opaque one does not: the depth buffer orders it whatever sequence it
    /// arrives in, so an instance apiece would be a node per material on every
    /// ordinary model and would buy nothing.
    /// </summary>
    [Fact]
    public void AnOpaqueMaterialSharesTheOneMesh()
    {
        Assert.False(VoxelMeshSet.NeedsOwnInstance(1f));
    }

    /// <summary>An element with nothing visible in it is a valid thing to build.</summary>
    [Fact]
    public void ASetWithNoMeshesIsEmpty()
    {
        Assert.True(default(VoxelMeshSet).IsEmpty);
    }
}
