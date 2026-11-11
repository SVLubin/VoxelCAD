using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Preferences;

/// <summary>
/// Settings that belong to the application rather than to a document: whether the
/// agent endpoint runs and on what terms, and how long the user gets before
/// control changes hands.
/// </summary>
/// <remarks>
/// These outlive the process, which is what separates them from
/// <c>EditorSession</c>, and they are not saved with a project, which separates
/// them from the model. The token in particular has to persist: an endpoint that
/// invented a new one every launch would make an agent's client config wrong
/// before it was ever used.
/// </remarks>
public interface IEditorPreferences
{
    /// <summary>Raised once values have changed and reached disk, never before.</summary>
    event Action Changed;

    /// <summary>
    /// True when this launch found no settings file at all — the first time the
    /// editor has run for this user, which is when it opens a sample rather than an
    /// empty project. A damaged file is not a first run: somebody has been here.
    /// </summary>
    bool IsFirstRun { get; }

    bool McpEnabled { get; }

    int McpPort { get; }

    /// <summary>The bearer token the endpoint expects. Generated once, then kept.</summary>
    string McpToken { get; }

    /// <summary>
    /// Where the client config is written, so connecting an agent is pointing it
    /// at a file rather than copying a token out of a log.
    /// </summary>
    string ClientConfigPath { get; }

    bool CountdownEnabled { get; }

    double CountdownSeconds { get; }

    /// <summary>
    /// How long a request waits on a busy user before the countdown runs anyway.
    /// A leaked interaction token then only delays a handover instead of stalling
    /// it until the editor restarts.
    /// </summary>
    double MaximumWaitSeconds { get; }

    /// <summary>
    /// How long an agent holding control may send nothing at all before the banner
    /// says it is not responding.
    /// </summary>
    /// <remarks>
    /// The signal is silence, not time held: filling a large model legitimately
    /// takes minutes, and every command an agent sends — a read as much as a
    /// write — is proof it is still there.
    /// </remarks>
    double SilenceWarningSeconds { get; }

    /// <summary>
    /// How much longer after the warning before control returns to the user. A
    /// further amount rather than a second threshold, so the warning is always
    /// seen before control changes hands.
    /// </summary>
    double SilenceGraceSeconds { get; }

    /// <summary>
    /// Whether an agent may drive the interface itself rather than only the
    /// command surface — clicking buttons, dragging handles, pressing keys.
    /// </summary>
    /// <remarks>
    /// Off by default. It exists so the editor can exercise its own interface —
    /// the transform handles above all, which no semantic command can stand in for
    /// — and not as a way to work: an agent that clicks where it could have called
    /// <c>set-voxels</c> is an agent slowly making the command surface redundant.
    /// </remarks>
    bool InputInjectionEnabled { get; }

    /// <summary>
    /// Which corner of the viewport the navigation cube sits in. A display setting
    /// rather than a term of the handover, and the first of its kind here. The form
    /// has a display page now; this one has no row on it yet, so it is set in the
    /// file.
    /// </summary>
    ViewCubeCorner ViewCubeCorner { get; }

    /// <summary>
    /// Whether the chrome animates at all. Off, every transition lands at once.
    /// </summary>
    bool AnimationsEnabled { get; }

    /// <summary>
    /// How long a transition takes, as a multiple of the shipped pace. What each
    /// position <em>means</em> is the view's answer, not this layer's: the file
    /// stores the position the user picked.
    /// </summary>
    AnimationSpeed AnimationSpeed { get; }

    /// <summary>Which way round the chrome is drawn.</summary>
    EditorTheme Theme { get; }

    /// <summary>
    /// Which language the interface is written in. Which catalogue that loads is
    /// the localization layer's answer, not this one's: the file stores the
    /// language the user picked.
    /// </summary>
    EditorLanguage Language { get; }

    /// <summary>
    /// The accent, as a canonical <c>#RRGGBB</c>. A string rather than a colour
    /// because this layer is files and numbers; the one place that turns six hex
    /// digits into a colour is the layer that draws with it.
    /// </summary>
    string AccentColor { get; }

    /// <summary>
    /// Where each chrome panel stands — one entry per <see cref="PanelKind"/>,
    /// orders normalized per side. Display settings like the cube's corner; see
    /// <c>docs/panels.md</c>.
    /// </summary>
    IReadOnlyList<PanelPlacement> PanelPlacements { get; }

    /// <summary>
    /// How wide each side's panel bodies are — the grip's other half, stored and
    /// clamped on the placements' own terms.
    /// </summary>
    PanelWidths PanelWidths { get; }

    /// <summary>
    /// What the render form was last set to. Not part of
    /// <see cref="PreferenceValues"/> for the reason the panel widths are not:
    /// the settings form does not edit it, and a form that carried a value it
    /// never shows would write back whatever it happened to be holding.
    /// </summary>
    RenderDials RenderDials { get; }

