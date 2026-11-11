using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The camera's two pieces of arithmetic. The pan's directions are the sort of
/// thing that is a sign error away from feeling wrong and impossible to argue
/// about from a screenshot, so they are pinned here.
/// </summary>
public sealed class CameraFramingTests
{
    private const float Fov = 50f;
    private const float ViewportHeight = 800f;
    private const float Distance = 24f;

    /// <summary>Looking down at 45°, where a pan is well conditioned.</summary>
    private const float Pitch = -Mathf.Pi / 4f;

    [Fact]
    public void APanNeverChangesTheFocusHeight()
    {
        foreach (float pitch in new[] { -1.5f, -Mathf.Pi / 4f, -0.05f, 0f, 0.6f })
        {
            Vector3 offset = CameraFraming.CalculatePanOffset(
                new Vector2(37f, -84f), 0.7f, pitch, Distance, Fov, ViewportHeight);

            Assert.Equal(0f, offset.Y, 5);
        }
    }

    /// <summary>
    /// Yaw zero puts the camera on +Z looking towards -Z, so its right is +X and
    /// its ground forward is -Z. Dragging right pulls the world right, which the
    /// focus point answers by going left.
    /// </summary>
    [Fact]
    public void DraggingRightMovesTheFocusPointLeft()
    {
        Vector3 offset = CameraFraming.CalculatePanOffset(
            new Vector2(100f, 0f), 0f, Pitch, Distance, Fov, ViewportHeight);

        Assert.True(offset.X < 0f);
        Assert.Equal(0f, offset.Z, 5);
    }

    /// <summary>
    /// Dragging down pulls the ground towards the viewer, which is the rig
    /// advancing along its own forward — towards -Z at yaw zero.
    /// </summary>
    [Fact]
    public void DraggingDownMovesTheFocusPointAway()
    {
        Vector3 offset = CameraFraming.CalculatePanOffset(
            new Vector2(0f, 100f), 0f, Pitch, Distance, Fov, ViewportHeight);

        Assert.True(offset.Z < 0f);
        Assert.Equal(0f, offset.X, 5);
    }

    /// <summary>A quarter turn of yaw turns the pan with it.</summary>
    [Fact]
    public void ThePanFollowsTheYaw()
    {
        Vector3 offset = CameraFraming.CalculatePanOffset(
            new Vector2(100f, 0f), Mathf.Pi / 2f, Pitch, Distance, Fov, ViewportHeight);

        Assert.Equal(0f, offset.X, 4);
        Assert.True(offset.Z > 0f);
    }

    /// <summary>
    /// Twice as far away is twice as much world under the same drag, which is
    /// what keeps a pan feeling like the same gesture at every zoom.
    /// </summary>
    [Fact]
    public void ThePanScalesWithDistance()
    {
        var travel = new Vector2(60f, 40f);

        Vector3 near = CameraFraming.CalculatePanOffset(
            travel, 0.3f, Pitch, Distance, Fov, ViewportHeight);
        Vector3 far = CameraFraming.CalculatePanOffset(
            travel, 0.3f, Pitch, Distance * 2f, Fov, ViewportHeight);

        Assert.Equal(near.X * 2f, far.X, 4);
        Assert.Equal(near.Z * 2f, far.Z, 4);
    }

    /// <summary>
    /// Sliding exactly under the cursor costs 1/sin(pitch), which runs away as
    /// the view approaches level. The bound is what stops a pan at the horizon
    /// throwing the focus point into the distance.
    /// </summary>
    [Fact]
    public void APanAtTheHorizonStaysBounded()
    {
        Vector3 level = CameraFraming.CalculatePanOffset(
            new Vector2(0f, 100f), 0f, 0f, Distance, Fov, ViewportHeight);
        Vector3 steep = CameraFraming.CalculatePanOffset(
            new Vector2(0f, 100f), 0f, -Mathf.Pi / 2f, Distance, Fov, ViewportHeight);

        Assert.True(Mathf.Abs(level.Z) < Mathf.Abs(steep.Z) * 3f);
    }

