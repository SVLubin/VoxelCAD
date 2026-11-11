using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Render;

/// <summary>
/// Walks a scene into a <see cref="RenderScene"/>: every visible voxel model
/// becomes a dense grid in its own cells with the transform that places it, and
/// the project's materials become the palette those grids index into.
/// </summary>
/// <remarks>
/// A second walk of the document beside the exporter's, and deliberately so: the
/// exporter bakes into world cells and loses rotated elements to
/// <c>UnbakeableElements</c>, while a tracer wants each element left in its own
/// frame precisely so a rotation costs it nothing. The two answer different
/// questions about the same tree.
/// </remarks>
internal static class RenderSceneBuilder
{
    /// <summary>
    /// Past this many cells an element's dense grid is refused rather than held.
    /// A model that reaches it is one whose bounding box is mostly empty; the
    /// answer is a sparse structure, not a bigger allocation.
    /// </summary>
    public const int MaximumCellsPerElement = 8_000_000;

    public const int MaximumCellsTotal = 32_000_000;

    /// <summary>
    /// How many emissive cells the tracer will aim at. Past this the rest still
    /// light the scene — a path that lands on one still carries its radiance —
    /// they simply are not aimed at, and the picture says so.
    /// </summary>
    public const int MaximumLights = 512;

    /// <summary>
    /// The part of a cell's word that names its material — the low sixteen bits,
    /// which is also the most materials one scene may hold. The high half is
    /// spare. Shared with the shader, which masks by the same number; a
    /// disagreement here is a model whose materials are read as nonsense rather
    /// than a bug anybody could see.
    /// </summary>
    public const uint MaterialMask = 0xFFFFu;

    /// <summary>
    /// Which materials a compiled grid actually names, each once. One pass over
    /// the cells that were just written, which is cheap beside the passes that
    /// wrote them.
    /// </summary>
    private static IEnumerable<uint> UsedMaterialIndices(uint[] voxels)
    {
        var seen = new HashSet<uint>();

        foreach (uint word in voxels)
        {
            uint index = word & MaterialMask;

            if (index != 0)
                seen.Add(index);
        }

        return seen;
    }

