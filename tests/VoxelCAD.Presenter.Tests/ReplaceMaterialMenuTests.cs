using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// "Replace uses…" as the shell offers it: the picker is fed the project's
/// materials, the item is live only on a material some cell of the scene being
/// worked on wears, with another material to give it, and the answer is one
/// record over that scene and no other. The document is built through the agent
/// surface, which is the one route a shell test has into the model.
/// </summary>
public sealed class ReplaceMaterialMenuTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;
    private readonly ICommandDispatcher _agent;

    public ReplaceMaterialMenuTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
        _agent = _shell.CreateAgentDispatchers().ForClient("menu-tests");
        _shell.Tick(0.0);
    }

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void ThePickerIsFedTheProjectsMaterialsAsTheyChange()
    {
        TakeControl();

        Assert.Equal(new[] { "Default" }, _view.AskMaterialChoices().Select(choice => choice.Name));

        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);

        Assert.Contains(_view.AskMaterialChoices(), choice => choice.Id == red && choice.Name == "Red");

        Ok("rename-material", ("material", red.ToString()), ("name", "Crimson"));

        Assert.Contains(
            _view.AskMaterialChoices(), choice => choice.Id == red && choice.Name == "Crimson");
    }

    [Fact]
    public void TheItemIsLiveOnlyForAWornMaterialWithAnAlternative()
    {
        TakeControl();
        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);
        Ok("create-element", ("name", "Model"), ("kind", "VoxelModel"));
        ReleaseControl();

        var row = new SelectionTarget(SelectionKind.Material, red);

        Assert.False(_view.AskCanReplaceMaterialUses(row));

        TakeControl();
        Ok("fill-box", ("element", "Model"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        ReleaseControl();

        Assert.True(_view.AskCanReplaceMaterialUses(row));

        // The row the menu was opened on decides it, not the selection: the
        // question is asked with a target, so a stale selection cannot answer for
        // a different row.
        Assert.False(_view.AskCanReplaceMaterialUses(
            _view.HierarchyView.Items.Keys.First(target => target.Kind == SelectionKind.Element)));
    }

    [Fact]
    public void AnsweringThePickerReplacesAcrossTheSceneAsOneRecord()
    {
        TakeControl();
        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);
        Guid blue = Guid.Parse((string)Ok("create-material", ("name", "Blue"), ("color", "0,0,1"))["id"]!);
        Ok("create-element", ("name", "A"), ("kind", "VoxelModel"));
        Ok("create-element", ("name", "B"), ("kind", "VoxelModel"));
        Ok("fill-box", ("element", "A"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        Ok("fill-box", ("element", "B"), ("from", "4,0,0"), ("to", "4,0,0"), ("material", "Red"));
        ReleaseControl();
        int before = _view.HistoryView.Labels.Count;

        _view.RaiseReplaceMaterialUses(red, blue);

        Assert.Equal(before + 1, _view.HistoryView.Labels.Count);
        Assert.Equal("Replace uses of Red", _view.HistoryView.Labels[^1]);
        Assert.False(InUse(red));

        _view.RaiseUndo();

        Assert.True(InUse(red));
    }

    /// <summary>
    /// The menu's reach is the scene being worked on. Which needs a second scene
    /// to say at all: with one, a walk over the whole project passes every other
    /// test here exactly as a walk over the scene does.
    /// </summary>
    [Fact]
    public void TheMenuStopsAtTheSceneBeingWorkedOn()
    {
        TakeControl();
        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);
        Guid blue = Guid.Parse((string)Ok("create-material", ("name", "Blue"), ("color", "0,0,1"))["id"]!);
        string first = ActiveSceneName();
        Ok("create-element", ("name", "A"), ("kind", "VoxelModel"));
        Ok("fill-box", ("element", "A"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        Ok("create-scene", ("name", "Other"));
        Ok("create-element", ("name", "C"), ("kind", "VoxelModel"));
        Ok("fill-box", ("element", "C"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        Ok("activate-scene", ("scene", first));
        ReleaseControl();
        int before = _view.HistoryView.Labels.Count;

        _view.RaiseReplaceMaterialUses(red, blue);

        Assert.Equal(before + 1, _view.HistoryView.Labels.Count);
        Assert.Equal(new[] { "B" }, Row("A"));

        // The other scene's cell still wears it, so the material is still in use.
        Assert.True(InUse(red));

        TakeControl();
        Ok("activate-scene", ("scene", "Other"));
        ReleaseControl();

        Assert.Equal(new[] { "R" }, Row("C"));
    }

    /// <summary>
    /// And the item follows the same reach: a material worn only in another scene
    /// has nothing to replace here, so the menu does not offer a press that would
    /// do nothing.
    /// </summary>
    [Fact]
    public void TheItemIsNotLiveForAMaterialWornOnlyInAnotherScene()
    {
        TakeControl();
        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);
        string first = ActiveSceneName();
        Ok("create-scene", ("name", "Other"));
        Ok("create-element", ("name", "C"), ("kind", "VoxelModel"));
        Ok("fill-box", ("element", "C"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        ReleaseControl();

        var row = new SelectionTarget(SelectionKind.Material, red);

        Assert.True(_view.AskCanReplaceMaterialUses(row));

        TakeControl();
        Ok("activate-scene", ("scene", first));
        ReleaseControl();

        Assert.False(_view.AskCanReplaceMaterialUses(row));
    }

    /// <summary>
    /// A prefab instance's contents are derived and read-only, and a replace over
    /// the scene passes them over — so the item must too, or it would offer a
    /// press that records nothing. The two walks are held to one answer here.
    /// </summary>
    [Fact]
    public void AMaterialWornOnlyByInstanceContentsIsNotOfferedAndNothingIsWritten()
    {
        TakeControl();
        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);
        Guid blue = Guid.Parse((string)Ok("create-material", ("name", "Blue"), ("color", "0,0,1"))["id"]!);
        Ok("create-element", ("name", "A"), ("kind", "VoxelModel"));
        Ok("fill-box", ("element", "A"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        Ok("create-prefab", ("element", "A"));
        ReleaseControl();
        int before = _view.HistoryView.Labels.Count;

        Assert.False(_view.AskCanReplaceMaterialUses(new SelectionTarget(SelectionKind.Material, red)));

        _view.RaiseReplaceMaterialUses(red, blue);

        Assert.Equal(before, _view.HistoryView.Labels.Count);
        Assert.True(InUse(red));
    }

    [Fact]
    public void AnUnknownTargetOrTheSameMaterialDoesNothing()
    {
        TakeControl();
        Guid red = Guid.Parse((string)Ok("create-material", ("name", "Red"), ("color", "1,0,0"))["id"]!);
        Ok("create-element", ("name", "A"), ("kind", "VoxelModel"));
        Ok("fill-box", ("element", "A"), ("from", "0,0,0"), ("to", "0,0,0"), ("material", "Red"));
        ReleaseControl();
        int before = _view.HistoryView.Labels.Count;

        _view.RaiseReplaceMaterialUses(red, Guid.NewGuid());
        _view.RaiseReplaceMaterialUses(red, red);

        Assert.Equal(before, _view.HistoryView.Labels.Count);
    }

    private bool InUse(Guid materialId)
    {
        // The list answer: 'materials' reports the registry as a list rather than
        // as an object.
        CommandResult result = _agent.Execute("materials", new Dictionary<string, string>());
        Assert.True(result.Ok, result.Error);

        return ((List<object?>)result.Value!)
            .Cast<IReadOnlyDictionary<string, object?>>()
            .Single(material => (string)material["id"]! == materialId.ToString())["inUse"] is true;
    }

    private string ActiveSceneName()
    {
        CommandResult result = _agent.Execute("scenes", new Dictionary<string, string>());
        Assert.True(result.Ok, result.Error);

        return (string)((List<object?>)result.Value!)
            .Cast<IReadOnlyDictionary<string, object?>>()
            .Single(scene => scene["active"] is true)["name"]!;
    }

    /// <summary>The model's one layer, as 'slice' draws it.</summary>
    private string[] Row(string element)
    {
        CommandResult result = _agent.Execute(
            "slice",
            new Dictionary<string, string>
            {
                ["element"] = element, ["axis"] = "y", ["index"] = "0"
            });

        Assert.True(result.Ok, result.Error);

        var slice = (IReadOnlyDictionary<string, object?>)result.Value!;

        return ((List<object?>)slice["grid"]!).Cast<string>().ToArray();
    }

    private void TakeControl()
    {
        _agent.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        _shell.Tick(10.0);
    }

    private void ReleaseControl() =>
        _agent.Execute("release-control", new Dictionary<string, string>());

    private IReadOnlyDictionary<string, object?> Ok(
        string name,
        params (string Key, string Value)[] arguments)
    {
        CommandResult result = _agent.Execute(
            name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value));

        Assert.True(result.Ok, result.Error);

        return (IReadOnlyDictionary<string, object?>)result.Value!;
    }
}