    /// <summary>
    /// Straight down is the one angle at which the ground plane faces the camera
    /// squarely, so the exact rate and the screen rate coincide there.
    /// </summary>
    [Fact]
    public void LookingStraightDownPansAtTheScreenRate()
    {
        Vector3 offset = CameraFraming.CalculatePanOffset(
            new Vector2(0f, ViewportHeight), 0f, -Mathf.Pi / 2f, Distance, Fov, ViewportHeight);

        float viewHeightAtFocus = 2f * Distance * Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f);

        Assert.Equal(viewHeightAtFocus, Mathf.Abs(offset.Z), 3);
    }

    /// <summary>
    /// The focus point is the centre of the view, and the view belongs in the
    /// room: outside it there is nothing to turn around.
    /// </summary>
    [Fact]
    public void TheFocusPointStaysInsideTheRegion()
    {
        var region = new Vector3(32f, 32f, 32f);

        Assert.Equal(
            new Vector3(0f, 0f, 32f),
            CameraFraming.ClampToRegion(new Vector3(-14f, -3f, 91f), region));
    }

    /// <summary>
    /// Per axis, so a pan into a wall slides along it. Clamping the vector as a
    /// whole would stop the drag dead the moment one axis ran out.
    /// </summary>
    [Fact]
    public void AFocusPointAgainstOneWallStillMovesAlongTheOthers()
    {
        var region = new Vector3(32f, 32f, 32f);

        Vector3 first = CameraFraming.ClampToRegion(new Vector3(40f, 0f, 10f), region);
        Vector3 second = CameraFraming.ClampToRegion(new Vector3(45f, 0f, 18f), region);

        Assert.Equal(first.X, second.X);
        Assert.True(second.Z > first.Z);
    }

    /// <summary>
    /// A pan is measured from where it began, so the clamp never feeds its own
    /// output back in: dragging back off a wall returns at once, rather than
    /// having to spend the distance it was held against it.
    /// </summary>
    [Fact]
    public void APanComesBackOffAWallImmediately()
    {
        var region = new Vector3(32f, 32f, 32f);
        var begin = new Vector3(30f, 0f, 16f);

        Vector3 intoTheWall = CameraFraming.ClampToRegion(
            begin + CameraFraming.CalculatePanOffset(
                new Vector2(-4000f, 0f), 0f, Pitch, Distance, Fov, ViewportHeight),
            region);

        Vector3 backOff = CameraFraming.ClampToRegion(
            begin + CameraFraming.CalculatePanOffset(
                new Vector2(-40f, 0f), 0f, Pitch, Distance, Fov, ViewportHeight),
            region);

        Assert.Equal(32f, intoTheWall.X, 4);
        Assert.True(backOff.X < 32f);
    }

    /// <summary>
    /// Yaw zero puts the camera on +Z looking towards -Z (the same convention
    /// <see cref="DraggingRightMovesTheFocusPointLeft"/> pins for the pan), so
    /// its ground right is +X and its ground forward is -Z.
    /// </summary>
    [Fact]
    public void AtYawZeroRightIsPlusXAndForwardIsMinusZ()
    {
        Assert.Equal(new Vector3(1f, 0f, 0f), CameraFraming.GetHorizontalRight(0f));
        Assert.Equal(new Vector3(0f, 0f, -1f), CameraFraming.GetHorizontalForward(0f));
    }

    /// <summary>
    /// Both vectors stay in the ground plane at every yaw — there is no pitch
    /// parameter to leave it out of, which is the point: a turntable has no
    /// roll, so this frame never tilts.
    /// </summary>
    [Fact]
    public void TheGroundFrameStaysHorizontalAndOrthonormalAtEveryYaw()
    {
        foreach (float yaw in new[] { -2.4f, -0.3f, 0f, 0.7f, 3.1f })
        {
            Vector3 right = CameraFraming.GetHorizontalRight(yaw);
            Vector3 forward = CameraFraming.GetHorizontalForward(yaw);

            Assert.Equal(0f, right.Y, 5);
            Assert.Equal(0f, forward.Y, 5);
            Assert.Equal(1f, right.Length(), 5);
            Assert.Equal(1f, forward.Length(), 5);
            Assert.Equal(0f, right.Dot(forward), 5);
        }
    }

    [Fact]
    public void AWiderTargetIsFramedFromFurtherBack()
    {
        float small = CameraFraming.CalculateFitDistance(
            new Aabb(Vector3.Zero, new Vector3(4f, 4f, 4f)), Fov, 1.6f);
        float large = CameraFraming.CalculateFitDistance(
            new Aabb(Vector3.Zero, new Vector3(40f, 40f, 40f)), Fov, 1.6f);

        Assert.True(large > small);
    }

    /// <summary>
    /// A single cell has a bounding sphere of half a cell, and without a floor
    /// under the radius the camera would be asked to sit inside it.
    /// </summary>
    [Fact]
    public void AnEmptyTargetIsStillFramedFromOutsideItself()
    {
        float distance = CameraFraming.CalculateFitDistance(
            new Aabb(new Vector3(3f, 1f, 3f), Vector3.Zero), Fov, 1.6f);

        Assert.True(distance > 0.5f);
    }

    /// <summary>
    /// A tall window is narrower than it is high, so the horizontal axis becomes
    /// the binding one and the camera has to back off further than the vertical
    /// field alone would ask.
    /// </summary>
    [Fact]
    public void ANarrowWindowFramesFromFurtherBack()
    {
        var bounds = new Aabb(Vector3.Zero, new Vector3(10f, 10f, 10f));

        float wide = CameraFraming.CalculateFitDistance(bounds, Fov, 1.6f);
        float narrow = CameraFraming.CalculateFitDistance(bounds, Fov, 0.5f);

        Assert.True(narrow > wide);
    }

    /// <summary>
    /// The whole point of deriving the limit: a scene larger than the old fixed
    /// 120 units has to be backable-away-from far enough to be seen at all.
    /// </summary>
    [Fact]
    public void ALargerRegionMayBeBackedAwayFromFurther()
    {
        float small = CameraFraming.CalculateMaxDistance(new Vector3(32f, 32f, 32f));
        float large = CameraFraming.CalculateMaxDistance(new Vector3(48f, 40f, 112f));

        Assert.True(large > small);
        Assert.True(large > 120f);
    }

    /// <summary>
    /// The limit has to clear what framing actually asks for, or the camera would
    /// be clamped short of the distance that fits the region on screen — which is
    /// the failure this replaced.
    /// </summary>
    [Theory]
    [InlineData(32f, 32f, 32f)]
    [InlineData(48f, 40f, 112f)]
    [InlineData(8f, 8f, 8f)]
    public void TheLimitClearsTheDistanceThatFramesTheWholeRegion(float x, float y, float z)
    {
        var size = new Vector3(x, y, z);

        float fit = CameraFraming.CalculateFitDistance(new Aabb(Vector3.Zero, size), Fov, 1.6f);

        Assert.True(CameraFraming.CalculateMaxDistance(size) > fit);
    }

    /// <summary>
    /// A scene measured in single cells still needs somewhere to stand back to.
    /// </summary>
    [Fact]
    public void ATinyRegionKeepsAUsableLimit()
    {
        Assert.True(CameraFraming.CalculateMaxDistance(new Vector3(2f, 2f, 2f)) >= 16f);
    }

    /// <summary>
    /// The bug this guards: at Godot's default budget of 100 units the shadows in
    /// a 48x40x112 room faded out well before the camera reached its own limit, so
    /// backing away made them vanish. The budget has to outlast the zoom.
    /// </summary>
    [Theory]
    [InlineData(32f, 32f, 32f)]
    [InlineData(48f, 40f, 112f)]
    [InlineData(2f, 2f, 2f)]
    [InlineData(256f, 256f, 256f)]
    public void ShadowsCarryPastEverywhereTheCameraCanStand(float x, float y, float z)
    {
        var size = new Vector3(x, y, z);

        // The worst case: pulled all the way back, with the far corner one further
        // diagonal beyond the focus point the camera turns around.
        float furthestVisible = CameraFraming.CalculateMaxDistance(size) + size.Length();

        // Past the fade's start, not merely inside the budget — a shadow inside the
        // fade band is already weakening, which is the symptom rather than the line.
        float fadeBegins = CameraFraming.CalculateShadowDistance(size) * CameraFraming.ShadowFadeStart;

        Assert.True(fadeBegins >= furthestVisible);
    }

    /// <summary>
    /// It is one number per room, not per frame: the same region must always be
    /// answered the same way, or the shadow's sharpness would follow the camera.
    /// </summary>
    [Fact]
    public void TheShadowBudgetFollowsTheRoomAndNothingElse()
    {
        var size = new Vector3(48f, 40f, 112f);

        Assert.Equal(
            CameraFraming.CalculateShadowDistance(size),
            CameraFraming.CalculateShadowDistance(size));

        Assert.True(
            CameraFraming.CalculateShadowDistance(new Vector3(96f, 80f, 224f))
            > CameraFraming.CalculateShadowDistance(size));
    }

    /// <summary>
    /// The same failure the shadow budget had, one plane further out: the camera
    /// must not be able to retreat through its own far plane.
    /// </summary>
    /// <remarks>
    /// Strictly past, not merely up to. The worst case is genuinely reachable —
    /// focus point in one corner, camera at the zoom limit, opposite corner
    /// directly behind it — and geometry lying exactly on the far plane loses its
    /// own depth test under Forward+'s reversed depth, so the far corner would
    /// wink out where the far half used to be cut.
    /// <para>
    /// The worst case is spelled out here from the other two published numbers
    /// rather than taken from <c>CalculateViewDistance</c>, so that this asserts
    /// something about the camera instead of restating the implementation.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(32f, 32f, 32f)]
    [InlineData(48f, 40f, 112f)]
    [InlineData(2f, 2f, 2f)]
    [InlineData(208f, 72f, 152f)]
    [InlineData(256f, 256f, 256f)]
    public void TheFarPlaneClearsEverywhereTheCameraCanStand(float x, float y, float z)
    {
        var size = new Vector3(x, y, z);

        float furthestVisible = CameraFraming.CalculateMaxDistance(size) + size.Length();

        Assert.True(CameraFraming.CalculateFarPlane(size) > furthestVisible);
    }

    /// <summary>
    /// The bug this guards, with its own numbers: in a 208x72x152 room the zoom
    /// limit is 588 units and the far plane was the constant 500, so pulling back
    /// sliced the room in half along a plane — the floor and the far wall cut by a
    /// straight diagonal, with the model still lit in front of it.
    /// </summary>
    [Fact]
    public void ALargeRoomIsNotCutInHalfByTheFarPlane()
    {
        var size = new Vector3(208f, 72f, 152f);

        // The old constant, and the distance the camera could already stand at.
        Assert.True(CameraFraming.CalculateMaxDistance(size) > 500f);
        Assert.True(CameraFraming.CalculateFarPlane(size) > 500f);
    }

    /// <summary>
    /// One number per room, like the shadow budget: the far plane may not follow
    /// the camera, or depth precision would shift as the view moved.
    /// </summary>
    [Fact]
    public void TheFarPlaneFollowsTheRoomAndNothingElse()
    {
        var size = new Vector3(48f, 40f, 112f);

        Assert.Equal(
            CameraFraming.CalculateFarPlane(size),
            CameraFraming.CalculateFarPlane(size));

        Assert.True(
            CameraFraming.CalculateFarPlane(new Vector3(96f, 80f, 224f))
            > CameraFraming.CalculateFarPlane(size));
    }

    /// <summary>
    /// A scene measured in single cells still gets a plane past its own zoom
    /// limit, which is what the floor under the limit exists to guarantee.
    /// </summary>
    [Fact]
    public void ATinyRegionKeepsAUsableFarPlane()
    {
        var size = new Vector3(2f, 2f, 2f);

        Assert.True(CameraFraming.CalculateFarPlane(size) > CameraFraming.CalculateMaxDistance(size));
    }

    /// <summary>
    /// The three budgets are one derivation, and this is what stops them drifting
    /// apart again: everything the camera can see is inside the far plane, and
    /// everything inside the far plane is inside the shadow budget.
    /// </summary>
    [Theory]
    [InlineData(32f, 32f, 32f)]
    [InlineData(2f, 2f, 2f)]
    [InlineData(208f, 72f, 152f)]
    [InlineData(256f, 256f, 256f)]
    public void TheDepthBudgetsAgreeWithEachOther(float x, float y, float z)
    {
        var size = new Vector3(x, y, z);

        float reach = CameraFraming.CalculateViewDistance(size);

        Assert.Equal(CameraFraming.CalculateMaxDistance(size) + size.Length(), reach, 3);
        Assert.True(CameraFraming.CalculateFarPlane(size) > reach);
        Assert.True(CameraFraming.CalculateShadowDistance(size) >= CameraFraming.CalculateFarPlane(size));
    }

    /// <summary>
    /// The bug this guards: from underneath, the ground plane is projected
    /// mirrored, so the same drag has to move the focus point the other way or
    /// the world slides against the cursor. Measured in the editor before the
    /// fix — the same downward drag moved the pivot the same way from above and
    /// from below, while on screen the model went with the cursor from above and
    /// against it from below.
    /// </summary>
    [Fact]
    public void APanFromBelowGoesTheOtherWayFromAPanFromAbove()
    {
        var travel = new Vector2(0f, 100f);

        Vector3 above = CameraFraming.CalculatePanOffset(
            travel, 0f, -0.6f, Distance, Fov, ViewportHeight);
        Vector3 below = CameraFraming.CalculatePanOffset(
            travel, 0f, +0.6f, Distance, Fov, ViewportHeight);

        Assert.True(above.Z < 0f);
        Assert.True(below.Z > 0f);
        Assert.Equal(-above.Z, below.Z, 4);
    }

    /// <summary>
    /// The horizontal half must not flip with it: the camera's own right vector
    /// is cos(pitch) times the turntable's, and cos never changes sign inside the
    /// pitch limit. Dragging right pulls the world right from either side.
    /// </summary>
    [Fact]
    public void ThePanSidewaysIsTheSameFromEitherSide()
    {
        var travel = new Vector2(100f, 0f);

        Vector3 above = CameraFraming.CalculatePanOffset(
            travel, 0f, -0.6f, Distance, Fov, ViewportHeight);
        Vector3 below = CameraFraming.CalculatePanOffset(
            travel, 0f, +0.6f, Distance, Fov, ViewportHeight);

        Assert.True(above.X < 0f);
        Assert.Equal(above.X, below.X, 4);
    }

    /// <summary>
    /// The grazing bound is on the magnitude alone, so the near-level case stays
    /// bounded on both sides rather than only on the one the old absolute value
    /// collapsed them into.
    /// </summary>
    [Fact]
    public void APanJustBelowTheHorizonIsBoundedToo()
    {
        Vector3 offset = CameraFraming.CalculatePanOffset(
            new Vector2(0f, 100f), 0f, 0.01f, Distance, Fov, ViewportHeight);

        Vector3 steep = CameraFraming.CalculatePanOffset(
            new Vector2(0f, 100f), 0f, 1.2f, Distance, Fov, ViewportHeight);

        Assert.True(offset.Z > 0f);
        Assert.True(offset.Z < steep.Z * 4f);
    }
}
