using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// One facture a scene needs a height field for: the material that wears it, and
/// the finish that says how it tiles.
/// </summary>
/// <remarks>
/// Two fields rather than one because they answer different questions. The
/// <paramref name="Name"/> is what a downloaded set is filed under — a material
/// wearing a scan wears the one named after it, and there are far more of those
/// than the finish enum has members. The <paramref name="Finish"/> still decides
/// how many of the pattern's own units span a tile, and therefore what the scale
/// dial means, whether the pattern was drawn or photographed.
/// </remarks>
/// <param name="Name">
/// The material's name, which is what the atlas is keyed by — and therefore what
/// the render's field table is keyed by too. That is only sound because names are
/// unique: <c>Project</c> refuses a new or renamed material a shipped name and
/// composes a duplicate away from one. A document saved before that guard existed
/// can still carry two materials under one name, and there the second wins.
/// </param>
/// <param name="Finish">Which facture is laid over it.</param>
public readonly record struct RenderFacture(string Name, MaterialFinish Finish);
