using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IReferenceModel"/>
internal sealed class ReferenceModel : IReferenceModel
{
    public ReferenceModel(IHierarchyElement owner) => Owner = owner;

    public ComponentKind Kind => ComponentKind.ReferenceModel;

    public IHierarchyElement Owner { get; }

    public ReferenceModelSettings Settings { get; private set; } = ReferenceModelSettings.Default;

    /// <inheritdoc cref="ReferenceImage.ApplySettings"/>
    internal void ApplySettings(ReferenceModelSettings settings) => Settings = settings.Clamped();
}
