using VoxelCAD.CommonTypes;

namespace VoxelCAD.Preferences;

/// <summary>
/// The stored shape, flat and named so the file stays worth opening in an editor.
/// </summary>
/// <remarks>
/// <see cref="Version"/> is here for the same reason the voxel blob carries one:
/// so the layout can change later without a migration format having to be
/// invented first. Anything missing from the file keeps the default beside it.
/// </remarks>
internal sealed class PreferencesDocument
{
    public const int CurrentVersion = 1;

    /// <summary>Loopback, above the privileged range, and clear of Beckett's 8770.</summary>
    public const int DefaultPort = 8971;

    public const double DefaultCountdownSeconds = 3.0;

    /// <summary>Long enough that a focused field is not a nuisance, short enough to heal.</summary>
    public const double DefaultMaximumWaitSeconds = 30.0;

    /// <summary>
    /// Past a minute of no commands at all, an agent is worth asking about. Well
    /// clear of the tens of seconds a live one spends deciding on its next call.
    /// </summary>
    public const double DefaultSilenceWarningSeconds = 60.0;

    public const double DefaultSilenceGraceSeconds = 30.0;

    /// <summary>
    /// Two minutes: at most that much work is lost to a crash, and a snapshot of
    /// a large model costs a fraction of a second, so more often would be paying
    /// for a promise nobody feels.
    /// </summary>
    public const double DefaultAutosaveIntervalSeconds = 120.0;

    public int Version { get; set; } = CurrentVersion;

    public bool McpEnabled { get; set; } = true;

    public int McpPort { get; set; } = DefaultPort;

    /// <summary>Empty until first run, which is when one is generated.</summary>
    public string McpToken { get; set; } = string.Empty;

    /// <summary>Empty means "beside the preferences file"; resolved on load.</summary>
    public string ClientConfigPath { get; set; } = string.Empty;

    public bool CountdownEnabled { get; set; } = true;

    public double CountdownSeconds { get; set; } = DefaultCountdownSeconds;

    public double MaximumWaitSeconds { get; set; } = DefaultMaximumWaitSeconds;

    public double SilenceWarningSeconds { get; set; } = DefaultSilenceWarningSeconds;

    public double SilenceGraceSeconds { get; set; } = DefaultSilenceGraceSeconds;

    /// <summary>
    /// Off, and a missing key reads as off — which is the point. It is a debug
    /// instrument, so an editor that has never been told otherwise does not have
    /// one switched on.
    /// </summary>
    public bool InputInjectionEnabled { get; set; }

    /// <summary>
    /// Where the navigation cube sits. The default is the first member, so a
    /// missing key and a value outside the enum both land on the top right.
    /// </summary>
    public ViewCubeCorner ViewCubeCorner { get; set; } = ViewCubeCorner.TopRight;

    /// <summary>
    /// On, and a missing key reads as on. Unlike the debug switch, this is not
    /// an instrument: the chrome is built on the premise that nothing is placed
    /// outright, so animating is the look and the switch is the way out of it.
    /// </summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>
    /// A word — "fast", "medium", "slow" — for the reason a panel's side is one:
    /// the file is meant to stay worth opening in an editor. Empty means
    /// "whatever the default is" rather than a pace somebody would be wrong on.
    /// </summary>
    public string AnimationSpeed { get; set; } = string.Empty;

    /// <summary>"dark" or "light"; anything else reads as the shipped one.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>
    /// A BCP 47 code — "en", "ru", "de", "fr", "es", "pt-BR" — for the reason the
    /// pace is a word: the file is meant to stay worth opening in an editor.
    /// Anything else reads as English, which is the language the interface is
    /// written in and so the one a missing catalogue falls back to anyway.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The accent, as <c>#RRGGBB</c>. Anything else reads as the shipped one.</summary>
    public string AccentColor { get; set; } = string.Empty;

    /// <summary>
    /// Where each chrome panel stands (see <c>docs/panels.md</c>). One entry per
    /// panel, each with the default beside it, so a missing entry lands the panel
    /// exactly where a fresh editor puts it — the old two-dock layout.
    /// </summary>
    public PanelPlacementDocument PanelToolbar { get; set; } = new() { Side = "left", Order = 0 };

    public PanelPlacementDocument PanelProject { get; set; } = new() { Side = "left", Order = 1 };

