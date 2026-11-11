using System;
using System.IO;
using VoxelCAD.CommonTypes;
using VoxelCAD.Preferences;
using Xunit;

namespace VoxelCAD.Preferences.Tests;

/// <summary>
/// Settings that survive the process. The token is the point of most of this: an
/// endpoint that reissued one every launch would invalidate the client config it
/// had just written.
/// </summary>
public sealed class EditorPreferencesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"voxelcad-prefs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AFreshInstallGetsWorkingDefaults()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.True(preferences.McpEnabled);
        Assert.Equal(8971, preferences.McpPort);
        Assert.Equal("http://127.0.0.1:8971/mcp", preferences.McpUrl);
        Assert.True(preferences.CountdownEnabled);
        Assert.Equal(3.0, preferences.CountdownSeconds);
        Assert.Equal(30.0, preferences.MaximumWaitSeconds);
        Assert.Equal(60.0, preferences.SilenceWarningSeconds);
        Assert.Equal(30.0, preferences.SilenceGraceSeconds);
        Assert.NotEmpty(preferences.McpToken);
        Assert.Equal(
            Path.Combine(_directory, "mcp.json"),
            preferences.ClientConfigPath);
    }

    /// <summary>
    /// A first run is the absence of the file, asked before the first load writes
    /// one — so it is true exactly once, and the next launch is an ordinary one.
    /// </summary>
    [Fact]
    public void OnlyTheLaunchThatFindsNoFileIsAFirstRun()
    {
        Assert.True(PreferencesFactory.Load(_directory).IsFirstRun);
        Assert.False(PreferencesFactory.Load(_directory).IsFirstRun);
    }

    [Fact]
    public void ADamagedFileIsNotAFirstRun()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "preferences.json"), "{ not json");

        Assert.False(PreferencesFactory.Load(_directory).IsFirstRun);
    }

    [Fact]
    public void TheTokenIsWrittenOnFirstRunAndKeptAfterwards()
    {
        string first = PreferencesFactory.Load(_directory).McpToken;

        Assert.True(File.Exists(Path.Combine(_directory, "preferences.json")));
        Assert.Equal(first, PreferencesFactory.Load(_directory).McpToken);
    }

    [Fact]
    public void TokensAreNotGuessableAndDifferBetweenInstalls()
    {
        var other = Path.Combine(Path.GetTempPath(), $"voxelcad-prefs-{Guid.NewGuid():N}");

        try
        {
            string token = PreferencesFactory.Load(_directory).McpToken;

            Assert.True(token.Length >= 32);
            Assert.NotEqual(token, PreferencesFactory.Load(other).McpToken);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void RegeneratingReplacesTheTokenAndKeepsTheNewOne()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);
        string before = preferences.McpToken;

        preferences.RegenerateToken();

        Assert.NotEqual(before, preferences.McpToken);
        Assert.Equal(preferences.McpToken, PreferencesFactory.Load(_directory).McpToken);
    }

    [Fact]
    public void AppliedValuesSurviveARestart()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(new PreferenceValues(
            McpEnabled: false,
            McpPort: 9100,
            ClientConfigPath: "/tmp/somewhere/.mcp.json",
            CountdownEnabled: false,
            CountdownSeconds: 8.5,
            MaximumWaitSeconds: 45.0,
            SilenceWarningSeconds: 90.0,
            SilenceGraceSeconds: 20.0,
            InputInjectionEnabled: true,
            ViewCubeCorner: ViewCubeCorner.BottomLeft,
            PanelPlacements: System.Array.Empty<PanelPlacement>(),
            AnimationsEnabled: false,
            AnimationSpeed: AnimationSpeed.Slow,
            Theme: EditorTheme.Light,
            AccentColor: "#ff8800",
            Language: EditorLanguage.Russian,
            ViewportOcclusion: false,
            ViewportShadows: false,
            ViewportFactures: false,
            GlassPanels: false,
            AutosaveEnabled: false,
            AutosaveIntervalSeconds: 45.0));

        IEditorPreferences reloaded = PreferencesFactory.Load(_directory);

        Assert.False(reloaded.McpEnabled);
        Assert.Equal(9100, reloaded.McpPort);
        Assert.Equal("/tmp/somewhere/.mcp.json", reloaded.ClientConfigPath);
        Assert.Equal(EditorTheme.Light, reloaded.Theme);

        // Canonicalised on the way in, so the file holds one spelling.
        Assert.Equal("#FF8800", reloaded.AccentColor);
        Assert.False(reloaded.CountdownEnabled);
        Assert.Equal(8.5, reloaded.CountdownSeconds);
        Assert.Equal(45.0, reloaded.MaximumWaitSeconds);
        Assert.Equal(90.0, reloaded.SilenceWarningSeconds);
        Assert.Equal(20.0, reloaded.SilenceGraceSeconds);
        Assert.False(reloaded.AnimationsEnabled);
        Assert.Equal(AnimationSpeed.Slow, reloaded.AnimationSpeed);
        Assert.Equal(EditorLanguage.Russian, reloaded.Language);
        Assert.False(reloaded.AutosaveEnabled);
        Assert.Equal(45.0, reloaded.AutosaveIntervalSeconds);
    }

    /// <summary>
    /// Every preferences.json in existence lacks both keys, and an editor that has
    /// never been asked keeps its user's work: on, every two minutes.
    /// </summary>
    [Fact]
    public void AFileWithoutTheAutosaveKeysReadsAsOnEveryTwoMinutes()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            """{ "version": 1, "mcpPort": 9200 }""");

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.True(preferences.AutosaveEnabled);
        Assert.Equal(120.0, preferences.AutosaveIntervalSeconds);
        Assert.Equal(Path.Combine(_directory, "autosave"), preferences.AutosaveDirectory);
    }

    /// <summary>
    /// The switch is a plain bool that defaults to true, so the one way it could
    /// silently come back on is a serializer told to leave defaults out. This is
    /// the guard against that option ever being added to the storage.
    /// </summary>
    [Fact]
    public void AStoredFalseAutosaveSwitchSurvivesTheRoundTrip()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { AutosaveEnabled = false });

        Assert.Contains(
            "\"autosaveEnabled\": false",
            File.ReadAllText(Path.Combine(_directory, "preferences.json")));
        Assert.False(PreferencesFactory.Load(_directory).AutosaveEnabled);
    }

    [Theory]
    [InlineData(5.0, 10.0)]
    [InlineData(-1.0, 10.0)]
    [InlineData(99999.0, 3600.0)]
    [InlineData(double.NaN, 120.0)]
    public void AnImpossibleAutosaveIntervalIsBroughtIntoRange(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { AutosaveIntervalSeconds = asked });

        Assert.Equal(expected, preferences.AutosaveIntervalSeconds);
        Assert.Equal(expected, PreferencesFactory.Load(_directory).AutosaveIntervalSeconds);
    }

    /// <summary>
    /// Repaired on load as well as on apply, or a hand-edited number stays wrong
    /// in the file until the next save — the pace's own rule.
    /// </summary>
    [Fact]
    public void AHandEditedAutosaveIntervalIsRepairedOnLoad()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            """{ "autosaveIntervalSeconds": 1 }""");

        Assert.Equal(10.0, PreferencesFactory.Load(_directory).AutosaveIntervalSeconds);
    }

    /// <summary>
    /// The class guard: Apply, GetValues, the form and two fakes are each a
    /// hand-written list of the same positional record, and a swapped pair of
    /// same-typed neighbours passes every test that reads one field at a time.
    /// Every field here differs from its default and from its same-typed
    /// neighbours, and the whole record is compared after a reload.
    /// </summary>
    [Fact]
    public void EveryPreferenceValueRoundTripsWithAValueUnlikeItsNeighbours()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);
        var placements = new[]
        {
            new PanelPlacement(PanelKind.Toolbar, PanelSide.Right, 0, Pinned: false, Folded: true)
        };

        var unlike = new PreferenceValues(
            McpEnabled: false,
            McpPort: 9110,
            ClientConfigPath: "/tmp/unlike/.mcp.json",
            CountdownEnabled: false,
            CountdownSeconds: 7.5,
            MaximumWaitSeconds: 41.0,
            SilenceWarningSeconds: 91.0,
            SilenceGraceSeconds: 23.0,
            InputInjectionEnabled: true,
            ViewCubeCorner: ViewCubeCorner.BottomLeft,
            PanelPlacements: placements,
            AnimationsEnabled: false,
            AnimationSpeed: AnimationSpeed.Medium,
            Theme: EditorTheme.Light,
            AccentColor: "#123456",
            Language: EditorLanguage.German,
            ViewportOcclusion: false,
            ViewportShadows: true,
            ViewportFactures: false,
            GlassPanels: true,
            AutosaveEnabled: false,
            AutosaveIntervalSeconds: 45.0);

        preferences.Apply(unlike);

        PreferenceValues reloaded = PreferencesFactory.Load(_directory).GetValues();

        // The placements are normalized on the way in, so they are compared apart.
        Assert.Equal(
            unlike with { PanelPlacements = reloaded.PanelPlacements },
            reloaded);
        PanelPlacement toolbar = Assert.Single(
            reloaded.PanelPlacements, placement => placement.Kind == PanelKind.Toolbar);
        Assert.Equal(PanelSide.Right, toolbar.Side);
        Assert.True(toolbar.Folded);
    }

    /// <summary>
    /// Every preferences.json in existence lacks both keys, so this is the
    /// migration assertion: an existing install reads on and Fast, which is what
    /// it did before there was a setting.
    /// </summary>
    [Fact]
    public void AMissingKeyMeansAnimatedAndFast()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.True(preferences.AnimationsEnabled);
        Assert.Equal(AnimationSpeed.Fast, preferences.AnimationSpeed);
    }

    /// <summary>
    /// A word rather than a number, so the file stays worth opening in an
    /// editor — the panel side's own argument, and the complaint the view cube's
    /// corner earned by being a bare int.
    /// </summary>
    [Fact]
    public void ThePaceIsStoredAsAWord()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { AnimationSpeed = AnimationSpeed.Slow });

        Assert.Contains(
            "\"slow\"",
            File.ReadAllText(Path.Combine(_directory, "preferences.json")));
    }

    /// <summary>
    /// A set with no nearest member falls back rather than clamping, and the
    /// repair happens on load as well as on apply — or a hand-edited word stays
    /// wrong in the file until the next save.
    /// </summary>
    [Fact]
    public void AnUnknownPaceFallsBackToTheShippedOne()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            "{\"animationSpeed\": \"brisk\", \"animationsEnabled\": true}");

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.Equal(AnimationSpeed.Fast, preferences.AnimationSpeed);
        Assert.True(preferences.AnimationsEnabled);
    }

    [Fact]
    public void BothChangesAnnounceThemselves()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);
        var changes = 0;

        preferences.Changed += () => changes++;

        preferences.Apply(preferences.GetValues());
        preferences.RegenerateToken();

        Assert.Equal(2, changes);
    }

    /// <summary>
    /// Out of range is clamped rather than refused, the way the model treats a
    /// transform: a settings form should not be able to raise an exception.
    /// </summary>
    [Theory]
    [InlineData(80, 1024)]
    [InlineData(-1, 1024)]
    [InlineData(999999, 65535)]
    public void AnImpossiblePortIsBroughtIntoRange(int asked, int expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { McpPort = asked });

        Assert.Equal(expected, preferences.McpPort);
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(-5.0, 0.5)]
    [InlineData(600.0, 60.0)]
    [InlineData(double.NaN, 3.0)]
    public void AnImpossibleCountdownIsBroughtIntoRange(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { CountdownSeconds = asked });

        Assert.Equal(expected, preferences.CountdownSeconds);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-10.0, 1.0)]
    [InlineData(9999.0, 600.0)]
    [InlineData(double.NaN, 30.0)]
    public void AnImpossibleMaximumWaitIsBroughtIntoRange(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { MaximumWaitSeconds = asked });

        Assert.Equal(expected, preferences.MaximumWaitSeconds);
    }

    [Theory]
    [InlineData(1.0, 5.0)]
    [InlineData(-1.0, 5.0)]
    [InlineData(99999.0, 3600.0)]
    [InlineData(double.NaN, 60.0)]
    public void AnImpossibleSilenceWarningIsBroughtIntoRange(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { SilenceWarningSeconds = asked });

        Assert.Equal(expected, preferences.SilenceWarningSeconds);
    }

    [Theory]
    [InlineData(1.0, 5.0)]
    [InlineData(99999.0, 3600.0)]
    [InlineData(double.NaN, 30.0)]
    public void AnImpossibleSilenceGraceIsBroughtIntoRange(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { SilenceGraceSeconds = asked });

        Assert.Equal(expected, preferences.SilenceGraceSeconds);
    }

    [Fact]
    public void ABlankConfigPathMeansTheDefaultOne()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { ClientConfigPath = "   " });

        Assert.Equal(Path.Combine(_directory, "mcp.json"), preferences.ClientConfigPath);
    }

    /// <summary>
    /// A hand-edited file that no longer parses must not stop the editor starting.
    /// It is settings, not the document.
    /// </summary>
    [Fact]
    public void ADamagedFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "preferences.json"), "{ not json at all");

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.Equal(8971, preferences.McpPort);
        Assert.NotEmpty(preferences.McpToken);
    }

    [Fact]
    public void AFileMissingHalfItsKeysKeepsTheDefaultsForThem()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            """{ "version": 1, "mcpPort": 9200 }""");

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.Equal(9200, preferences.McpPort);
        Assert.True(preferences.McpEnabled);
        Assert.Equal(3.0, preferences.CountdownSeconds);
        Assert.NotEmpty(preferences.McpToken);
    }

    [Fact]
    public void AFreshInstallPutsEveryPanelWhereTheTwoDockLayoutHadIt()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.Collection(
            preferences.PanelPlacements,
            placement => Assert.Equal(
                new PanelPlacement(PanelKind.Toolbar, PanelSide.Left, 0, Pinned: true, Folded: false),
                placement),
            placement => Assert.Equal(
                new PanelPlacement(PanelKind.Project, PanelSide.Left, 1, Pinned: true, Folded: false),
                placement),
            placement => Assert.Equal(
                new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: true, Folded: false),
                placement),
            placement => Assert.Equal(
                new PanelPlacement(PanelKind.Inspector, PanelSide.Right, 0, Pinned: true, Folded: false),
                placement));
    }

    [Fact]
    public void PanelPlacementsSurviveARestart()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.ApplyPanelPlacements(new[]
        {
            new PanelPlacement(PanelKind.Inspector, PanelSide.Right, 0, Pinned: false, Folded: false),
            new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: true, Folded: true)
        });

        IEditorPreferences reloaded = PreferencesFactory.Load(_directory);

        PanelPlacement inspector = Assert.Single(
            reloaded.PanelPlacements, placement => placement.Kind == PanelKind.Inspector);
        PanelPlacement history = Assert.Single(
            reloaded.PanelPlacements, placement => placement.Kind == PanelKind.History);
        PanelPlacement toolbar = Assert.Single(
            reloaded.PanelPlacements, placement => placement.Kind == PanelKind.Toolbar);

        Assert.False(inspector.Pinned);
        Assert.True(history.Folded);
        Assert.True(toolbar.Pinned);
    }

    [Fact]
    public void AFreshInstallHasTheShippedWidthOnBothSides()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.Equal(PanelWidths.Default, preferences.PanelWidths.Left);
        Assert.Equal(PanelWidths.Default, preferences.PanelWidths.Right);
    }

    [Fact]
    public void PanelWidthsSurviveARestart()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.ApplyPanelWidths(new PanelWidths(420.0, 360.0));

        IEditorPreferences reloaded = PreferencesFactory.Load(_directory);

        Assert.Equal(420.0, reloaded.PanelWidths.Left);
        Assert.Equal(360.0, reloaded.PanelWidths.Right);
    }

    /// <summary>
    /// The same clamp the chrome's own drag uses — one pair of bounds, or a file
    /// could store a width no grip can reach.
    /// </summary>
    [Theory]
    [InlineData(10.0, PanelWidths.Minimum)]
    [InlineData(5000.0, PanelWidths.Maximum)]
    [InlineData(double.NaN, PanelWidths.Default)]
    public void AnImpossibleWidthIsBroughtIntoRange(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.ApplyPanelWidths(new PanelWidths(asked, PanelWidths.Default));

        Assert.Equal(expected, PreferencesFactory.Load(_directory).PanelWidths.Left);
    }

    [Fact]
    public void ADraggedHeightSurvivesARestartAndAnUntouchedOneStaysNatural()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.ApplyPanelPlacements(new[]
        {
            new PanelPlacement(
                PanelKind.History, PanelSide.Left, 2, Pinned: true, Folded: false,
                Height: 240.0)
        });

        IEditorPreferences reloaded = PreferencesFactory.Load(_directory);

        Assert.Equal(
            240.0,
            Assert.Single(
                reloaded.PanelPlacements, placement => placement.Kind == PanelKind.History).Height);
        Assert.Equal(
            0.0,
            Assert.Single(
                reloaded.PanelPlacements, placement => placement.Kind == PanelKind.Project).Height);
    }

    /// <summary>
    /// A height is either a height or it means "natural" — there is no clamping
    /// a negative number to a floor this layer does not know, the floor being
    /// each panel's own.
    /// </summary>
    [Theory]
    [InlineData(-50.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(99999.0, 4000.0)]
    public void AnImpossibleHeightMeansNaturalOrTheCeiling(double asked, double expected)
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.ApplyPanelPlacements(new[]
        {
            new PanelPlacement(
                PanelKind.History, PanelSide.Left, 2, Pinned: true, Folded: false,
                Height: asked)
        });

        Assert.Equal(
            expected,
            Assert.Single(
                PreferencesFactory.Load(_directory).PanelPlacements,
                placement => placement.Kind == PanelKind.History).Height);
    }

    /// <summary>
    /// An entry that is present but silent about its side keeps the panel's own
    /// default — the inspector stays right, not whatever a class-level default
    /// happens to spell.
    /// </summary>
    [Fact]
    public void AnEntryMissingItsSideKeepsThePanelsDefaultSide()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            """{ "version": 1, "panelInspector": { "pinned": false } }""");

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        PanelPlacement inspector = Assert.Single(
            preferences.PanelPlacements, placement => placement.Kind == PanelKind.Inspector);

        Assert.Equal(PanelSide.Right, inspector.Side);
        Assert.False(inspector.Pinned);
    }

    [Fact]
    public void AnUnknownSideFallsBackToThePanelsDefault()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            """{ "version": 1, "panelInspector": { "side": "top", "order": 3, "pinned": false } }""");

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        PanelPlacement inspector = Assert.Single(
            preferences.PanelPlacements, placement => placement.Kind == PanelKind.Inspector);

        Assert.Equal(PanelSide.Right, inspector.Side);
        Assert.False(inspector.Pinned);
    }

    /// <summary>
    /// A hand-edited file can number a column 5, 5, 12; what the layout needs is
    /// an order, so loading renumbers each side 0..n-1 with ties broken stably.
    /// </summary>
    [Fact]
    public void ColumnOrdersAreRenumberedOnLoad()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            """
            {
              "version": 1,
              "panelToolbar": { "side": "left", "order": 12 },
              "panelProject": { "side": "left", "order": 5 },
              "panelHistory": { "side": "left", "order": 5 }
            }
            """);

        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        PanelPlacement toolbar = Assert.Single(
            preferences.PanelPlacements, placement => placement.Kind == PanelKind.Toolbar);
        PanelPlacement project = Assert.Single(
            preferences.PanelPlacements, placement => placement.Kind == PanelKind.Project);
        PanelPlacement history = Assert.Single(
            preferences.PanelPlacements, placement => placement.Kind == PanelKind.History);

        Assert.Equal(0, project.Order);
        Assert.Equal(1, history.Order);
        Assert.Equal(2, toolbar.Order);
    }

    /// <summary>
    /// The shipped look, and what a hand-edited file falls back to. Both
    /// directions of the rule the whole preferences layer keeps: out of range is
    /// repaired, never refused.
    /// </summary>
    [Fact]
    public void AFreshFileIsDarkWithTheShippedAccent()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.Equal(EditorTheme.Dark, preferences.Theme);
        Assert.Equal("#70BAFA", preferences.AccentColor);
    }

    [Fact]
    public void AnAccentThatIsNotAColourFallsBackToTheShippedOne()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { AccentColor = "not a colour" });

        Assert.Equal("#70BAFA", PreferencesFactory.Load(_directory).AccentColor);
    }

    /// <summary>A hash is optional and the case is not, so one spelling reaches the file.</summary>
    [Fact]
    public void AnAccentIsCanonicalisedWhicheverWayItIsWritten()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { AccentColor = "3a7bd5" });

        Assert.Equal("#3A7BD5", preferences.AccentColor);
    }

    /// <summary>
    /// A theme outside the enum is the shipped one, not an exception — the
    /// corner's own rule, and the reason Dark is the enum's first member.
    /// </summary>
    [Fact]
    public void AThemeOutsideTheEnumIsTheShippedOne()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.Apply(preferences.GetValues() with { Theme = (EditorTheme)99 });

        Assert.Equal(EditorTheme.Dark, preferences.Theme);
    }

    /// <summary>
    /// A render setup is a thing somebody arrived at, so it outlives the card it
    /// was turned on.
    /// </summary>
    [Fact]
    public void RenderDialsSurviveAReload()
    {
        var dials = new RenderDials(
            Width: 1920,
            Height: 1080,
            Samples: 900,
            Exposure: 0.45f,
            SunElevationDegrees: 22f,
            SunAzimuthDegrees: -95f,
            SunSizeDegrees: 6.5f,
            Glare: 0.12f,
            Backdrop: RenderBackdrop.Transparent,
            Ground: false);

        PreferencesFactory.Load(_directory).ApplyRenderDials(dials);

        Assert.Equal(dials, PreferencesFactory.Load(_directory).RenderDials);
    }

    /// <summary>
    /// A damaged file is settings rather than a document: what it says is brought
    /// into range rather than refusing to start the editor.
    /// </summary>
    [Fact]
    public void AbsurdRenderDialsAreBroughtIntoRange()
    {
        PreferencesFactory.Load(_directory).ApplyRenderDials(new RenderDials(
            Width: 1,
            Height: 99999,
            Samples: -5,
            Exposure: 0f,
            SunElevationDegrees: 900f,
            SunAzimuthDegrees: 0f,
            SunSizeDegrees: 0f,
            Glare: 9f,
            Backdrop: RenderBackdrop.Environment,
            Ground: true));

        RenderDials held = PreferencesFactory.Load(_directory).RenderDials;

        Assert.Equal(RenderLimits.MinimumSize, held.Width);
        Assert.Equal(RenderLimits.MaximumSize, held.Height);
        Assert.Equal(RenderLimits.MinimumSamples, held.Samples);
        Assert.Equal(RenderLimits.MinimumExposure, held.Exposure);
        Assert.Equal(RenderLimits.MaximumElevation, held.SunElevationDegrees);
        Assert.Equal(RenderLimits.MinimumSunRadius, held.SunSizeDegrees);
        Assert.Equal(RenderLimits.MaximumGlare, held.Glare);
    }

    /// <summary>
    /// The backdrop is spelled in the file, so a word nobody recognises falls back
    /// rather than becoming a number nothing means.
    /// </summary>
    /// <summary>
    /// How the material section is being worked in. Stored on the render card's
    /// own terms: a depth, a notation and a fold are answers to "how do you like to
    /// work", and one that reset with the window would have to be given again every
    /// morning. It shipped as session state, which is what this pins.
    /// </summary>
    [Fact]
    public void TheMaterialSectionOpensTheWayItWasLastLeft()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        Assert.False(preferences.MaterialAdvanced);
        Assert.Equal(ColorTextFormat.Hex, preferences.MaterialColorFormat);
        Assert.False(preferences.MaterialPreviewShown);

        preferences.ApplyMaterialAdvanced(true);
        preferences.ApplyMaterialColorFormat(ColorTextFormat.Hsv);
        preferences.ApplyMaterialPreviewShown(true);

        IEditorPreferences reloaded = PreferencesFactory.Load(_directory);

        Assert.True(reloaded.MaterialAdvanced);
        Assert.Equal(ColorTextFormat.Hsv, reloaded.MaterialColorFormat);
        Assert.True(reloaded.MaterialPreviewShown);
    }

    [Fact]
    public void AnUnknownColourNotationFallsBackToHex()
    {
        // Spelled rather than numbered so the file stays worth opening, which means
        // a word nobody knows is a case that has to have an answer — the theme's
        // and the backdrop's own bargain.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "preferences.json"),
            "{ \"MaterialColorFormat\": \"Cmyk\" }");

        Assert.Equal(ColorTextFormat.Hex, PreferencesFactory.Load(_directory).MaterialColorFormat);
    }

    [Fact]
    public void AnUnknownBackdropFallsBack()
    {
        IEditorPreferences preferences = PreferencesFactory.Load(_directory);

        preferences.ApplyRenderDials(preferences.RenderDials with
        {
            Backdrop = RenderBackdrop.Transparent
        });

        string path = Path.Combine(_directory, "preferences.json");
        string text = File.ReadAllText(path);

        File.WriteAllText(path, text.Replace("Transparent", "Seethrough"));

        Assert.Equal(
            RenderBackdrop.Environment,
            PreferencesFactory.Load(_directory).RenderDials.Backdrop);
    }
}