    public static RenderScene Build(IProject project, IScene scene)
    {
        var materialsList = new List<RenderMaterial>(project.Materials.Count);
        var facturesList = new List<RenderFacture>();
        var emissiveByIndex = new List<bool>();
        var indexByIdDictionary = new Dictionary<Guid, uint>(project.Materials.Count);

        foreach (IMaterial material in project.AllMaterials)
        {
            indexByIdDictionary[material.Id] = (uint)materialsList.Count + 1;
            materialsList.Add(
                RenderMaterial.From(material.Name, material.Color, material.Appearance));
            emissiveByIndex.Add(material.Appearance.Emission > 0f);
        }

        // Every visible model, in the order the tree holds them. Walking the tree
        // is cheap — it touches no cells — and doing it on its own is what lets
        // the two passes that do touch cells run over several of them at once.
        var foundList = new List<(IHierarchyElement Element, IVoxelModel Model)>();

        void Visit(IHierarchyContainer container, bool visible)
        {
            foreach (IHierarchyElement element in container.Children)
            {
                bool shown = visible && element.Visible;

                if (shown && element.Component is IVoxelModel model && model.Voxels.Count > 0)
                    foundList.Add((element, model));

                Visit(element, shown);
            }
        }

        Visit(scene, visible: true);

        // Measuring is per element and independent, and it is one of the two walks
        // over every cell a compile does. Ordered results, so what the grid ends up
        // holding does not depend on which thread finished first.
        var boxes = new (Vector3I Min, Vector3I Max)[foundList.Count];

        RunOverElements(foundList.Count, i => boxes[i] = MeasureCells(foundList[i].Model.Voxels.Keys));

        var plansList = new List<ElementPlan>(foundList.Count);
        var oversizedList = new List<string>();
        long total = 0;

        for (int i = 0; i < foundList.Count; i++)
        {
            Vector3I size = boxes[i].Max - boxes[i].Min + Vector3I.One;
            long cells = (long)size.X * size.Y * size.Z;

            if (cells > MaximumCellsPerElement || total + cells > MaximumCellsTotal)
            {
                oversizedList.Add(foundList[i].Element.Name);

                continue;
            }

            plansList.Add(new ElementPlan(
                foundList[i].Element,
                foundList[i].Model,
                boxes[i].Min,
                boxes[i].Max,
                size,
                (int)total));

            total += cells;
        }

        // One allocation for the whole scene rather than a list grown a cell at a
        // time: the grids are dense, so the total is known before any of it is
        // filled and the growing was most of what a compile cost.
        uint[] voxels = new uint[Math.Max(total, 1)];
        var filled = new ElementFill[plansList.Count];

        RunOverElements(
            plansList.Count,
            i => filled[i] = Fill(plansList[i], voxels, indexByIdDictionary, emissiveByIndex));

        // The factures, and only the ones the scene actually wears. Once per
        // material rather than once per finish — two materials sharing a finish may
        // wear two different shipped sets — and only for materials that turned up
        // in a cell, because the library the editor ships with is three hundred
        // strong: compiling a facture for every one of them meant fifteen hundred
        // images decoded and a thirty-megabyte atlas uploaded to draw three cubes.
        foreach (uint index in UsedMaterialIndices(voxels))
        {
            RenderMaterial material = materialsList[(int)index - 1];

            if (material.Finish != MaterialFinish.Smooth && material.Relief > 0f)
                facturesList.Add(new RenderFacture(material.Name, material.Finish));
        }

        var elementsList = new List<RenderElement>(plansList.Count);
        var lightsList = new List<RenderLight>();
        bool truncated = false;

        Vector3 boundsMin = Vector3.Inf;
        Vector3 boundsMax = -Vector3.Inf;

        for (int i = 0; i < plansList.Count; i++)
        {
            ElementPlan plan = plansList[i];
            ElementFill fill = filled[i];

            truncated |= fill.Truncated;

            // The cap is applied here rather than inside the fill, so which lamps
            // are aimed at is the tree's order and not the order threads happened
            // to finish in.
            foreach (RenderLight light in fill.Lights)
            {
                if (lightsList.Count >= MaximumLights)
                {
                    truncated = true;

                    break;
                }

                lightsList.Add(light with { Element = elementsList.Count });
            }

            boundsMin = boundsMin.Min(fill.BoundsMin);
            boundsMax = boundsMax.Max(fill.BoundsMax);

            elementsList.Add(new RenderElement(
                WorldToLocal: plan.World.AffineInverse(),
                GridMin: plan.Min,
                GridSize: plan.Size,
                VoxelOffset: plan.Offset,
                BoundsMin: fill.BoundsMin,
                BoundsMax: fill.BoundsMax));
        }

        if (elementsList.Count == 0)
        {
            boundsMin = Vector3.Zero;
            boundsMax = Vector3.Zero;
        }

        return new RenderScene(
            elementsList,
            materialsList,
            voxels,
            boundsMin,
            boundsMax,
            oversizedList,
            facturesList,
            lightsList,
            truncated);
    }

    /// <summary>
    /// Walks one element's cells into its own range of the grid, and finds the
    /// lamps in it. Every element writes to a range nothing else touches, which is
    /// what makes running several of them at once safe rather than merely fast.
    /// </summary>
    private static ElementFill Fill(
        ElementPlan plan,
        uint[] voxels,
        IReadOnlyDictionary<Guid, uint> indexById,
        IReadOnlyList<bool> emissiveByIndex)
    {
        Vector3I size = plan.Size;

        foreach (KeyValuePair<Vector3I, Guid> voxel in plan.Model.Voxels)
        {
            Vector3I local = voxel.Key - plan.Min;
            int index = plan.Offset + local.X + size.X * (local.Y + size.Y * local.Z);

            voxels[index] = indexById.TryGetValue(voxel.Value, out uint found) ? found : 1u;
        }

        var lightsList = new List<RenderLight>();

        bool complete = CollectLights(
            plan.World, plan.Min, size, plan.Offset, voxels, emissiveByIndex, lightsList);

        (Vector3 min, Vector3 max) = MeasureWorldBox(plan.World, plan.Min, plan.Max + Vector3I.One);

        return new ElementFill(lightsList, !complete, min, max);
    }

    /// <summary>
    /// Runs a per-element step over as many of them at once as there is a
    /// processor for, and sequentially when there is only one of them to do —
    /// which is the common case and where handing it to a thread pool would cost
    /// more than the work.
    /// </summary>
    private static void RunOverElements(int count, Action<int> step)
    {
        if (count <= 1)
        {
            for (int i = 0; i < count; i++)
                step(i);

            return;
        }

        Parallel.For(0, count, step);
    }

