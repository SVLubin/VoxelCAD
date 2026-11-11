using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// One filled cell of an imported model, in the model's own zero-based lattice,
/// wearing the index of the palette entry that fills it.
/// </summary>
/// <remarks>
/// The index is the reader's own, not the file's: a format numbers its colours
/// however it likes — MagicaVoxel from one, Qubicle not at all — and the reader
/// resolves that into a dense list before anything downstream sees it.
/// </remarks>
internal readonly record struct ImportedCell(Vector3I Cell, int PaletteIndex);
