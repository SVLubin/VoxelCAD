using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The change stream is the only way views learn about the model, so every
/// mutation must announce itself with enough detail to update a view incrementally.
/// </summary>
public sealed class SceneChangeTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly List<SceneChange> _changesList = new();

    public SceneChangeTests()
    {
        _scene = _project.Scenes[0];
        _scene.Changed += _changesList.Add;
    }

    [Fact]
    public void CreateReportsParentAndIndex()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        Guid second = _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");

        SceneChange change = Assert.Single(_changesList, c => c.ElementId == second);

        Assert.Equal(SceneChangeKind.ElementAdded, change.Kind);
        Assert.Equal(_scene.Id, change.ParentId);
        Assert.Equal(1, change.Index);
    }

    [Fact]
    public void DeleteReportsOnlyTheSubtreeRoot()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.None, "Child");

        _changesList.Clear();
        _scene.DeleteElement(parent);

        SceneChange change = Assert.Single(_changesList);

        Assert.Equal(SceneChangeKind.ElementRemoved, change.Kind);
        Assert.Equal(parent, change.ElementId);
        Assert.DoesNotContain(_changesList, c => c.ElementId == child);
    }

    [Fact]
    public void ReparentReportsTheMoveAndTheResultingTransform()
    {
        Guid target = _scene.CreateElement(_scene.Id, ComponentKind.None, "Target");
        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");

        _changesList.Clear();
        _scene.ReparentElement(moving, target, 0);

        Assert.Contains(
            _changesList,
            c => c.Kind == SceneChangeKind.ElementMoved
                && c.ElementId == moving
                && c.ParentId == target
                && c.Index == 0);

        Assert.Contains(
            _changesList,
            c => c.Kind == SceneChangeKind.TransformChanged && c.ElementId == moving);
    }

    [Fact]
    public void RenameIsReported()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Before");

        _changesList.Clear();
        _scene.RenameElement(element, "After");

        Assert.Equal(SceneChangeKind.ElementRenamed, Assert.Single(_changesList).Kind);
    }

    [Fact]
    public void TransformAndModeChangesAreReported()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        _changesList.Clear();
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(1f, 2f, 3f));
        _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Float);

        Assert.Equal(2, _changesList.Count);
        Assert.All(_changesList, c => Assert.Equal(SceneChangeKind.TransformChanged, c.Kind));
    }

    [Fact]
    public void RefusedOperationsAnnounceNothing()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        _changesList.Clear();

        Assert.Throws<ArgumentException>(() =>
            _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Snap90));
        Assert.Throws<InvalidOperationException>(() =>
            _scene.ReparentElement(element, element, 0));

        Assert.Empty(_changesList);
    }
}
