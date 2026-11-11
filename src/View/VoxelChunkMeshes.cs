using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One element's geometry as a grid of chunks, so an edit rebuilds what it
/// touched instead of the whole model.
/// </summary>
/// <remarks>
/// Measured before it was written, on a model of 170,401 cells: placing one voxel
/// cost 400 ms, of which 82 ms walked every cell looking for visible faces, 145 ms
/// asked each surface cell for its 27 neighbours, 119 ms filled the vertex arrays
/// and 45 ms handed them to the engine. Nothing in that is slow per operation —
/// it is two and a half million dictionary probes over the whole model, every one
/// of them to draw a single cell.
/// <para>
/// A chunk is <see cref="Size"/> cells a side, which is a trade rather than a
/// tuning: <see cref="VoxelMesher"/> walks a box by probing every coordinate in
/// it, so a chunk costs its own volume whether or not anything is in it — small
/// chunks make an edit cheap and a full rebuild dearer.
/// </para>
/// <para>
/// <b>The region is grown by a cell before it is turned into chunks, and the
/// mesher's own box never is.</b> Those are two different growths and only the
/// first happens here: a cell appearing hides the face of its neighbour <em>in
/// the next chunk</em>, and the occlusion a face corner earns is read from the
/// cells crowding it, which is again a cell in every direction — so the chunks
/// rebuilt are those <see cref="CellRegion"/> touches grown by one, and every
/// face whose appearance the edit could have changed is in one of them.
/// </para>
/// <para>
/// The box each of those chunks is then meshed over is exactly its own
/// (<c>BoxOf</c>), and growing <em>that</em> would be a bug rather than belt and
/// braces: the mesher emits a face for every cell in the box it is given, so two
/// overlapping boxes emit the faces of the overlap twice. What keeps a chunk's
/// edge correct without overlap is that the mesher reads neighbours from the
/// whole model and only its <em>emitting</em> is bounded — see
/// <see cref="VoxelMesher.Build"/>.
/// </para>
/// </remarks>
internal sealed class VoxelChunkMeshes
{
    public const int Size = VoxelChunkGrid.Size;

    private readonly Node3D _host;
    private readonly Dictionary<Vector3I, Node3D> _nodesByChunkDictionary = new();
    private readonly Dictionary<Vector3I, List<MeshInstance3D>> _instancesByChunkDictionary = new();

    public VoxelChunkMeshes(Node3D host) => _host = host;

    /// <summary>
    /// Rebuilds the chunks a change touched, or every chunk when the change has
    /// no region — which is what a fresh document or a material edit is.
    /// </summary>
    public void Apply(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        IReadOnlyDictionary<Guid, Material> materialsById,
        IReadOnlyDictionary<Guid, float> opacityByMaterial,
        CellRegion? region,
        IReadOnlyDictionary<Guid, AtlasPlacement> atlasByMaterial,
        Material? atlasMaterial)
    {
        foreach (Vector3I chunk in ChunksToRebuild(voxels, region))
        {
            Rebuild(
                chunk,
                voxels,
                materialsById,
                opacityByMaterial,
                atlasByMaterial,
                atlasMaterial);
        }
    }

    public void Clear()
    {
        foreach (Node3D node in _nodesByChunkDictionary.Values)
        {
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }

        _nodesByChunkDictionary.Clear();
        _instancesByChunkDictionary.Clear();
    }

    private void Rebuild(
        Vector3I chunk,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        IReadOnlyDictionary<Guid, Material> materialsById,
        IReadOnlyDictionary<Guid, float> opacityByMaterial,
        IReadOnlyDictionary<Guid, AtlasPlacement> atlasByMaterial,
        Material? atlasMaterial)
    {
        VoxelMeshSet set = VoxelMesher.Build(
            voxels,
            materialsById,
            opacityByMaterial,
            BoxOf(chunk),
            atlasByMaterial,
            atlasMaterial);

        if (set.IsEmpty)
        {
            // Emptied rather than kept: a chunk that has been erased would
            // otherwise go on holding its last mesh forever, and a model painted
            // and rubbed out repeatedly would accumulate nodes drawing nothing.
            if (_nodesByChunkDictionary.Remove(chunk, out Node3D? spent))
            {
                _instancesByChunkDictionary.Remove(chunk);
                spent.GetParent()?.RemoveChild(spent);
                spent.QueueFree();
            }

            return;
        }

        if (!_nodesByChunkDictionary.TryGetValue(chunk, out Node3D? node))
        {
            node = new Node3D { Name = $"Chunk{chunk.X}_{chunk.Y}_{chunk.Z}" };
            _host.AddChild(node);
            _nodesByChunkDictionary[chunk] = node;
        }

        if (!_instancesByChunkDictionary.TryGetValue(chunk, out List<MeshInstance3D>? instancesList))
        {
            instancesList = new List<MeshInstance3D>();
            _instancesByChunkDictionary[chunk] = instancesList;
        }

        VoxelMeshInstances.Apply(
            node,
            instancesList,
            set,
            GeometryInstance3D.ShadowCastingSetting.On,
            RenderLayers.Voxels);
    }

    private IEnumerable<Vector3I> ChunksToRebuild(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        CellRegion? region)
    {
        if (region is not CellRegion box)
            return AllChunks(voxels);

        return VoxelChunkGrid.Touching(box);
    }

    /// <summary>
    /// Every chunk that holds a cell, plus every chunk that currently holds a
    /// mesh — the second half is what empties a chunk whose cells have gone.
    /// </summary>
    private IEnumerable<Vector3I> AllChunks(IReadOnlyDictionary<Vector3I, Guid> voxels)
    {
        var keys = new HashSet<Vector3I>(_nodesByChunkDictionary.Keys);

        foreach (Vector3I cell in voxels.Keys)
            keys.Add(KeyOf(cell));

        return keys;
    }

    private static Vector3I KeyOf(Vector3I cell) => VoxelChunkGrid.KeyOf(cell);

    private static CellRegion BoxOf(Vector3I chunk) => VoxelChunkGrid.BoxOf(chunk);
}
