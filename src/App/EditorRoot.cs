using VoxelCAD.Agent;
using VoxelCAD.Localization;
using VoxelCAD.Preferences;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using VoxelCAD.View;
using Godot;

namespace VoxelCAD.App;

/// <summary>
/// The composition root, and the only script attached to a scene. It creates the
/// preferences, the shell and the agent endpoint, then pumps them each frame. The
/// document is the shell's own business.
/// </summary>
/// <remarks>
/// The MCP endpoint here shares the user's live project. What keeps the agent and
/// the user from editing at once is control handover, enforced by the gated
/// dispatcher the shell hands out — not anything in this file.
/// <para>
/// Where preferences live is the one question only this layer can answer:
/// <c>user://</c> is the engine's idea of "beside the application", and the layer
/// that stores them is deliberately engine-free.
/// </para>
/// </remarks>
[GlobalClass]
public partial class EditorRoot : Node
{
    private IEditorPreferences? _preferences;
    private IEditorShell? _shell;
    private IMcpEndpoint? _endpoint;

    /// <summary>
    /// The last endpoint status put in the log, so an unchanged one is not put
    /// there again. Null until the first pass, which always reports.
    /// </summary>
    private string? _reportedEndpointStatus;

    public override void _Ready()
    {
        // The materials the editor ships with, put in place before a document
        // exists: a project resolves them and never stores them, so they have to
        // be there before the first file is read. Here rather than in the model
        // because the library is the editor's — see StandardMaterials.
        StandardMaterials.Install();

        RenderingServer.SetDefaultClearColor(new Color(0.12f, 0.12f, 0.14f));

        _preferences = PreferencesFactory.Load(ProjectSettings.GlobalizePath("user://"));

        // Before the shell, and that ordering is the whole of it: a control is
        // given its text once, as it is built, so a catalogue put in force after
        // the chrome exists reaches nothing that is already on screen.
        Translations.Use(CatalogLoader.Load(_preferences.Language));

        IShellView view = ViewFactory.CreateShell(this);
        _shell = PresenterFactory.CreateShell(view, _preferences);

        // The driver's step is handed to the endpoint because a synthesized
        // gesture spans frames: the queue has to advance it, hold the answer of
        // the command that started it, and keep everything behind it waiting.
        _endpoint = AgentFactory.CreateEndpoint(
            _shell.CreateAgentDispatchers().ForClient,
            view.UiDriver.Step);

        _preferences.Changed += OnPreferencesChanged;
        _endpoint.ListenerStopped += OnListenerStopped;

        ApplyPreferences();
    }

    public override void _Process(double delta)
    {
        _endpoint?.Pump();
        _shell?.Tick(delta);
    }

    public override void _ExitTree()
    {
        if (_preferences != null)
            _preferences.Changed -= OnPreferencesChanged;

        // Unsubscribed before the endpoint is disposed: shutting down tears the
        // listener down too, and the interface it would release is on its way out
        // with it.
        if (_endpoint != null)
            _endpoint.ListenerStopped -= OnListenerStopped;

        _endpoint?.Dispose();
        _shell?.Dispose();

        _endpoint = null;
        _shell = null;
        _preferences = null;
    }

    /// <summary>
    /// Settings changed, from the dialog or from anywhere else. The endpoint is
    /// rebuilt to match and says what came of it, which is the only route that
    /// answer has back to the user.
    /// </summary>
    private void OnPreferencesChanged() => ApplyPreferences();

    /// <summary>
    /// The endpoint went down — disabled, moved, or lost — so control returns to
    /// the user rather than leaving the interface dimmed for an agent that can no
    /// longer reach it.
    /// </summary>
    private void OnListenerStopped() => _shell?.ReleaseAgentControl();

    private void ApplyPreferences()
    {
        if (_preferences == null || _endpoint == null || _shell == null)
            return;

        string status = _endpoint.Apply(_preferences);

        _shell.ReportAgentEndpoint(status);

        // Only when it changes. Every preference reaches here — the form has no
        // Save, so a tick of any checkbox is a pass — and most of them are none
        // of the endpoint's business, so an unchanged status printed on each one
        // filled the log with itself: seventeen identical lines in one session,
        // in the one channel there is for diagnosing a running editor.
        if (status == _reportedEndpointStatus)
            return;

        _reportedEndpointStatus = status;

        if (_endpoint.IsListening || !_preferences.McpEnabled)
            GD.Print($"[mcp] {status}");
        else
            GD.PushError($"[mcp] {status}");
    }
}
