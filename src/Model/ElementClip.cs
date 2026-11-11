using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// An element and everything under it, taken out of a scene. Pasted back through
/// <see cref="IScene.PasteElement"/> — into the scene it came from or any other,
/// since a clip refers to nothing.
/// </summary>
public sealed class ElementClip : ClipContent
{
    internal ElementClip(
        ElementNode root,
        IReadOnlyList<ClipMaterial> materialsList,
        IReadOnlyList<ClipPrefab> prefabsList)
        : base(root.Name)
    {
        Root = root;
        Materials = materialsList;
        Prefabs = prefabsList;
    }

    internal ElementNode Root { get; }

    /// <summary>The materials the subtree's cells wear, as they stood when it was copied.</summary>
    internal IReadOnlyList<ClipMaterial> Materials { get; }

    /// <summary>The prefabs the subtree's instances refer to, closure included.</summary>
    internal IReadOnlyList<ClipPrefab> Prefabs { get; }
}
