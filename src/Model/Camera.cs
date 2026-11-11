using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="ICamera"/>
internal sealed class Camera : ICamera
{
    public Camera(IHierarchyElement owner) => Owner = owner;

    public ComponentKind Kind => ComponentKind.Camera;

    public IHierarchyElement Owner { get; }

    public CameraLens Lens { get; private set; } = CameraLens.Default;

    /// <summary>
    /// Written by the scene's command and by the reader, never by a caller: the
    /// clamp is applied here rather than at every site that could set one, so a
    /// damaged file cannot put a lens in the document that no form could express.
    /// </summary>
    internal void ApplyLens(CameraLens lens) => Lens = lens.Clamped();
}
