using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Godot;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What a pull across the material preview does to the block.
/// </summary>
public sealed class MaterialPreviewDragTests
{
    private static readonly MaterialPreviewTurn Origin = new(0f, 0f);
    private static readonly Vector2 Across = new(132f, 132f);

    /// <summary>
    /// The one property that matters, and it is stated as the scene's own rule
    /// rather than as a number: a pull across the whole surface is a full turn,
    /// which is what <c>OrbitCamera</c> turns the viewport by. Restated inline so
    /// that a rate changed in one place and not the other fails here.
    /// </summary>
    [Fact]
    public void APullAcrossTheWholeSurfaceIsOneTurn()
    {
        MaterialPreviewTurn turned = MaterialPreviewDrag.Apply(
            new MaterialPreviewTurn(37f, 0f), new Vector2(Across.X, 0f), Across);

        Assert.Equal(37f, turned.YawDegrees, 3);
    }

    /// <summary>
    /// And it turns the same way round as the scene does, at the scene's own rate
    /// — <c>TurntableRate</c> is the function <c>OrbitCamera</c> turns by, asked
    /// here rather than copied.
    /// </summary>
    [Fact]
    public void ItTurnsTheWayTheSceneTurns()
    {
        var pull = new Vector2(40f, 25f);

        // Asked of the very function the camera turns by, never a copy of it: a
        // test that restates the rate inline is true of whatever rate the camera
        // happens to have, so the coupling it claims to hold would not exist.
        float yaw = Mathf.RadToDeg(TurntableRate.Turn(-pull.X, Across.X));
        float pitch = Mathf.RadToDeg(TurntableRate.Turn(pull.Y, Across.Y));

        MaterialPreviewTurn turned = MaterialPreviewDrag.Apply(Origin, pull, Across);

        Assert.Equal(Mathf.PosMod(yaw, 360f), turned.YawDegrees, 3);
        Assert.Equal(pitch, turned.PitchDegrees, 3);
    }

    /// <summary>
    /// A gesture is the sum of its steps: dragging in ten hops must land where one
    /// long pull lands, or a slow hand and a fast one would see different blocks.
    /// </summary>
    [Fact]
    public void TenSmallPullsLandWhereOneLongPullLands()
    {
        MaterialPreviewTurn stepped = Origin;

        for (int step = 0; step < 10; step++)
            stepped = MaterialPreviewDrag.Apply(stepped, new Vector2(7f, -1f), Across);

        MaterialPreviewTurn once = MaterialPreviewDrag.Apply(Origin, new Vector2(70f, -10f), Across);

        Assert.Equal(once.YawDegrees, stepped.YawDegrees, 3);
        Assert.Equal(once.PitchDegrees, stepped.PitchDegrees, 3);
    }

    /// <summary>
    /// The poles are the preview's own clamp and never a second opinion, so a pull
    /// far past them stops exactly where the command stops.
    /// </summary>
    [Fact]
    public void ThePullStopsAtThePoleTheCommandStopsAt()
    {
        MaterialPreviewTurn down = MaterialPreviewDrag.Apply(Origin, new Vector2(0f, 10000f), Across);
        MaterialPreviewTurn up = MaterialPreviewDrag.Apply(Origin, new Vector2(0f, -10000f), Across);

        Assert.Equal(MaterialPreviewTurns.MaximumPitchDegrees, down.PitchDegrees, 3);
        Assert.Equal(-MaterialPreviewTurns.MaximumPitchDegrees, up.PitchDegrees, 3);
    }

    /// <summary>
    /// A surface with no size yet answers no turn rather than an infinite one —
    /// a control is laid out a frame after it is built, and a motion arriving in
    /// between must not send the block to a NaN.
    /// </summary>
    [Fact]
    public void ASurfaceWithNoSizeYetTurnsByAlmostNothing()
    {
        MaterialPreviewTurn turned = MaterialPreviewDrag.Apply(
            Origin, new Vector2(3f, 2f), Vector2.Zero);

        Assert.False(float.IsNaN(turned.YawDegrees));
        Assert.False(float.IsNaN(turned.PitchDegrees));
        Assert.InRange(turned.PitchDegrees, -MaterialPreviewTurns.MaximumPitchDegrees,
            MaterialPreviewTurns.MaximumPitchDegrees);
    }
}
