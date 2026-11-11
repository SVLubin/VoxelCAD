using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Where the brush marker stands. The rule is that it stands exactly where the
/// voxel would: the cell offset belongs in the element's own frame, and applying
/// it in the parent's put the marker somewhere else entirely as soon as the
/// element was rotated or scaled.
/// </summary>
public sealed class BrushPreviewTests
{
    private const float Tolerance = 1e-4f;

    /// <summary>
    /// Where the voxel itself ends up: the element's world transform applied to
    /// the centre of the cell. The marker has to agree with this, whatever the
    /// transform.
    /// </summary>
    private static Vector3 CellCentreInWorld(Transform3D world, Vector3I cell) =>
        world * new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    private static void AssertMarkerSitsOnTheCell(Transform3D world, Vector3I cell)
    {
        Transform3D marker = BrushPreview.GetCellTransform(world, cell);
        Vector3 expected = CellCentreInWorld(world, cell);

        Assert.Equal(expected.X, marker.Origin.X, Tolerance);
        Assert.Equal(expected.Y, marker.Origin.Y, Tolerance);
        Assert.Equal(expected.Z, marker.Origin.Z, Tolerance);
    }

    [Fact]
    public void AtTheOriginTheMarkerCoversTheCell()
    {
        Transform3D marker = BrushPreview.GetCellTransform(Transform3D.Identity, new Vector3I(2, 3, 4));

        Assert.Equal(new Vector3(2.5f, 3.5f, 4.5f), marker.Origin);
        Assert.True(marker.Basis.IsEqualApprox(Basis.Identity));
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1f, 1f, 1f, 0f, 0f, 0f)]
    [InlineData(0f, 90f, 0f, 1f, 1f, 1f, 0f, 0f, 0f)]
    [InlineData(0f, 45f, 0f, 1f, 1f, 1f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 2f, 2f, 2f, 0f, 0f, 0f)]
    [InlineData(0f, 30f, 0f, 1f, 2f, 3f, -16f, -6.27f, -16f)]
    [InlineData(30f, 40f, 50f, 1.5f, 1f, 0.5f, 3.5f, 0f, -2f)]
    public void TheMarkerFollowsTheElementsOwnFrame(
        float rotationX,
        float rotationY,
        float rotationZ,
        float scaleX,
        float scaleY,
        float scaleZ,
        float positionX,
        float positionY,
        float positionZ)
    {
        Vector3 radians = new(
            Mathf.DegToRad(rotationX),
            Mathf.DegToRad(rotationY),
            Mathf.DegToRad(rotationZ));

        var world = new Transform3D(
            Basis.FromEuler(radians) * Basis.FromScale(new Vector3(scaleX, scaleY, scaleZ)),
            new Vector3(positionX, positionY, positionZ));

        AssertMarkerSitsOnTheCell(world, new Vector3I(5, -2, 7));
    }

    /// <summary>
    /// A nested element is placed by the composed transform, which is what the
    /// node tree hands over — so the marker has to be right for that too.
    /// </summary>
    [Fact]
    public void ANestedElementIsNoDifferent()
    {
        var parent = new Transform3D(
            Basis.FromEuler(new Vector3(0f, Mathf.DegToRad(90f), 0f)),
            new Vector3(4f, 0f, 0f));
        var child = new Transform3D(
            Basis.FromScale(new Vector3(2f, 2f, 2f)),
            new Vector3(1f, 1f, 0f));

        AssertMarkerSitsOnTheCell(parent * child, new Vector3I(1, 0, -3));
    }

    /// <summary>The marker is the cell, so it carries the element's rotation and scale.</summary>
    [Fact]
    public void TheMarkerIsShapedLikeTheVoxelItStandsFor()
    {
        var world = new Transform3D(
            Basis.FromEuler(new Vector3(0f, Mathf.DegToRad(90f), 0f))
                * Basis.FromScale(new Vector3(2f, 2f, 2f)),
            Vector3.Zero);

        Transform3D marker = BrushPreview.GetCellTransform(world, Vector3I.Zero);

        Assert.True(marker.Basis.IsEqualApprox(world.Basis));
    }
}
