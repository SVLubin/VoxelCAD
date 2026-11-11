using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using VoxelCAD.CommonTypes;
using VoxelCAD.Localization;
using Godot;

// Both namespaces above carry the name, and only one of them generates keys.
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace VoxelCAD.Preferences;

/// <inheritdoc cref="IEditorPreferences"/>
internal sealed class EditorPreferences : IEditorPreferences
{
    /// <summary>Below this belongs to the system, and binding there needs privileges.</summary>
    private const int MinimumPort = 1024;

    private const int MaximumPort = 65535;

    /// <summary>
    /// Long enough that the countdown is a warning rather than a flicker, short
    /// enough that an agent asking politely is not left waiting a minute.
    /// </summary>
    private const double MinimumCountdownSeconds = 0.5;

    private const double MaximumCountdownSeconds = 60.0;

    /// <summary>
    /// A second is the shortest wait that is still a wait; ten minutes is longer
    /// than anyone would sit through before deciding the handover is broken.
    /// </summary>
    private const double MinimumWaitSeconds = 1.0;

    private const double MaximumWaitLimitSeconds = 600.0;

    /// <summary>
    /// Below five seconds every pause between an agent's calls would read as a
    /// death; above an hour nothing is being protected.
    /// </summary>
    private const double MinimumSilenceSeconds = 5.0;

    private const double MaximumSilenceSeconds = 3600.0;

    /// <summary>
    /// Ten seconds is the shortest interval at which a snapshot is still a
    /// backstop rather than a save on every keystroke; an hour is the longest at
    /// which it still protects anything.
    /// </summary>
    private const double MinimumAutosaveIntervalSeconds = 10.0;

    private const double MaximumAutosaveIntervalSeconds = 3600.0;

    /// <summary>The folder beside the preferences file that recovery snapshots go in.</summary>
    private const string AutosaveDirectoryName = "autosave";

    private const int TokenBytes = 24;

    /// <summary>Taller than any monitor's worth of canvas units.</summary>
    private const double MaximumPanelHeight = 4000.0;

    /// <summary>
    /// The accent the editor ships with — Godot's own editor blue, which is
    /// where the rest of the palette's derivation comes from.
    /// </summary>
    private const string DefaultAccent = "#70BAFA";

    private readonly string _directory;
    private readonly string _path;
    private readonly PreferencesDocument _document;

    private IReadOnlyList<PanelPlacement> _panelPlacements = Array.Empty<PanelPlacement>();
    private AnimationSpeed _animationSpeed = AnimationSpeed.Fast;
    private EditorTheme _theme = EditorTheme.Dark;
    private EditorLanguage _language = EditorLanguage.English;

    public EditorPreferences(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, PreferencesStorage.FileName);
        // Asked before anything below can write the file, which a first run always
        // does: the token is created and kept on the very first load.
        IsFirstRun = !File.Exists(_path);
        _document = PreferencesStorage.Load(_path);

        // First run, or a file somebody emptied out: the token has to exist before
        // anything can connect, and the config path has to be somewhere real.
        var repair = false;

        if (string.IsNullOrWhiteSpace(_document.McpToken))
        {
            _document.McpToken = CreateToken();
            repair = true;
        }

        if (string.IsNullOrWhiteSpace(_document.ClientConfigPath))
        {
            _document.ClientConfigPath = GetDefaultClientConfigPath();
            repair = true;
        }

        _document.McpPort = ClampPort(_document.McpPort);
        _document.CountdownSeconds = ClampCountdown(_document.CountdownSeconds);
        _document.MaximumWaitSeconds = ClampWait(_document.MaximumWaitSeconds);
        _document.SilenceWarningSeconds = ClampSilenceWarning(_document.SilenceWarningSeconds);
        _document.SilenceGraceSeconds = ClampSilenceGrace(_document.SilenceGraceSeconds);
        _document.AutosaveIntervalSeconds = ClampAutosaveInterval(_document.AutosaveIntervalSeconds);
        _document.ViewCubeCorner = ResolveCorner(_document.ViewCubeCorner);
        _document.PanelWidthLeft = ClampPanelWidth(_document.PanelWidthLeft);
        _document.PanelWidthRight = ClampPanelWidth(_document.PanelWidthRight);

