using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Which elements a group drag actually writes to. Everything selected, minus
/// anything standing under something else that is selected — the ancestor's move
/// carries the subtree, so writing the child's channel as well would move it twice.
/// </summary>
public sealed class TransformTargetsTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public TransformTargetsTests() => _scene = _project.Scenes[0];

    [Fact]
    public void SiblingsAllGetWritten()
    {
        Guid first = CreateElement();
        Guid second = CreateElement();

        Assert.Equal(new[] { first, second }, Collect(Element(first), Element(second)));
    }

    [Fact]
    public void TheSelectionsOwnOrderIsKept()
    {
        Guid first = CreateElement();
        Guid second = CreateElement();

        Assert.Equal(new[] { second, first }, Collect(Element(second), Element(first)));
    }

    [Fact]
    public void AChildOfASelectedParentIsDropped()
    {
        Guid group = CreateElement();
        Guid child = CreateElement(group);

        Assert.Equal(new[] { group }, Collect(Element(group), Element(child)));
    }

    /// <summary>Whichever order they were picked out in.</summary>
    [Fact]
    public void EvenWhenTheChildWasSelectedFirst()
    {
        Guid group = CreateElement();
        Guid child = CreateElement(group);

        Assert.Equal(new[] { group }, Collect(Element(child), Element(group)));
    }

    /// <summary>
    /// And anything the model would refuse to write to at all. A prefab
    /// instance's contents are read-only wherever they appear and are selectable
    /// rows in the tree, so a hand reaches this by picking the inner row and
    /// pulling a handle — where the write throws once per frame of the drag.
    /// </summary>
    [Fact]
    public void PrefabInstanceContentIsDropped()
    {
        Guid model = CreateElement();
        _scene.SetVoxels(model, new[] { new VoxelEdit(Godot.Vector3I.Zero, _project.Materials[0].Id) });
        _scene.CreatePrefab(model);

        // A transfer, not a copy: the subtree went to the library and an instance
        // stands where the element did, so the id to act on is the instance's.
        IHierarchyElement instance = OnlyInstance();
        Guid content = instance.Children[0].Id;

        Assert.Equal(new[] { instance.Id }, Collect(Element(instance.Id), Element(content)));
        Assert.Empty(Collect(Element(content)));
    }

    /// <summary>Not only the immediate parent — anything above it counts.</summary>
    [Fact]
    public void AGrandchildIsDroppedToo()
    {
        Guid group = CreateElement();
        Guid middle = CreateElement(group);
        Guid leaf = CreateElement(middle);

        Assert.Equal(new[] { group }, Collect(Element(group), Element(leaf)));
    }

    /// <summary>
    /// A child whose parent is <em>not</em> selected is written like any other: it
    /// is nobody's passenger.
    /// </summary>
    [Fact]
    public void AChildWhoseParentIsNotSelectedIsWritten()
    {
        Guid group = CreateElement();
        Guid child = CreateElement(group);

        Assert.Equal(new[] { child }, Collect(Element(child)));
    }

    /// <summary>
    /// A scene or a material row has no transform to take hold of, and an element
    /// of another scene names nothing here.
    /// </summary>
    [Fact]
    public void AnythingThatIsNotAnElementOfThisSceneIsDropped()
    {
        Guid element = CreateElement();

        Assert.Equal(
            new[] { element },
            Collect(
                new SelectionTarget(SelectionKind.Scene, _scene.Id),
                new SelectionTarget(SelectionKind.Material, Guid.NewGuid()),
                Element(Guid.NewGuid()),
                Element(element)));
    }

    [Fact]
    public void NothingSelectedIsNothingToWrite() =>
        Assert.Empty(TransformTargets.Collect(_scene, Array.Empty<SelectionTarget>()));

    private IHierarchyElement OnlyInstance()
    {
        foreach (IHierarchyElement child in _scene.Children)
        {
            if (child.ComponentKind == ComponentKind.PrefabInstance)
                return child;
        }

        throw new InvalidOperationException("no instance in the scene");
    }

    private static SelectionTarget Element(Guid id) => new(SelectionKind.Element, id);

    private IReadOnlyList<Guid> Collect(params SelectionTarget[] targets) =>
        TransformTargets.Collect(_scene, targets);

    private Guid CreateElement(Guid? parent = null) =>
        _scene.CreateElement(parent ?? _scene.Id, ComponentKind.VoxelModel, "Model");
}
