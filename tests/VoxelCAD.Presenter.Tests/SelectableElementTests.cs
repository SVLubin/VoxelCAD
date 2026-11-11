using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What a click on an element means when the element is a prefab instance's
/// contents: the instance. Found by driving — a click on a model that had just
/// become a prefab selected its read-only twin, and the inspector went quiet.
/// </summary>
public sealed class SelectableElementTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _material;

    public SelectableElementTests()
    {
        _scene = _project.Scenes[0];
        _material = _project.Materials[0].Id;
    }

    [Fact]
    public void AnOrdinaryElementMeansItself()
    {
        Guid model = CreateModel("Wheel");

        Assert.Equal(new[] { model }, SelectableElements.Resolve(_scene, new[] { model }));
    }

    [Fact]
    public void ContentsMeanTheInstanceTheyBelongTo()
    {
        _scene.CreatePrefab(CreateModel("Wheel"));

        Guid instance = _scene.Children[0].Id;
        Guid content = _scene.Children[0].Children[0].Id;

        Assert.Equal(new[] { instance }, SelectableElements.Resolve(_scene, new[] { content }));
    }

    /// <summary>
    /// Inside a nested instance the contents belong to the inner instance, which is
    /// itself the outer one's contents — so one hop outward is not enough.
    /// </summary>
    [Fact]
    public void NestedContentsMeanTheOutermostInstance()
    {
        _scene.CreatePrefab(CreateModel("Bolt"));

        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");
        _scene.ReparentElement(_scene.Children[0].Id, group, 0);
        _scene.CreatePrefab(group);

        Guid outer = _scene.Children[0].Id;

        // scene → outer instance → Bracket → inner instance → Bolt
        Guid deepest = _scene.Children[0].Children[0].Children[0].Children[0].Id;

        Assert.Equal(new[] { outer }, SelectableElements.Resolve(_scene, new[] { deepest }));
    }

    /// <summary>
    /// A stack of hits inside one instance is one thing to the hand; leaving the
    /// duplicates in would make clicking the same pixel again cycle between two
    /// names for the same element.
    /// </summary>
    [Fact]
    public void ARunOfHitsInsideOneInstanceCollapses()
    {
        Guid model = CreateModel("Wheel");
        Guid inner = _scene.CreateElement(model, ComponentKind.VoxelModel, "Hub");

        _scene.SetVoxels(inner, new[] { new VoxelEdit(new Vector3I(1, 1, 1), _material) });
        _scene.CreatePrefab(model);

        IHierarchyElement instance = _scene.Children[0];
        var hitsList = new List<Guid>
        {
            instance.Children[0].Children[0].Id,
            instance.Children[0].Id
        };

        Assert.Equal(new[] { instance.Id }, SelectableElements.Resolve(_scene, hitsList));
    }

    /// <summary>An id naming nothing passes through rather than being dropped silently.</summary>
    [Fact]
    public void AnUnknownIdIsLeftAsItIs()
    {
        Guid stranger = Guid.NewGuid();

        Assert.Equal(new[] { stranger }, SelectableElements.Resolve(_scene, new[] { stranger }));
    }

    private Guid CreateModel(string name)
    {
        Guid id = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

        _scene.SetVoxels(id, new[] { new VoxelEdit(Vector3I.Zero, _material) });

        return id;
    }
}
