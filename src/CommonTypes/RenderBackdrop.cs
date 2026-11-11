namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a camera ray that reaches nothing shows. Deliberately not the same
/// question as what lights the scene: the environment goes on lighting it
/// whichever of these is chosen, the way a renderer's "transparent film" has
/// never meant "switch the sky off".
/// </summary>
public enum RenderBackdrop
{
    /// <summary>The sky itself — the gradient the scene is lit by.</summary>
    Environment,

    /// <summary>One flat colour behind everything. What a specification sheet wants.</summary>
    Plain,

    /// <summary>
    /// Nothing: the picture carries an alpha channel and the model can be
    /// composited onto whatever it is going to stand on.
    /// </summary>
    Transparent
}
