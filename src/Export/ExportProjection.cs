using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;

namespace VoxelCAD.Export;

/// <summary>
/// The one place that knows the model. It walks a scene, composes world
/// transforms, reads voxels and materials, and hands the mesher or the volume
/// baker a finished frame — so a model change lands here and the writers never
/// notice.
/// </summary>
internal static class ExportProjection
{
    public static ExportModel Build(
        IProject project,
        IScene scene,
        ExportFacet facet,
        CullingPolicy culling)
    {
        var palette = BuildPalette(project, scene);

        return facet switch
        {
            ExportFacet.Mesh => new ExportModel(
                scene.Name,
                palette,
                BuildMesh(project, scene, palette, culling),
                volume: null,
                Array.Empty<string>()),
            ExportFacet.Volume => BuildVolumeModel(scene, palette),
            _ => throw new ArgumentOutOfRangeException(nameof(facet), facet, null)
        };
    }

    /// <summary>
    /// The project's own materials, then whichever of the shipped library
    /// <em>this scene</em> wears. Not the whole library: a <c>.vox</c> palette
    /// holds 256 entries and the library alone is three hundred, so an export
    /// that listed all of them would be refused for materials nothing uses. The
    /// project's own come first so their indices are exactly what they were
    /// before the library existed.
    /// </summary>
    /// <remarks>
    /// <b>This scene, not the document.</b> It used to ask
    /// <c>IProject.IsMaterialInUse</c>, which walks every scene and every prefab
    /// — so exporting a scene wearing two shipped materials carried whatever the
    /// other scenes wore as well, and a small export could be refused for
    /// materials it does not contain. It also cost three hundred whole-document
    /// scans, one per shipped material, where one pass over the scene answers all
    /// three hundred at once.
    /// </remarks>
    private static ExportPalette BuildPalette(IProject project, IScene scene)
    {
        var materialsList = new List<ExportMaterial>(project.Materials.Count);

        foreach (IMaterial material in project.Materials)
            materialsList.Add(Entry(material));

        HashSet<Guid> worn = CollectWornMaterials(scene);

        foreach (IMaterial material in project.StandardMaterials)
        {
            if (worn.Contains(material.Id))
                materialsList.Add(Entry(material));
        }

        return new ExportPalette(materialsList);
    }

    /// <summary>
    /// One palette entry. The colour carries the material's <em>declared</em>
    /// opacity rather than whatever alpha the colour happens to hold: an opaque
    /// material may be carrying one nobody chose, and writing it would put a
    /// dissolve in the file for a surface the same export culled faces as if it
    /// were solid.
    /// </summary>
    private static ExportMaterial Entry(IMaterial material) => new(
        material.Id,
        material.Name,
        new Color(
            material.Color.R,
            material.Color.G,
            material.Color.B,
            material.Appearance.OpacityOf(material.Color.A)));

    /// <summary>
    /// Every material id the scene's cells actually wear, in one pass. Hidden
    /// elements are counted: being in the palette costs an entry, and being
    /// missing from it is what makes a cell unwriteable — so the cheap direction
    /// is the safe one.
    /// </summary>
    private static HashSet<Guid> CollectWornMaterials(IHierarchyContainer container)
    {
        var wornHashSet = new HashSet<Guid>();

        CollectWornMaterials(container, wornHashSet);

        return wornHashSet;
    }

    private static void CollectWornMaterials(IHierarchyContainer container, HashSet<Guid> wornHashSet)
    {
        foreach (IHierarchyElement child in container.Children)
        {
            if (child.Component is IVoxelModel model)
            {
                foreach (Guid id in model.Voxels.Values)
                    wornHashSet.Add(id);
            }

            CollectWornMaterials(child, wornHashSet);
        }
    }

    private static ExportMesh BuildMesh(
        IProject project,
        IScene scene,
        ExportPalette palette,
        CullingPolicy culling)
    {
        IReadOnlyDictionary<Guid, float> opacityById = BuildOpacityMap(project);
        var surfacesList = new List<ExportSurface>();

        Walk(scene, Transform3D.Identity, (element, world, model) =>
            surfacesList.AddRange(ExportMesher.BuildElementSurfaces(
                element.Name,
                model.Voxels,
                world,
                culling,
                opacityById,
                palette)));

        return new ExportMesh(palette, surfacesList);
    }

