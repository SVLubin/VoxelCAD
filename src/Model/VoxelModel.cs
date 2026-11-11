using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IVoxelModel"/>
internal sealed class VoxelModel : IVoxelModel
{
    private readonly Dictionary<Vector3I, Guid> _voxelsByCellDictionary = new();

    public VoxelModel(IHierarchyElement owner)
    {
        Owner = owner;
    }

    public ComponentKind Kind => ComponentKind.VoxelModel;

    public IHierarchyElement Owner { get; }

    public IReadOnlyDictionary<Vector3I, Guid> Voxels => _voxelsByCellDictionary;

    public SymmetryValues Symmetry { get; private set; }

    public bool TryGetVoxel(Vector3I cell, out Guid materialId) =>
        _voxelsByCellDictionary.TryGetValue(cell, out materialId);

    internal bool UsesMaterial(Guid materialId) =>
        _voxelsByCellDictionary.ContainsValue(materialId);

    internal void ApplySymmetry(SymmetryValues values) => Symmetry = values;

    /// <summary>A null <paramref name="materialId"/> empties the cell.</summary>
    internal void Apply(Vector3I cell, Guid? materialId)
    {
        if (materialId == null)
            _voxelsByCellDictionary.Remove(cell);
        else
            _voxelsByCellDictionary[cell] = materialId.Value;
    }
}
