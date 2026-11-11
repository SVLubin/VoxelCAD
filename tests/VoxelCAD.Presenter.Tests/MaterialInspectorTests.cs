using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The material section: what it shows for a selected material, what a preset
/// does, what a tuning row does, and which of those the document hears about.
/// </summary>
public sealed class MaterialInspectorTests : IDisposable
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly FakePreferences _preferences = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _material;
    private DocumentPresenter? _document;

    public MaterialInspectorTests()
    {
        _scene = _project.Scenes[0];
        _material = _project.CreateMaterial("Wall", Colors.White);
    }

    public void Dispose() => _document?.Dispose();

    /// <summary>
    /// Both lists are filled before anything is selected: the words are the
    /// presenter's, and a panel that had to wait for a material to learn them
    /// would show an empty chooser the first time one was clicked.
    /// </summary>
    [Fact]
    public void TheListsArriveWithThePanel()
    {
        Build();

        Assert.Equal(MaterialPresetTable.All.Count, _inspector.PresetOptions.Count);

        // Every finish is listed, and every one but Standard may be picked:
        // Standard is what a shipped material wears, not something anybody
        // chooses — see InspectorPresenter.
        Assert.Equal(
            Enum.GetValues<MaterialFinish>(),
            _inspector.FinishOptions.Select(option => option.Finish));

        Assert.All(
            _inspector.FinishOptions,
            option => Assert.Equal(option.Finish != MaterialFinish.Standard, option.Choosable));

        // Composed here rather than stored with the values.
        Assert.Contains(_inspector.PresetOptions, option => option.Label == "Brushed steel");
        Assert.Contains(_inspector.FinishOptions, option => option.Label == "Herringbone");
    }

    /// <summary>
    /// A material of one's own can wear Standard — a duplicate of a shipped one
    /// keeps it, and the command surface accepts it — so the chooser has to be
    /// able to say so. With no row for it the panel kept showing the finish of
    /// whichever material had been selected before, and only that is tested here:
    /// that there is a row to show, which may not be picked.
    /// </summary>
    [Fact]
    public void AMaterialWearingStandardHasARowToShowItOn()
    {
        Guid brick = _project.CreateMaterial("Brick wall", Colors.White);
        _project.SetMaterialPreset(brick, MaterialPreset.Brick);
        IMaterial wall = _project.Materials.First(material => material.Id == _material);
        _project.SetMaterialAppearance(
            _material, wall.Appearance with { Finish = MaterialFinish.Standard });
        Build();

        Select(brick);
        Assert.Equal(MaterialFinish.Brick, _inspector.MaterialAppearance?.Finish);

        Select();
        Assert.Equal(MaterialFinish.Standard, _inspector.MaterialAppearance?.Finish);

        MaterialFinishOption shown = Assert.Single(
            _inspector.FinishOptions, option => option.Finish == _inspector.MaterialAppearance?.Finish);
        Assert.False(shown.Choosable);
        Assert.Equal("Standard", shown.Label);
    }

    [Fact]
    public void SelectingAMaterialShowsItsAppearanceAndItsPreset()
    {
        Build();
        _project.SetMaterialPreset(_material, MaterialPreset.TreadPlate);
        Select();

        Assert.Equal(MaterialPreset.TreadPlate, _inspector.MaterialPreset);
        Assert.Equal(MaterialFinish.Tread, _inspector.MaterialAppearance?.Finish);
    }

    [Fact]
    public void ATunedMaterialShowsNoPreset()
    {
        Build();
        _project.SetMaterialPreset(_material, MaterialPreset.TreadPlate);
        _project.TryGetMaterial(_material, out IMaterial material);
        _project.SetMaterialAppearance(_material, material.Appearance with { Roughness = 0.111f });
        Select();

        Assert.Null(_inspector.MaterialPreset);
    }

    [Fact]
    public void ChoosingAPresetReachesTheDocument()
    {
        Build();
        Select();

        _inspector.ChoosePreset(MaterialPreset.Gold);
        _project.TryGetMaterial(_material, out IMaterial material);

        MaterialPresetEntry entry = MaterialPresetTable.Get(MaterialPreset.Gold);

        Assert.Equal(entry.Appearance, material.Appearance);
        Assert.Equal(entry.Color, material.Color);
    }

    [Fact]
    public void ATuningRowReachesTheDocumentAndComesBack()
    {
        Build();
        Select();

        _project.TryGetMaterial(_material, out IMaterial before);
        _inspector.EditAppearance(before.Appearance with { Metallic = 0.5f });

        _project.TryGetMaterial(_material, out IMaterial after);

        Assert.Equal(0.5f, after.Appearance.Metallic, 3);

        // The panel is told rather than left to assume, like every other push.
        Assert.Equal(0.5f, _inspector.MaterialAppearance?.Metallic ?? 0f, 3);
    }

    /// <summary>
    /// A tuning row that named a value the model would refuse must still leave the
    /// panel showing what the model actually holds, not what was asked for.
    /// </summary>
    [Fact]
    public void AnOutOfRangeTuningComesBackClamped()
    {
        Build();
        Select();

        _project.TryGetMaterial(_material, out IMaterial before);
        _inspector.EditAppearance(before.Appearance with { FinishScale = 500f });

        Assert.Equal(
            MaterialAppearanceLimits.MaximumScale,
            _inspector.MaterialAppearance?.FinishScale ?? 0f,
            3);
    }

    /// <summary>
    /// Which depth the panel is at is not the document's business, so nothing is
    /// recorded — and it is not the window's either: it is stored, like the render
    /// card's fold and a panel's own, because having chosen once is choosing.
    /// </summary>
    [Fact]
    public void TheDepthIsStoredAndLeavesNoRecord()
    {
        Build();
        Select();

        int records = _project.History.Records.Count;

        _inspector.ChooseAdvanced(true);

        Assert.True(_preferences.MaterialAdvanced);
        Assert.True(_inspector.MaterialAdvanced);
        Assert.Equal(records, _project.History.Records.Count);
    }

    [Fact]
    public void TheColourNotationIsStoredToo()
    {
        Build();
        Select();

        _inspector.ChooseColorFormat(ColorTextFormat.Rgb);

        Assert.Equal(ColorTextFormat.Rgb, _preferences.MaterialColorFormat);
    }

    [Fact]
    public void ThePreviewFoldIsStoredToo()
    {
        Build();
        Select();

        _inspector.ShowPreview(true);

        Assert.Equal(true, _preferences.MaterialPreviewShown);
    }

    /// <summary>
    /// And a panel built against a file that already says so opens that way. This
    /// is the whole point of storing them: the alternative is choosing again every
    /// time the window opens, which is what shipped.
    /// </summary>
    [Fact]
    public void APanelOpensTheWayItWasLastLeft()
    {
        _preferences.ApplyMaterialAdvanced(true);
        _preferences.ApplyMaterialColorFormat(ColorTextFormat.Hsv);
        _preferences.ApplyMaterialPreviewShown(true);

        Build();
        Select();

        Assert.True(_inspector.MaterialAdvanced);
        Assert.Equal(ColorTextFormat.Hsv, _inspector.ColorFormat);
        Assert.True(_inspector.PreviewShown);
    }

    /// <summary>
    /// The viewport is told the appearance, not only the colour: a facture that
    /// reached the document and not the mesh would be invisible.
    /// </summary>
    [Fact]
    public void TheViewportHearsTheAppearance()
    {
        Build();
        _project.SetMaterialPreset(_material, MaterialPreset.Corrugated);

        Assert.Equal(MaterialFinish.Ribbed, _view.Appearances[_material].Finish);
    }

    private void Select() => Select(_material);

    private void Select(Guid material) =>
        _session.SetSelection(new SelectionTarget(SelectionKind.Material, material));

    private void Build()
    {
        _session.CurrentMaterialId = _project.Materials[0].Id;

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

    /// <summary>
    /// The switch reaches the document, and it is the switch that decides — the
    /// colour's alpha is left exactly where it was.
    /// </summary>
    [Fact]
    public void TheTransparencyCheckboxReachesTheMaterial()
    {
        Build();

        Select();

        _inspector.RaiseTransparencyEdited(true);

        _project.TryGetMaterial(_material, out IMaterial material);

        Assert.True(material.Appearance.Transparent);
    }

    [Fact]
    public void TurningItOffLeavesTheMaterialOpaqueWhateverItsAlpha()
    {
        Build();

        Select();

        _project.SetMaterialColor(_material, new Color(1f, 1f, 1f, 0.3f));
        _inspector.RaiseTransparencyEdited(true);
        _inspector.RaiseTransparencyEdited(false);

        _project.TryGetMaterial(_material, out IMaterial material);

        Assert.False(material.Appearance.Transparent);
        Assert.Equal(0.3f, material.Color.A, 4);
        Assert.Equal(1f, material.Appearance.OpacityOf(material.Color.A), 4);
    }
}
