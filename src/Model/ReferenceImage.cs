using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IReferenceImage"/>
internal sealed class ReferenceImage : IReferenceImage
{
    public ReferenceImage(IHierarchyElement owner) => Owner = owner;

    public ComponentKind Kind => ComponentKind.ReferenceImage;

    public IHierarchyElement Owner { get; }

    public ReferenceImageSettings Settings { get; private set; } = ReferenceImageSettings.Default;

    /// <summary>
    /// Written by the scene's command and by the reader, never by a caller: the
    /// clamp is applied here rather than at every site that could set one, so a
    /// damaged file cannot put a plane in the document that no form could express.
    /// </summary>
    internal void ApplySettings(ReferenceImageSettings settings) => Settings = settings.Clamped();
}
