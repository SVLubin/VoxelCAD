using System;
using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// Anything that holds hierarchy children: the scene itself, or an element.
/// Read-only — every mutation goes through <see cref="IScene"/>.
/// </summary>
public interface IHierarchyContainer
{
    Guid Id { get; }

    IReadOnlyList<IHierarchyElement> Children { get; }
}
