using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.Model;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// Every filled cell of a scene in world coordinates, with the colour it wears.
/// </summary>
/// <remarks>
/// What a round trip has to compare. An element's own cells would prove nothing
/// about where the element ended up, and a picture would prove nothing at all. The
/// composition is the model's own <see cref="IHierarchyElement.WorldTransform"/>,
/// so this cannot come to disagree with the document about where anything is.
/// </remarks>
internal static class SceneCells
{
    private static readonly Vector3 CellCentre = new(0.5f, 0.5f, 0.5f);

    public static Dictionary<Vector3I, Color> Collect(IProject project, IScene scene)
    {
        var cellsDictionary = new Dictionary<Vector3I, Color>();

        Walk(project, scene.Children, cellsDictionary);

        return cellsDictionary;
    }

    private static void Walk(
        IProject project,
        IReadOnlyList<IHierarchyElement> children,
        Dictionary<Vector3I, Color> cellsDictionary)
    {
        foreach (IHierarchyElement child in children)
        {
            if (child.Component is IVoxelModel model)
            {
                Transform3D world = child.WorldTransform;

                foreach ((Vector3I cell, Guid materialId) in model.Voxels)
                {
                    Vector3 centre = world * (new Vector3(cell.X, cell.Y, cell.Z) + CellCentre);

                    project.TryGetMaterial(materialId, out IMaterial material);

                    cellsDictionary[new Vector3I(
                        Mathf.FloorToInt(centre.X),
                        Mathf.FloorToInt(centre.Y),
                        Mathf.FloorToInt(centre.Z))] = material.Color;
                }
            }

            Walk(project, child.Children, cellsDictionary);
        }
    }
}