        // Repaired on load as well as on apply, or a hand-edited word stays
        // wrong in the file until the next save.
        _animationSpeed = ParseSpeed(_document.AnimationSpeed) ?? AnimationSpeed.Fast;
        _document.AnimationSpeed = FormatSpeed(_animationSpeed);

        _theme = ParseTheme(_document.Theme) ?? EditorTheme.Dark;
        _document.Theme = FormatTheme(_theme);

        _language = LanguageCodes.TryParse(_document.Language) ?? EditorLanguage.English;
        _document.Language = LanguageCodes.ToCode(_language);
        _document.AccentColor = ResolveAccent(_document.AccentColor);
        NormalizePanelPlacements();
        _document.Version = PreferencesDocument.CurrentVersion;

        if (repair)
            PreferencesStorage.TrySave(_path, _document);
    }

    public event Action? Changed;

    public bool IsFirstRun { get; }

    public bool McpEnabled => _document.McpEnabled;

    public int McpPort => _document.McpPort;

    public string McpToken => _document.McpToken;

    public string ClientConfigPath => _document.ClientConfigPath;

    public bool CountdownEnabled => _document.CountdownEnabled;

    public double CountdownSeconds => _document.CountdownSeconds;

    public double MaximumWaitSeconds => _document.MaximumWaitSeconds;

    public double SilenceWarningSeconds => _document.SilenceWarningSeconds;

    public double SilenceGraceSeconds => _document.SilenceGraceSeconds;

    public bool InputInjectionEnabled => _document.InputInjectionEnabled;

    public ViewCubeCorner ViewCubeCorner => _document.ViewCubeCorner;

    public bool AnimationsEnabled => _document.AnimationsEnabled;

    public AnimationSpeed AnimationSpeed => _animationSpeed;

    public EditorTheme Theme => _theme;

    public EditorLanguage Language => _language;

    public string AccentColor => _document.AccentColor;

    public IReadOnlyList<PanelPlacement> PanelPlacements => _panelPlacements;

    public PanelWidths PanelWidths =>
        new(_document.PanelWidthLeft, _document.PanelWidthRight);

    public string McpUrl => $"http://127.0.0.1:{_document.McpPort}/mcp";

    public bool AutosaveEnabled => _document.AutosaveEnabled;

    public double AutosaveIntervalSeconds => _document.AutosaveIntervalSeconds;

    public string AutosaveDirectory => Path.Combine(_directory, AutosaveDirectoryName);

    public void Apply(PreferenceValues values)
    {
        _document.McpEnabled = values.McpEnabled;
        _document.McpPort = ClampPort(values.McpPort);
        _document.CountdownEnabled = values.CountdownEnabled;
        _document.CountdownSeconds = ClampCountdown(values.CountdownSeconds);
        _document.MaximumWaitSeconds = ClampWait(values.MaximumWaitSeconds);
        _document.SilenceWarningSeconds = ClampSilenceWarning(values.SilenceWarningSeconds);
        _document.SilenceGraceSeconds = ClampSilenceGrace(values.SilenceGraceSeconds);
        _document.InputInjectionEnabled = values.InputInjectionEnabled;
        _document.ViewCubeCorner = ResolveCorner(values.ViewCubeCorner);
        _document.AnimationsEnabled = values.AnimationsEnabled;
        _document.ViewportOcclusion = values.ViewportOcclusion;
        _document.ViewportShadows = values.ViewportShadows;
        _document.ViewportFactures = values.ViewportFactures;
        _document.GlassPanels = values.GlassPanels;
        _document.AutosaveEnabled = values.AutosaveEnabled;
        _document.AutosaveIntervalSeconds = ClampAutosaveInterval(values.AutosaveIntervalSeconds);

        // A set with no nearest member, so it falls back rather than clamping —
        // the corner's own rule.
        _animationSpeed = Enum.IsDefined(values.AnimationSpeed)
            ? values.AnimationSpeed
            : AnimationSpeed.Fast;

        _document.AnimationSpeed = FormatSpeed(_animationSpeed);

        _theme = Enum.IsDefined(values.Theme) ? values.Theme : EditorTheme.Dark;
        _document.Theme = FormatTheme(_theme);

        _language = Enum.IsDefined(values.Language) ? values.Language : EditorLanguage.English;
        _document.Language = LanguageCodes.ToCode(_language);
        _document.AccentColor = ResolveAccent(values.AccentColor);

        // A default record carries no list; a form that never showed the panels
        // hands back whatever it was given, and either way nothing moves.
        if (values.PanelPlacements is not null)
            WritePanelPlacements(values.PanelPlacements);

        // A field left blank means "wherever it goes by default", not "nowhere".
        _document.ClientConfigPath = string.IsNullOrWhiteSpace(values.ClientConfigPath)
            ? GetDefaultClientConfigPath()
            : values.ClientConfigPath.Trim();

        Store();
    }

    public PreferenceValues GetValues() =>
        new(
            McpEnabled,
            McpPort,
            ClientConfigPath,
            CountdownEnabled,
            CountdownSeconds,
            MaximumWaitSeconds,
            SilenceWarningSeconds,
            SilenceGraceSeconds,
            InputInjectionEnabled,
            ViewCubeCorner,
            PanelPlacements,
            AnimationsEnabled,
            AnimationSpeed,
            Theme,
            AccentColor,
            Language,
            ViewportOcclusion,
            ViewportShadows,
            ViewportFactures,
            GlassPanels,
            AutosaveEnabled,
            AutosaveIntervalSeconds);

    public void ApplyPanelPlacements(IReadOnlyList<PanelPlacement> placements)
    {
        if (placements is null)
            throw new ArgumentNullException(nameof(placements));

        WritePanelPlacements(placements);
        Store();
    }

    public RenderDials RenderDials => new(
        Width: Math.Clamp(_document.RenderWidth, RenderLimits.MinimumSize, RenderLimits.MaximumSize),
        Height: Math.Clamp(_document.RenderHeight, RenderLimits.MinimumSize, RenderLimits.MaximumSize),
        Samples: Math.Clamp(
            _document.RenderSamples,
            RenderLimits.MinimumSamples,
            RenderLimits.MaximumSamples),
        Exposure: Clamp(
            _document.RenderExposure,
            RenderLimits.MinimumExposure,
            RenderLimits.MaximumExposure),
        SunElevationDegrees: Clamp(
            _document.RenderSunElevation,
            -RenderLimits.MaximumElevation,
            RenderLimits.MaximumElevation),
        SunAzimuthDegrees: Clamp(
            _document.RenderSunAzimuth,
            -RenderLimits.MaximumAzimuth,
            RenderLimits.MaximumAzimuth),
        SunSizeDegrees: Clamp(
            _document.RenderSunSize,
            RenderLimits.MinimumSunRadius,
            RenderLimits.MaximumSunRadius),
        Glare: Clamp(_document.RenderGlare, 0.0, RenderLimits.MaximumGlare),
        Backdrop: Read(_document.RenderBackdrop, RenderBackdrop.Environment),
        Ground: _document.RenderGround);

    /// <summary>
    /// Clamped rather than refused, and to bounds the card's own grip shares, so a
    /// hand-edited file and a drag cannot disagree about how small a card may be.
    /// </summary>
    public Vector2 RenderCardSize => new(
        Clamp(
            _document.RenderCardWidth,
            RenderLimits.MinimumCardWidth,
            RenderLimits.MaximumCardSide),
        Clamp(
            _document.RenderCardHeight,
            RenderLimits.MinimumCardHeight,
            RenderLimits.MaximumCardSide));

    public bool RenderDialsOpen => _document.RenderDialsOpen;

    public bool MaterialAdvanced => _document.MaterialAdvanced;

    /// <remarks>
    /// A notation nobody has heard of reads back as hex rather than throwing — the
    /// same bargain the theme and the animation pace make with a damaged file.
    /// </remarks>
    public ColorTextFormat MaterialColorFormat =>
        Enum.TryParse(_document.MaterialColorFormat, ignoreCase: true, out ColorTextFormat format)
        && Enum.IsDefined(typeof(ColorTextFormat), format)
            ? format
            : ColorTextFormat.Hex;

    public bool MaterialPreviewShown => _document.MaterialPreviewShown;

    // Nullable in the document and true here when unset: a file written before
    // these existed must read as the editor everybody already knows.
    public bool ViewportOcclusion => _document.ViewportOcclusion ?? true;

    public bool ViewportShadows => _document.ViewportShadows ?? true;

    public bool ViewportFactures => _document.ViewportFactures ?? true;

    public bool GlassPanels => _document.GlassPanels ?? true;

    public void ApplyRenderDialsOpen(bool open)
    {
        _document.RenderDialsOpen = open;

        Store();
    }

    public void ApplyMaterialAdvanced(bool advanced)
    {
        _document.MaterialAdvanced = advanced;

        Store();
    }

    public void ApplyMaterialColorFormat(ColorTextFormat format)
    {
        _document.MaterialColorFormat = format.ToString();

        Store();
    }

    public void ApplyMaterialPreviewShown(bool shown)
    {
        _document.MaterialPreviewShown = shown;

        Store();
    }

    public void ApplyViewportOcclusion(bool enabled)
    {
        _document.ViewportOcclusion = enabled;

        Store();
    }

    public void ApplyViewportShadows(bool enabled)
    {
        _document.ViewportShadows = enabled;

        Store();
    }

    public void ApplyViewportFactures(bool enabled)
    {
        _document.ViewportFactures = enabled;

        Store();
    }

    public void ApplyGlassPanels(bool enabled)
    {
        _document.GlassPanels = enabled;

        Store();
    }

    public void ApplyRenderCardSize(Vector2 size)
    {
        _document.RenderCardWidth = size.X;
        _document.RenderCardHeight = size.Y;

        Store();
    }

    public void ApplyRenderDials(RenderDials dials)
    {
        _document.RenderWidth = dials.Width;
        _document.RenderHeight = dials.Height;
        _document.RenderSamples = dials.Samples;
        _document.RenderExposure = dials.Exposure;
        _document.RenderSunElevation = dials.SunElevationDegrees;
        _document.RenderSunAzimuth = dials.SunAzimuthDegrees;
        _document.RenderSunSize = dials.SunSizeDegrees;
        _document.RenderGlare = dials.Glare;
        _document.RenderBackdrop = dials.Backdrop.ToString();
        _document.RenderGround = dials.Ground;

        Store();
    }

    /// <summary>
    /// A float held between two bounds, from a file that may say anything. Clamped
    /// rather than refused, and a value that is not a number at all falls to the
    /// lower bound rather than travelling on as one.
    /// </summary>
    private static float Clamp(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? (float)Math.Clamp(value, minimum, maximum) : (float)minimum;

    /// <summary>A spelled enum from a file, or the default where the word is not one.</summary>
    private static T Read<T>(string text, T fallback) where T : struct, Enum =>
        Enum.TryParse(text, ignoreCase: true, out T value) && Enum.IsDefined(value)
            ? value
            : fallback;

    public void ApplyPanelWidths(PanelWidths widths)
    {
        _document.PanelWidthLeft = ClampPanelWidth(widths.Left);
        _document.PanelWidthRight = ClampPanelWidth(widths.Right);

        Store();
    }

    public void RegenerateToken()
    {
        _document.McpToken = CreateToken();

        Store();
    }

    private void Store()
    {
        PreferencesStorage.TrySave(_path, _document);

        // Raised whether or not the write landed: what the running editor is now
        // using has changed either way, and a settings surface that appeared to
        // do nothing would be the worse lie.
        Changed?.Invoke();
    }

    private string GetDefaultClientConfigPath() =>
        Path.Combine(_directory, McpClientConfigWriter.DefaultFileName);

    private static string CreateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes)).ToLowerInvariant();

    private static int ClampPort(int port) => Math.Clamp(port, MinimumPort, MaximumPort);

    /// <summary>
    /// A width the chrome would actually draw. The bounds are
    /// <see cref="CommonTypes.PanelWidths"/>'s own, so what a file may say and
    /// what a drag may ask for cannot come apart.
    /// </summary>
    private static double ClampPanelWidth(double width) =>
        double.IsFinite(width)
            ? Math.Clamp(width, CommonTypes.PanelWidths.Minimum, CommonTypes.PanelWidths.Maximum)
            : CommonTypes.PanelWidths.Default;

    /// <summary>
    /// Zero — and anything that is not a height — means "as tall as the content";
    /// the real floor is per panel and lives with the cascade, so this only keeps
    /// a hand-edited file from storing nonsense.
    /// </summary>
    private static double ClampPanelHeight(double height) =>
        double.IsFinite(height) && height > 0.0 ? Math.Min(height, MaximumPanelHeight) : 0.0;

    /// <summary>
    /// A corner the enum actually has. An enum is an int underneath, so a hand-
    /// edited file can name a fifth corner — and out of range falls back to the
    /// default rather than being clamped, because the four are a set with no
    /// nearest member, unlike a port or a number of seconds.
    /// </summary>
    private static ViewCubeCorner ResolveCorner(ViewCubeCorner corner) =>
        Enum.IsDefined(corner) ? corner : ViewCubeCorner.TopRight;

    /// <summary>
    /// Reads the stored panel entries, resolves anything a hand-edited file made
    /// up, renumbers each side 0..n-1, and writes the tidied answer back — the
    /// same repair-on-load bargain every other value here makes.
    /// </summary>
    private void NormalizePanelPlacements() =>
        SetPanelPlacements(new[]
        {
            ReadPanel(PanelKind.Toolbar, _document.PanelToolbar),
            ReadPanel(PanelKind.Project, _document.PanelProject),
            ReadPanel(PanelKind.History, _document.PanelHistory),
            ReadPanel(PanelKind.Inspector, _document.PanelInspector)
        });

    /// <summary>
    /// Overlays <paramref name="placements"/> on what is stored — a panel the
    /// caller does not name stays where it was — then normalizes and keeps it.
    /// </summary>
    private void WritePanelPlacements(IReadOnlyList<PanelPlacement> placements)
    {
        var merged = new List<PanelPlacement>();

        foreach (PanelPlacement current in _panelPlacements)
        {
            PanelPlacement candidate = current;

            foreach (PanelPlacement requested in placements)
            {
                if (requested.Kind != current.Kind)
                    continue;

                candidate = Enum.IsDefined(requested.Side)
                    ? requested
                    : requested with { Side = current.Side };
                break;
            }

            merged.Add(candidate);
        }

        SetPanelPlacements(merged);
    }

    /// <summary>Renumbers each side by stored order (ties by kind), then stores.</summary>
    private void SetPanelPlacements(IReadOnlyList<PanelPlacement> placements)
    {
        var normalized = new List<PanelPlacement>(placements.Count);

        foreach (PanelSide side in Enum.GetValues<PanelSide>())
        {
            var column = placements
                .Where(placement => placement.Side == side)
                .OrderBy(placement => placement.Order)
                .ThenBy(placement => (int)placement.Kind)
                .ToList();

            for (var index = 0; index < column.Count; index++)
                normalized.Add(column[index] with { Order = index });
        }

        _panelPlacements = normalized
            .OrderBy(placement => (int)placement.Kind)
            .ToList();

        foreach (PanelPlacement placement in _panelPlacements)
            WritePanelDocument(placement);
    }

    private PanelPlacement ReadPanel(PanelKind kind, PanelPlacementDocument? entry)
    {
        PanelPlacement fallback = DefaultPlacement(kind);

        if (entry is null)
            return fallback;

        PanelSide? side = ParseSide(entry.Side);

        return new PanelPlacement(
            kind,
            side ?? fallback.Side,
            entry.Order,
            entry.Pinned,
            entry.Folded,
            ClampPanelHeight(entry.Height));
    }

    private void WritePanelDocument(PanelPlacement placement)
    {
        var entry = new PanelPlacementDocument
        {
            Side = placement.Side.ToString().ToLowerInvariant(),
            Order = placement.Order,
            Pinned = placement.Pinned,
            Folded = placement.Folded,
            Height = ClampPanelHeight(placement.Height)
        };

        switch (placement.Kind)
        {
            case PanelKind.Toolbar:
                _document.PanelToolbar = entry;
                break;

            case PanelKind.Project:
                _document.PanelProject = entry;
                break;

            case PanelKind.History:
                _document.PanelHistory = entry;
                break;

            case PanelKind.Inspector:
                _document.PanelInspector = entry;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(placement), placement.Kind, null);
        }
    }

    /// <summary>The fresh-editor layout: the old two-dock arrangement, everything pinned.</summary>
    private static PanelPlacement DefaultPlacement(PanelKind kind) => kind switch
    {
        PanelKind.Toolbar => new PanelPlacement(kind, PanelSide.Left, 0, Pinned: true, Folded: false),
        PanelKind.Project => new PanelPlacement(kind, PanelSide.Left, 1, Pinned: true, Folded: false),
        PanelKind.History => new PanelPlacement(kind, PanelSide.Left, 2, Pinned: true, Folded: false),
        PanelKind.Inspector => new PanelPlacement(kind, PanelSide.Right, 0, Pinned: true, Folded: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>
    /// A side the enum actually has, spelled as a word. Unknown words fall back
    /// to the panel's own default rather than to a nearest member, because there
    /// is none — the same argument as the view cube's corner.
    /// </summary>
    /// <summary>
    /// A pace the enum actually has, spelled as a word. An unknown word falls
    /// back to the shipped pace rather than to a nearest member, because there
    /// is none — the view cube corner's own argument.
    /// </summary>
    private static AnimationSpeed? ParseSpeed(string? speed) =>
        speed?.Trim().ToLowerInvariant() switch
        {
            "fast" => AnimationSpeed.Fast,
            "medium" => AnimationSpeed.Medium,
            "slow" => AnimationSpeed.Slow,
            _ => null
        };

    private static EditorTheme? ParseTheme(string? theme) =>
        theme?.Trim().ToLowerInvariant() switch
        {
            "dark" => EditorTheme.Dark,
            "light" => EditorTheme.Light,
            _ => null
        };

    private static string FormatTheme(EditorTheme theme) =>
        theme == EditorTheme.Light ? "light" : "dark";

    /// <summary>
    /// The accent as a canonical <c>#RRGGBB</c>, or the shipped one where the
    /// value is not a colour. Clamped rather than refused, like every other
    /// setting: a hand-edited file with nonsense in it must still open.
    /// </summary>
    /// <remarks>
    /// Parsed here rather than with a colour type, so the preferences layer stays
    /// what it is — files and numbers — and the one place that turns six hex
    /// digits into a colour is the layer that draws with it.
    /// </remarks>
    private static string ResolveAccent(string? accent) =>
        TryReadAccent(accent, out string canonical) ? canonical : DefaultAccent;

    private static bool TryReadAccent(string? accent, out string canonical)
    {
        canonical = string.Empty;

        if (accent is null)
            return false;

        string trimmed = accent.Trim().TrimStart('#');

        if (trimmed.Length != 6)
            return false;

        foreach (char digit in trimmed)
        {
            if (!Uri.IsHexDigit(digit))
                return false;
        }

        canonical = $"#{trimmed.ToUpperInvariant()}";

        return true;
    }

    private static string FormatSpeed(AnimationSpeed speed) =>
        speed.ToString().ToLowerInvariant();

    private static PanelSide? ParseSide(string? side) =>
        side?.Trim().ToLowerInvariant() switch
        {
            "left" => PanelSide.Left,
            "right" => PanelSide.Right,
            _ => null
        };

    private static double ClampCountdown(double seconds) =>
        ClampSeconds(
            seconds,
            MinimumCountdownSeconds,
            MaximumCountdownSeconds,
            PreferencesDocument.DefaultCountdownSeconds);

    private static double ClampWait(double seconds) =>
        ClampSeconds(
            seconds,
            MinimumWaitSeconds,
            MaximumWaitLimitSeconds,
            PreferencesDocument.DefaultMaximumWaitSeconds);

    private static double ClampSilenceWarning(double seconds) =>
        ClampSeconds(
            seconds,
            MinimumSilenceSeconds,
            MaximumSilenceSeconds,
            PreferencesDocument.DefaultSilenceWarningSeconds);

    private static double ClampAutosaveInterval(double seconds) =>
        ClampSeconds(
            seconds,
            MinimumAutosaveIntervalSeconds,
            MaximumAutosaveIntervalSeconds,
            PreferencesDocument.DefaultAutosaveIntervalSeconds);

    private static double ClampSilenceGrace(double seconds) =>
        ClampSeconds(
            seconds,
            MinimumSilenceSeconds,
            MaximumSilenceSeconds,
            PreferencesDocument.DefaultSilenceGraceSeconds);

    /// <summary>
    /// Out of range is clamped, but a value that is not a number has no nearest
    /// legal neighbour to clamp to — that one falls back to the default.
    /// </summary>
    private static double ClampSeconds(double seconds, double minimum, double maximum, double fallback) =>
        double.IsFinite(seconds) ? Math.Clamp(seconds, minimum, maximum) : fallback;
}
