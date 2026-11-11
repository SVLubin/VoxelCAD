using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What the panels do with a reference: the viewport draws one so it can be traced
/// over, the inspector says which file it is and whether that file is there, and
/// one switch takes them all off screen without touching the document.
/// </summary>
public sealed class ReferencePanelTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly FakeSceneView _view = new();
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakePreferences _preferences = new();
    private readonly EditorSession _session = new();
    private readonly DocumentPresenter _document;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-{Guid.NewGuid():N}");

    public ReferencePanelTests()
    {
        _scene = _project.Scenes[0];
        _session.CurrentMaterialId = _project.Materials[0].Id;

        Directory.CreateDirectory(_directory);

        _document = new DocumentPresenter(
            _project,
            _scene,
            _hierarchy,
            _inspector,
            _history,
            _view,
            _session,
            new FakeDialogService(),
            _preferences,
            _ => { });
    }

    public void Dispose()
    {
        _document.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // ---- the viewport ----

    /// <summary>
    /// Null is a fact rather than an absence: an element the view has never been
    /// told about would otherwise keep whatever it was last told, and a model that
    /// used to be a picture would go on being drawn as one.
    /// </summary>
    [Fact]
    public void APictureIsPushedAndAnOrdinaryElementIsPushedAsNothing()
    {
        Guid picture = Import(Picture("plan.png", 32, 16));
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Assert.NotNull(_view.ReferenceImages[picture]);
        Assert.Null(_view.ReferenceModels[picture]);

        Assert.Null(_view.ReferenceImages[model]);
        Assert.Null(_view.ReferenceModels[model]);
    }

    [Fact]
    public void AnEditReachesTheViewport()
    {
        Guid picture = Import(Picture("plan.png", 32, 16));

        _scene.SetReferenceImage(picture, Image(picture) with { Opacity = 0.25f });

        Assert.Equal(0.25f, _view.ReferenceImages[picture]!.Value.Opacity);
    }

    [Fact]
    public void AModelIsPushedOnItsOwnChannel()
    {
        Guid model = Import(Touch("bust.glb"));

        Assert.Null(_view.ReferenceImages[model]);
        Assert.NotNull(_view.ReferenceModels[model]);
    }

    // ---- the whole-scene switch ----

    /// <summary>
    /// It is for looking at the model clean for a moment, so it touches no
    /// element's own flag and leaves no record — putting it back shows exactly what
    /// was shown before.
    /// </summary>
    [Fact]
    public void TheSwitchReachesTheViewportAndNothingElse()
    {
        Guid picture = Import(Picture("plan.png", 8, 8));
        int records = _project.History.Records.Count;

        _session.ReferencesVisible = false;

        Assert.False(_view.ReferencesVisible);
        _scene.TryGetElement(picture, out IHierarchyElement element);

        Assert.True(element.Visible);
        Assert.Equal(records, _project.History.Records.Count);

        _session.ReferencesVisible = true;

        Assert.True(_view.ReferencesVisible);
    }

    /// <summary>
    /// A scene opened with the switch already off must not draw its references for
    /// a frame first, so the push happens as the presenter is built.
    /// </summary>
    [Fact]
    public void AFreshViewportIsToldWhereTheSwitchStands()
    {
        _session.ReferencesVisible = false;

        var second = new FakeSceneView();

        using var document = new DocumentPresenter(
            _project,
            _scene,
            _hierarchy,
            _inspector,
            _history,
            second,
            _session,
            new FakeDialogService(),
            _preferences,
            _ => { });

        Assert.False(second.ReferencesVisible);
    }

    // ---- the inspector ----

    [Fact]
    public void TheSectionArrivesForAReferenceAndForNothingElse()
    {
        Guid picture = Import(Picture("plan.png", 40, 20));

        Select(picture);

        Assert.NotNull(_inspector.ReferenceImage);
        Assert.Null(_inspector.ReferenceModel);
        Assert.Equal(new Vector2(40f, 20f), _inspector.ReferenceImage!.Value.Settings.SizeInCells);

        Select(_scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model"));

        Assert.Null(_inspector.ReferenceImage);
    }

    /// <summary>
    /// The first thing anybody looking at a blank reference needs to be told, and
    /// the reason the row costs a look at the disk.
    /// </summary>
    [Fact]
    public void ThePanelIsToldWhetherTheFileIsThere()
    {
        Select(Import(Picture("plan.png", 8, 8)));

        Assert.True(_inspector.ReferenceImage!.Value.Found);

        Select(Import(Path.Combine(_directory, "absent.png")));

        Assert.False(_inspector.ReferenceImage!.Value.Found);
    }

    [Fact]
    public void AnEditFromThePanelIsOneRecord()
    {
        Guid picture = Import(Picture("plan.png", 8, 8));

        Select(picture);

        int records = _project.History.Records.Count;

        _inspector.RaiseReferenceImageEdited(Image(picture) with { Filtered = true });

        Assert.True(Image(picture).Filtered);
        Assert.Equal(records + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.ChangeReference, _project.History.Records[^1].Kind);
    }

    [Fact]
    public void AModelEditFromThePanelReachesTheModel()
    {
        Guid model = Import(Touch("bust.glb"));

        Select(model);

        Assert.NotNull(_inspector.ReferenceModel);

        _inspector.RaiseReferenceModelEdited(Model(model) with { Wireframe = true });

        Assert.True(Model(model).Wireframe);
    }

    private Guid Import(string path) => _scene.CreateReference(_scene.Id, "Reference", path);

    private void Select(Guid id) =>
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, id));

    private ReferenceImageSettings Image(Guid id)
    {
        _scene.TryGetElement(id, out IHierarchyElement element);

        return ((IReferenceImage)element.Component!).Settings;
    }

    private ReferenceModelSettings Model(Guid id)
    {
        _scene.TryGetElement(id, out IHierarchyElement element);

        return ((IReferenceModel)element.Component!).Settings;
    }

    private string Picture(string name, int width, int height)
    {
        string path = Path.Combine(_directory, name);
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        bytes.AddRange(new byte[] { 0, 0, 0, 13 });
        bytes.AddRange(new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        bytes.AddRange(new[]
        {
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width
        });
        bytes.AddRange(new[]
        {
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height
        });
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });

        File.WriteAllBytes(path, bytes.ToArray());

        return path;
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });

        return path;
    }
}
