using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Export;

/// <summary>
/// The one place an import knows the model. It turns a read file into elements,
/// materials and cells, and does the whole of it as one undoable record.
/// </summary>
/// <remarks>
/// The mirror of <see cref="ExportProjection"/>, and it keeps the same wall:
/// readers know their format and never see a document, this sees a document and
/// never a format.
/// <para>
/// <b>A refused import leaves nothing behind, and that is structural rather than
/// checked.</b> A reader either produces a whole <see cref="ImportedFile"/> or
/// throws, so most of what could go wrong is already over by the time this is
/// called — but not all of it: the caller names the parent, and a parent that
/// refuses children (prefab contents, an instance, an id that is gone) refuses
/// when the first element is created. A batch that threw part way would close
/// over what it had managed and file a half-finished import as one undoable
/// record, because a history batch can be closed and never discarded.
/// </para>
/// <para>
/// So the elements are created <em>first</em>, before a single material: the very
/// first act inside the batch is the one that can fail, and a batch that collected
/// nothing leaves no record. Materials come next, because the cells name them, and
/// the cells last. Three passes over a handful of nodes, for a property worth more
/// than the pass.
/// </para>
/// </remarks>
internal static class ImportProjection
{
    public static ImportResult Apply(IProject project, IScene scene, ImportPlan plan, Guid parentId)
    {
        ImportedFile file = plan.File;

        // The import lands with its own corner on the scene's origin. A file may
        // place its models anywhere, including well into negative cells, and a
        // model arriving outside the room reads as a broken import. The offset is
        // one translation over the whole import, so nothing moves relative to
        // anything else — which is the half that has to be exact.
        CellTransform landing = plan.Landing;

        var elementNames = new ImportNames();
        CollectElementNames(scene.Children, elementNames);

        var materialNames = new ImportNames();

        foreach (IMaterial material in project.AllMaterials)
            materialNames.Reserve(material.Name);

        var placedList = new List<Placed>();

        using (project.History.CreateBatch(
            ActionKind.CreateElement, scene.Id, parentId, plan.Name))
        {
            // First, and deliberately: this is the only thing left that can be
            // refused, and refused here the batch has collected nothing.
            Guid rootId = file.Roots.Count == 1
                ? Create(scene, file.Roots[0], parentId, landing, elementNames, placedList)
                : Wrap(scene, file, parentId, landing, elementNames, placedList, plan.Name);

            IReadOnlyList<Guid> materialsList =
                CreateMaterials(project, file.Materials, materialNames);

            var cells = 0;

            foreach (Placed placed in placedList)
                cells += Fill(scene, placed, materialsList);

            scene.TryGetElement(rootId, out IHierarchyElement root);

            return new ImportResult(
                rootId,
                root.Name,
                plan.Format,
                placedList.Count,
                cells,
                materialsList.Count,
                plan.Extent,
                file.Notes);
        }
    }

    /// <summary>
    /// Several top-level parts go under one grouping element named after the file,
    /// so an import is one thing in the project tree and one thing to move. A file
    /// holding a single part needs no such wrapper and does not get one.
    /// </summary>
    private static Guid Wrap(
        IScene scene,
        ImportedFile file,
        Guid parentId,
        CellTransform landing,
        ImportNames names,
        List<Placed> placedList,
        string fileName)
    {
        Guid groupId = scene.CreateElement(
            parentId, ComponentKind.None, names.Compose(fileName));

        placedList.Add(new Placed(groupId, Node: null, landing, Vector3.Zero));

        foreach (ImportedNode child in file.Roots)
            Create(scene, child, groupId, CellTransform.Identity, names, placedList);

        return groupId;
    }

    /// <summary>
    /// One element per node, depth first, and nothing in them yet. What each one
    /// is to be given is remembered rather than applied, so that no cell and no
    /// material exists until every element does.
    /// </summary>
    private static Guid Create(
        IScene scene,
        ImportedNode node,
        Guid parentId,
        CellTransform outer,
        ImportNames names,
        List<Placed> placedList)
    {
        CellTransform placement = outer.Compose(node.Placement);
        ComponentKind kind = node.Volume == null ? ComponentKind.None : ComponentKind.VoxelModel;
        Guid id = scene.CreateElement(parentId, kind, names.Compose(node.Name));

        placedList.Add(new Placed(id, node, placement, node.Volume?.Pivot ?? Vector3.Zero));

        foreach (ImportedNode child in node.Children)
            Create(scene, child, id, CellTransform.Identity, names, placedList);

        return id;
    }

