using System;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// A dense integer block of voxels in world cell coordinates. One element becomes
/// one or more bricks; several elements become several bricks. A brick is small
/// enough for the target format's per-model size limit.
/// </summary>
/// <remarks>
/// <see cref="PaletteIndices"/> is one byte per cell in x-fastest, then y, then z
/// order (<c>index = x + Size.X * (y + Size.Y * z)</c>). A byte is <c>0</c> for an
/// empty cell and otherwise the palette index plus one, matching MagicaVoxel's
/// rule that colour index 0 is empty and 1..255 are addressable.
/// </remarks>
public readonly record struct ExportBrick(Vector3I Origin, Vector3I Size, byte[] PaletteIndices)
{
    /// <summary>The flat <see cref="PaletteIndices"/> offset of a cell local to the brick.</summary>
    public int IndexOf(int x, int y, int z) => x + Size.X * (y + Size.Y * z);
}
