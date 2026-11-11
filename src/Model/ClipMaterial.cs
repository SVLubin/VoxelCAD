using System;

namespace VoxelCAD.Model;

/// <summary>
/// A material a clip's cells wear, carried with the clip under the id they wear it
/// by, so that a paste can put back a material the document has lost in the
/// meantime.
/// </summary>
/// <remarks>
/// The window that makes this necessary is narrow and real: cutting a model takes
/// its cells out of the document, which can leave the material they were the last
/// users of deletable — and deleted, before the paste. Without the state travelling
/// alongside, the paste would write cells wearing an id that names nothing.
/// </remarks>
internal readonly record struct ClipMaterial(Guid Id, MaterialSnapshot State);
