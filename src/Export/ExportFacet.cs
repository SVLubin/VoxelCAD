namespace VoxelCAD.Export;

/// <summary>
/// Which family of intermediate representation a format consumes. The two are
/// irreconcilable: mesh formats want welded triangles, voxel formats want an
/// integer grid, so a writer declares which it reads and the projection builds
/// only that one.
/// </summary>
public enum ExportFacet
{
    /// <summary>Welded, indexed triangles — OBJ, STL, FBX.</summary>
    Mesh,

    /// <summary>An integer voxel grid — MagicaVoxel.</summary>
    Volume
}
