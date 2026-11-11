using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The shell's share of the settings: opening the form, and passing on what the
/// composition root says became of the agent endpoint. The form itself belongs to
/// its own presenter, and the shell relays nothing else to it.
/// </summary>
public sealed class ShellSettingsTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly FakePreferences _preferences = new();
    private readonly ShellPresenter _shell;

    public ShellSettingsTests() => _shell = new ShellPresenter(_view, _preferences);

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void TheMenuItemOpensTheForm()
    {
        _view.RaisePreferences();

        Assert.True(_view.SettingsView.IsOpen);
        Assert.Equal("token-one", _view.SettingsView.Token);
    }

    /// <summary>
    /// The snapshot's own sentence reaches the Files page through the shell's
    /// wiring, the way the endpoint's does — and it is the snapshot's, not the
    /// form's guess: with the switch off it says so.
    /// </summary>
    [Fact]
    public void TheAutosaveStatusReachesTheForm()
    {
        _view.RaisePreferences();

        Assert.Equal(_preferences.AutosaveDirectory, _view.SettingsView.AutosaveDirectory);
        Assert.StartsWith("Off.", _view.SettingsView.AutosaveStatus);

        _view.SettingsView.RaiseEdited(_preferences.GetValues() with { AutosaveEnabled = true });

        Assert.StartsWith("Nothing to snapshot", _view.SettingsView.AutosaveStatus);
    }

    [Fact]
    public void WhatTheEndpointReportsReachesTheForm()
    {
        _shell.ReportAgentEndpoint("Listening on http://127.0.0.1:8971/mcp.");

        _view.RaisePreferences();

        Assert.Contains("Listening", _view.SettingsView.Status);
    }

    /// <summary>
    /// Preferences are not document data, so the form survives what a document
    /// change throws away.
    /// </summary>
    [Fact]
    public void TheFormStillWorksAfterANewDocument()
    {
        _view.RaiseNew();
        _view.RaisePreferences();

        Assert.True(_view.SettingsView.IsOpen);
    }

    [Fact]
    public void EditingTheFormReachesTheStoredPreferences()
    {
        _view.RaisePreferences();

        _view.SettingsView.RaiseEdited(
            _preferences.GetValues() with { McpPort = 9333, CountdownSeconds = 1.5 });

        Assert.Equal(9333, _preferences.McpPort);
        Assert.Equal(1.5, _preferences.CountdownSeconds);
    }

    /// <summary>
    /// The display half of applying live, end to end: a theme chosen in the form
    /// is on the chrome before the form has been dismissed. That is the whole
    /// argument for the change — a theme is picked by looking at it.
    /// </summary>
    [Fact]
    public void AThemeChosenInTheFormReachesTheChromeWithTheFormStillUp()
    {
        _view.RaisePreferences();

        _view.SettingsView.RaiseEdited(
            _preferences.GetValues() with { Theme = EditorTheme.Light });

        Assert.Equal(EditorTheme.Light, _view.Theme);
        Assert.True(_view.SettingsView.IsOpen);
    }

    /// <summary>
    /// A display preference, applied before anything else happens — the widget must
    /// not spend the first document in the wrong corner waiting for a change.
    /// </summary>
    [Fact]
    public void TheStoredCornerIsAppliedAtStartup()
    {
        Assert.Equal(1, _view.AnimationCount);
        Assert.True(_view.AnimationsEnabled);
        Assert.Equal(AnimationSpeed.Fast, _view.AnimationSpeed);
        Assert.Equal(1, _view.ViewCubeCornerCount);
        Assert.Equal(ViewCubeCorner.TopRight, _view.ViewCubeCorner);
        Assert.Equal(1, _view.SkinCount);
        Assert.Equal(EditorTheme.Dark, _view.Theme);
        Assert.Equal("#70BAFA", _view.Accent);
    }

    /// <summary>
    /// The side widths are display preferences like the corner: pushed at
    /// startup, and a resize let go is stored and echoed back — the loop
    /// settling, not a second edit.
    /// </summary>
    [Fact]
    public void TheStoredWidthsAreAppliedAtStartupAndAResizeIsStored()
    {
        Assert.Equal(1, _view.PanelWidthsCount);
        Assert.Equal(PanelWidths.Default, _view.PanelWidths.Left);

        _view.RaisePanelWidthsEdited(new PanelWidths(420.0, 360.0));

        Assert.Equal(new PanelWidths(420.0, 360.0), _preferences.PanelWidths);
        Assert.Equal(new PanelWidths(420.0, 360.0), _view.PanelWidths);
    }

    /// <summary>
    /// And again whenever preferences reach disk, so moving it in the file moves the
    /// widget without a restart.
    /// </summary>
    [Fact]
    public void MovingTheCornerInThePreferencesMovesTheWidget()
    {
        _preferences.Apply(
            _preferences.GetValues() with { ViewCubeCorner = ViewCubeCorner.BottomLeft });

        Assert.Equal(ViewCubeCorner.BottomLeft, _view.ViewCubeCorner);
        Assert.Equal(2, _view.ViewCubeCornerCount);
    }

    /// <summary>
    /// It belongs to the application, not to the document, so it survives what a
    /// document change rebuilds.
    /// </summary>
    [Fact]
    public void TheCornerSurvivesANewDocument()
    {
        _preferences.Apply(
            _preferences.GetValues() with { ViewCubeCorner = ViewCubeCorner.BottomRight });

        _view.RaiseNew();

        Assert.Equal(ViewCubeCorner.BottomRight, _view.ViewCubeCorner);
    }

    /// <summary>
    /// The theme is a display preference like the corner, so it is pushed on
    /// every change rather than only at startup — which is what makes the
    /// setting take effect the moment it is set instead of at the next launch.
    /// </summary>
    [Fact]
    public void ChangingTheThemeReachesTheView()
    {
        _preferences.Apply(_preferences.GetValues() with { Theme = EditorTheme.Light });

        Assert.Equal(EditorTheme.Light, _view.Theme);
        Assert.Equal(2, _view.SkinCount);
    }

    [Fact]
    public void ChangingTheAccentReachesTheView()
    {
        _preferences.Apply(_preferences.GetValues() with { AccentColor = "#FF8800" });

        Assert.Equal("#FF8800", _view.Accent);
    }

    [Fact]
    public void TheThemeSurvivesANewDocument()
    {
        _preferences.Apply(_preferences.GetValues() with { Theme = EditorTheme.Light });

        _view.RaiseNew();

        Assert.Equal(EditorTheme.Light, _view.Theme);
    }

    /// <summary>
    /// The panes are frosted unless somebody said otherwise, and the switch
    /// reaches the chrome the way every other display setting does.
    /// </summary>
    [Fact]
    public void ThePanelsAreFrostedUntilTheyAreNot()
    {
        Assert.True(_view.GlassPanels);

        _preferences.Apply(_preferences.GetValues() with { GlassPanels = false });

        Assert.False(_view.GlassPanels);

        _preferences.Apply(_preferences.GetValues() with { GlassPanels = true });

        Assert.True(_view.GlassPanels);
    }

    /// <summary>
    /// And it survives a document, being a fact about the window rather than
    /// about what is open in it.
    /// </summary>
    [Fact]
    public void TheGlassSurvivesANewDocument()
    {
        _preferences.Apply(_preferences.GetValues() with { GlassPanels = false });

        _view.RaiseNew();

        Assert.False(_view.GlassPanels);
    }
}
