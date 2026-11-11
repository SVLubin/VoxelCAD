using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Binds the settings form to the stored preferences. It outlives documents, the
/// way the preferences it edits do — none of this is part of a project.
/// </summary>
/// <remarks>
/// A preference takes effect when it is set: there is no Save and no Cancel, so
/// there is no half-filled form and no window in which the form and the stored
/// values disagree. That is the modern shape for settings, and this form had two
/// reasons of its own for wanting it — the display page is chosen by
/// <em>looking</em>, which a transaction hides behind a card that has to be
/// dismissed first, and half the file was already written live anyway, since
/// pinning a panel stores its placement with no button anywhere.
/// <para>
/// It never starts or stops the endpoint itself. Applying preferences raises
/// their <c>Changed</c>, and whoever owns the endpoint acts on that and reports
/// back through <see cref="SetEndpointStatus"/> — so what the form says about the
/// endpoint is what the endpoint actually did, not what was asked of it. That
/// round trip is synchronous, which is what lets the status line answer an edit
/// on the spot rather than a beat later.
/// </para>
/// </remarks>
internal sealed class SettingsPresenter : PresenterBase
{
    private readonly ISettingsView _view;
    private readonly IEditorPreferences _preferences;

    /// <summary>
    /// What the recovery snapshot is doing, asked of whoever knows at the two
    /// moments the form acts. A delegate rather than the sibling that answers
    /// it, on <c>DocumentPresenter</c>'s own <c>activateScene</c> precedent: the
    /// shell relays nothing between panels, and this form has no business
    /// holding the presenter that writes files.
    /// </summary>
    private readonly Func<string> _describeAutosave;

    /// <summary>
    /// How often the autosave line is re-asked while the form is up. Once a
    /// second: the sentence carries a countdown, and a countdown that stands
    /// still reads as a clock that stopped.
    /// </summary>
    private const double AutosaveRefreshSeconds = 1.0;

    private string _endpointStatus = string.Empty;
    private bool _isOpen;
    private double _sinceAutosaveRefresh;

    public SettingsPresenter(
        ISettingsView view,
        IEditorPreferences preferences,
        Func<string> describeAutosave)
    {
        _view = view;
        _preferences = preferences;
        _describeAutosave = describeAutosave;

        _view.Edited += OnEdited;
        _view.CloseRequested += OnCloseRequested;
        _view.RegenerateTokenRequested += OnRegenerateTokenRequested;
    }

    public void Open()
    {
        _isOpen = true;

        Fill();
        _view.SetStatus(_endpointStatus);
        _view.SetAutosaveStatus(_describeAutosave());
        _view.Open();
    }

    /// <summary>
    /// What the endpoint made of the current settings. Kept whether or not the
    /// form is up, so opening it shows the state as it stands rather than nothing
    /// until something changes.
    /// </summary>
    public void SetEndpointStatus(string status)
    {
        _endpointStatus = status;

        if (_isOpen)
            _view.SetStatus(status);
    }

    protected override void OnDispose()
    {
        _view.Edited -= OnEdited;
        _view.CloseRequested -= OnCloseRequested;
        _view.RegenerateTokenRequested -= OnRegenerateTokenRequested;
    }

    /// <summary>Advances the autosave line's own clock; the shell calls it once per frame.</summary>
    public void Tick(double delta)
    {
        if (!_isOpen)
            return;

        _sinceAutosaveRefresh += delta;

        if (_sinceAutosaveRefresh < AutosaveRefreshSeconds)
            return;

        _sinceAutosaveRefresh = 0.0;
        _view.SetAutosaveStatus(_describeAutosave());
    }

    /// <summary>
    /// Pushes the autosave line while the form is up. The presenter that owns the
    /// snapshot calls this as its state moves — a write landing, a failure — so
    /// the sentence on the page is never a minute behind the disk.
    /// </summary>
    public void RefreshAutosaveStatus()
    {
        if (_isOpen)
            _view.SetAutosaveStatus(_describeAutosave());
    }

    private void Fill() =>
        _view.Fill(
            _preferences.GetValues(),
            _preferences.McpToken,
            _preferences.McpUrl,
            _preferences.AutosaveDirectory);

    /// <remarks>
    /// The form is not refilled afterwards, and that is the one thing to be
    /// careful of here: writing values back into controls the user is working in
    /// is how a form takes the caret away from whoever is typing. It is safe to
    /// leave because the fields carry the same limits the preferences clamp to,
    /// so what is shown and what was stored cannot come apart.
    /// </remarks>
    private void OnEdited(PreferenceValues values)
    {
        _preferences.Apply(values);

        // After the store, because the interval and the switch change the sentence.
        _view.SetAutosaveStatus(_describeAutosave());
    }

    private void OnCloseRequested()
    {
        _isOpen = false;

        _view.Close();
    }

    /// <summary>
    /// The form stays up: the point of the button is to see the replacement, and
    /// where it has been written, before deciding anything else.
    /// </summary>
    private void OnRegenerateTokenRequested()
    {
        _preferences.RegenerateToken();

        Fill();
        _view.SetStatus($"New token issued. {_endpointStatus}");
    }
}