    /// <summary>
    /// What one element is given, and the order is load-bearing.
    /// </summary>
    /// <remarks>
    /// <b>The rotation goes first, while the element still has no pivot.</b> A
    /// rotation written against a pivot is <em>anchored</em> — the model re-solves
    /// the origin so the pivot does not move — and it re-solves it from a basis
    /// rebuilt out of Euler angles, so the answer carries a float error that grows
    /// with the pivot's distance from the origin. Where that error is larger than
    /// the model's own tolerance the position channel degrades to Float, silently,
    /// and the cells stop sitting on the world's grid. Measured: a model of 44
    /// cells a side was enough. Written first there is no pivot to anchor to, so
    /// there is nothing to re-solve and nothing to lose.
    /// <para>
    /// Then the cells, because the first edit of an empty model is what gives it a
    /// default pivot; then the pivot this file actually wants, which holds the
    /// origin; and the position last, absolute, which is the one write that
    /// quantises rather than anchoring — so it absorbs the float error instead of
    /// tripping over it.
    /// </para>
    /// </remarks>
    private static int Fill(IScene scene, Placed placed, IReadOnlyList<Guid> materialsList)
    {
        var cells = 0;
        Vector3 degrees = ImportPlacement.RotationDegrees(placed.Placement.Rotation);

        if (degrees != Vector3.Zero)
            scene.SetTransform(placed.Id, TransformChannel.Rotation, degrees);

        if (placed.Node?.Volume is { } volume)
        {
            // One call with every cell of the model. A cell at a time would remesh
            // the model once per voxel, which is the difference between an import
            // that lands and one that hangs.
            scene.SetVoxels(placed.Id, Edits(volume, materialsList));
            scene.SetPivot(placed.Id, placed.Pivot);
            cells = volume.Cells.Count;
        }

        scene.SetTransform(
            placed.Id, TransformChannel.Position, Position(placed.Placement, placed.Pivot));

        return cells;
    }

    private static Vector3 Position(CellTransform placement, Vector3 pivot) =>
        ImportPlacement.Position(placement, pivot);

    private static IReadOnlyList<VoxelEdit> Edits(
        ImportedVolume volume,
        IReadOnlyList<Guid> materialsList)
    {
        var editsList = new List<VoxelEdit>(volume.Cells.Count);

        foreach (ImportedCell cell in volume.Cells)
            editsList.Add(new VoxelEdit(cell.Cell, materialsList[cell.PaletteIndex]));

        return editsList;
    }

    private static IReadOnlyList<Guid> CreateMaterials(
        IProject project,
        IReadOnlyList<ImportedMaterial> materialsList,
        ImportNames names)
    {
        var idsList = new List<Guid>(materialsList.Count);

        foreach (ImportedMaterial material in materialsList)
        {
            string name = names.Compose(material.Name);
            Guid id = project.CreateMaterial(name, material.Color);

            if (material.Appearance != MaterialAppearance.Default)
                project.SetMaterialAppearance(id, material.Appearance);

            idsList.Add(id);
        }

        return idsList;
    }

    private static void CollectElementNames(
        IReadOnlyList<IHierarchyElement> children,
        ImportNames names)
    {
        foreach (IHierarchyElement child in children)
        {
            names.Reserve(child.Name);
            CollectElementNames(child.Children, names);
        }
    }

    /// <summary>
    /// An element that exists and has nothing in it yet, with what it is to be
    /// given once every other element exists too.
    /// </summary>
    /// <param name="Node">Null for the wrapper, which stands for no node of the file.</param>
    private readonly record struct Placed(
        Guid Id,
        ImportedNode? Node,
        CellTransform Placement,
        Vector3 Pivot);
}
