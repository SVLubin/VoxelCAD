using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The settings form and what it does to the stored preferences. What it must not
/// do is decide anything about the endpoint: it reports what the endpoint said,
/// and nothing more.
/// </summary>
public sealed class SettingsPresenterTests
{
    private readonly FakeSettingsView _view = new();
    private readonly FakePreferences _preferences = new();
    private readonly SettingsPresenter _presenter;

    private string _autosaveStatus = "Nothing to snapshot.";

    public SettingsPresenterTests()
    {
        _presenter = new SettingsPresenter(_view, _preferences, () => _autosaveStatus);
    }

    [Fact]
    public void OpeningShowsWhatIsStored()
    {
        _presenter.Open();

        Assert.True(_view.IsOpen);
        Assert.Equal(8971, _view.Shown.McpPort);
        Assert.Equal("token-one", _view.Token);
        Assert.Equal("http://127.0.0.1:8971/mcp", _view.Endpoint);
    }

    /// <summary>
    /// The whole of what replaced Save: an edit is stored the moment it is made,
    /// and the form is left standing so the next one can be made after it.
    /// </summary>
    [Fact]
    public void AnEditIsStoredAtOnceAndLeavesTheFormUp()
    {
        _presenter.Open();

        _view.RaiseEdited(Values(port: 9000, countdownSeconds: 5.0));

        Assert.Equal(9000, _preferences.McpPort);
        Assert.False(_preferences.McpEnabled);
        Assert.Equal(5.0, _preferences.CountdownSeconds);
        Assert.True(_view.IsOpen);
    }

    /// <summary>
    /// And the one after it lands too. A transaction only ever had to work once
    /// per opening; this has to work every time, which is the shape of bug the
    /// ledger keeps calling "verify the second use".
    /// </summary>
    [Fact]
    public void EveryFurtherEditLandsAsWell()
    {
        _presenter.Open();

        _view.RaiseEdited(Values(port: 9000));
        _view.RaiseEdited(Values(port: 9100));
        _view.RaiseEdited(Values(port: 9200));

        Assert.Equal(9200, _preferences.McpPort);
    }

    /// <summary>
    /// Closing is not an answer to anything — everything the form asked for has
    /// already happened — so it must not reach the preferences at all.
    /// </summary>
    [Fact]
    public void ClosingStoresNothing()
    {
        _presenter.Open();

        _view.RaiseClose();

        Assert.Null(_preferences.Applied);
        Assert.False(_view.IsOpen);
    }

    /// <summary>
    /// The form stays up so the replacement, and where it has been written, can be
    /// read before anything else is decided.
    /// </summary>
    [Fact]
    public void AskingForANewTokenShowsItWithoutClosingTheForm()
    {
        _presenter.Open();

        _view.RaiseRegenerateToken();

        Assert.Equal(1, _preferences.TokensIssued);
        Assert.Equal("token-2", _view.Token);
        Assert.True(_view.IsOpen);
        Assert.Contains("New token issued", _view.Status);
    }

    /// <summary>
    /// The Files page shows where snapshots go and what the snapshot is doing,
    /// both asked at opening — the folder of the preferences, the sentence of
    /// whoever writes the files.
    /// </summary>
    [Fact]
    public void OpeningShowsTheAutosaveFolderAndStatus()
    {
        _presenter.Open();

        Assert.Equal(_preferences.AutosaveDirectory, _view.AutosaveDirectory);
        Assert.Equal("Nothing to snapshot.", _view.AutosaveStatus);
    }

    /// <summary>The switch and the interval change the sentence, so an edit re-asks it.</summary>
    [Fact]
    public void AnEditRefreshesTheAutosaveStatus()
    {
        _presenter.Open();
        _autosaveStatus = "Off.";

        _view.RaiseEdited(Values(port: 9000));

        Assert.Equal("Off.", _view.AutosaveStatus);
    }

    [Fact]
    public void AStatusChangeReachesTheFormOnlyWhileItIsUp()
    {
        _autosaveStatus = "Last snapshot at 12:03:07 (41 ms).";
        _presenter.RefreshAutosaveStatus();
        Assert.Equal(string.Empty, _view.AutosaveStatus);

        _presenter.Open();
        _autosaveStatus = "Writing a snapshot.";
        _presenter.RefreshAutosaveStatus();

        Assert.Equal("Writing a snapshot.", _view.AutosaveStatus);
    }

    [Fact]
    public void TheFormReportsWhatTheEndpointActuallyDid()
    {
        _presenter.SetEndpointStatus("Port 8971 could not be opened: address in use.");
        _presenter.Open();

        Assert.Contains("could not be opened", _view.Status);
    }

    /// <summary>
    /// Applying reaches the endpoint through the preferences' own Changed, and the
    /// answer comes back before the form is closed — so the next opening shows the
    /// state as it stands rather than the one before the change.
    /// </summary>
    [Fact]
    public void AStatusArrivingWhileTheFormIsShutIsKeptForNextTime()
    {
        _presenter.SetEndpointStatus("Listening on http://127.0.0.1:8971/mcp.");
        _presenter.Open();

        Assert.Contains("Listening", _view.Status);
    }

    [Fact]
    public void AClosedFormIsNotWrittenTo()
    {
        _presenter.Open();
        _view.RaiseClose();

        _presenter.SetEndpointStatus("The agent endpoint is off.");

        Assert.Equal(string.Empty, _view.Status);
    }

    /// <summary>
    /// A whole form, since that is what one control changing reports. Only what a
    /// test is actually looking at is named.
    /// </summary>
    private static PreferenceValues Values(int port, double countdownSeconds = 5.0) =>
        new(
            McpEnabled: false,
            McpPort: port,
            ClientConfigPath: "/tmp/elsewhere/.mcp.json",
            CountdownEnabled: false,
            CountdownSeconds: countdownSeconds,
            MaximumWaitSeconds: 30.0,
            SilenceWarningSeconds: 60.0,
            SilenceGraceSeconds: 30.0,
            InputInjectionEnabled: true,
            ViewCubeCorner: ViewCubeCorner.TopRight,
            PanelPlacements: System.Array.Empty<PanelPlacement>(),
            AnimationsEnabled: true,
            AnimationSpeed: AnimationSpeed.Fast,
            Theme: EditorTheme.Dark,
            AccentColor: "#70BAFA",
            Language: EditorLanguage.English,
            ViewportOcclusion: true,
            ViewportShadows: true,
            ViewportFactures: true,
            GlassPanels: true,
            AutosaveEnabled: false,
            AutosaveIntervalSeconds: 120.0);
}
