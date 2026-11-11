namespace VoxelCAD.CommonTypes;

/// <summary>
/// How the camera sees. The viewport is always perspective, because that is what
/// a hand navigates by; a picture of a part is often not.
/// </summary>
public enum RenderProjection
{
    Perspective,

    /// <summary>
    /// No convergence: parallel edges stay parallel, and a cube seen from a corner
    /// draws as an isometric cube. What a drawing of a part looks like.
    /// </summary>
    Orthographic
}
