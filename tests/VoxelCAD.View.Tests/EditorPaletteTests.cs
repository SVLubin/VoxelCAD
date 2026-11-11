using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Godot;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The two looks the editor is drawn in.
/// </summary>
/// <remarks>
/// Most of this file exists for one sentence: <b>the dark theme is what the
/// editor ships with, and parameterising the palette must not have moved it.</b>
/// The derived values below were computed from the single-theme version's own
/// formulas before the light theme existed, so they are a record of the shipped
/// look rather than a restatement of the code that produces it — which is the
/// difference between a guard and a tautology.
/// </remarks>
public sealed class EditorPaletteTests
{
    private const float Tolerance = 0.0005f;

    [Fact]
    public void TheDarkSurfacesAreWhatTheyWere()
    {
        EditorPalette palette = EditorPalette.Dark();

        Same(new Color(0.21f, 0.24f, 0.29f), palette.PanelBackground);
        Same(new Color(0.1155f, 0.132f, 0.1595f), palette.WindowBackground);
        Same(new Color(0.147f, 0.168f, 0.203f), palette.WidgetBackground);
        Same(new Color(0.084f, 0.096f, 0.116f), palette.Border);
    }

    /// <summary>
    /// The two that changed shape in the refactor and had to come out the same:
    /// hovering used to be spelled as a lerp toward white and pressing as
    /// <c>Darkened</c>, and both are now written in terms of the ink.
    /// </summary>
    [Fact]
    public void TheDarkWidgetStatesAreWhatTheyWere()
    {
        EditorPalette palette = EditorPalette.Dark();

        Same(new Color(0.24083f, 0.25952f, 0.29067f), palette.WidgetBackgroundHover);
        Same(new Color(0.128625f, 0.147f, 0.177625f), palette.WidgetBackgroundPressed);
        Same(new Color(0.447f, 0.468f, 0.503f), palette.BorderHover);
    }

    [Fact]
    public void TheDarkTextIsWhatItWas()
    {
        EditorPalette palette = EditorPalette.Dark();

        Same(new Color(0.8025f, 0.81f, 0.8225f), palette.Text);
        Same(new Color(0.90125f, 0.905f, 0.91125f), palette.TextHover);
        Same(new Color(1f, 1f, 1f, 0.35f), palette.TextDisabled);
        Same(new Color(1f, 1f, 1f, 0.65f), palette.TextReadonly);
        Same(new Color(1f, 1f, 1f, 0.1f), palette.Separator);
    }

    /// <summary>
    /// The signal colours are picked for a dark surface, so on the dark theme
    /// they are the literals and nothing has been pulled anywhere.
    /// </summary>
    [Fact]
    public void TheDarkSemanticColoursAreWhatTheyWere()
    {
        EditorPalette palette = EditorPalette.Dark();

        Same(new Color(1f, 0.87f, 0.4f), palette.Warning);
        Same(new Color(1f, 0.47f, 0.42f), palette.Error);
        Same(new Color(0.45f, 0.95f, 0.5f), palette.Success);
        Same(new Color(0.988f, 0.5f, 0.5f), palette.IconElement);
        Same(new Color(0.96f, 0.72f, 0.28f), palette.AgentBorder);
        Same(new Color(0.5f, 0.5f, 0.55f, 0.28f), palette.DimOverlay);
    }

    /// <summary>
    /// The two colours the 3D scene takes from the theme. Pinned like the rest,
    /// because the dark theme's room is what the shading model was calibrated
    /// against — Goxel's own look — and moving it would be moving that.
    /// </summary>
    [Fact]
    public void TheDarkRoomIsWhatItWas()
    {
        EditorPalette palette = EditorPalette.Dark();

        Same(new Color(0.42f, 0.44f, 0.48f), palette.SceneRoom);

        // A gradient with no gradient in it: the flat void the dark theme has
        // always had, reached the way the light theme reaches its backdrop.
        Same(new Color(0.12f, 0.12f, 0.14f), palette.VoidHigh);
        Same(palette.VoidHigh, palette.VoidLow);
    }

    /// <summary>
    /// The light room is lighter and <b>tinted</b>.
    /// </summary>
    /// <remarks>
    /// The tint is what does the work: a grey voxel is told from a lavender room
    /// by hue, which is what lets the room be lighter than the model at all. A
    /// neutral one could not — its only separation was brightness, so its albedo
    /// was bounded above by the model's own.
    /// </remarks>
    [Fact]
    public void TheLightRoomIsLighterAndTinted()
    {
        EditorPalette dark = EditorPalette.Dark();
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);