    private static ExportModel BuildVolumeModel(IScene scene, ExportPalette palette)
    {
        var bricksList = new List<ExportBrick>();
        var unbakeableList = new List<string>();

        Walk(scene, Transform3D.Identity, (element, world, model) =>
        {
            if (model.Voxels.Count == 0)
                return;

            if (!GridAlignment.IsAligned(world))
            {
                unbakeableList.Add(element.Name);
                return;
            }

            bricksList.Add(BakeBrick(model, world, palette));
        });

        return new ExportModel(scene.Name, palette, mesh: null, new ExportVolume(palette, bricksList), unbakeableList);
    }

    /// <summary>
    /// Visits every voxel element of the tree with its composed world transform.
    /// World transforms are never cached in the model, so the projection carries
    /// the running product down the recursion itself.
    /// </summary>
    private static void Walk(
        IHierarchyContainer container,
        Transform3D world,
        Action<IHierarchyElement, Transform3D, IVoxelModel> visit)
    {
        foreach (IHierarchyElement child in container.Children)
        {
            // A hidden element takes its whole subtree out of the file with it —
            // the flag is the element's own and composes down the tree, exactly as
            // the viewport's node visibility does. What leaves the editor is what
            // is on screen, which is the only reading of "hidden" worth having.
            if (!child.Visible)
                continue;

            // Transform.Position names the pivot, so the pivot goes back into the
            // composition — or every element whose pivot has left zero would bake
            // shifted by exactly basis · pivot.
            Transform3D childWorld =
                world * TransformHelper.ToTransform(child.Transform, child.PivotPoint);

            if (child.Component is IVoxelModel model)
                visit(child, childWorld, model);

            Walk(child, childWorld, visit);
        }
    }

    private static ExportBrick BakeBrick(IVoxelModel model, Transform3D world, ExportPalette palette)
    {
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        var worldCellsList = new List<(Vector3I Cell, byte Index)>(model.Voxels.Count);

        foreach (KeyValuePair<Vector3I, Guid> voxel in model.Voxels)
        {
            Vector3I worldCell = ToWorldCell(voxel.Key, world);
            byte index = palette.TryGetIndex(voxel.Value, out int paletteIndex) ? (byte)(paletteIndex + 1) : (byte)0;

            worldCellsList.Add((worldCell, index));

            min = Min(min, worldCell);
            max = Max(max, worldCell);
        }

        Vector3I size = max - min + Vector3I.One;
        var brick = new ExportBrick(min, size, new byte[(long)size.X * size.Y * size.Z]);

        foreach ((Vector3I cell, byte index) in worldCellsList)
        {
            Vector3I local = cell - min;
            brick.PaletteIndices[brick.IndexOf(local.X, local.Y, local.Z)] = index;
        }

        return brick;
    }

    /// <summary>
    /// The world cell a local cell lands in: the cell centre transformed, then
    /// floored. For an axis-aligned transform a centre always maps to another
    /// centre, so the floor is exact.
    /// </summary>
    private static Vector3I ToWorldCell(Vector3I cell, Transform3D world)
    {
        Vector3 centre = world * new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

        return new Vector3I(
            Mathf.FloorToInt(centre.X),
            Mathf.FloorToInt(centre.Y),
            Mathf.FloorToInt(centre.Z));
    }

    private static IReadOnlyDictionary<Guid, float> BuildOpacityMap(IProject project)
    {
        var opacityByIdDictionary = new Dictionary<Guid, float>(project.Materials.Count);

        foreach (IMaterial material in project.AllMaterials)
            opacityByIdDictionary[material.Id] = material.Appearance.OpacityOf(material.Color.A);

        return opacityByIdDictionary;
    }

    private static Vector3I Min(Vector3I a, Vector3I b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Vector3I Max(Vector3I a, Vector3I b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
}
