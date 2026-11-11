using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The component that makes an element somebody else's 3D model, standing in the
/// scene to be built around. How big it is and where it stands is the element's
/// own transform.
/// </summary>
/// <inheritdoc cref="IReferenceImage" path="/remarks"/>
public interface IReferenceModel : IElementComponent
{
    /// <summary>
    /// The file and the drawing. Changed through
    /// <see cref="IScene.SetReferenceModel"/> so the change is undoable.
    /// </summary>
    ReferenceModelSettings Settings { get; }
}
