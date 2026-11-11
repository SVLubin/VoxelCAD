using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Hangs a <see cref="VoxelMeshSet"/> under a node as one instance per mesh,
/// reusing the instances already there so a remesh does not churn the tree.
/// </summary>
/// <remarks>
/// One place rather than two, because the rule the split encodes — the opaque
/// mesh first, then one instance per transparent material — is the whole of what
/// makes the sorting come out right, and the viewport and the material preview
/// must not come to disagree about it.
/// </remarks>
internal static class VoxelMeshInstances
{
    public static void Apply(
        Node3D host,
        List<MeshInstance3D> instancesList,
        VoxelMeshSet set,
        GeometryInstance3D.ShadowCastingSetting castShadow,
        uint layers)
    {
        var used = 0;

        if (set.Opaque is not null)
            Take(host, instancesList, used++, castShadow, layers).Mesh = set.Opaque;

        if (set.Transparent is not null)
        {
            foreach (VoxelMeshPart part in set.Transparent)
                Take(host, instancesList, used++, castShadow, layers).Mesh = part.Mesh;
        }

        // Whatever the last build needed and this one does not is emptied rather
        // than freed: an element's material list moves about as it is painted, and
        // a spare instance holding no mesh draws nothing and costs nothing.
        for (int i = used; i < instancesList.Count; i++)
            instancesList[i].Mesh = null;
    }

    private static MeshInstance3D Take(
        Node3D host,
        List<MeshInstance3D> instancesList,
        int index,
        GeometryInstance3D.ShadowCastingSetting castShadow,
        uint layers)
    {
        if (index < instancesList.Count)
            return instancesList[index];

        var instance = new MeshInstance3D
        {
            Name = index == 0 ? "VoxelMesh" : $"VoxelMesh{index}",
            CastShadow = castShadow,
            Layers = layers
        };

        host.AddChild(instance);
        instancesList.Add(instance);

        return instance;
    }
}
