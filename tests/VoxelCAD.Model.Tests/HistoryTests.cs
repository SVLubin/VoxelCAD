using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>Undo/redo: identity, exact restoration, and what does and does not get recorded.</summary>
public sealed class HistoryTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public HistoryTests()
    {
        _scene = _project.Scenes[0];
    }

    private IHistory History => _project.History;

    [Fact]
    public void NewSceneHasNothingToUndo()
    {
        Assert.Empty(History.Records);
        Assert.Equal(-1, History.CurrentIndex);
        Assert.False(History.CanUndo);
        Assert.False(History.CanRedo);
    }

    [Fact]
    public void UndoRemovesACreatedElement()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        History.Undo();

        Assert.Empty(_scene.Children);
        Assert.False(_scene.TryGetElement(element, out _));
    }

    [Fact]
    public void RedoRestoresTheSameIdentity()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        History.Undo();
        History.Redo();

        Assert.True(_scene.TryGetElement(element, out IHierarchyElement restored));
        Assert.Equal(element, restored.Id);
    }

    [Fact]
    public void UndoOfADeleteRestoresTheWholeSubtreeWithItsIds()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.None, "Child");
        Guid grandchild = _scene.CreateElement(child, ComponentKind.None, "Grandchild");

        _scene.DeleteElement(parent);
        History.Undo();

        Assert.True(_scene.TryGetElement(child, out _));
        Assert.True(_scene.TryGetElement(grandchild, out _));
        Assert.Equal(child, _scene.Element(parent).Children[0].Id);
    }

    [Fact]
    public void UndoOfADeleteRestoresThePositionAmongSiblings()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        Guid middle = _scene.CreateElement(_scene.Id, ComponentKind.None, "Middle");
        Guid last = _scene.CreateElement(_scene.Id, ComponentKind.None, "Last");

        _scene.DeleteElement(middle);
        History.Undo();

        Assert.Equal(
            new[] { first, middle, last },
            _scene.Children.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void UndoRestoresAPreviousTransformValue()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(1f, 2f, 3f));
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(9f, 9f, 9f));

        History.Undo();

        Assert.Equal(new Vector3(1f, 2f, 3f), _scene.Element(element).Transform.Position);
    }

    [Fact]
    public void UndoOfAModeChangeGivesBackTheLostPrecision()
    {
        // The whole reason the model may destroy the fraction when a mode changes:
        // it is recoverable here, so there is never a second stored truth.
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Float);
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(10.7f, 0f, 0f));
        _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Integer);

        Assert.Equal(11f, _scene.Element(element).Transform.Position.X);

        History.Undo();

        Assert.Equal(10.7f, _scene.Element(element).Transform.Position.X);
        Assert.Equal(FieldMode.Float, _scene.Modes(element).Position);
    }

    [Fact]
    public void UndoOfAReparentRestoresParentTransformAndMode()
    {
        Guid rotated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rotated");
        _scene.SetFieldMode(rotated, TransformChannel.Rotation, FieldMode.Float);
        _scene.SetTransform(rotated, TransformChannel.Rotation, new Vector3(0f, 37f, 0f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetFieldMode(moving, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 0f, -3f));

        _scene.ReparentElement(moving, rotated, 0);
        Assert.Equal(FieldMode.Float, _scene.Modes(moving).Position);

        History.Undo();

        Assert.Equal(_scene.Id, _scene.Element(moving).Parent.Id);
        Assert.Equal(FieldMode.Integer, _scene.Modes(moving).Position);
        Assert.Equal(new Vector3(11f, 0f, -3f), _scene.Element(moving).Transform.Position);
    }

    [Fact]
    public void RepeatedUndoRedoOfAReparentDoesNotDrift()
    {
        Guid rotated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rotated");
        _scene.SetFieldMode(rotated, TransformChannel.Rotation, FieldMode.Float);
        _scene.SetTransform(rotated, TransformChannel.Rotation, new Vector3(0f, 37f, 0f));
        _scene.SetTransform(rotated, TransformChannel.Position, new Vector3(5f, 1f, -2f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 2f, -3f));

        Transform3D expected = _scene.WorldTransform(moving);
        _scene.ReparentElement(moving, rotated, 0);

        for (var i = 0; i < 20; i++)
        {
            History.Undo();
            History.Redo();
        }

        Assert.True(expected.IsEqualApprox(_scene.WorldTransform(moving)));
    }

    [Fact]
    public void UndoAnnouncesTheSameChangesAsTheOriginalEdit()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        var changesList = new List<SceneChange>();
        _scene.Changed += changesList.Add;

        History.Undo();
        Assert.Equal(SceneChangeKind.ElementRemoved, Assert.Single(changesList).Kind);

        changesList.Clear();
        History.Redo();
        Assert.Equal(SceneChangeKind.ElementAdded, Assert.Single(changesList).Kind);
    }

    [Fact]
    public void NoOpEditsAreNotRecorded()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(1f, 0f, 0f));

        var before = History.Records.Count;

        _scene.RenameElement(element, "Element");
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(1f, 0f, 0f));
        _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Integer);

        Assert.Equal(before, History.Records.Count);
    }

    [Fact]
    public void QuantisedNoOpEditsAreNotRecorded()
    {
        // Dragging within one grid cell must not fill the history with identical steps.
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(11f, 0f, 0f));

        var before = History.Records.Count;
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(11.2f, 0f, 0f));

        Assert.Equal(before, History.Records.Count);
    }

    [Fact]
    public void RefusedEditsAreNotRecorded()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        var before = History.Records.Count;

        Assert.Throws<ArgumentException>(() =>
            _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Snap90));
        Assert.Throws<InvalidOperationException>(() =>
            _scene.ReparentElement(element, element, 0));

        Assert.Equal(before, History.Records.Count);
    }

    [Fact]
    public void NewWorkDiscardsTheUndoneTail()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");

        History.Undo();
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Third");

        Assert.False(History.CanRedo);
        Assert.Equal(2, History.Records.Count);
        Assert.Equal(new[] { first }, new[] { _scene.Children[0].Id });
    }

    [Fact]
    public void UndoToWalksInBothDirections()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Third");

        History.UndoTo(-1);
        Assert.Empty(_scene.Children);

        History.UndoTo(2);
        Assert.Equal(3, _scene.Children.Count);

        History.UndoTo(0);
        Assert.Single(_scene.Children);
    }

    [Fact]
    public void RecordsDescribeWhatHappened()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Cube");
        _scene.SetTransform(element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));
        _scene.DeleteElement(element);

        Assert.Equal(
            new[] { ActionKind.CreateElement, ActionKind.RotateElement, ActionKind.DeleteElement },
            History.Records.Select(r => r.Kind).ToArray());

        Assert.All(History.Records, r => Assert.Equal(element, r.TargetId));
    }

    [Fact]
    public void ADeleteRecordKeepsTheNameOfAnElementThatNoLongerExists()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Cube");
        _scene.DeleteElement(element);

        Assert.False(_scene.TryGetElement(element, out _));
        Assert.Equal("Cube", History.Records[^1].TargetName);
    }

    [Fact]
    public void RecordsHaveDistinctIdentity()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.RenameElement(element, "Renamed");

        Assert.Equal(2, History.Records.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void HistoryAnnouncesEveryChange()
    {
        var notifications = 0;
        History.Changed += _ => notifications++;

        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.RenameElement(element, "Renamed");
        History.Undo();
        History.Redo();

        Assert.Equal(4, notifications);
    }

    [Fact]
    public void OldRecordsAreDroppedOnceTheLimitIsReached()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        for (var i = 0; i < 250; i++)
            _scene.RenameElement(element, $"Name {i}");

        Assert.Equal(200, History.Records.Count);
        Assert.Equal(199, History.CurrentIndex);
        Assert.True(History.CanUndo);
        Assert.Equal("Name 249", _scene.Element(element).Name);
    }
}
