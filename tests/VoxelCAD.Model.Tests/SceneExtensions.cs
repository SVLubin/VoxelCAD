using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>Reading helpers, so tests state their intent instead of their plumbing.</summary>
internal static class SceneExtensions
{
    public static IHierarchyElement Element(this IScene scene, Guid id)
    {
        Assert.True(scene.TryGetElement(id, out IHierarchyElement element), $"No element {id}.");

        return element;
    }

    public static TransformModes Modes(this IScene scene, Guid id) =>
        scene.Element(id).TransformModes;

    /// <summary>
    /// Composed from the public interfaces only, so it checks the scene's own
    /// transform arithmetic rather than trusting it.
    /// </summary>
    public static Transform3D WorldTransform(this IScene scene, Guid id)
    {
        var result = Transform3D.Identity;

        for (IHierarchyContainer current = scene.Element(id);
            current is IHierarchyElement element;
            current = element.Parent)
        {
            TransformValues values = element.Transform;
            Basis basis = Basis.FromEuler(
                new Vector3(
                    Mathf.DegToRad(values.RotationDegrees.X),
                    Mathf.DegToRad(values.RotationDegrees.Y),
                    Mathf.DegToRad(values.RotationDegrees.Z)));

            result = new Transform3D(basis.Scaled(values.Scale), values.Position) * result;
        }

        return result;
    }
}
