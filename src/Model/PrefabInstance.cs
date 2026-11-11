using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IPrefabInstance"/>
internal sealed class PrefabInstance : IPrefabInstance
{
    public PrefabInstance(IHierarchyElement owner, Guid sourceId)
    {
        Owner = owner;
        SourceId = sourceId;
    }

    public ComponentKind Kind => ComponentKind.PrefabInstance;

    public IHierarchyElement Owner { get; }

    public Guid SourceId { get; }
}
