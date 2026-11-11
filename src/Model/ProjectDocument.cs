using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// Everything a project is, as documents. The storage layer decides whether this
/// becomes one container file or a project file beside separate assets — this
/// layer never knows which, which is what keeps the two modes from drifting into
/// two serializers.
/// </summary>
internal sealed class ProjectDocument
{
    /// <summary>
    /// Schema version of the document itself, distinct from the version byte
    /// inside a voxel blob. Costs one line and pays for itself at the first
    /// migration.
    /// </summary>
    public int Version { get; set; }

    public List<MaterialDocument> Materials { get; set; } = new();

    public List<SceneDocument> Scenes { get; set; } = new();

    /// <summary>
    /// The prefab library. A prefab is scene-shaped — a name, a room and an element
    /// tree — so it is written with the same document rather than a near-copy of
    /// one; what tells the two apart is which list they are in. Absent in schema
    /// versions below 3.
    /// </summary>
    public List<SceneDocument> Prefabs { get; set; } = new();
}
