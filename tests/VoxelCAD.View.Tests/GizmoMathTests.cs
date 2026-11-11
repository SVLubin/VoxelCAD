using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The handle arithmetic. Signs and units are the whole of it: a drag that goes
/// the wrong way, or that measures world distance where the element's own frame
/// was meant, looks like a broken gizmo and is really one line of algebra.
/// </summary>
public sealed class GizmoMathTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void ProjectingReadsHowFarAlongTheAxisThePointerIs()
    {
        Assert.True(GizmoMath.TryProjectOnAxis(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(5f, 10f, 0f),
            new Vector3(0f, -1f, 0f),
            out float parameter));

        Assert.Equal(5f, parameter, Tolerance);
    }

    /// <summary>
    /// The units are the direction vector's, not the world's — which is what lets
    /// the caller pass a scaled parent's axis and get an answer already in the
    /// units that element's position is written in.
    /// </summary>
    [Fact]
    public void TheAnswerIsInMultiplesOfTheDirectionGiven()
    {
        Assert.True(GizmoMath.TryProjectOnAxis(
            Vector3.Zero,
            new Vector3(2f, 0f, 0f),
            new Vector3(5f, 10f, 0f),
            new Vector3(0f, -1f, 0f),
            out float parameter));

        Assert.Equal(2.5f, parameter, Tolerance);
    }

    [Fact]
    public void ProjectingAlongTheAxisHasNoAnswer()
    {
        Assert.False(GizmoMath.TryProjectOnAxis(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            out _));
    }

    /// <summary>
    /// The plane's own two coordinates, and in the units of the directions given —
    /// the two-axis reading of the axis case above, and what makes a plane drag come
    /// out already in the units the element's position is written in.
    /// </summary>
    [Fact]
    public void ProjectingOnAPlaneReadsBothCoordinates()
    {
        Assert.True(GizmoMath.TryProjectOnPlane(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(3f, 10f, 4f),
            new Vector3(0f, -1f, 0f),
            out float alongU,
            out float alongV));

        Assert.Equal(3f, alongU, Tolerance);
        Assert.Equal(4f, alongV, Tolerance);
    }

    [Fact]
    public void ThePlaneCoordinatesAreInMultiplesOfTheDirectionsGiven()
    {
        Assert.True(GizmoMath.TryProjectOnPlane(
            Vector3.Zero,
            new Vector3(2f, 0f, 0f),
            new Vector3(0f, 0f, 4f),
            new Vector3(3f, 10f, 4f),
            new Vector3(0f, -1f, 0f),
            out float alongU,
            out float alongV));

        Assert.Equal(1.5f, alongU, Tolerance);
        Assert.Equal(1f, alongV, Tolerance);
    }

    /// <summary>
    /// A skewed parent has axes that are genuinely not perpendicular, and the pair
    /// wanted is the one that adds back up to where the pointer is — which two
    /// independent projections would not give.
    /// </summary>
    [Fact]
    public void SkewedAxesGetTheCoordinatesThatAddBackUp()
    {
        Vector3 directionU = new(1f, 0f, 0f);
        Vector3 directionV = new(1f, 0f, 1f);

        Assert.True(GizmoMath.TryProjectOnPlane(
            Vector3.Zero,
            directionU,
            directionV,
            new Vector3(3f, 10f, 2f),
            new Vector3(0f, -1f, 0f),
            out float alongU,
            out float alongV));

        Assert.Equal(1f, alongU, Tolerance);
        Assert.Equal(2f, alongV, Tolerance);
        Assert.Equal(
            new Vector3(3f, 0f, 2f),
            directionU * alongU + directionV * alongV);
    }

    [Fact]
    public void ProjectingOnAPlaneSeenEdgeOnHasNoAnswer()
    {
        Assert.False(GizmoMath.TryProjectOnPlane(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 5f),
            new Vector3(1f, 0f, 0f),
            out _,
            out _));
    }

    /// <summary>Two directions that span no plane have no plane to project onto.</summary>
    [Fact]
    public void ParallelDirectionsSpanNoPlane()
    {
        Assert.False(GizmoMath.TryProjectOnPlane(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(0f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            out _,
            out _));
    }

    [Fact]
    public void ARayThroughThePatchHitsIt()
    {
        Assert.True(GizmoMath.TryHitQuad(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            0.3f,
            0.6f,
            new Vector3(0.45f, 5f, 0.45f),
            new Vector3(0f, -1f, 0f),
            out float distance));

        Assert.Equal(5f, distance, Tolerance);
    }

    /// <summary>
    /// The patch stands off the origin, so the corner between the two shafts is a
    /// hole in it — that is what leaves the shafts themselves grabbable.
    /// </summary>
    [Fact]
    public void ARayInsideTheNearCornerMissesThePatch()
    {
        Assert.False(GizmoMath.TryHitQuad(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            0.3f,
            0.6f,
            new Vector3(0.1f, 5f, 0.1f),
            new Vector3(0f, -1f, 0f),
            out _));
    }

    /// <summary>
    /// Past the far edge on one axis alone: the bounds are a square, not a band, so
    /// being inside on the other axis does not save it.
    /// </summary>
    [Fact]
    public void ARayBeyondOneEdgeMissesThePatch()
    {
        Assert.False(GizmoMath.TryHitQuad(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            0.3f,
            0.6f,
            new Vector3(0.45f, 5f, 0.9f),
            new Vector3(0f, -1f, 0f),
            out _));
    }

    /// <summary>
    /// The bounds are in the directions' own units, so passing the very columns the
    /// patch is drawn from tests the parallelogram that is on screen — the reason
    /// there is no second copy of its shape to keep in step.
    /// </summary>
    [Fact]
    public void ThePatchIsBoundedInTheUnitsOfItsDirections()
    {
        Vector3 directionU = new(4f, 0f, 0f);
        Vector3 directionV = new(0f, 0f, 4f);

        Assert.True(GizmoMath.TryHitQuad(
            Vector3.Zero,
            directionU,
            directionV,
            0.3f,
            0.6f,
            new Vector3(1.8f, 5f, 1.8f),
            new Vector3(0f, -1f, 0f),
            out _));

        Assert.False(GizmoMath.TryHitQuad(
            Vector3.Zero,
            directionU,
            directionV,
            0.3f,
            0.6f,
            new Vector3(0.45f, 5f, 0.45f),
            new Vector3(0f, -1f, 0f),
            out _));
    }

    [Fact]
    public void APatchFacingTheEyeIsWorthAimingAt()
    {
        Assert.True(GizmoMath.IsPlaneFacing(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 10f, 0f),
            0.2f));
    }

    /// <summary>
    /// Nearly edge-on is a line on screen, laid over the shafts it sits between —
    /// unclickable, and worse than unclickable if it were still hit-tested.
    /// </summary>
    [Fact]
    public void APatchNearlyEdgeOnIsNot()
    {
        Assert.False(GizmoMath.IsPlaneFacing(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 1f, 10f),
            0.2f));
    }

    /// <summary>
    /// Either side of the plane counts: the patch is a two-sided sheet, and which
    /// side of it the camera is on says nothing about whether it can be grabbed.
    /// </summary>
    [Fact]
    public void APatchSeenFromBelowFacesTheEyeToo()
    {
        Assert.True(GizmoMath.IsPlaneFacing(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, -10f, 0f),
            0.2f));
    }

    [Fact]
    public void ARayThroughTheShaftHitsIt()
    {
        Assert.True(GizmoMath.TryHitSegment(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0.5f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            0.1f,
            out float distance));

        Assert.Equal(5f, distance, Tolerance);
    }

    /// <summary>Past the end of the shaft is past the handle, however well aimed.</summary>
    [Fact]
    public void ARayBeyondTheEndMisses()
    {
        Assert.False(GizmoMath.TryHitSegment(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(2f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            0.1f,
            out _));
    }

    [Fact]
    public void AHandleBehindTheEyeIsNotHit()
    {
        Assert.False(GizmoMath.TryHitSegment(
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            new Vector3(0.5f, 5f, 0f),
            new Vector3(0f, 1f, 0f),
            0.1f,
            out _));
    }

    [Fact]
    public void ARayThroughTheRimHitsTheRing()
    {
        Assert.True(GizmoMath.TryHitRing(
            Vector3.Zero,
            new Vector3(0f, 1f, 0f),
            1f,
            0.1f,
            new Vector3(1f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            out float distance));

        Assert.Equal(5f, distance, Tolerance);
    }

    /// <summary>Through the middle of the ring is not on it — that is the hole.</summary>
    [Fact]
    public void ARayThroughTheMiddleMissesTheRing()
    {
        Assert.False(GizmoMath.TryHitRing(
            Vector3.Zero,
            new Vector3(0f, 1f, 0f),
            1f,
            0.1f,
            new Vector3(0.5f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            out _));
    }

    [Fact]
    public void ARingSeenExactlyEdgeOnIsNotHit()
    {
        Assert.False(GizmoMath.TryHitRing(
            Vector3.Zero,
            new Vector3(0f, 1f, 0f),
            1f,
            0.1f,
            new Vector3(5f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            out _));
    }

    /// <summary>
    /// Zero where the pointer is on the first axis given, and a quarter turn where
    /// it has moved towards the second — which is a positive rotation about the
    /// normal, matching what the same value added to that channel would do.
    /// </summary>
    [Fact]
    public void TheAngleGrowsFromTheFirstAxisTowardsTheSecond()
    {
        Vector3 normal = new(0f, 1f, 0f);
        Vector3 axisU = new(0f, 0f, 1f);
        Vector3 axisV = new(1f, 0f, 0f);

        Assert.True(GizmoMath.TryAngleOnPlane(
            Vector3.Zero,
            normal,
            axisU,
            axisV,
            new Vector3(0f, 5f, 1f),
            new Vector3(0f, -1f, 0f),
            out float atStart));

        Assert.True(GizmoMath.TryAngleOnPlane(
            Vector3.Zero,
            normal,
            axisU,
            axisV,
            new Vector3(1f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            out float aQuarterOn));

        Assert.Equal(0f, atStart, Tolerance);
        Assert.Equal(Mathf.Pi / 2f, aQuarterOn, Tolerance);
    }

    [Fact]
    public void AnAngleOnAPlaneEdgeOnHasNoAnswer()
    {
        Assert.False(GizmoMath.TryAngleOnPlane(
            Vector3.Zero,
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(1f, 0f, 0f),
            new Vector3(5f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            out _));
    }

    /// <summary>
    /// The step that crosses the wrap point: measured angles jump by nearly a whole
    /// turn there, and the drag has to read that as the small step it really was.
    /// </summary>
    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(-0.5f, -0.5f)]
    [InlineData(Mathf.Tau - 0.5f, -0.5f)]
    [InlineData(-(Mathf.Tau - 0.5f), 0.5f)]
    public void AStepIsBroughtWithinHalfATurn(float step, float expected)
    {
        Assert.Equal(expected, GizmoMath.WrapRadians(step), Tolerance);
    }
}