    /// <summary>
    /// How big the render card is on screen, in canvas units. Stored beside the
    /// dials and for the same reason — somebody who dragged the card to the size
    /// they wanted should not have to do it again — and deliberately separate from
    /// them, because it says nothing about what is being rendered.
    /// </summary>
    Vector2 RenderCardSize { get; }

    /// <summary>
    /// Whether the render card's settings are unfolded. Remembered on the card
    /// size's own terms: somebody who folded them away to see the picture should
    /// not have to do it again.
    /// </summary>
    bool RenderDialsOpen { get; }

    /// <summary>
    /// Whether the material section shows its tuning rows. How somebody likes to
    /// work rather than a fact about the model, so it belongs here and not in the
    /// document — and it outlives the window for the same reason the render card's
    /// fold does: having chosen once is choosing.
    /// </summary>
    bool MaterialAdvanced { get; }

    /// <summary>Which notation the colour row is written in.</summary>
    ColorTextFormat MaterialColorFormat { get; }

    /// <summary>Whether the material preview's little viewport is unfolded.</summary>
    bool MaterialPreviewShown { get; }

    /// <summary>
    /// Whether the viewport bakes the occlusion the cells crowding a corner earn.
    /// On by default: it is what makes a model read as solid rather than as flat
    /// paint, and it is computed once per remesh rather than per frame.
    /// </summary>
    bool ViewportOcclusion { get; }

    /// <summary>Whether the key light casts a shadow in the viewport.</summary>
    bool ViewportShadows { get; }

    /// <summary>
    /// Whether a material's facture is laid over its surface in the viewport. Off,
    /// a model is its flat colours — which is also what leaves the editor, so this
    /// doubles as "show me what an export will carry".
    /// </summary>
    bool ViewportFactures { get; }

    /// <summary>
    /// Whether the chrome's panels are glass. Off, no panel reads the screen at
    /// all and the surfaces are a flat fill — the point being the reading, not the
    /// look: a blurred pane is a backbuffer copy per panel, and a setting that
    /// merely painted them opaque would cost exactly what it claimed to save.
    /// </summary>
    bool GlassPanels { get; }

    /// <summary>
    /// Whether a recovery snapshot of the open document is kept while it is
    /// worked on. It is written beside the editor's own settings, never into the
    /// user's file, and the next launch offers it back if the editor stopped
    /// before a save.
    /// </summary>
    bool AutosaveEnabled { get; }

    /// <summary>
    /// Seconds between snapshots, counted from the last one and honoured only
    /// when something changed since it. Clamped like every other threshold.
    /// </summary>
    double AutosaveIntervalSeconds { get; }

    /// <summary>
    /// Where the snapshots go: an <c>autosave</c> folder beside the preferences
    /// file. Derived from the directory this layer was handed, never stored —
    /// the same shape as <see cref="ClientConfigPath"/>'s default and
    /// <see cref="McpUrl"/>.
    /// </summary>
    string AutosaveDirectory { get; }

    /// <summary>The endpoint an agent connects to, composed from the port.</summary>
    string McpUrl { get; }

    /// <summary>
    /// Stores <paramref name="values"/>, clamping anything out of range rather
    /// than throwing — the same bargain the model makes with a transform.
    /// </summary>
    void Apply(PreferenceValues values);

    /// <summary>Reads the current values back, ready to fill a form with.</summary>
    PreferenceValues GetValues();

    /// <summary>
    /// Stores panel placements alone — a pin or fold click is not a settings
    /// form, and must not re-apply values some open dialog is halfway through
    /// editing. A panel the list does not name stays where it was.
    /// </summary>
    void ApplyPanelPlacements(IReadOnlyList<PanelPlacement> placements);

    /// <summary>
    /// Stores the side widths alone, on <see cref="ApplyPanelPlacements"/>'s own
    /// argument: a resize drag is not a settings form.
    /// </summary>
    void ApplyPanelWidths(PanelWidths widths);

    /// <summary>Remembers what the render form was set to, held inside its bounds.</summary>
    void ApplyRenderDials(RenderDials dials);

    void ApplyRenderCardSize(Vector2 size);

    void ApplyRenderDialsOpen(bool open);

    /// <summary>Remembers how the material section is being worked in.</summary>
    void ApplyMaterialAdvanced(bool advanced);

    void ApplyMaterialColorFormat(ColorTextFormat format);

    void ApplyMaterialPreviewShown(bool shown);

    void ApplyViewportOcclusion(bool enabled);

    void ApplyViewportShadows(bool enabled);

    void ApplyViewportFactures(bool enabled);

    void ApplyGlassPanels(bool enabled);

    /// <summary>
    /// Issues a new token and forgets the old one, which is the only recourse when
    /// one has been copied somewhere it should not have been.
    /// </summary>
    void RegenerateToken();
}
