using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The click that takes the keyboard back from a panel to the scene. It is also a
/// cursor click, so a click on what is already selected keeps it, and the walk
/// through a stack of hits starts over whenever the hand has been somewhere else.
/// "Already selected" is asked of what the click means: a model inside a prefab
/// instance means the instance, so a model picked in the tree gives way to it.
/// </summary>
/// <remarks>
/// Found by driving the sample castle: a selection of several collapsed on the
/// click meant only to focus the viewport, and a click in the very spot of an
/// earlier one walked past the tower to the keep.
/// </remarks>
public sealed class FocusClickTests : IDisposable
{
    private static readonly Vector2 Spot = new(400f, 300f);

    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _material;
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly SelectPresenter _presenter;

    public FocusClickTests()
    {
        _scene = _project.Scenes[0];
        _material = _project.Materials[0].Id;
        _presenter = new SelectPresenter(_scene, _view, _session);
    }

    public void Dispose() => _presenter.Dispose();

    /// <summary>
    /// A model picked inside an instance cannot be changed where it stands; the
    /// instance can be moved, so a click in the scene means the instance even then.
    /// </summary>
    [Fact]
    public void AModelPickedInsideAnInstanceGivesWayToTheInstanceOnAClick()
    {
        (Guid instance, Guid content) = MakeInstance("Tower");
        _session.SetSelection(Element(content));

        _view.RaiseElementPicked(Spot, content);

        Assert.Equal(Element(instance), _session.ActiveTarget);
    }

    [Fact]
    public void ASelectedInstanceSurvivesAClickOnItsContents()
    {
        (Guid instance, Guid content) = MakeInstance("Tower");
        _session.SetSelection(Element(instance));

        _view.RaiseElementPicked(Spot, content);

        Assert.Equal(Element(instance), _session.ActiveTarget);
    }

    /// <summary>The rule it narrows still holds where nothing of the instance was picked.</summary>
    [Fact]
    public void WithNothingOfItSelectedTheClickMeansTheInstance()
    {
        (Guid instance, Guid content) = MakeInstance("Tower");

        _view.RaiseElementPicked(Spot, content);

        Assert.Equal(Element(instance), _session.ActiveTarget);
    }

    [Fact]
    public void ASecondClickInTheSameSpotStillWalksToWhatIsBehind()
    {
        (_, Guid content) = MakeInstance("Tower");
        Guid keep = CreateModel("Keep");
        _session.SetSelection(Element(content));

        _view.RaiseElementPicked(Spot, content, keep);
        _view.RaiseElementPicked(Spot, content, keep);

        Assert.Equal(Element(keep), _session.ActiveTarget);
    }

    [Fact]
    public void ASelectionOfSeveralSurvivesAClickOnOneOfIt()
    {
        Guid first = CreateModel("First");
        Guid second = CreateModel("Second");

        _view.RaiseElementPicked(Spot, first);
        _view.RaiseElementPicked(SelectionIntent.Toggle, Spot + new Vector2(80f, 0f), second);
        _view.RaiseElementPicked(Spot + new Vector2(0f, 80f), first);

        Assert.True(_session.Selection.Contains(Element(first)));
        Assert.True(_session.Selection.Contains(Element(second)));
    }

    [Fact]
    public void AClickOnSomethingElseStillSelectsIt()
    {
        Guid picked = CreateModel("Picked");
        Guid other = CreateModel("Other");
        _session.SetSelection(Element(picked));

        _view.RaiseElementPicked(Spot, other, picked);

        Assert.Equal(Element(other), _session.ActiveTarget);
    }

    /// <summary>
    /// A row picked in the tree between two clicks in one spot is not a walk being
    /// continued: the second click is a first click, so it means the nearest.
    /// </summary>
    [Fact]
    public void ASelectionMadeElsewhereEndsTheWalk()
    {
        Guid near = CreateModel("Near");
        Guid middle = CreateModel("Middle");
        Guid far = CreateModel("Far");

        _view.RaiseElementPicked(Spot, near, middle, far);
        _session.SetSelection(Element(middle));
        _view.RaiseElementPicked(Spot, near, middle, far);

        Assert.Equal(Element(near), _session.ActiveTarget);
    }

    /// <summary>
    /// A row clicked that was selected already changes nothing, so no selection
    /// event says the hand went elsewhere — the keyboard leaving the viewport does.
    /// </summary>
    [Fact]
    public void TheKeyboardLeavingTheViewportEndsTheWalkToo()
    {
        Guid near = CreateModel("Near");
        Guid far = CreateModel("Far");

        _view.RaiseElementPicked(Spot, near, far);
        _view.RaiseKeyboardLost();
        _view.RaiseElementPicked(Spot, near, far);

        Assert.Equal(Element(near), _session.ActiveTarget);
    }

    private static SelectionTarget Element(Guid id) => new(SelectionKind.Element, id);

    private Guid CreateModel(string name)
    {
        Guid id = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

        _scene.SetVoxels(id, new[] { new VoxelEdit(new Vector3I(1, 1, 1), _material) });

        return id;
    }

    private (Guid Instance, Guid Content) MakeInstance(string name)
    {
        _scene.CreatePrefab(CreateModel(name));

        IHierarchyElement instance = _scene.Children[^1];

        return (instance.Id, instance.Children[0].Id);
    }
}
