using System;
using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// The materials an export refers to, in a stable order (that of
/// <see cref="Model.IProject.Materials"/>), so a material's index means the same
/// thing in the mesh, the volume and every writer.
/// </summary>
public sealed class ExportPalette
{
    private readonly IReadOnlyList<ExportMaterial> _materialsList;
    private readonly Dictionary<Guid, int> _indexByIdDictionary;

    public ExportPalette(IReadOnlyList<ExportMaterial> materialsList)
    {
        _materialsList = materialsList;
        _indexByIdDictionary = new Dictionary<Guid, int>(materialsList.Count);

        for (var index = 0; index < materialsList.Count; index++)
            _indexByIdDictionary[materialsList[index].Id] = index;
    }

    public IReadOnlyList<ExportMaterial> Materials => _materialsList;

    /// <summary>The palette index of a material, or false when it is not in the palette.</summary>
    public bool TryGetIndex(Guid materialId, out int index) =>
        _indexByIdDictionary.TryGetValue(materialId, out index);
}
