using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A gesture is one undo step. Without batching, a single mouse drag would bury
/// everything before it under hundreds of records.
/// </summary>
public sealed class HistoryBatchTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;

    public HistoryBatchTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
    }

    private IHistory History => _project.History;

    [Fact]
    public void ABatchCollapsesToASingleRecord()
    {
        var before = History.Records.Count;

        using (History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element"))
        {
            for (var i = 1; i <= 50; i++)
                _scene.SetTransform(_element, TransformChannel.Position, new Vector3(i, 0f, 0f));
        }

        Assert.Equal(before + 1, History.Records.Count);
        Assert.Equal(ActionKind.MoveElement, History.Records[^1].Kind);
    }

    [Fact]
    public void UndoingABatchRewindsTheWholeGesture()
    {
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(1f, 0f, 0f));

        using (History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element"))
        {
            for (var i = 2; i <= 20; i++)
                _scene.SetTransform(_element, TransformChannel.Position, new Vector3(i, 0f, 0f));
        }

        History.Undo();

        Assert.Equal(new Vector3(1f, 0f, 0f), _scene.Element(_element).Transform.Position);
    }

    [Fact]
    public void RedoingABatchReplaysTheWholeGesture()
    {
        using (History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element"))
        {
            _scene.SetTransform(_element, TransformChannel.Position, new Vector3(5f, 0f, 0f));
            _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));
        }

        History.Undo();
        History.Redo();

        Assert.Equal(new Vector3(5f, 0f, 0f), _scene.Element(_element).Transform.Position);
        Assert.Equal(new Vector3(0f, 90f, 0f), _scene.Element(_element).Transform.RotationDegrees);
    }

    [Fact]
    public void ABatchThatChangedNothingLeavesNoTrace()
    {
        var before = History.Records.Count;

        using (History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element"))
        {
            // A drag that never left the starting cell.
            _scene.SetTransform(_element, TransformChannel.Position, Vector3.Zero);
        }

        Assert.Equal(before, History.Records.Count);
    }

    [Fact]
    public void ABatchMixesActionsUnderOneLabel()
    {
        using (History.CreateBatch(ActionKind.ReparentElement, _scene.Id, _element, "Element"))
        {
            _scene.RenameElement(_element, "Renamed");
            _scene.SetTransform(_element, TransformChannel.Position, new Vector3(3f, 0f, 0f));
        }

        Assert.Equal(ActionKind.ReparentElement, History.Records[^1].Kind);

        History.Undo();

        Assert.Equal("Element", _scene.Element(_element).Name);
        Assert.Equal(Vector3.Zero, _scene.Element(_element).Transform.Position);
    }

    [Fact]
    public void NestedBatchesAreRefused()
    {
        using IDisposable outer = History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element");

        Assert.Throws<InvalidOperationException>(() =>
            History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element"));
    }

    [Fact]
    public void WorkAfterABatchIsRecordedNormally()
    {
        using (History.CreateBatch(ActionKind.MoveElement, _scene.Id, _element, "Element"))
            _scene.SetTransform(_element, TransformChannel.Position, new Vector3(1f, 0f, 0f));

        var afterBatch = History.Records.Count;
        _scene.RenameElement(_element, "Renamed");

        Assert.Equal(afterBatch + 1, History.Records.Count);
        Assert.Equal(ActionKind.RenameElement, History.Records[^1].Kind);
    }
}
