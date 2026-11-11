using System;

namespace VoxelCAD.Model;

/// <summary>
/// The component that makes an element a prefab instance: a reference to a library
/// entry, and nothing else.
/// </summary>
/// <remarks>
/// There are no per-instance overrides. An instance is its source plus the
/// element's own transform and name, which is what makes its contents derived
/// data: they are expanded from the source, marked read-only through
/// <see cref="IHierarchyElement.PrefabInstanceId"/>, rebuilt whenever the source
/// changes, and never written to a file.
/// </remarks>
public interface IPrefabInstance : IElementComponent
{
    /// <summary>The prefab this instance stands for. Never changes.</summary>
    Guid SourceId { get; }
}
