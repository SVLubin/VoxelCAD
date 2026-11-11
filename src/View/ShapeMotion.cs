using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// A rectangle and the notch bitten out of it, moving as one shape: set on one
/// clock, sized by whichever has the furthest still to go among those actually
/// restarting.
/// </summary>
/// <remarks>
/// Given clocks of their own the notch — 36 units, and so always at the duration
/// floor — closes in 50 ms while the surface it is cut out of is still 70 ms from
/// its place. A class rather than two copies of the rule, because the panel and
/// the outline drawn where it will land must arrive the same way or the two come
/// apart mid-gesture.
/// </remarks>
internal sealed class ShapeMotion
{
    private readonly MotionTiming _timing;
    private readonly RectMotion _rect;
    private readonly Smoothed _cut;

    public ShapeMotion(MotionTiming timing)
    {
        _timing = timing;
        _rect = new RectMotion(timing);
        _cut = new Smoothed(MotionSpeed.Length, timing);
    }

    public bool IsStarted => _rect.IsStarted;

    public bool IsSettled => _rect.IsSettled;

    public bool IsSizeSettled => _rect.IsSizeSettled;

    /// <summary>Both edges on whole units. See <see cref="RectMotion.Snapped"/>.</summary>
    public Rect2 Snapped => _rect.Snapped;

    /// <summary>
    /// Whole units for the reason the rect is: the body's inset is measured from
    /// this, and a fractional one puts the edge the contents hang off half a unit
    /// from the drawn one.
    /// </summary>
    public float Cut => MathF.Round(_cut.Value);

    public void SnapTo(Rect2 rect, float cut)
    {
        _rect.SnapTo(rect);
        _cut.SnapTo(cut);
    }

    public void CarryTo(Vector2 position) => _rect.CarryTo(position);

    public void Retarget(Rect2 rect, float cut)
    {
        float clock = Smoothed.DurationFor(
            MathF.Max(_rect.RestartTravel(rect), _cut.RestartTravel(cut)),
            MotionSpeed.Length,
            _timing);

        _rect.RetargetOver(rect, clock);
        _cut.RetargetOver(cut, clock);
    }

    public void Tick(float deltaSeconds)
    {
        _rect.Tick(deltaSeconds);
        _cut.Tick(deltaSeconds);
    }
}
