using System;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Every record remembers who caused it, so the panel can show the user which
/// steps were theirs and which an agent's.
/// </summary>
public sealed class HistoryAuthorTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public HistoryAuthorTests()
    {
        _scene = _project.Scenes[0];
    }

    private IHistory History => _project.History;

    [Fact]
    public void RecordsAreTheUsersByDefault()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.Equal(ActorKind.User, History.Records[0].Actor.Kind);
    }

    [Fact]
    public void RecordsMadeWhileAnAgentHoldsControlAreAttributedToIt()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Mine");

        History.CurrentActor = new HistoryActor(ActorKind.Agent, "Claude");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Theirs");
        History.CurrentActor = default;

        _scene.CreateElement(_scene.Id, ComponentKind.None, "MineAgain");

        Assert.Equal(
            new[] { ActorKind.User, ActorKind.Agent, ActorKind.User },
            History.Records.Select(record => record.Actor.Kind).ToArray());
        Assert.Equal("Claude", History.Records[1].Actor.Name);
    }

    [Fact]
    public void AttributionStaysWithTheRecordAcrossUndoAndRedo()
    {
        History.CurrentActor = new HistoryActor(ActorKind.Agent, "Claude");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Theirs");
        History.CurrentActor = default;

        History.Undo();
        History.Redo();

        Assert.Equal(ActorKind.Agent, History.Records[0].Actor.Kind);
    }

    [Fact]
    public void ChangingTheCurrentActorDoesNotTouchExistingRecords()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        History.CurrentActor = new HistoryActor(ActorKind.Agent, "Claude");

        Assert.Equal(ActorKind.User, History.Records[0].Actor.Kind);
    }
}