        Assert.True(light.SceneRoom.Luminance > dark.SceneRoom.Luminance);

        // Tinted, not merely bright: the separation is the blue in it.
        Assert.True(light.SceneRoom.B > light.SceneRoom.R);
    }

    /// <summary>
    /// The light theme's backdrop is a CAD grey — a real sweep, faintly cool,
    /// ending exactly on the dark theme's own void.
    /// </summary>
    /// <remarks>
    /// The room's lavender deliberately does <b>not</b> reach it: the backdrop
    /// is what the room is seen against, and a surround of the same hue is one
    /// less thing separating them. Which end is the grey one is taste and is
    /// not asserted; that the two ends differ at all is not — the first attempt
    /// put them a hundredth apart and there was a gradient in the numbers and
    /// none on the screen.
    /// </remarks>
    [Fact]
    public void TheLightBackdropIsACadGrey()
    {
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);

        Assert.True(
            Mathf.Abs(light.VoidHigh.Luminance - light.VoidLow.Luminance) > 0.1f,
            "the backdrop's two stops are the same colour to look at");

        // Cool rather than neutral, the way a CAD grey is, and nowhere near the
        // room's own lavender: a hue this faint is a cast, not a colour.
        foreach (Color stop in new[] { light.VoidHigh, light.VoidLow })
        {
            Assert.True(stop.B > stop.R, $"{stop} is not cool");
            Assert.True(stop.B - stop.R < light.SceneRoom.B - light.SceneRoom.R);
        }

        // And it ends where the dark theme's whole backdrop sits.
        Same(EditorPalette.Dark().VoidLow, light.VoidLow);
    }

    /// <summary>
    /// The backdrop is darker than the room it stands behind — at both stops —
    /// or the depth cue inverts and the buildable region reads as a hole cut in
    /// a pale field.
    /// </summary>
    /// <remarks>
    /// Compared against the room's <em>albedo</em>, which is the conservative
    /// bound: the floor is lit and renders brighter still. The light theme
    /// shipped with a bottom stop of 0.90 against a room of 0.72 and the whole
    /// lower half of the window inverted.
    /// </remarks>
    [Fact]
    public void TheBackdropIsDarkerThanTheRoomItStandsBehind()
    {
        foreach (EditorPalette palette in Themes())
        {
            Assert.True(
                palette.VoidHigh.Luminance < palette.SceneRoom.Luminance,
                $"the backdrop's top stop is not behind the room ({palette.VoidHigh})");

            Assert.True(
                palette.VoidLow.Luminance < palette.SceneRoom.Luminance,
                $"the backdrop's bottom stop is not behind the room ({palette.VoidLow})");
        }
    }

    /// <summary>
    /// A pane of glass stands off the backdrop it is drawn over, at both stops.
    /// </summary>
    /// <remarks>
    /// The one invariant that could not be stated while the composition lived
    /// inside the shader, and the one the light theme broke: over the bottom of
    /// its own backdrop a pane came out at 1.01 — the same luminance to within a
    /// hundredth — so every panel over the lower half of the window was its
    /// hairline and nothing else. The floor is what the dark theme's own pane
    /// manages over its flat void, rounded down.
    /// </remarks>
    [Fact]
    public void APaneStandsOffTheBackdropBehindIt()
    {
        foreach (EditorPalette palette in Themes())
        {
            GlassTint glass = GlassTint.For(palette);

            foreach (Color stop in new[] { palette.VoidHigh, palette.VoidLow })
                Assert.True(
                    Contrast(glass.Over(stop), stop) >= 1.1d,
                    $"a pane over {stop} composes to {glass.Over(stop)}");
        }

        // And the light theme's own is a step rather than the technicality the
        // dark theme's flat void allows, because a light pane is mostly opaque.
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);
        GlassTint lightGlass = GlassTint.For(light);

        Assert.True(Contrast(lightGlass.Over(light.VoidLow), light.VoidLow) >= 1.5d);
        Assert.True(Contrast(lightGlass.Over(light.VoidHigh), light.VoidHigh) >= 2d);
    }

    [Fact]
    public void TheDefaultAccentIsGodotsEditorBlue()
    {
        Same(new Color(0.44f, 0.73f, 0.98f), EditorPalette.Dark().Accent);
        Same(new Color(0.44f, 0.73f, 0.98f), EditorPalette.DefaultAccent);
    }

    /// <summary>The light theme's surfaces are lighter than the ink written on them.</summary>
    [Fact]
    public void TheLightThemeIsLight()
    {
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);

        Assert.False(light.IsDark);
        Assert.True(light.PanelBackground.Luminance > light.Text.Luminance);
        Assert.True(EditorPalette.Dark().PanelBackground.Luminance < EditorPalette.Dark().Text.Luminance);
    }

    /// <summary>
    /// And the whole reason the ink is a parameter: pointing at a widget adds
    /// contrast in both themes, which means opposite directions.
    /// </summary>
    [Fact]
    public void HoveringMovesAwayFromTheSurfaceInEitherTheme()
    {
        EditorPalette dark = EditorPalette.Dark();
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);

        Assert.True(dark.WidgetBackgroundHover.Luminance > dark.WidgetBackground.Luminance);
        Assert.True(light.WidgetBackgroundHover.Luminance < light.WidgetBackground.Luminance);
    }

    /// <summary>A press sinks in both, and further than a hover in both.</summary>
    [Fact]
    public void PressingSinksInEitherTheme()
    {
        EditorPalette dark = EditorPalette.Dark();
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);

        Assert.True(dark.WidgetBackgroundPressed.Luminance < dark.WidgetBackground.Luminance);
        Assert.True(light.WidgetBackgroundPressed.Luminance < light.WidgetBackgroundHover.Luminance);
    }

    /// <summary>
    /// The accent is the user's, and every colour derived from it follows —
    /// which is what makes it one setting rather than a theme of its own.
    /// </summary>
    [Fact]
    public void TheAccentReachesEverythingDerivedFromIt()
    {
        var chosen = new Color(0.9f, 0.3f, 0.1f);

        foreach (EditorTheme theme in new[] { EditorTheme.Dark, EditorTheme.Light })
        {
            EditorPalette palette = EditorPalette.For(theme, chosen);

            Same(chosen, palette.Accent);
            Same(chosen, palette.Caret);
            Same(new Color(chosen, 0.4f), palette.SelectionFill);
            Same(new Color(chosen, 0.275f), palette.Highlight);
        }

        // As text it is the accent itself on dark and a darker relative on
        // light — it is written on the accent's own tint, where blue on pale
        // blue would be one colour said twice.
        Same(chosen, EditorPalette.Dark(chosen).TextPressed);
        Assert.True(
            EditorPalette.Light(chosen).TextPressed.Luminance
            < EditorPalette.Light(chosen).SelectionFill.Luminance);
    }

    /// <summary>
    /// The readability floor, said as a comparison rather than as a number: the
    /// dark theme is what shipped and was looked at, so <b>the light one has to
    /// be at least as legible on every pair</b>. That is the claim worth holding,
    /// and it is the one the first light theme failed — its glyphs landed at 3.1
    /// against dark's 4.3, and its receded rows a full step quieter.
    /// </summary>
    [Fact]
    public void TheLightThemeIsNoLessLegibleThanTheDarkOne()
    {
        EditorPalette dark = EditorPalette.Dark();
        EditorPalette light = EditorPalette.Light(EditorPalette.DefaultAccent);

        AtLeastAsLegible(dark.Text, light.Text, dark, light, "text");
        AtLeastAsLegible(dark.IconStructure, light.IconStructure, dark, light, "glyph");
        AtLeastAsLegible(dark.TextReceded, light.TextReceded, dark, light, "receded");
        AtLeastAsLegible(dark.TextHover, light.TextHover, dark, light, "hovered text");
    }

    /// <summary>
    /// And the pairs that are meant to be quiet are still meant to be read. The
    /// unchosen segment is the one that caught this: its colour is chosen against
    /// the panel and it is drawn on the sunken track, which on a light theme is a
    /// different question — measured at 2.2 before the fade was shallowed.
    /// </summary>
    [Fact]
    public void TheQuietLabelsAreStillLegible()
    {
        foreach (EditorTheme theme in new[] { EditorTheme.Dark, EditorTheme.Light })
        {
            EditorPalette palette = EditorPalette.For(theme, EditorPalette.DefaultAccent);

            Assert.True(
                Contrast(palette.TextReceded, palette.PanelBackground) >= 2.6d,
                $"{theme} receded on the panel");
            Assert.True(
                Contrast(palette.TextReceded, palette.InsetBackground) >= 2.6d,
                $"{theme} receded on a sunken track");
            Assert.True(
                Contrast(Over(palette.TextDisabled, palette.WidgetBackground), palette.WidgetBackground)
                >= 3d,
                $"{theme} disabled text on a field");
        }
    }

    /// <summary>
    /// The accent written on its own selection tint. It is the same colour as the
    /// accent on dark and a darker relative on light; either way the row has to be
    /// readable, which the raw accent is not — 1.3 there.
    /// </summary>
    [Fact]
    public void AnEmphasizedRowIsReadableOnTheSelectionBand()
    {
        foreach (EditorTheme theme in new[] { EditorTheme.Dark, EditorTheme.Light })
        {
            EditorPalette palette = EditorPalette.For(theme, EditorPalette.DefaultAccent);
            Color band = Over(palette.SelectionFill, palette.PanelBackground);

            Assert.True(
                Contrast(palette.TextPressed, band) >= 2.5d, $"{theme} emphasized row");
        }

        // And it has to read at least as well as it does on the theme that
        // shipped, on the tint it is most often written on.
        AtLeastAsLegible(
            EditorPalette.Dark().TextPressed,
            EditorPalette.Light(EditorPalette.DefaultAccent).TextPressed,
            EditorPalette.Dark(),
            EditorPalette.Light(EditorPalette.DefaultAccent),
            "accent as text");
    }

    /// <summary>A translucent colour as it actually lands on what is behind it.</summary>
    /// <summary>Both themes, for the invariants that hold either way round.</summary>
    private static IEnumerable<EditorPalette> Themes()
    {
        yield return EditorPalette.Dark();
        yield return EditorPalette.Light(EditorPalette.DefaultAccent);
    }

    private static Color Over(Color color, Color background) =>
        new(
            (color.R * color.A) + (background.R * (1f - color.A)),
            (color.G * color.A) + (background.G * (1f - color.A)),
            (color.B * color.A) + (background.B * (1f - color.A)));

    private static void AtLeastAsLegible(
        Color onDark, Color onLight, EditorPalette dark, EditorPalette light, string what)
    {
        double shipped = Contrast(onDark, dark.PanelBackground);
        double added = Contrast(onLight, light.PanelBackground);

        Assert.True(added >= shipped, $"{what}: light {added:F2} against dark {shipped:F2}");
    }

    /// <summary>
    /// A disabled widget has to be told from a live one. On light the dark
    /// theme's own sink landed it within a hundredth of the live fill, which is
    /// the whole reason that number is a parameter.
    /// </summary>
    [Fact]
    public void ADisabledWidgetIsToldFromALiveOne()
    {
        foreach (EditorTheme theme in new[] { EditorTheme.Dark, EditorTheme.Light })
        {
            EditorPalette palette = EditorPalette.For(theme, EditorPalette.DefaultAccent);

            Assert.True(
                Mathf.Abs(palette.WidgetBackgroundDisabled.Luminance - palette.WidgetBackground.Luminance)
                > 0.02f,
                $"{theme} disabled");

            // And it is the flatter of the two — nearer the panel it sits on.
            Assert.True(
                Mathf.Abs(palette.WidgetBackgroundDisabled.Luminance - palette.PanelBackground.Luminance)
                < Mathf.Abs(palette.WidgetBackground.Luminance - palette.PanelBackground.Luminance),
                $"{theme} disabled is flatter");
        }
    }

    /// <summary>WCAG relative luminance, so the numbers above mean what they say.</summary>
    private static double Contrast(Color first, Color second)
    {
        double a = Relative(first);
        double b = Relative(second);

        return (System.Math.Max(a, b) + 0.05d) / (System.Math.Min(a, b) + 0.05d);
    }

    private static double Relative(Color color) =>
        (0.2126d * Channel(color.R)) + (0.7152d * Channel(color.G)) + (0.0722d * Channel(color.B));

    private static double Channel(float value) =>
        value <= 0.04045f
            ? value / 12.92d
            : System.Math.Pow((value + 0.055d) / 1.055d, 2.4d);

    /// <summary>A theme outside the enum is the shipped one, like every other setting.</summary>
    [Fact]
    public void AnUnknownThemeIsDark() =>
        Same(
            EditorPalette.Dark().PanelBackground,
            EditorPalette.For((EditorTheme)99, EditorPalette.DefaultAccent).PanelBackground);

    private static void Same(Color expected, Color actual)
    {
        Assert.Equal(expected.R, actual.R, Tolerance);
        Assert.Equal(expected.G, actual.G, Tolerance);
        Assert.Equal(expected.B, actual.B, Tolerance);
        Assert.Equal(expected.A, actual.A, Tolerance);
    }
}
