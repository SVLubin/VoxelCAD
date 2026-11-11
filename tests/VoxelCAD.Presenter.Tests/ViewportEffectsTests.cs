using System;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Which of the viewport's optional effects run, and how that reaches the view.
/// </summary>
/// <remarks>
/// What is held here is the seam: that the preference is pushed at all, that it is
/// pushed <em>before</em> the materials derived from it, and that changing one
/// reaches the viewport without the document being touched. What a material then
/// looks like is <c>VoxelMaterialLookTests</c>'s, and whether the picture actually
/// changes is a photograph's — a <c>StandardMaterial3D</c> cannot be built in this
/// host at all.
/// </remarks>
public sealed class ViewportEffectsTests : IDisposable
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

    public ViewportEffectsTests()
    {
        _scene = _project.Scenes[0];
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

    public void Dispose() => _document.Dispose();

    /// <summary>
    /// The defaults are everything on, which is the editor as it has always
    /// looked: a switch nobody has touched must not change the picture.
    /// </summary>
    [Fact]
    public void AViewportThatWasNotAskedForAnythingRunsEverything()
    {
        Assert.Equal(ViewportEffects.Full, _view.Effects);
    }

    [Fact]
    public void SwitchingOneOffReachesTheViewport()
    {
        _preferences.ApplyViewportOcclusion(false);

        Assert.False(_view.Effects.Occlusion);
        Assert.True(_view.Effects.Shadows);
        Assert.True(_view.Effects.Factures);

        _preferences.ApplyViewportShadows(false);
        _preferences.ApplyViewportFactures(false);

        Assert.Equal(new ViewportEffects(false, false, false), _view.Effects);

        _preferences.ApplyViewportOcclusion(true);

        Assert.True(_view.Effects.Occlusion);
    }

    /// <summary>
    /// A material's look is derived from the appearance <em>and</em> from these, so
    /// arriving after the materials would mean every one of them built twice — once
    /// against whatever the view happened to be holding, and again on the push.
    /// </summary>
    [Fact]
    public void TheEffectsArriveBeforeTheMaterialsDerivedFromThem()
    {
        int effects = _view.EffectsAndMaterialsLog.FindIndex(entry => entry.StartsWith("effects"));
        int material = _view.EffectsAndMaterialsLog.FindIndex(entry => entry.StartsWith("material"));

        Assert.True(effects >= 0, "the viewport was never told which effects to run");
        Assert.True(material >= 0, "no material was ever pushed");
        Assert.True(
            effects < material,
            $"the effects were pushed at {effects}, after the first material at {material}");
    }

    /// <summary>
    /// It is a display setting, so nothing about it is in the document — no record,
    /// and nothing to save.
    /// </summary>
    [Fact]
    public void SwitchingAnEffectEditsNothing()
    {
        int records = _project.History.Records.Count;

        _preferences.ApplyViewportOcclusion(false);
        _preferences.ApplyViewportShadows(false);
        _preferences.ApplyViewportFactures(false);

        Assert.Equal(records, _project.History.Records.Count);
    }
}
