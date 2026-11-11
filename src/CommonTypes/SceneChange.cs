using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One entry of a scene's change stream. <see cref="ParentId"/> and
/// <see cref="Index"/> carry meaning only for <see cref="SceneChangeKind.ElementAdded"/>
/// and <see cref="SceneChangeKind.ElementMoved"/>; other kinds leave them at
/// <see cref="Guid.Empty"/> and <c>-1</c>.
/// </summary>
/// <param name="Region">
/// For <see cref="SceneChangeKind.VoxelsChanged"/>, the box of cells the edit
/// touched; null for every other kind, and for an edit whose extent is not worth
/// working out. A consumer that can act on part of a model needs this and cannot
/// derive it: it is handed the cells as they are now and never saw them as they
/// were.
/// </param>
public readonly record struct SceneChange(
    SceneChangeKind Kind,
    Guid ElementId,
    Guid ParentId,
    int Index,
    CellRegion? Region = null);
