using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// Builds small projects for the projection tests the short way, so a test reads
/// as the shape it is exporting rather than as scene-graph bookkeeping.
/// </summary>
internal static class ModelBuilder
{
    public static (IProject Project, IScene Scene) NewProject(Vector3I? size = null)
    {
        IProject project = ModelFactory.CreateProject(size ?? new Vector3I(32, 32, 32));

        return (project, project.Scenes[0]);
    }

    public static Guid DefaultMaterial(IProject project) => project.Materials[0].Id;

    /// <summary>
    /// A material that is actually see-through. Transparency is declared rather
    /// than inferred from the alpha, so a colour with an alpha in it is an opaque
    /// material until somebody says otherwise — which is the whole point of the
    /// flag and exactly what a test called "glass" has to say out loud.
    /// </summary>
    public static Guid Glass(IProject project, string name = "Glass", float alpha = 0.45f)
    {
        Guid id = project.CreateMaterial(name, new Color(0.4f, 0.7f, 1f, alpha));

        project.SetMaterialAppearance(
            id, MaterialAppearance.Default with { Transparent = true });

        return id;
    }

    public static Guid VoxelElement(IScene scene, string name) =>
        scene.CreateElement(scene.Id, ComponentKind.VoxelModel, name);

    public static void Fill(IScene scene, Guid element, Guid material, params Vector3I[] cells)
    {
        IReadOnlyList<VoxelEdit> edits = cells
            .Select(cell => new VoxelEdit(cell, material))
            .ToList();

        scene.SetVoxels(element, edits);
    }
}
