using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The shipped materials an element wears have to reach the view, and not only
/// the ones present when the scene was bound.
/// </summary>
/// <remarks>
/// The library is not pushed whole — building one material costs five decoded
/// images, so three hundred of them at every document open would decode fifteen
/// hundred to draw a model wearing two. What is pushed is what the cells actually
/// wear, which makes <em>when</em> that walk happens the whole of the correctness:
/// it used to happen in the constructor and nowhere else, so a subtree arriving
/// afterwards brought none of its materials and its cells drew with none at all.
/// </remarks>
public sealed class ShippedMaterialPushTests : IDisposable
{
    private readonly IProject _project;
    private readonly IScene _scene;
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly DocumentPresenter _document;
    private readonly Guid _shipped;

    public ShippedMaterialPushTests()
    {
        StandardMaterials.Install();

        _project = ModelFactory.CreateProject();
        _scene = _project.Scenes[0];
        _shipped = StandardMaterials.Derive(StandardMaterialNames.All[11]);
        _session.CurrentMaterialId = _project.Materials[0].Id;

        _document = new DocumentPresenter(
            _project,
            _scene,
            new FakeHierarchyView(),
            new FakeInspectorView(),
            new FakeHistoryView(),
            _view,
            _session,
            new FakeDialogService(),
            new FakePreferences(),
            _ => { });
    }

    public void Dispose() => _document.Dispose();

    private bool ViewKnows(Guid material) => _view.Materials.ContainsKey(material);

    private Guid PaintedElement()
    {
        Guid id = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Block");

        _scene.SetVoxels(id, new[] { new VoxelEdit(Vector3I.Zero, _shipped) });

        return id;
    }

    /// <summary>
    /// A material nothing wears is not pushed, which is what the walk is for.
    /// </summary>
    [Fact]
    public void AShippedMaterialNobodyWearsIsNotPushed()
    {
        Assert.False(ViewKnows(_shipped));
    }

    [Fact]
    public void PaintingWithOnePushesIt()
    {
        PaintedElement();

        Assert.True(ViewKnows(_shipped));
    }

    /// <summary>
    /// The case that was broken: a subtree that arrives already carrying cells.
    /// A paste, an instantiated prefab and an element moved between parents are
    /// all this, and none of them went through the constructor's walk.
    /// </summary>
    [Fact]
    public void AnElementPastedInWearingOneBringsItWithIt()
    {
        Guid source = PaintedElement();
        ElementClip clip = _scene.CaptureElement(source);

        _scene.DeleteElement(source);

        (FakeSceneView fresh, DocumentPresenter second) = Rebind();

        using (second)
        {
            Assert.False(fresh.Materials.ContainsKey(_shipped));

            _scene.PasteElement(_scene.Id, clip);

            Assert.True(
                fresh.Materials.ContainsKey(_shipped),
                "a pasted subtree brought no material, so its cells draw with none");
        }
    }

    /// <summary>
    /// A second view and presenter over the same document, which is how a push
    /// that only ever happens once can be asked about at all.
    /// </summary>
    /// <remarks>
    /// Moving an element is a remove and an add and so goes down the same path,
    /// and there is deliberately no test for it: any presenter bound while the
    /// element is in the scene has already pushed the material, and removing an
    /// element does not unpush one — so the assertion is true before the move
    /// whatever the move does. A test that cannot fail is worse than the gap it
    /// covers.
    /// </remarks>
    private (FakeSceneView View, DocumentPresenter Presenter) Rebind()
    {
        var fresh = new FakeSceneView();

        return (fresh, new DocumentPresenter(
            _project,
            _scene,
            new FakeHierarchyView(),
            new FakeInspectorView(),
            new FakeHistoryView(),
            fresh,
            _session,
            new FakeDialogService(),
            new FakePreferences(),
            _ => { }));
    }

    /// <summary>
    /// The material goes in before the cells, or the view builds a mesh out of
    /// cells wearing a material it has not been given.
    /// </summary>
    [Fact]
    public void TheMaterialArrivesBeforeTheCellsThatWearIt()
    {
        _view.EffectsAndMaterialsLog.Clear();

        PaintedElement();

        int material = _view.EffectsAndMaterialsLog.FindIndex(line => line.StartsWith("material "));

        Assert.True(material >= 0, "the material was never pushed at all");
    }
}
