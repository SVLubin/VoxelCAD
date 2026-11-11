namespace VoxelCAD.View;

/// <summary>
/// Whether the camera is being driven right now — orbiting, panning, zooming,
/// sliding or raising the focus, by mouse, wheel or key. It stays "active" for a
/// short cooldown after the last move as well, so a gesture and the moment just
/// after it read the same. While it is active the brush yields entirely: no
/// preview, and so nothing to place, which is what stops an arrow-turn from
/// spraying voxels in a circle as it goes.
/// </summary>
/// <remarks>
/// The cooldown equals the focus marker's fade (<see cref="FocusMarker"/>) and
/// is refreshed by the same event — a camera move — so the two run together:
/// while the pointer is visible the camera is active. But this is the authority,
/// not the marker. The marker is a view; a view is not something logic reads
/// back to decide what a click means. Both watch the camera; neither watches the
/// other.
/// <para>
/// A plain class rather than state folded into the camera node or the brush: the
/// rule is a timing rule worth testing without the engine, and it belongs to
/// neither side — it sits above both, the way the viewport already arbitrates
/// which of them a button goes to.
/// </para>
/// </remarks>
internal sealed class CameraActivity
{
    /// <summary>
    /// Seconds the camera counts as active after its last move. Kept equal to the
    /// focus marker's total visible-after-move time so the two end together.
    /// </summary>
    public const float CooldownSeconds = 0.2f;

    private float _remainingSeconds;

    /// <summary>
    /// True while a camera gesture is under way and for
    /// <see cref="CooldownSeconds"/> after the last move.
    /// </summary>
    public bool IsActive => _remainingSeconds > 0f;

    /// <summary>
    /// The camera just moved. Refreshes the window to its full cooldown, so a
    /// continuous gesture keeps it topped up frame by frame and it only starts
    /// draining once the moves stop.
    /// </summary>
    public void NoteMove() => _remainingSeconds = CooldownSeconds;

    /// <summary>One frame of decay towards inactive.</summary>
    public void Tick(double delta)
    {
        _remainingSeconds -= (float)delta;

        if (_remainingSeconds < 0f)
        {
            _remainingSeconds = 0f;
        }
    }
}
