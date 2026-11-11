namespace VoxelCAD.Export;

/// <summary>
/// How the mesher decides which shared faces to drop. A format picks its default;
/// an export may override it.
/// </summary>
public enum CullingPolicy
{
    /// <summary>
    /// Opacity-aware, for colour formats: a face survives where its neighbour is
    /// empty or thinner, so an opaque cell under glass keeps the surface that has
    /// to read through it — and a seam emits one quad rather than two in one
    /// plane. The viewport meshes by the same rule, so a file and the screen show
    /// the same faces.
    /// </summary>
    Visual,

    /// <summary>
    /// The filled/empty boundary only, for printing: opacity and material are
    /// ignored, so a face survives exactly when its neighbour cell is empty. The
    /// result is the watertight outer shell of the body.
    /// </summary>
    Solid
}
