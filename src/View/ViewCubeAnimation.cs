using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The turn a click on the view cube starts: two angles walking from where the
/// camera was to where the zone asks it to be, eased at both ends.
/// </summary>
/// <remarks>
/// Stepped with the frame time the view already receives, exactly as
/// <see cref="CameraActivity"/> is — no clock of its own and no node, so the whole
/// of it is checked by advancing a number. Cancelling is dropping the object: the
/// last angles it wrote stay on the camera, which is what "no snap to the target,
/// no snap back" means in practice.
/// </remarks>
internal sealed class ViewCubeAnimation
{
    /// <summary>
    /// How long the turn takes. Long enough to read as a turn rather than a cut —
    /// which is the whole point of animating it, since a camera that teleports
    /// leaves the user working out which way round the model now is — and short
    /// enough that it never becomes something to sit through.
    /// </summary>
    public const float DurationSeconds = 0.3f;

    private readonly float _startYawDegrees;
    private readonly float _startPitchDegrees;
    private readonly float _yawTravelDegrees;
    private readonly float _pitchTravelDegrees;
    private readonly float _durationSeconds;

    private float _elapsedSeconds;

    public ViewCubeAnimation(
        float startYawDegrees,
        float startPitchDegrees,
        float targetYawDegrees,
        float targetPitchDegrees,
        float durationSeconds = DurationSeconds)
    {
        _startYawDegrees = startYawDegrees;
        _startPitchDegrees = startPitchDegrees;
        _yawTravelDegrees = WrapDegrees(targetYawDegrees - startYawDegrees);
        _pitchTravelDegrees = targetPitchDegrees - startPitchDegrees;
        _durationSeconds = Mathf.Max(durationSeconds, 0f);

        YawDegrees = startYawDegrees;
        PitchDegrees = startPitchDegrees;
    }

    public float YawDegrees { get; private set; }

    public float PitchDegrees { get; private set; }

    public bool IsFinished => _elapsedSeconds >= _durationSeconds;

    /// <summary>
    /// A signed angle brought into (-180°, +180°], which is what makes a turn take
    /// the short way round: 350° to 10° travels forward 20° rather than back 340°.
    /// </summary>
    /// <remarks>
    /// The half turn is deliberately at the closed end. Both ways round are the
    /// same distance there, so the tie has to be broken somewhere, and breaking it
    /// forwards means 10° → 190° and 190° → 10° turn the same way instead of the
    /// direction flipping on a rounding error.
    /// </remarks>
    public static float WrapDegrees(float degrees)
    {
        float wrapped = degrees % 360f;

        if (wrapped > 180f)
            return wrapped - 360f;

        return wrapped <= -180f ? wrapped + 360f : wrapped;
    }

    public void Advance(double delta)
    {
        _elapsedSeconds = Mathf.Min(_elapsedSeconds + (float)delta, _durationSeconds);

        float eased = Ease(_durationSeconds <= 0f ? 1f : _elapsedSeconds / _durationSeconds);

        YawDegrees = _startYawDegrees + _yawTravelDegrees * eased;
        PitchDegrees = _startPitchDegrees + _pitchTravelDegrees * eased;
    }

    /// <summary>
    /// Smoothstep: it leaves and arrives at zero speed, so the turn neither jerks
    /// into motion nor stops dead. A closed-form polynomial rather than a tuned
    /// curve, so there is nothing in it to nudge until a screenshot looks right.
    /// </summary>
    private static float Ease(float progress) => progress * progress * (3f - 2f * progress);
}
