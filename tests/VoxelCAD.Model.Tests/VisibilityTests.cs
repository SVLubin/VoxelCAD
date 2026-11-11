using System;
using System.IO;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Hiding an element: a flag every element has, undoable like everything else,
/// and its own rather than its subtree's.
/// </summary>
public sealed class VisibilityTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public VisibilityTests()
    {
        _scene = _project.Scenes[0];
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void AnElementStartsShown()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.True(_scene.Element(element).Visible);
    }

    /// <summary>
    /// Every element has one, not only a voxel model: a group is what a whole
    /// assembly is hidden by.
    /// </summary>
    [Fact]
    public void EveryKindOfElementCanBeHidden()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        _scene.SetVisible(group, false);
        _scene.SetVisible(model, false);

        Assert.False(_scene.Element(group).Visible);
        Assert.False(_scene.Element(model).Visible);
    }

    [Fact]
    public void HidingIsUndoneAndRedone()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        _scene.SetVisible(element, false);
        _project.History.Undo();

        Assert.True(_scene.Element(element).Visible);

        _project.History.Redo();

        Assert.False(_scene.Element(element).Visible);
    }

    /// <summary>
    /// The bargain every other edit makes: a value equal to the current one is
    /// not an action, so clicking an eye twice leaves two records and holding
    /// still leaves none.
    /// </summary>
    [Fact]
    public void SettingTheStateItIsAlreadyInRecordsNothing()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        int before = _project.History.Records.Count;

        _scene.SetVisible(element, visible: true);

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void HidingIsOneRecordAgainstTheElement()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        _scene.SetVisible(element, false);

        HistoryRecord record = _project.History.Records[^1];

        Assert.Equal(ActionKind.ChangeVisibility, record.Kind);
        Assert.Equal(element, record.TargetId);
        Assert.Equal("Group", record.TargetName);
    }

    [Fact]
    public void HidingReportsTheChange()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        SceneChange? seen = null;

        _scene.Changed += change =>
        {
            if (change.Kind == SceneChangeKind.VisibilityChanged)
                seen = change;
        };

        _scene.SetVisible(element, false);

        Assert.NotNull(seen);
        Assert.Equal(element, seen!.Value.ElementId);
    }

    /// <summary>
    /// The flag is the element's own. What is under a hidden group goes with it
    /// wherever visibility is composed — the viewport, the exporter — and the
    /// child's own answer never changes, so showing the group again restores
    /// exactly what was there.
    /// </summary>
    [Fact]
    public void HidingAParentLeavesTheChildsOwnFlagAlone()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.VoxelModel, "Child");

        _scene.SetVisible(parent, false);

        Assert.True(_scene.Element(child).Visible);
    }

    [Fact]
    public void HidingSurvivesTheRoundTrip()
    {
        Guid shown = _scene.CreateElement(_scene.Id, ComponentKind.None, "Shown");
        Guid hidden = _scene.CreateElement(_scene.Id, ComponentKind.None, "Hidden");

        _scene.SetVisible(hidden, false);
        _project.Save(_path);

        IProject loaded = ModelFactory.LoadProject(_path);

        Assert.True(loaded.Scenes[0].Element(shown).Visible);
        Assert.False(loaded.Scenes[0].Element(hidden).Visible);
    }

    /// <summary>
    /// The state is written as the exception rather than as a field, so a project
    /// where nothing is hidden reads back the same as one saved before hiding
    /// existed — and its elements carry no entry at all.
    /// </summary>
    [Fact]
    public void AShownElementCarriesNothingInTheFile()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        _project.Save(_path);

        Assert.DoesNotContain(
            "hidden", File.ReadAllText(_path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file from before hiding existed says nothing about it, and everything in
    /// it is shown — the reading that keeps the omission above lossless.
    /// </summary>
    [Fact]
    public void AFileWithoutTheFlagLoadsEverythingShown()
    {
        File.WriteAllText(
            _path,
            "version: 2\n"
            + "materials:\n"
            + "- id: 11111111-1111-1111-1111-111111111111\n"
            + "  name: Default\n"
            + "  color:\n"
            + "  - 0.5\n"
            + "  - 0.5\n"
            + "  - 0.5\n"
            + "  - 1\n"
            + "scenes:\n"
            + "- id: 22222222-2222-2222-2222-222222222222\n"
            + "  name: Scene\n"
            + "  size:\n"
            + "  - 32\n"
            + "  - 32\n"
            + "  - 32\n"
            + "  elements:\n"
            + "  - id: 33333333-3333-3333-3333-333333333333\n"
            + "    name: Group\n"
            + "    component: None\n"
            + "    positionMode: Integer\n"
            + "    rotationMode: Snap90\n"
            + "    scaleMode: Integer\n");

        IProject loaded = ModelFactory.LoadProject(_path);

        Assert.True(loaded.Scenes[0].Children[0].Visible);
    }
}
