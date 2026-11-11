using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The conversion between the document's three values and a matrix. Both
/// directions were wrong in ways that only showed up together with rotation, and
/// both of them moved geometry: the composition applied scale in the parent's
/// frame where the viewport applies it in the element's, and the decomposition
/// read Euler angles off a basis that still had the scale in it.
/// </summary>
public sealed class TransformHelperTests
{
    private const float Tolerance = 1e-3f;

    /// <summary>
    /// The reference composition, spelled out the way <see cref="Node3D"/> builds
    /// it from position, rotation and scale — which is what the viewport shows.
    /// </summary>
    private static Basis NodeBasis(Vector3 rotationDegrees, Vector3 scale)
    {
        Vector3 radians = new(
            Mathf.DegToRad(rotationDegrees.X),
            Mathf.DegToRad(rotationDegrees.Y),
            Mathf.DegToRad(rotationDegrees.Z));

        return Basis.FromEuler(radians) * Basis.FromScale(scale);
    }

    [Theory]
    [InlineData(0f, 90f, 0f, 1f, 2f, 3f)]
    [InlineData(0f, 30f, 0f, 1f, 2f, 3f)]
    [InlineData(30f, 40f, 50f, 1f, 2f, 3f)]
    [InlineData(90f, 0f, 0f, 2f, 1f, 1f)]
    public void ScaleIsAppliedInTheElementsOwnFrame(
        float rotationX,
        float rotationY,
        float rotationZ,
        float scaleX,
        float scaleY,
        float scaleZ)
    {
        Vector3 rotation = new(rotationX, rotationY, rotationZ);
        Vector3 scale = new(scaleX, scaleY, scaleZ);

        Transform3D composed = TransformHelper.ToTransform(
            new TransformValues(Vector3.Zero, rotation, scale));

        Assert.True(
            composed.Basis.IsEqualApprox(NodeBasis(rotation, scale)),
            $"expected {NodeBasis(rotation, scale)}, got {composed.Basis}");
    }

    [Theory]
    [InlineData(0f, 30f, 0f, 1f, 2f, 3f)]
    [InlineData(30f, 40f, 50f, 1f, 2f, 3f)]
    [InlineData(0f, 90f, 0f, 1f, 2f, 3f)]
    [InlineData(30f, 40f, 50f, 1f, 1f, 1f)]
    [InlineData(0f, 0f, 0f, -1f, 1f, 1f)]
    [InlineData(0f, 90f, 0f, -1f, 1f, 1f)]
    public void ARoundTripThroughTheMatrixKeepsTheOrientation(
        float rotationX,
        float rotationY,
        float rotationZ,
        float scaleX,
        float scaleY,
        float scaleZ)
    {
        var original = new TransformValues(
            new Vector3(1f, 2f, 3f),
            new Vector3(rotationX, rotationY, rotationZ),
            new Vector3(scaleX, scaleY, scaleZ));

        Transform3D matrix = TransformHelper.ToTransform(original);
        Transform3D again = TransformHelper.ToTransform(TransformHelper.ToValues(matrix));

        Assert.True(again.IsEqualApprox(matrix), $"expected {matrix}, got {again}");
    }

    /// <summary>
    /// The case that made this visible: with the scale still in the basis, Godot's
    /// GetEuler answered (90, -45.8, 0) for a (30, 40, 50) rotation.
    /// </summary>
    [Fact]
    public void AnglesAreReadOffTheRotationAloneAndNotTheScaledBasis()
    {
        var original = new TransformValues(
            Vector3.Zero,
            new Vector3(30f, 40f, 50f),
            new Vector3(1f, 2f, 3f));

        TransformValues values = TransformHelper.ToValues(TransformHelper.ToTransform(original));

        Assert.Equal(30f, values.RotationDegrees.X, Tolerance);
        Assert.Equal(40f, values.RotationDegrees.Y, Tolerance);
        Assert.Equal(50f, values.RotationDegrees.Z, Tolerance);
        Assert.Equal(1f, values.Scale.X, Tolerance);
        Assert.Equal(2f, values.Scale.Y, Tolerance);
        Assert.Equal(3f, values.Scale.Z, Tolerance);
    }
}
