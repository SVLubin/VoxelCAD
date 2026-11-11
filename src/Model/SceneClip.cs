using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// A whole scene — its buildable region and every element in it — taken out of a
/// project. Pasted back through <see cref="IProject.PasteScene"/>.
/// </summary>
public sealed class SceneClip : ClipContent
{
    internal SceneClip(
        string name,
        Vector3I size,
        IReadOnlyList<ElementNode> elementsList,
        IReadOnlyList<ClipMaterial> materialsList,
        IReadOnlyList<ClipPrefab> prefabsList)
        : base(name)
    {
        Size = size;
        Elements = elementsList;
        Materials = materialsList;
        Prefabs = prefabsList;
    }

    internal Vector3I Size { get; }

    internal IReadOnlyList<ElementNode> Elements { get; }

    /// <summary>The materials the scene's cells wear, as they stood when it was copied.</summary>
    internal IReadOnlyList<ClipMaterial> Materials { get; }

    /// <summary>The prefabs the scene's instances refer to, closure included.</summary>
    internal IReadOnlyList<ClipPrefab> Prefabs { get; }
}
