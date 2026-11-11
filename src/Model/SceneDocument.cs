using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>Serialized form of one scene.</summary>
internal sealed class SceneDocument
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Absent in schema version 1, where a project held one unnamed scene.</summary>
    public string Name { get; set; } = string.Empty;

    public int[] Size { get; set; } = { 32, 32, 32 };

    /// <summary>
    /// Which camera this scene renders through, where somebody chose one. Absent
    /// means nobody has, and the scene falls back to the first camera it holds —
    /// so a file that names nothing still renders, and one whose named camera has
    /// been deleted since falls back rather than failing to load.
    /// </summary>
    public string? ActiveCamera { get; set; }

    public List<ElementDocument> Elements { get; set; } = new();
}
