using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Preferences;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Preferences held in memory. The real ones are a file, and what they are stored
/// in is not what a presenter is being tested on.
/// </summary>
internal sealed class FakePreferences : IEditorPreferences
{
    private PreferenceValues _values = new(
        McpEnabled: true,
        McpPort: 8971,
        ClientConfigPath: "/tmp/voxelcad/mcp.json",
        CountdownEnabled: true,
        CountdownSeconds: 3.0,
        MaximumWaitSeconds: 30.0,
        SilenceWarningSeconds: 60.0,
        SilenceGraceSeconds: 30.0,
        InputInjectionEnabled: false,
        ViewCubeCorner: ViewCubeCorner.TopRight,
        PanelPlacements: new[]
        {
            new PanelPlacement(PanelKind.Toolbar, PanelSide.Left, 0, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.Project, PanelSide.Left, 1, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.Inspector, PanelSide.Right, 0, Pinned: true, Folded: false)
        },
        AnimationsEnabled: true,
        AnimationSpeed: AnimationSpeed.Fast,
        Theme: EditorTheme.Dark,
        Language: EditorLanguage.English,
        AccentColor: "#70BAFA",
        ViewportOcclusion: true,
        ViewportShadows: true,
        ViewportFactures: true,
        GlassPanels: true,

        // Off, unlike the real default, and on purpose: the shell tests dispose
        // modified documents by the dozen, and every one of them would otherwise
        // write a recovery snapshot to disk on its way out. The autosave tests
        // switch it on and say so.
        AutosaveEnabled: false,
        AutosaveIntervalSeconds: 120.0);

    public event Action? Changed;

    /// <summary>
    /// False unless a test says otherwise: a first run opens a sample, and every
    /// other shell test expects the empty project it has always started with.
    /// </summary>
    public bool IsFirstRun { get; init; }

    public bool McpEnabled => _values.McpEnabled;

    public int McpPort => _values.McpPort;

    public string McpToken { get; private set; } = "token-one";

    public string ClientConfigPath => _values.ClientConfigPath;

    public bool CountdownEnabled => _values.CountdownEnabled;

    public double CountdownSeconds => _values.CountdownSeconds;

    public double MaximumWaitSeconds => _values.MaximumWaitSeconds;

    public double SilenceWarningSeconds => _values.SilenceWarningSeconds;

    public double SilenceGraceSeconds => _values.SilenceGraceSeconds;

    public bool InputInjectionEnabled => _values.InputInjectionEnabled;

    public ViewCubeCorner ViewCubeCorner => _values.ViewCubeCorner;

    public bool AnimationsEnabled => _values.AnimationsEnabled;

    public AnimationSpeed AnimationSpeed => _values.AnimationSpeed;

    public EditorTheme Theme => _values.Theme;

    public EditorLanguage Language => _values.Language;

    public string AccentColor => _values.AccentColor;

    public IReadOnlyList<PanelPlacement> PanelPlacements => _values.PanelPlacements;

    public PanelWidths PanelWidths { get; private set; } =
        new(PanelWidths.Default, PanelWidths.Default);

    public RenderDials RenderDials { get; private set; } = new(
        Width: 1280,
        Height: 720,
        Samples: 256,
        Exposure: 1f,
        SunElevationDegrees: 70f,
        SunAzimuthDegrees: 120f,
        SunSizeDegrees: 2f,
        Glare: 0.06f,
        Backdrop: RenderBackdrop.Environment,
        Ground: true);

    public string McpUrl => $"http://127.0.0.1:{McpPort}/mcp";

    /// <summary>What was last applied, so a test can see what the form sent.</summary>
    public PreferenceValues? Applied { get; private set; }

    public int TokensIssued { get; private set; }

    public void Apply(PreferenceValues values)
    {
        _values = values;
        Applied = values;

        Changed?.Invoke();
    }

    public PreferenceValues GetValues() => _values;

    /// <summary>
    /// Raised, because the real one raises it: every write here goes through the
    /// same Store that saves the file and announces it, and a fake that stayed
    /// quiet would hide every consumer that follows the announcement — the
    /// viewport's camera frustum being the first of them.
    /// </summary>
    public void ApplyRenderDials(RenderDials dials)
    {
        RenderDials = dials;
        Changed?.Invoke();
    }

    public Vector2 RenderCardSize { get; private set; } =
        new(RenderLimits.DefaultCardWidth, RenderLimits.DefaultCardHeight);

    public void ApplyRenderCardSize(Vector2 size)
    {
        RenderCardSize = size;
        Changed?.Invoke();
    }

    public bool RenderDialsOpen { get; private set; } = true;

    public void ApplyRenderDialsOpen(bool open)
    {
        RenderDialsOpen = open;
        Changed?.Invoke();
    }

    public bool MaterialAdvanced { get; private set; }

    public void ApplyMaterialAdvanced(bool advanced)
    {
        MaterialAdvanced = advanced;
        Changed?.Invoke();
    }

    public ColorTextFormat MaterialColorFormat { get; private set; } = ColorTextFormat.Hex;

    public void ApplyMaterialColorFormat(ColorTextFormat format)
    {
        MaterialColorFormat = format;
        Changed?.Invoke();
    }

    public bool MaterialPreviewShown { get; private set; }

    public void ApplyMaterialPreviewShown(bool shown)
    {
        MaterialPreviewShown = shown;
        Changed?.Invoke();
    }

    // Read out of the form's own values rather than kept beside them, because the
    // real one keeps them in the one document Apply writes — a field of its own
    // here would make `Apply(GetValues() with { ... })` silently do nothing, which
    // is exactly the shape of "a fake quieter than the real thing".
    public bool ViewportOcclusion => _values.ViewportOcclusion;

    public bool ViewportShadows => _values.ViewportShadows;

    public bool ViewportFactures => _values.ViewportFactures;

    public bool GlassPanels => _values.GlassPanels;

    public bool AutosaveEnabled => _values.AutosaveEnabled;

    public double AutosaveIntervalSeconds => _values.AutosaveIntervalSeconds;

    /// <summary>
    /// A directory of this test's own, never created unless something writes
    /// into it — which nothing does while the switch above is off.
    /// </summary>
    public string AutosaveDirectory { get; set; } =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"voxelcad-autosave-{Guid.NewGuid():N}");

    public void ApplyViewportOcclusion(bool enabled) =>
        Apply(_values with { ViewportOcclusion = enabled });

    public void ApplyViewportShadows(bool enabled) =>
        Apply(_values with { ViewportShadows = enabled });

    public void ApplyViewportFactures(bool enabled) =>
        Apply(_values with { ViewportFactures = enabled });

    public void ApplyGlassPanels(bool enabled) =>
        Apply(_values with { GlassPanels = enabled });

    public void ApplyPanelWidths(PanelWidths widths)
    {
        PanelWidths = widths;

        Changed?.Invoke();
    }

    public void ApplyPanelPlacements(IReadOnlyList<PanelPlacement> placements)
    {
        _values = _values with
        {
            PanelPlacements = _values.PanelPlacements
                .Select(current =>
                    placements.FirstOrDefault(
                        requested => requested.Kind == current.Kind,
                        current))
                .ToList()
        };

        Changed?.Invoke();
    }

    public void RegenerateToken()
    {
        TokensIssued++;
        McpToken = $"token-{TokensIssued + 1}";

        Changed?.Invoke();
    }
}