    /// <summary>An element that has room in the grid, and where its cells go.</summary>
    private readonly record struct ElementPlan(
        IHierarchyElement Element,
        IVoxelModel Model,
        Vector3I Min,
        Vector3I Max,
        Vector3I Size,
        int Offset)
    {
        public Transform3D World => Element.WorldTransform;
    }

    /// <summary>What walking one element's cells found.</summary>
    /// <param name="Truncated">
    /// Whether this element alone had more lamps than the whole scene may aim at.
    /// Reported per element because the cap is applied when they are merged, and
    /// one element that filled it on its own would otherwise arrive looking like
    /// a scene that exactly fits.
    /// </param>
    private readonly record struct ElementFill(
        IReadOnlyList<RenderLight> Lights,
        bool Truncated,
        Vector3 BoundsMin,
        Vector3 BoundsMax);

    /// <summary>
    /// Every emissive cell of one element that has a face on the outside, as
    /// something to aim at. A cell walled in by its own neighbours lights nothing
    /// and would only waste the samples drawn towards it.
    /// </summary>
    /// <returns>
    /// False when it stopped at the cap with lamps still to find, which is what
    /// makes a scene say it is only lighting some of them.
    /// </returns>
    private static bool CollectLights(
        Transform3D world,
        Vector3I min,
        Vector3I size,
        int offset,
        uint[] voxels,
        IReadOnlyList<bool> emissiveByIndex,
        List<RenderLight> lights)
    {
        // The sphere that bounds one cell, carried into the world: half the
        // longest diagonal, bounded above by the sum of the three axes.
        Vector3 basis = world.Basis.Column0.Abs() + world.Basis.Column1.Abs() + world.Basis.Column2.Abs();
        float radius = basis.Length() * 0.5f;

        for (int z = 0; z < size.Z; z++)
        {
            for (int y = 0; y < size.Y; y++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    uint value = voxels[offset + x + size.X * (y + size.Y * z)]
                        & MaterialMask;

                    if (value == 0u
                        || value > emissiveByIndex.Count
                        || !emissiveByIndex[(int)value - 1])
                    {
                        continue;
                    }

                    if (!HasOpenFace(voxels, offset, size, x, y, z))
                        continue;

                    // More than the whole scene may aim at: the caller keeps
                    // the first of them in the tree's own order and says so.
                    if (lights.Count >= MaximumLights)
                        return false;

                    var cell = new Vector3I(min.X + x, min.Y + y, min.Z + z);

                    lights.Add(new RenderLight(
                        0,
                        cell,
                        world * (cell + new Vector3(0.5f, 0.5f, 0.5f)),
                        radius));
                }
            }
        }

        return true;
    }

    private static bool HasOpenFace(uint[] voxels, int offset, Vector3I size, int x, int y, int z)
    {
        return IsEmpty(voxels, offset, size, x - 1, y, z)
            || IsEmpty(voxels, offset, size, x + 1, y, z)
            || IsEmpty(voxels, offset, size, x, y - 1, z)
            || IsEmpty(voxels, offset, size, x, y + 1, z)
            || IsEmpty(voxels, offset, size, x, y, z - 1)
            || IsEmpty(voxels, offset, size, x, y, z + 1);
    }

    private static bool IsEmpty(uint[] voxels, int offset, Vector3I size, int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x >= size.X || y >= size.Y || z >= size.Z)
            return true;

        return (voxels[offset + x + size.X * (y + size.Y * z)] & MaterialMask) == 0u;
    }

    /// <summary>
    /// The world box of a local cell box, by carrying its eight corners across —
    /// axis-aligned in the world rather than hugging a rotated element, which is
    /// what a reject test wants.
    /// </summary>
    private static (Vector3 Min, Vector3 Max) MeasureWorldBox(
        Transform3D world,
        Vector3I localMin,
        Vector3I localMax)
    {
        var min = Vector3.Inf;
        var max = -Vector3.Inf;

        for (int corner = 0; corner < 8; corner++)
        {
            var point = new Vector3(
                (corner & 1) == 0 ? localMin.X : localMax.X,
                (corner & 2) == 0 ? localMin.Y : localMax.Y,
                (corner & 4) == 0 ? localMin.Z : localMax.Z);

            Vector3 placed = world * point;

            min = min.Min(placed);
            max = max.Max(placed);
        }

        return (min, max);
    }

    private static (Vector3I Min, Vector3I Max) MeasureCells(IEnumerable<Vector3I> cells)
    {
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);

        foreach (Vector3I cell in cells)
        {
            min = min.Min(cell);
            max = max.Max(cell);
        }

        return (min, max);
    }
}
