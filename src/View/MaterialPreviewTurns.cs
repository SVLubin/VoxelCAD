using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a preview does with the angle it is handed. One answer with two readers —
/// the drag a hand makes across the panel's block and the <c>material-preview</c>
/// command — because a picture an agent asks for and the block a user turns have
/// to be the same block seen the same way, and a limit restated in the command
/// would be a second opinion about where the poles are.
/// </summary>
/// <remarks>
/// Public, unlike the rest of the preview, on <see cref="ViewCubeGeometry"/>'s own
/// precedent: the command answers with the turn this returns, so the gesture and
/// the dispatcher cannot come to disagree about it. Pure functions over arithmetic
/// types, so nothing about the view leaks out with it.
/// </remarks>
public static class MaterialPreviewTurns
{
    /// <summary>
    /// How near the pole a preview may be tipped. Short of it, for the reason the
    /// scene camera's pitch is clamped: at the pole the turn has no direction left
    /// to mean.
    /// </summary>
    public const float MaximumPitchDegrees = 85f;

    public static MaterialPreviewTurn Clamp(MaterialPreviewTurn turn) => new(
        YawDegrees: Mathf.PosMod(turn.YawDegrees, 360f),
        PitchDegrees: Math.Clamp(turn.PitchDegrees, -MaximumPitchDegrees, MaximumPitchDegrees));
}
