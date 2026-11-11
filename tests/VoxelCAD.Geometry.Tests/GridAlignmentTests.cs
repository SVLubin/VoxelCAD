using Godot;
using VoxelCAD.Geometry;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// Whether an element's cells sit on the world's cell grid. Three layers ask it —
/// the exporter, the viewport and the command surface — so what it answers is
/// worth pinning down in one place: same cell size, same orientation, same phase.
/// </summary>
public sealed class GridAlignmentTests
{
    private static Transform3D Build(Vector3 rotationDegrees, Vector3 scale, Vector3 origin)
    {
        Vector3 radians = new(
            Mathf.DegToRad(rotationDegrees.X),
            Mathf.DegToRad(rotationDegrees.Y),
            Mathf.DegToRad(rotationDegrees.Z));

        return new Transform3D(Basis.FromEuler(radians) * Basis.FromScale(scale), origin);
    }

    [Fact]
    public void TheIdentityIsAligned() =>
        Assert.True(GridAlignment.IsAligned(Transform3D.Identity));

    /// <summary>
    /// The case the whole predicate is shaped around: a right angle is a signed
    /// permutation of the axes, so it keeps the two grids on top of each other and
    /// must not read as a tilt.
    /// </summary>
    [Theory]
    [InlineData(90f, 0f, 0f)]
    [InlineData(0f, 90f, 0f)]
    [InlineData(0f, 0f, 90f)]
    [InlineData(180f, 0f, 0f)]
    [InlineData(-90f, 0f, 0f)]
    [InlineData(90f, 90f, 0f)]
    [InlineData(90f, 180f, 270f)]
    public void RightAnglesStayAligned(float x, float y, float z) =>
        Assert.True(GridAlignment.IsAligned(Build(new Vector3(x, y, z), Vector3.One, Vector3.Zero)));

    [Theory]
    [InlineData(45f, 0f, 0f)]
    [InlineData(0f, 30f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(89f, 0f, 0f)]
    public void AnyOtherAngleIsNot(float x, float y, float z) =>
        Assert.False(GridAlignment.IsAligned(Build(new Vector3(x, y, z), Vector3.One, Vector3.Zero)));

    [Theory]
    [InlineData(2f, 1f, 1f)]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(1f, 1f, 3f)]
    public void AScaleOtherThanUnitIsNotAligned(float x, float y, float z) =>
        Assert.False(GridAlignment.IsAligned(Build(Vector3.Zero, new Vector3(x, y, z), Vector3.Zero)));

    /// <summary>A mirrored axis still maps the grid onto itself, cell for cell.</summary>
    [Theory]
    [InlineData(-1f, 1f, 1f)]
    [InlineData(-1f, -1f, -1f)]
    public void AMirroredUnitScaleIsAligned(float x, float y, float z) =>
        Assert.True(GridAlignment.IsAligned(Build(Vector3.Zero, new Vector3(x, y, z), Vector3.Zero)));

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(3f, -7f, 12f)]
    public void AWholeTranslationKeepsThePhase(float x, float y, float z) =>
        Assert.True(GridAlignment.IsAligned(Build(Vector3.Zero, Vector3.One, new Vector3(x, y, z))));

    [Theory]
    [InlineData(0.5f, 0f, 0f)]
    [InlineData(0f, -6.27f, 0f)]
    public void AFractionalTranslationDoesNot(float x, float y, float z) =>
        Assert.False(GridAlignment.IsAligned(Build(Vector3.Zero, Vector3.One, new Vector3(x, y, z))));

    /// <summary>
    /// A right angle is not exact in floating point and a composed chain drifts
    /// further, so the tolerance has to cover more than one multiplication. This is
    /// the measured worst case of one: Godot's cosine of 90°.
    /// </summary>
    [Fact]
    public void AccumulatedFloatErrorStillCountsAsAligned()
    {
        Transform3D parent = Build(new Vector3(0f, 90f, 0f), Vector3.One, new Vector3(3f, 0f, 0f));
        Transform3D child = Build(new Vector3(90f, 0f, 0f), Vector3.One, new Vector3(1f, 0f, 0f));

        Assert.True(GridAlignment.IsAligned(parent * child));
    }

    [Fact]
    public void ADegenerateBasisIsNotAligned() =>
        Assert.False(GridAlignment.IsAligned(
            Build(Vector3.Zero, new Vector3(1f, 0f, 1f), Vector3.Zero)));
}
