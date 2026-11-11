using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The referee that holds the brush off while the camera is being driven. The
/// rules are timing rules — active during a move, active through a cooldown
/// after, idle once it lapses — and timing is exactly what a screenshot cannot
/// argue about, so it is pinned here rather than left to the running editor.
/// </summary>
public sealed class CameraActivityTests
{
    private const float Cooldown = CameraActivity.CooldownSeconds;

    [Fact]
    public void StartsIdle()
    {
        Assert.False(new CameraActivity().IsActive);
    }

    [Fact]
    public void AMoveMakesItActive()
    {
        var activity = new CameraActivity();

        activity.NoteMove();

        Assert.True(activity.IsActive);
    }

    [Fact]
    public void StaysActiveThroughTheCooldown()
    {
        var activity = new CameraActivity();
        activity.NoteMove();

        // Just short of the full cooldown, in small steps.
        activity.Tick(Cooldown * 0.4);
        activity.Tick(Cooldown * 0.4);

        Assert.True(activity.IsActive);
    }

    [Fact]
    public void GoesIdleOnceTheCooldownElapses()
    {
        var activity = new CameraActivity();
        activity.NoteMove();

        activity.Tick(Cooldown + 0.001);

        Assert.False(activity.IsActive);
    }

    [Fact]
    public void EachMoveRefreshesTheFullCooldown()
    {
        var activity = new CameraActivity();
        activity.NoteMove();

        activity.Tick(Cooldown * 0.9);
        activity.NoteMove();
        // Total elapsed since the first move is now past the cooldown, but the
        // second move reset the clock, so it is still active.
        activity.Tick(Cooldown * 0.9);

        Assert.True(activity.IsActive);
    }
}
