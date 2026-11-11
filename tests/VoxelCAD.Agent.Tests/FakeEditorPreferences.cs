using System;
using Godot;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Preferences;

namespace VoxelCAD.Agent.Tests;

/// <summary>
/// Preferences held in memory. The endpoint only ever reads them — it is driven
/// by <c>Apply</c> from outside, never by <c>Changed</c> — so the write half of
/// the interface refuses rather than pretending to store anything.
/// </summary>
internal sealed class FakeEditorPreferences : IEditorPreferences
{
    public event Action? Changed { add { } remove { } }

    public bool IsFirstRun => false;

    public bool McpEnabled { get; set; } = true;

    public int McpPort { get; set; }

    public string McpToken { get; set; } = "token-one";

    public string ClientConfigPath { get; set; } = string.Empty;

    public bool CountdownEnabled => true;

    public double CountdownSeconds => 3.0;

    public double MaximumWaitSeconds => 30.0;

    public double SilenceWarningSeconds => 60.0;

    public double SilenceGraceSeconds => 30.0;

    public bool InputInjectionEnabled => false;

    public ViewCubeCorner ViewCubeCorner => ViewCubeCorner.TopRight;

    public bool AnimationsEnabled => true;

    // The viewport's optional effects and the chrome's frosting. On, as a file that
    // has never heard of them reads; the endpoint neither reads nor writes them.
    public bool ViewportOcclusion => true;

    public bool ViewportShadows => true;

    public bool ViewportFactures => true;

    public bool GlassPanels => true;

    public AnimationSpeed AnimationSpeed => AnimationSpeed.Fast;

    public EditorTheme Theme => EditorTheme.Dark;

    public EditorLanguage Language => EditorLanguage.English;

    public string AccentColor => "#70BAFA";

    public IReadOnlyList<PanelPlacement> PanelPlacements => Array.Empty<PanelPlacement>();

    public PanelWidths PanelWidths => new(PanelWidths.Default, PanelWidths.Default);

    // The renderer's own defaults rather than a positional copy of them: restated,
    // this one was already a dial short of the record it was building.
    public RenderDials RenderDials => RenderDials.Default;

    public Vector2 RenderCardSize =>
        new(RenderLimits.DefaultCardWidth, RenderLimits.DefaultCardHeight);

    public bool RenderDialsOpen => true;

    public bool MaterialAdvanced => false;

    public ColorTextFormat MaterialColorFormat => ColorTextFormat.Hex;

    public bool MaterialPreviewShown => false;

    public bool AutosaveEnabled => true;

    public double AutosaveIntervalSeconds => 120.0;

    public string AutosaveDirectory => string.Empty;

    public string McpUrl => $"http://127.0.0.1:{McpPort}/mcp";

    public void Apply(PreferenceValues values) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void ApplyPanelPlacements(IReadOnlyList<PanelPlacement> placements) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void ApplyRenderDials(RenderDials dials)
    {
    }

    public void ApplyRenderCardSize(Vector2 size)
    {
    }

    public void ApplyRenderDialsOpen(bool open)
    {
    }

    public void ApplyMaterialAdvanced(bool advanced)
    {
    }

    public void ApplyMaterialColorFormat(ColorTextFormat format)
    {
    }

    public void ApplyMaterialPreviewShown(bool shown)
    {
    }

    public void ApplyViewportOcclusion(bool enabled) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void ApplyViewportShadows(bool enabled) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void ApplyViewportFactures(bool enabled) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void ApplyGlassPanels(bool enabled) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void ApplyPanelWidths(PanelWidths widths) =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public PreferenceValues GetValues() =>
        throw new NotSupportedException("The endpoint only reads preferences.");

    public void RegenerateToken() =>
        throw new NotSupportedException("The endpoint only reads preferences.");
}
