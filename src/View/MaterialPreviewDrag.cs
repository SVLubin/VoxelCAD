using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a pull across the preview does to the block: the arithmetic alone, so the
/// one decision in the gesture can be put to a test rather than photographed.
/// </summary>
/// <remarks>
/// <b>The rate is the scene's, restated rather than chosen.</b> A drag across the
/// whole of the surface being dragged is one full turn — Goxel's rule, which is
/// what <c>OrbitCamera</c> already turns the viewport by — so the preview and the
/// scene answer a hand at the same rate against their own widths, and neither
/// carries a degrees-per-pixel number somebody picked by feel. It also makes the
/// gesture independent of how big the preview is drawn, which matters here: the
/// canvas-to-window factor is a fraction of the window and is not near one.
/// <para>
/// The clamp is <see cref="MaterialPreviewTurns"/>'s, never a second opinion about
/// where the poles are.
/// </para>
/// </remarks>
internal static class MaterialPreviewDrag
{
    public static MaterialPreviewTurn Apply(
        MaterialPreviewTurn from,
        Vector2 pixels,
        Vector2 across)
    {
        // TurntableRate is the camera's own rule, asked rather than copied, and it
        // guards a surface with no size yet: no turn rather than an infinite one.
        float yaw = Mathf.RadToDeg(TurntableRate.Turn(-pixels.X, across.X));
        float pitch = Mathf.RadToDeg(TurntableRate.Turn(pixels.Y, across.Y));

        return MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(
            from.YawDegrees + yaw,
            from.PitchDegrees + pitch));
    }
}
