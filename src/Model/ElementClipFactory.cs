using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Turns elements into clip nodes and clip nodes back into elements. Both
/// directions live here so that what a copy carries and what a paste restores
/// cannot drift apart.
/// </summary>
internal static class ElementClipFactory
{
    private static readonly IReadOnlyDictionary<Vector3I, Guid> NoVoxels =
        new Dictionary<Vector3I, Guid>();

    private static readonly IReadOnlyList<ElementNode> NoChildren = Array.Empty<ElementNode>();

    /// <summary>
    /// Everything the element is, its subtree included, with its identity left
    /// behind. The cells are copied rather than referenced: a clip has to survive
    /// the original being edited or deleted.
    /// </summary>
    /// <remarks>
    /// A prefab instance is the one thing captured as a reference rather than as
    /// content: the source id travels and the children do not, because those
    /// children are derived from that source and would be a stale copy of it the
    /// moment anybody edited the prefab.
    /// </remarks>
    public static ElementNode Capture(HierarchyElement element)
    {
        var model = element.Component as VoxelModel;
        var instance = element.Component as PrefabInstance;
        var camera = element.Component as Camera;
        var image = element.Component as ReferenceImage;
        var reference = element.Component as ReferenceModel;

        return new ElementNode(
            element.Name,
            element.ComponentKind,
            instance?.SourceId,
            element.ElementTransform.Values,
            element.TransformModes,
            element.PivotDoubled,
            element.Visible,
            model?.Symmetry ?? default,
            camera?.Lens ?? CameraLens.Default,
            image?.Settings ?? ReferenceImageSettings.Default,
            reference?.Settings ?? ReferenceModelSettings.Default,
            model == null ? NoVoxels : new Dictionary<Vector3I, Guid>(model.Voxels),
            instance == null ? CaptureChildren(element) : NoChildren);
    }

    /// <summary>Every material id the node's cells wear, its subtree included.</summary>
    public static void CollectMaterialIds(ElementNode node, HashSet<Guid> intoHashSet)
    {
        foreach (Guid materialId in node.Voxels.Values)
            intoHashSet.Add(materialId);

        foreach (ElementNode child in node.Children)
            CollectMaterialIds(child, intoHashSet);
    }

    /// <summary>Every prefab the node's instances refer to, its subtree included.</summary>
    public static void CollectSourceIds(ElementNode node, HashSet<Guid> intoHashSet)
    {
        if (node.SourceId is { } sourceId)
            intoHashSet.Add(sourceId);

        foreach (ElementNode child in node.Children)
            CollectSourceIds(child, intoHashSet);
    }

    /// <summary>
    /// Builds a detached element from a clip node — fresh ids throughout, cells
    /// repainted and instance references re-aimed through
    /// <paramref name="resolution"/>. Detached because attaching it is the command's
    /// job, which is what makes a paste one undoable record.
    /// </summary>
    /// <param name="name">
    /// What the element is called. Only the root of a pasted subtree is renamed;
    /// the descendants keep the names they were copied with, since the inside of a
    /// copied assembly is expected to read like the inside of the original.
    /// </param>
    /// <remarks>
    /// An instance's contents are not built here. They are derived, and the scene
    /// expands them as the subtree is attached — see <see cref="Scene.AttachElement"/>.
    /// </remarks>
    public static HierarchyElement Create(ElementNode node, ClipResolution resolution, string name)
    {
        var element = new HierarchyElement(Guid.NewGuid(), name);
        IElementComponent? component = CreateComponent(node, resolution, element);

        if (component != null)
            element.SetComponent(component);

        element.ElementTransform.Restore(node.Values, node.Modes);
        element.ApplyPivot(node.PivotDoubled);
        element.SetVisible(node.Visible);

        if (element.Component is VoxelModel model)
        {
            foreach (KeyValuePair<Vector3I, Guid> voxel in node.Voxels)
                model.Apply(voxel.Key, resolution.Material(voxel.Value));

            model.ApplySymmetry(node.Symmetry);
        }

        if (element.Component is Camera camera)
            camera.ApplyLens(node.Lens);

        if (element.Component is ReferenceImage image)
            image.ApplySettings(node.ImageSettings);

        if (element.Component is ReferenceModel reference)
            reference.ApplySettings(node.ModelSettings);

        foreach (ElementNode child in node.Children)
            element.ChildContainer.Add(Create(child, resolution, child.Name));

        return element;
    }

    private static IElementComponent? CreateComponent(
        ElementNode node,
        ClipResolution resolution,
        HierarchyElement owner) =>
        node.SourceId is { } sourceId
            ? new PrefabInstance(owner, resolution.Prefab(sourceId))
            : ElementComponentFactory.Create(node.ComponentKind, owner);

    private static IReadOnlyList<ElementNode> CaptureChildren(HierarchyElement element)
    {
        var childrenList = new List<ElementNode>(element.ChildContainer.Children.Count);

        foreach (HierarchyElement child in element.ChildContainer.Children)
            childrenList.Add(Capture(child));

        return childrenList;
    }
}
