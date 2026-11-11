using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// A rectangle that moves towards whatever it is last told: four
/// <see cref="Smoothed"/> numbers, so a panel growing, sliding up a stack and
/// changing width at once is one motion rather than three that could disagree.
/// </summary>
internal sealed class RectMotion
{
    private readonly MotionTiming _timing;
    private readonly Smoothed _x;
    private readonly Smoothed _y;
    private readonly Smoothed _width;
    private readonly Smoothed _height;

    public RectMotion(MotionTiming timing)
    {
        _timing = timing;
        _x = new Smoothed(MotionSpeed.Length, timing);
        _y = new Smoothed(MotionSpeed.Length, timing);
        _width = new Smoothed(MotionSpeed.Length, timing);
        _height = new Smoothed(MotionSpeed.Length, timing);
    }

    public Rect2 Current => new(_x.Value, _y.Value, _width.Value, _height.Value);

    /// <summary>
    /// The rectangle with <b>both</b> edges on whole units — not the position
    /// and the size rounded separately, which would let the far edge drift half
    /// a unit against the near one.
    /// </summary>
    /// <remarks>
    /// This is what a control may be given, and the reason is Godot's own
    /// pixel snapping: it rounds a control's <em>size</em> while leaving the
    /// position it was handed alone, so a child anchored to the far edge sits
    /// at "smooth position plus stepped size" — a sawtooth of about half a unit
    /// every frame. On a left column that child is anchored to the near edge
    /// and nothing shows; on a right column it is the far edge, and the whole
    /// header shivered for the length of every animation. Handing over a rect
    /// whose edges are already whole makes the engine's rounding a no-op.
    /// </remarks>
    public Rect2 Snapped => Snap(Current);

    public static Rect2 Snap(Rect2 rect)
    {
        float left = MathF.Round(rect.Position.X);
        float top = MathF.Round(rect.Position.Y);
        float right = MathF.Round(rect.Position.X + rect.Size.X);
        float bottom = MathF.Round(rect.Position.Y + rect.Size.Y);

        return new Rect2(left, top, right - left, bottom - top);
    }

    /// <summary>Whether this rectangle has ever been aimed anywhere.</summary>
    public bool IsStarted => _x.IsStarted;

    public bool IsSettled =>
        _x.IsSettled && _y.IsSettled && _width.IsSettled && _height.IsSettled;

    /// <summary>
    /// Whether the rectangle has stopped changing <b>size</b>, wherever it may
    /// still be travelling to.
    /// </summary>
    /// <remarks>
    /// Being collapsed is a fact about how big a panel is, not about where it
    /// stands — a tab sliding to a new place in the run is a tab the whole way.
    /// Asked of the whole rect it briefly stopped being one, and what a panel
    /// hides while collapsed (its title, its chevron, its pin) came back for
    /// the length of the slide, laid out against the width it last settled at:
    /// two units of the first letter showed past the tab's own clip.
    /// </remarks>
    public bool IsSizeSettled => _width.IsSettled && _height.IsSettled;

    public void SnapTo(Rect2 rect)
    {
        _x.SnapTo(rect.Position.X);
        _y.SnapTo(rect.Position.Y);
        _width.SnapTo(rect.Size.X);
        _height.SnapTo(rect.Size.Y);
    }

    /// <summary>
    /// Put outright where it is carried, and left at whatever size it already
    /// was. The position may not ease — a thing being dragged that lagged
    /// behind the pointer is not being dragged — and the size may not change at
    /// all: a panel is carried as the thing it is, so an open one travels open
    /// and a tab travels as a tab.
    /// </summary>
    public void CarryTo(Vector2 position)
    {
        _x.SnapTo(position.X);
        _y.SnapTo(position.Y);
    }

    /// <summary>
    /// The furthest any one edge would travel if this rectangle were aimed there
    /// — counting only the edges that would actually set off.
    /// </summary>
    /// <remarks>
    /// An edge whose target is unchanged keeps its own clock, so it must not
    /// lend its remaining distance to the ones that are restarting: a rect
    /// mid-slide with 370 units still to run, given a twenty-unit change of
    /// height, would otherwise move that height at 148 units a second against a
    /// nominal 2750.
    /// </remarks>
    public float RestartTravel(Rect2 rect) =>
        MathF.Max(
            MathF.Max(
                _x.RestartTravel(rect.Position.X),
                _y.RestartTravel(rect.Position.Y)),
            MathF.Max(
                _width.RestartTravel(rect.Size.X),
                _height.RestartTravel(rect.Size.Y)));

    /// <summary>
    /// One clock for four edges, because a rectangle is one shape: given four of
    /// their own, a panel would finish growing sideways and go on growing
    /// downward for several frames, which is the "three that could disagree"
    /// this class exists to prevent.
    /// </summary>
    /// <remarks>
    /// The furthest edge sets the pace, never a corner distance. A left column's
    /// panel opens with its near edge fixed, so only the width travels; a right
    /// column's opens with its far edge fixed, so the position travels as well.
    /// Under the furthest edge the two are the same gesture at the same length;
    /// under any norm over all four they are 328 and 471, so the same gesture
    /// would visibly take different times on the two sides — which is the
    /// mirroring asymmetry every defect in this chrome has hidden behind.
    /// </remarks>
    public void Retarget(Rect2 rect) =>
        RetargetOver(rect, Smoothed.DurationFor(RestartTravel(rect), MotionSpeed.Length, _timing));

    public void RetargetOver(Rect2 rect, float duration)
    {
        _x.RetargetOver(rect.Position.X, duration);
        _y.RetargetOver(rect.Position.Y, duration);
        _width.RetargetOver(rect.Size.X, duration);
        _height.RetargetOver(rect.Size.Y, duration);
    }

    public void Tick(float deltaSeconds)
    {
        _x.Tick(deltaSeconds);
        _y.Tick(deltaSeconds);
        _width.Tick(deltaSeconds);
        _height.Tick(deltaSeconds);
    }
}
