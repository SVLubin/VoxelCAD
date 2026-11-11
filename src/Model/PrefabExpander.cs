using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Builds the contents of a prefab instance from its source. Derived data, so it
/// is rebuilt rather than edited, and every element it makes is marked read-only.
/// </summary>
/// <remarks>
/// Walks the live source elements rather than going through a clip, and that is
/// deliberate: a clip leaves identity behind on purpose, and this needs the source
/// element's id to derive the twin's. A nested instance is rebuilt as an instance —
/// its own contents expanded here in turn — so editing the inner prefab still
/// reaches every copy of the outer one.
/// </remarks>
internal static class PrefabExpander
{
    /// <summary>
    /// Fills the instance's children in place, silently: no history, no change
    /// events and no registration, all of which belong to the scene that called it.
    /// A source that no longer exists leaves the instance empty rather than throwing
    /// — a dangling reference is a document to keep open, not one to refuse.
    /// </summary>
    public static void Fill(Project project, HierarchyElement instance)
    {
        instance.ChildContainer.Clear();

        if (instance.Component is not PrefabInstance component
            || !project.TryGetPrefabContent(component.SourceId, out Scene? content))
        {
            return;
        }

        foreach (HierarchyElement source in content.TopLevelElements)
            instance.ChildContainer.Add(Build(project, source, instance.Id));
    }

    /// <summary>Expands every instance in a freshly built subtree, itself included.</summary>
    public static void FillSubtree(Project project, HierarchyElement root)
    {
        if (root.ComponentKind == ComponentKind.PrefabInstance)
        {
            Fill(project, root);
            return;
        }

        foreach (HierarchyElement child in root.ChildContainer.Children)
            FillSubtree(project, child);
    }

    private static HierarchyElement Build(Project project, HierarchyElement source, Guid instanceId)
    {
        var element = new HierarchyElement(PrefabIdentity.Derive(instanceId, source.Id), source.Name);

        element.SetPrefabInstanceId(instanceId);
        element.ElementTransform.Restore(source.ElementTransform.Values, source.TransformModes);
        element.ApplyPivot(source.PivotDoubled);
        element.SetVisible(source.Visible);

        switch (source.Component)
        {
            // A grouping element: nothing to copy, and the children below are the
            // whole of what it is.
            case null:
                break;

            case VoxelModel model:
                element.SetComponent(CopyModel(model, element));
                break;

            case Camera camera:
                var copy = new Camera(element);

                copy.ApplyLens(camera.Lens);
                element.SetComponent(copy);
                break;

            case ReferenceImage picture:
                var copiedPicture = new ReferenceImage(element);

                copiedPicture.ApplySettings(picture.Settings);
                element.SetComponent(copiedPicture);
                break;

            case ReferenceModel reference:
                var copiedModel = new ReferenceModel(element);

                copiedModel.ApplySettings(reference.Settings);
                element.SetComponent(copiedModel);
                break;

            // The source's own contents are not copied: they are its instance's
            // derived data, and this one derives its own from the same prefab.
            case PrefabInstance nested:
                element.SetComponent(new PrefabInstance(element, nested.SourceId));
                Fill(project, element);

                return element;

            // Loud rather than silent, because silent is what this cost: a
            // component with no rule here used to fall straight through and the
            // instance's copy became a grouping element — the same shape, the same
            // transform, the same name, and nothing in it. A reference shipped that
            // way and only a tree read found it.
            default:
                throw new NotSupportedException(
                    $"A {source.ComponentKind} component has no expansion rule, so an "
                    + "instance of it would quietly be a grouping element.");
        }

        foreach (HierarchyElement child in source.ChildContainer.Children)
            element.ChildContainer.Add(Build(project, child, instanceId));

        return element;
    }

    private static VoxelModel CopyModel(VoxelModel source, HierarchyElement owner)
    {
        var model = new VoxelModel(owner);

        foreach (KeyValuePair<Vector3I, Guid> voxel in source.Voxels)
            model.Apply(voxel.Key, voxel.Value);

        model.ApplySymmetry(source.Symmetry);

        return model;
    }
}
