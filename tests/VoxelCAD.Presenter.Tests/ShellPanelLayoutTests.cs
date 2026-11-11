using System;
using System.Linq;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The shell's share of the panel layout: the stored placements reach the view
/// before anything else happens, a pin or fold click reaches the preferences,
/// and the stored answer comes back to the view — the loop settling, not a
/// second edit. What a header click means on screen lives in the view and is
/// verified by driving, like every panel node.
/// </summary>
public sealed class ShellPanelLayoutTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly FakePreferences _preferences = new();
    private readonly ShellPresenter _shell;

    public ShellPanelLayoutTests() => _shell = new ShellPresenter(_view, _preferences);

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void TheStoredLayoutIsAppliedBeforeTheFirstDocument()
    {
        Assert.True(_view.PanelLayoutCount > 0);
        Assert.Equal(_preferences.PanelPlacements, _view.PanelLayout);
    }

    [Fact]
    public void APinClickReachesThePreferencesAndTheStoredAnswerComesBack()
    {
        int applied = _view.PanelLayoutCount;

        var edited = _preferences.PanelPlacements
            .Select(placement => placement.Kind == PanelKind.Inspector
                ? placement with { Pinned = false }
                : placement)
            .ToList();

        _view.RaisePanelLayoutEdited(edited);

        PanelPlacement stored = Assert.Single(
            _preferences.PanelPlacements,
            placement => placement.Kind == PanelKind.Inspector);
        PanelPlacement shown = Assert.Single(
            _view.PanelLayout,
            placement => placement.Kind == PanelKind.Inspector);

        Assert.False(stored.Pinned);
        Assert.False(shown.Pinned);
        Assert.True(_view.PanelLayoutCount > applied);
    }

    [Fact]
    public void AFoldEditedElsewhereStillReachesTheView()
    {
        _preferences.ApplyPanelPlacements(new[]
        {
            new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: true, Folded: true)
        });

        PanelPlacement history = Assert.Single(
            _view.PanelLayout,
            placement => placement.Kind == PanelKind.History);

        Assert.True(history.Folded);
    }
}
