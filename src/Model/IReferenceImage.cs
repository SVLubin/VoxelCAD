using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The component that makes an element a picture standing in the scene: the file
/// it shows and how it is drawn. Where it stands and which way it faces is the
/// element's own transform, the same as for anything else in the tree.
/// </summary>
/// <remarks>
/// It holds no cells, so everything that walks the tree looking for geometry
/// passes over it: it does not mesh, it does not export, and the render's own
/// compile does not put it in the scene it is about to photograph. What reads it
/// is the viewport, which draws it so it can be traced over.
/// </remarks>
public interface IReferenceImage : IElementComponent
{
    /// <summary>
    /// The file, the size and the drawing. Changed through
    /// <see cref="IScene.SetReferenceImage"/> so the change is undoable.
    /// </summary>
    ReferenceImageSettings Settings { get; }
}
