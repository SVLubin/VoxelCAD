namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a click asks of the selection: to be the whole of it, to toggle its own
/// membership, or to widen it. Reported by the surface the click landed on;
/// what it does to the selection is decided a layer up.
/// </summary>
/// <remarks>
/// The modifiers behind these are the platform's own list convention, and they
/// are the same in the viewport and in the project tree so that one hand learns
/// one thing: the command modifier — Cmd on macOS, Ctrl elsewhere — toggles, and
/// Shift widens. <b>Ctrl is not available on macOS</b> and that is not a choice:
/// the engine's macOS window turns a Ctrl+left-click into a right-click before
/// anything here sees it, so the command modifier is the only one a click can
/// carry there.
/// <para>
/// <see cref="Extend"/> widens by whatever the surface can offer. A list has an
/// order, so it means the run from the anchor; the scene does not, so it means
/// the one thing under the pointer, added and never removed.
/// </para>
/// </remarks>
public enum SelectionIntent
{
    /// <summary>A plain click: what it names becomes the whole selection.</summary>
    Replace,

    /// <summary>In the selection, out of it — the command modifier.</summary>
    Toggle,

    /// <summary>Added to the selection, never taken out of it — Shift.</summary>
    Extend
}
