using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One material as a menu can offer it: the id a choice reports and the name a
/// hand reads. Crosses the view seam so a picker can list the project's
/// materials without the view knowing what a material is.
/// </summary>
public readonly record struct MaterialChoice(Guid Id, string Name);