    public PanelPlacementDocument PanelHistory { get; set; } = new() { Side = "left", Order = 2 };

    public PanelPlacementDocument PanelInspector { get; set; } = new() { Side = "right", Order = 0 };

    /// <summary>
    /// How wide each side's panel bodies are — one number per side, because a
    /// column's panels share their width (see <see cref="PanelWidths"/>).
    /// </summary>
    public double PanelWidthLeft { get; set; } = PanelWidths.Default;

    public double PanelWidthRight { get; set; } = PanelWidths.Default;

    // What the render form was last set to. Flat fields rather than a nested
    // object, like everything else here: the file is meant to stay worth opening
    // in an editor — but the values are RenderDials.Default's, so what the form has
    // to be able to express and what is stored when nobody has touched it are one
    // number apiece rather than two that agreed until they did not.
    public int RenderWidth { get; set; } = RenderDials.Default.Width;

    public int RenderHeight { get; set; } = RenderDials.Default.Height;

    public int RenderSamples { get; set; } = RenderDials.Default.Samples;

    public double RenderExposure { get; set; } = RenderDials.Default.Exposure;

    public double RenderSunElevation { get; set; } = RenderDials.Default.SunElevationDegrees;

    public double RenderSunAzimuth { get; set; } = RenderDials.Default.SunAzimuthDegrees;

    public double RenderSunSize { get; set; } = RenderDials.Default.SunSizeDegrees;

    public double RenderGlare { get; set; } = RenderDials.Default.Glare;

    /// <summary>Spelled rather than numbered, on the theme's own terms.</summary>
    public string RenderBackdrop { get; set; } = RenderDials.Default.Backdrop.ToString();

    public bool RenderGround { get; set; } = RenderDials.Default.Ground;

    /// <summary>How big the render card is on screen, not how big the picture is.</summary>
    public double RenderCardWidth { get; set; } = RenderLimits.DefaultCardWidth;

    public double RenderCardHeight { get; set; } = RenderLimits.DefaultCardHeight;

    /// <summary>Whether the render card's settings are unfolded.</summary>
    public bool RenderDialsOpen { get; set; } = true;

    /// <summary>Whether the material section shows its tuning rows.</summary>
    public bool MaterialAdvanced { get; set; }

    /// <summary>
    /// Which notation a colour is written in. Spelled rather than numbered, on the
    /// theme's own terms; anything unknown falls back to hex on load.
    /// </summary>
    public string MaterialColorFormat { get; set; } = ColorTextFormat.Hex.ToString();

    /// <summary>Whether the material preview's little viewport is unfolded.</summary>
    public bool MaterialPreviewShown { get; set; }

    /// <summary>
    /// What the viewport spends its frame on, and whether the chrome is glass.
    /// All four default to on, because on is what the editor has always looked
    /// like — a stored file that has never heard of them reads them as true, which
    /// is what these being nullable is for.
    /// </summary>
    public bool? ViewportOcclusion { get; set; }

    public bool? ViewportShadows { get; set; }

    public bool? ViewportFactures { get; set; }

    public bool? GlassPanels { get; set; }

    /// <summary>
    /// On, and a missing key reads as on: an editor that has never been asked
    /// keeps its user's work. A plain <c>bool</c> on <see cref="AnimationsEnabled"/>'s
    /// pattern rather than the viewport flags' nullable one — the storage writes
    /// a stored <c>false</c> as <c>false</c>, and a test holds it to that, because
    /// a default-ignoring serializer option added later would make a switched-off
    /// autosave vanish from the file and come back on.
    /// </summary>
    public bool AutosaveEnabled { get; set; } = true;

    public double AutosaveIntervalSeconds { get; set; } = DefaultAutosaveIntervalSeconds;
}

/// <summary>
/// One panel's stored placement. The side is a word rather than a number so the
/// file stays worth opening in an editor; anything that is not a known side —
/// including a missing key, which is why the default here is empty rather than
/// a side some panel would be wrong on — falls back to the panel's own default
/// on load.
/// </summary>
internal sealed class PanelPlacementDocument
{
    public string Side { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool Pinned { get; set; } = true;

    public bool Folded { get; set; }

    /// <summary>The dragged body height; zero means "as tall as the content".</summary>
    public double Height { get; set; }
}
