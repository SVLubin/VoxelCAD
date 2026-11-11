using System;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The first launch opens the castle rather than an empty room, and opens it the
/// way a new document is: untitled, so saving asks where, and clean, so closing it
/// asks nothing.
/// </summary>
public sealed class FirstRunTests
{
    private readonly FakeShellView _view = new();

    /// <summary>The castle wears the shipped materials, so a test that opens it says so.</summary>
    public FirstRunTests() => StandardMaterials.Install();

    [Fact]
    public void AFirstRunOpensTheCastleUntitledAndClean()
    {
        using var shell = new ShellPresenter(_view, new FakePreferences { IsFirstRun = true });

        Assert.Equal("Untitled — VoxelCAD", _view.WindowTitle);
        Assert.Contains("Castle", _view.HierarchyView.Items.Values);
        Assert.Contains("Tower NW", _view.HierarchyView.Items.Values);
    }

    [Fact]
    public void AnyOtherRunOpensAnEmptyProject()
    {
        using var shell = new ShellPresenter(_view, new FakePreferences());

        Assert.Equal("Untitled — VoxelCAD", _view.WindowTitle);
        Assert.DoesNotContain("Castle", _view.HierarchyView.Items.Values);
    }

    /// <summary>
    /// The view a new document starts on is pitched at an empty room's floor; the
    /// castle stands taller, so the first tick frames the whole scene — and only
    /// the first, and only on a first run.
    /// </summary>
    [Fact]
    public void TheFirstTickOfAFirstRunFramesTheScene()
    {
        using var shell = new ShellPresenter(_view, new FakePreferences { IsFirstRun = true });
        Guid scene = _view.HierarchyView.SceneId;

        Assert.Null(_view.SceneView.FocusTarget);

        shell.Tick(0.016);

        Assert.Equal(scene, _view.SceneView.FocusTarget);
    }

    [Fact]
    public void AnOrdinaryRunLeavesTheCameraAlone()
    {
        using var shell = new ShellPresenter(_view, new FakePreferences());

        shell.Tick(0.016);

        Assert.Null(_view.SceneView.FocusTarget);
    }

    /// <summary>
    /// What a first launch opens is the sample <c>new sample=</c> names, so an agent
    /// can put a user back where they started.
    /// </summary>
    [Fact]
    public void TheFirstRunSampleIsOneTheCommandCanName()
    {
        Assert.Contains(ShellPresenter.FirstRunSample, ModelFactory.SampleNames);
    }

    [Fact]
    public void TheCastleHoldsNothingToSaveSoANewDocumentAsksNothing()
    {
        using var shell = new ShellPresenter(_view, new FakePreferences { IsFirstRun = true });

        _view.RaiseNew();

        Assert.Equal(0, _view.ConfirmCount);
        Assert.DoesNotContain("Tower NW", _view.HierarchyView.Items.Values);
    }
}
