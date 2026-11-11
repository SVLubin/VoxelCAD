using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The view cube's arithmetic: which of the 26 zones a ray lands on, and which way
/// the camera has to look to be standing in one. Both are shared with the
/// <c>view</c> command, so what these pin is what an agent gets as much as what the
/// mouse does.
/// </summary>
public sealed class ViewCubeGeometryTests
{
    /// <summary>A neutral starting pose, well away from every angle under test.</summary>
    private static readonly CameraPose Current =
        new(new Vector3(8f, 3f, 8f), 123f, -17f, 30f);

    /// <summary>
    /// Everything the cube can be looked at from is outside it, so a ray is built
    /// by standing off a face and firing straight at it.
    /// </summary>
    private static Vector3I PickFacingMinusZ(float x, float y)
    {
        Assert.True(ViewCubeGeometry.TryPick(
            new Vector3(x, y, 3f),
            new Vector3(0f, 0f, -1f),
            out Vector3I zone));

        return zone;
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, -1)]
    public void EachFaceIsHitThroughItsOwnCentre(int x, int y, int z)
    {
        var zone = new Vector3I(x, y, z);
        Vector3 direction = ViewCubeGeometry.GetDirection(zone);

        Assert.True(ViewCubeGeometry.TryPick(direction * 3f, -direction, out Vector3I hit));
        Assert.Equal(zone, hit);
    }

    [Fact]
    public void TheCentreOfAFaceIsTheFaceZone()
    {
        Assert.Equal(new Vector3I(0, 0, 1), PickFacingMinusZ(0f, 0f));
    }

    [Theory]
    [InlineData(0.4f, 0f, 1, 0)]
    [InlineData(-0.4f, 0f, -1, 0)]
    [InlineData(0f, 0.4f, 0, 1)]
    [InlineData(0f, -0.4f, 0, -1)]
    public void AnOuterBandOfAFaceIsAnEdgeZone(float x, float y, int zoneX, int zoneY)
    {
        Assert.Equal(new Vector3I(zoneX, zoneY, 1), PickFacingMinusZ(x, y));
    }

    [Theory]
    [InlineData(0.4f, 0.4f, 1, 1)]
    [InlineData(-0.4f, 0.4f, -1, 1)]
    [InlineData(0.4f, -0.4f, 1, -1)]
    [InlineData(-0.4f, -0.4f, -1, -1)]
    public void ACornerOfAFaceIsACornerZone(float x, float y, int zoneX, int zoneY)
    {
        Assert.Equal(new Vector3I(zoneX, zoneY, 1), PickFacingMinusZ(x, y));
    }

    /// <summary>
    /// A ray down the body diagonal enters through a corner of the cube itself.
    /// Whichever of the three faces the slab test settles on, the cell is that
    /// face's corner one — so the answer is the corner zone either way, which is
    /// what makes the tie harmless rather than merely arbitrary.
    /// </summary>
    [Fact]
    public void ARayDownTheBodyDiagonalResolvesToTheCorner()
    {
        var direction = new Vector3(1f, 1f, 1f).Normalized();

        Assert.True(ViewCubeGeometry.TryPick(direction * 4f, -direction, out Vector3I zone));
        Assert.Equal(new Vector3I(1, 1, 1), zone);
    }

    [Fact]
    public void ARayPastTheCubeHitsNothing()
    {
        Assert.False(ViewCubeGeometry.TryPick(
            new Vector3(2f, 2f, 3f),
            new Vector3(0f, 0f, -1f),
            out _));
    }

    [Fact]
    public void ARayPointingAwayFromTheCubeHitsNothing()
    {
        Assert.False(ViewCubeGeometry.TryPick(
            new Vector3(0f, 0f, 3f),
            new Vector3(0f, 0f, 1f),
            out _));
    }

    /// <summary>
    /// The tie-break, pinned: a ray landing exactly on a band boundary resolves
    /// towards the face. The face is the biggest and most-wanted target, so that is
    /// where an ambiguity should fall — and it should not depend on which
    /// comparison happened to be written first.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void ARayGrazingABandBoundaryFallsToTheFace(float side)
    {
        const float boundary = ViewCubeGeometry.HalfExtent / 3f;

        Assert.Equal(new Vector3I(0, 0, 1), PickFacingMinusZ(boundary * side, 0f));
    }

    /// <summary>A hair past the boundary is the neighbouring band, so the edge is reachable.</summary>
    [Fact]
    public void JustPastTheBoundaryIsTheEdgeZone()
    {
        const float boundary = ViewCubeGeometry.HalfExtent / 3f;

        Assert.Equal(new Vector3I(1, 0, 1), PickFacingMinusZ(boundary + 0.01f, 0f));
    }

    [Theory]
    [InlineData(0, 0, 1, 0f)]
    [InlineData(0, 0, -1, 180f)]
    [InlineData(1, 0, 0, 90f)]
    [InlineData(-1, 0, 0, -90f)]
    public void AFaceLooksAlongItsOwnAxis(int x, int y, int z, float yaw)
    {
        CameraPose pose = ViewCubeGeometry.GetPose(Current, new Vector3I(x, y, z));

        Assert.Equal(yaw, pose.YawDegrees, 3);
        Assert.Equal(0f, pose.PitchDegrees, 3);
    }

    /// <summary>
    /// Straight up and straight down have no yaw of their own, so they keep the one
    /// they arrived with rather than spinning the model on the way there.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void TopAndBottomKeepTheCurrentYaw(int y)
    {
        CameraPose pose = ViewCubeGeometry.GetPose(Current, new Vector3I(0, y, 0));

        Assert.Equal(Current.YawDegrees, pose.YawDegrees, 3);
    }

    /// <summary>
    /// And they aim at the pitch the camera will actually hold, not the ±90° it
    /// would silently correct — otherwise an agent reading the pose back sees a
    /// number it did not ask for.
    /// </summary>
    [Fact]
    public void TopAimsAtThePitchLimitRatherThanStraightDown()
    {
        float limit = Mathf.RadToDeg(CameraFraming.PitchLimit);

        Assert.Equal(-limit, ViewCubeGeometry.GetPose(Current, new Vector3I(0, 1, 0)).PitchDegrees, 3);
        Assert.Equal(limit, ViewCubeGeometry.GetPose(Current, new Vector3I(0, -1, 0)).PitchDegrees, 3);
        Assert.True(limit < 90f);
    }

    /// <summary>The isometric view: 45° round, and the body diagonal's own angle above.</summary>
    [Fact]
    public void ACornerIsTheIsometricView()
    {
        CameraPose pose = ViewCubeGeometry.GetPose(Current, new Vector3I(1, 1, 1));

        Assert.Equal(45f, pose.YawDegrees, 3);
        Assert.Equal(-35.264f, pose.PitchDegrees, 3);
    }

    [Fact]
    public void AnEdgeIsHalfWayBetweenItsTwoFaces()
    {
        CameraPose pose = ViewCubeGeometry.GetPose(Current, new Vector3I(1, 0, 1));

        Assert.Equal(45f, pose.YawDegrees, 3);
        Assert.Equal(0f, pose.PitchDegrees, 3);

        CameraPose above = ViewCubeGeometry.GetPose(Current, new Vector3I(0, 1, 1));

        Assert.Equal(0f, above.YawDegrees, 3);
        Assert.Equal(-45f, above.PitchDegrees, 3);
    }

    /// <summary>
    /// The rule the whole widget rests on: a turn to a standard view is not a
    /// request to move the point the camera turns around, nor to change how far off
    /// it stands.
    /// </summary>
    [Fact]
    public void TheFocusPointAndTheDistanceAreNeverTouched()
    {
        foreach (string name in ViewCubeDirections.GetNames())
        {
            Assert.True(ViewCubeDirections.TryParse(name, out Vector3I zone));

            CameraPose pose = ViewCubeGeometry.GetPose(Current, zone);

            Assert.Equal(Current.Pivot, pose.Pivot);
            Assert.Equal(Current.Distance, pose.Distance);
        }
    }

    /// <summary>
    /// The pose a zone asks for really does put the camera in that zone's
    /// direction, measured with the very function that places the scene camera.
    /// Top and bottom are excluded because their pitch is deliberately clamped
    /// short of straight up.
    /// </summary>
    [Fact]
    public void AZonesPosePutsTheCameraInThatZonesDirection()
    {
        foreach (string name in ViewCubeDirections.GetNames())
        {
            Assert.True(ViewCubeDirections.TryParse(name, out Vector3I zone));

            if (zone.X == 0 && zone.Z == 0)
                continue;

            CameraPose pose = ViewCubeGeometry.GetPose(Current, zone);

            Vector3 offset = CameraFraming.GetOrbitOffset(
                Mathf.DegToRad(pose.YawDegrees),
                Mathf.DegToRad(pose.PitchDegrees));

            Assert.True(
                offset.DistanceTo(ViewCubeGeometry.GetDirection(zone)) < 1e-4f,
                $"{name}: {offset} is not {ViewCubeGeometry.GetDirection(zone)}");
        }
    }

    [Fact]
    public void ThereAreTwentySixZonesAndTheCentreIsNotOne()
    {
        Assert.Equal(26, ViewCubeDirections.GetNames().Count);
        Assert.False(ViewCubeGeometry.IsZone(Vector3I.Zero));
    }
}
