using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The component that makes an element a camera: the lens a picture is taken
/// through, and nothing else. Where the camera stands and which way it points is
/// the element's own transform, the same as for anything else in the tree.
/// </summary>
/// <remarks>
/// A camera holds no geometry, so everything that walks the tree looking for
/// cells passes over it: it does not mesh, it does not export, and the render's
/// own compile does not put it in the scene it is about to photograph. What reads
/// it is the render — through <see cref="IScene.ActiveCameraId"/>, since only one
/// camera takes the picture — and the viewport, which draws its frustum so it can
/// be aimed.
/// </remarks>
public interface ICamera : IElementComponent
{
    /// <summary>
    /// How the drawing converges, how wide it is, and what is out of focus.
    /// Changed through <see cref="IScene.SetCameraLens"/> so the change is
    /// undoable.
    /// </summary>
    CameraLens Lens { get; }
}
