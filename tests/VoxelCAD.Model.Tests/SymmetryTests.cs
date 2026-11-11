using System;
using System.IO;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Mirror-plane slots are voxel-component data: three fixed slots, changed only
/// through the scene so the change is undoable, and never touching a cell —
/// symmetry modifies future edits, not what was built.
/// </summary>
public sealed class SymmetryTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public SymmetryTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    private IHistory History => _project.History;

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void ANewModelHasEverySlotOffAtZero()
    {
        Assert.Equal(default, Model().Symmetry);
        Assert.False(Model().Symmetry.X.Enabled);
        Assert.Equal(0, Model().Symmetry.X.PlaneDoubled);
    }

    [Fact]
    public void SettingSymmetryStoresTheSlots()
    {
        var values = new SymmetryValues(
            new SymmetryPlane(true, 7),
            default,
            new SymmetryPlane(false, -2));

        _scene.SetSymmetry(_element, values);

        Assert.Equal(values, Model().Symmetry);
    }

    [Fact]
    public void SettingSymmetryRecordsOneUndoableAction()
    {
        int recordsBefore = History.Records.Count;

        _scene.SetSymmetry(_element, WithMirrorX(6));

        Assert.Equal(recordsBefore + 1, History.Records.Count);
        Assert.Equal(ActionKind.ChangeSymmetry, History.Records[^1].Kind);

        History.Undo();
        Assert.Equal(default(SymmetryValues), Model().Symmetry);

        History.Redo();
        Assert.Equal(WithMirrorX(6), Model().Symmetry);
    }

    [Fact]
    public void SettingTheCurrentValueRecordsNothing()
    {
        _scene.SetSymmetry(_element, WithMirrorX(6));
        int recordsBefore = History.Records.Count;

        _scene.SetSymmetry(_element, WithMirrorX(6));

        Assert.Equal(recordsBefore, History.Records.Count);
    }

    [Fact]
    public void SettingSymmetryRaisesSymmetryChangedAndTouchesNoCells()
    {
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Godot.Vector3I(1, 0, 0), _project.Materials[0].Id)
        });

        SceneChange? observed = null;
        _scene.Changed += change => observed = change;

        _scene.SetSymmetry(_element, WithMirrorX(6));

        Assert.Equal(SceneChangeKind.SymmetryChanged, observed?.Kind);
        Assert.Equal(_element, observed?.ElementId);
        Assert.Single(Model().Voxels);
    }

    [Fact]
    public void AGroupElementRefusesSymmetry()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.Throws<InvalidOperationException>(() => _scene.SetSymmetry(group, WithMirrorX(6)));
    }

    /// <summary>
    /// A disabled slot keeps its position through the round trip — switching the
    /// plane back on after a reload finds it where it was left.
    /// </summary>
    [Fact]
    public void SlotsSurviveTheRoundTrip()
    {
        var values = new SymmetryValues(
            new SymmetryPlane(true, 7),
            new SymmetryPlane(false, -4),
            default);

        _scene.SetSymmetry(_element, values);

        IVoxelModel loaded = LoadedModel();

        Assert.Equal(values, loaded.Symmetry);
    }

    [Fact]
    public void DefaultSlotsWriteNothingAndReadBackAsDefault()
    {
        _project.Save(_path);

        Assert.DoesNotContain("symmetry", File.ReadAllText(_path), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(default(SymmetryValues), LoadedModel().Symmetry);
    }

    private static SymmetryValues WithMirrorX(int planeDoubled) =>
        new(new SymmetryPlane(true, planeDoubled), default, default);

    private IVoxelModel Model() => (IVoxelModel)_scene.Element(_element).Component!;

    private IVoxelModel LoadedModel()
    {
        _project.Save(_path);
        IProject loaded = ModelFactory.LoadProject(_path);

        return (IVoxelModel)loaded.Scenes[0].Element(_element).Component!;
    }
}
