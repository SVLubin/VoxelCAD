using Godot;

namespace VoxelCAD.View;

/// <summary>
/// How far a drag turns a turntable: <b>a pull across the whole surface is one
/// full turn</b>.
/// </summary>
/// <remarks>
/// Goxel's rule, and the scene's. One answer with two readers — the viewport's
/// camera and the inspector's material preview — because the same fingers doing
/// the same thing over two surfaces of this editor must turn them at the same
/// rate, and a preview that spun faster than the scene would read as a different
/// gesture rather than as the same one.
/// <para>
/// Shared as a function rather than restated: the preview's test used to write
/// these two lines out inline and assert the preview agreed with the copy, which
/// is true of any rate whatsoever — a change to the camera's would have left the
/// two turning differently with every test still green.
/// </para>
/// <para>
/// Measured in the surface's own units rather than in pixels, which is what makes
/// it independent of how large the surface happens to be drawn: the preview is a
/// fraction of a window and the viewport is most of one.
/// </para>
/// </remarks>
internal static class TurntableRate
{
    /// <summary>The turn, in radians, a pull of <paramref name="pixels"/> earns.</summary>
    public static float Turn(float pixels, float across) =>
        pixels / Mathf.Max(1f, across) * Mathf.Tau;
}
