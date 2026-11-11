using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The editor's colour vocabulary in one place: the surface, widget and text
/// colours that make up the Godot-like chrome, plus the semantic colours
/// (warning, error, success, undone history) that used to live as stray
/// constants inside individual controls.
/// </summary>
/// <remarks>
/// The values follow Godot's own "Default" dark editor preset — a base colour,
/// an accent, and a contrast, from which everything else is derived exactly the
/// way <c>EditorThemeManager</c> does it (<c>dark_color_1..3</c>,
/// <c>contrast_color_1</c>, the font tints, the accent-alpha selection fill).
/// Reproducing the derivation rather than pasting the results keeps the palette
/// coherent: nudge the base or accent and the whole set moves together.
/// <para>
/// That coherence is what lets there be a second theme at all. A light palette
/// is the same derivation run from a light surface with the <b>ink</b> reversed
/// — ink being the direction more contrast lies in, white on a dark surface and
/// black on a light one — so hovering, text and hairlines all turn round
/// together rather than one at a time. Four numbers differ besides
/// (<c>contrast</c>, how deep a press sinks, how far the semantic colours are
/// pulled toward the ink, and the dimmer); everything else is one formula.
/// </para>
/// <para>
/// <b>Dark is the shipped look and this must not move it.</b>
/// <see cref="Dark"/> passes exactly the constants the single-theme version
/// carried, and the formulas below reduce to what they were: with
/// <c>ink = White</c> every <c>Lerp(ink, …)</c> is the old <c>Lerp(White, …)</c>,
/// and <c>Lerp(Black, 0.125)</c> is what <c>Darkened(0.125)</c> already meant.
/// </para>
/// </remarks>
internal sealed class EditorPalette
{
    private static readonly Color DarkSurface = new(0.21f, 0.24f, 0.29f);

    /// <summary>
    /// A light grey with the same faint cool cast the dark surface has, so the
    /// two themes read as one editor rather than as two applications.
    /// </summary>
    private static readonly Color LightSurface = new(0.87f, 0.88f, 0.90f);

    /// <summary>Godot's own editor blue, and what the accent preference defaults to.</summary>
    public static readonly Color DefaultAccent = new(0.44f, 0.73f, 0.98f);

    private static readonly Color Black = new(0f, 0f, 0f);
    private static readonly Color White = new(1f, 1f, 1f);

    /// <summary>How far text is pulled off the ink toward what it sits on.</summary>
    private const float TextPull = 0.25f;

    private const float DarkContrast = 0.3f;

    /// <summary>
    /// Gentler than the dark theme's. The same 0.3 on a light surface would put
    /// the window background at mid-grey and the whole card hierarchy would read
    /// as four different greys rather than as one pane with things on it.
    /// </summary>
    private const float LightContrast = 0.09f;

    private EditorPalette(
        Color surface,
        Color accent,
        Color ink,
        float contrast,
        float pressDepth,
        float disabledSink,
        float structurePull,
        float recessionPull,
        float disabledFade,
        float accentTextPull,
        float semanticPull,
        Color dimOverlay,
        Color sceneRoom,
        Color voidHigh,
        Color voidLow)
    {
        // Godot derives three progressively recessed shades from the base
        // colour; they carry the whole surface hierarchy. Recessed is toward
        // black in both themes — a sunken light surface is a darker grey, not a
        // whiter one — so this one does not follow the ink.
        Color shade1 = surface.Lerp(Black, contrast);
        Color shade2 = surface.Lerp(Black, contrast * 1.5f);
        Color shade3 = surface.Lerp(Black, contrast * 2f);

        Accent = accent;

        WindowBackground = shade2;
        PanelBackground = surface;
        InsetBackground = shade1.Lerp(shade2, 0.5f);

        WidgetBackground = shade1;

        // The ground a picture with an alpha channel is read against. Two steps
        // either side of the inset, so the chequer is quieter than anything drawn
        // on a panel and still legible as two squares; away from the ink for the
        // light one, which is what turns the pair round with the theme rather
        // than leaving a dark chequer under a light card.
        CheckerDark = InsetBackground;
        CheckerLight = InsetBackground.Lerp(ink.Inverted(), 0.06f);

        // Toward the ink: lighter under the pointer on a dark theme, darker on a
        // light one. The same sentence — "more contrast when you point at it" —
        // said once instead of twice.
        WidgetBackgroundHover = shade1.Lerp(ink, 0.11f);

        // And a press sinks, in both. Deeper on the light theme, where hovering
        // already went this way and the two would otherwise be the same colour.
        WidgetBackgroundPressed = shade1.Lerp(Black, pressDepth);

        // A disabled widget is nearer the panel than a live one — flatter, not
        // deeper. It has to be said as a distance from the surface rather than
        // as a fixed sink: at the dark theme's own 0.1 a light widget lands on
        // top of the live one and the two cannot be told apart.
        WidgetBackgroundDisabled = surface.Lerp(Black, disabledSink);

        Border = shade3;

        // Godot's contrast_color_1 — the grey a popup is outlined in, away from
        // the surface in whichever direction that is.
        BorderHover = surface.Lerp(ink, contrast);
        Separator = new Color(ink, 0.1f);
        SelectionFill = new Color(accent, 0.4f);
        Highlight = new Color(accent, 0.275f);

        Text = ink.Lerp(surface, TextPull);
        TextHover = ink.Lerp(surface, 0.125f);
        // The accent as *text*, which is not the same colour as the accent. It
        // is written on the accent's own selection tint — a chosen segment's
        // label, an emphasized row, a ticked checkbox — and on a light theme
        // blue on pale blue is one colour said twice. Its own pull rather than
        // the signal colours', because it has to reach past legible into
        // comfortable and the same distance would turn the plaque's gold brown.
        TextPressed = accent.Lerp(ink, accentTextPull);
        // Alpha rather than a colour, so it composites over whatever field it
        // lands on — and per theme, because the eye is compressed near white: the
        // dark theme's own 0.35 on a pale field is a label at 2.3 against 3.1.
        TextDisabled = new Color(ink, disabledFade);
        TextReadonly = new Color(ink, 0.65f);
        Caret = accent;

        // The three signal colours are picked for a dark surface; on a light one
        // they are pulled toward the ink until they can be read on it.
        Warning = new Color(1f, 0.87f, 0.4f).Lerp(ink, semanticPull);
        Error = new Color(1f, 0.47f, 0.42f).Lerp(ink, semanticPull);
        Success = new Color(0.45f, 0.95f, 0.5f).Lerp(ink, semanticPull);

        // What a structure row's glyph is drawn in — a folder, the project, the
        // history's origin: quieter than the words beside it, but not so quiet
        // that it stops being a picture. Faded toward the surface, and less far
        // on a light theme, where the same fade lands on a pale glass pane and
        // leaves a mark nobody can read.
        IconStructure = Text.Lerp(PanelBackground, structurePull);

        // A row that is present but not in effect — an undone history record, a
        // hidden element, the plaque's second line — reads as text that has faded
        // toward the background, rather than an arbitrary grey. How far is per
        // theme for the same reason the glyphs' fade is: the eye is not linear
        // in either direction, and the dark theme's own 0.55 on a light panel
        // reads a full step quieter than it does on a dark one.
        TextReceded = Text.Lerp(PanelBackground, recessionPull);

        // What an icon on such a row is modulated by. Alpha rather than a colour,
        // because an icon carries its own tint and receding must not repaint it —
        // which is also why this one is the same in both themes.
        IconReceded = new Color(1f, 1f, 1f, 0.45f);

        // The tint hierarchy element icons wear — Godot's own colour for 3D
        // nodes, which is what a scene's elements are.
        IconElement = new Color(0.988f, 0.5f, 0.5f).Lerp(ink, semanticPull);

        // A warning gold for the plaque's ring while an agent holds the editor —
        // deliberately not the blue focus accent, which means "focused", the
        // opposite of the "suspended, do not touch" this says.
        AgentBorder = new Color(0.96f, 0.72f, 0.28f).Lerp(ink, semanticPull);

        // Suspending the interface is a haze, not a blackout: enough to say
        // "not yours right now" without hiding what the agent is doing underneath,
        // which a whole-window screenshot would otherwise capture obscured.
        DimOverlay = dimOverlay;

        // The two colours the 3D scene takes from the theme, and the line they
        // are on the near side of. What a model is *seen against* — the void
        // beyond the room, and the room's own walls and floor — is scenery, and
        // scenery may follow the chrome. What a model is *measured by* — the key
        // and fill lights, the ambient colour and its energy, the occlusion, the
        // shadow's opacity — does not, ever, because that would change what the
        // model looks like and what an export is compared against.
        //
        // Safe because of two facts about this scene rather than by assertion:
        // the ambient is a colour of its own (AmbientSource.Color), not the
        // background, so lightening the void adds no light; and the room neither
        // casts nor receives, so its albedo reaches nothing but itself.
        SceneRoom = sceneRoom;
        VoidHigh = voidHigh;
        VoidLow = voidLow;
    }

    public Color Accent { get; }

    public Color WindowBackground { get; }

    public Color PanelBackground { get; }

    public Color InsetBackground { get; }

    public Color WidgetBackground { get; }

    public Color WidgetBackgroundHover { get; }

    public Color WidgetBackgroundPressed { get; }

    public Color WidgetBackgroundDisabled { get; }

    public Color Border { get; }

    public Color BorderHover { get; }

    public Color Separator { get; }

    public Color SelectionFill { get; }

    public Color Highlight { get; }

    public Color Text { get; }

    public Color TextHover { get; }

    public Color TextPressed { get; }

    public Color TextDisabled { get; }

    public Color TextReadonly { get; }

    public Color Caret { get; }

    public Color Warning { get; }

    public Color Error { get; }

    public Color Success { get; }

    /// <summary>The tint a structure row's glyph wears; see the constructor.</summary>
    public Color IconStructure { get; }

    /// <summary>Text of a row that is present but not in effect.</summary>
    public Color TextReceded { get; }

    /// <summary>Modulate for an icon on such a row; see <see cref="TextReceded"/>.</summary>
    public Color IconReceded { get; }

    /// <summary>Tint of hierarchy element icons — Godot's 3D-node salmon.</summary>
    public Color IconElement { get; }

    /// <summary>The plaque's ring while an agent holds control — a warning gold.</summary>
    public Color AgentBorder { get; }

    public Color DimOverlay { get; }

    /// <summary>
    /// The buildable region's walls and floor. Its colour and nothing else —
    /// the grid, the dashes and the shading are the room's own.
    /// </summary>
    public Color SceneRoom { get; }

    /// <summary>The top of the void behind the room, and <see cref="VoidLow"/> its bottom.</summary>
    /// <remarks>
    /// A gradient <b>on the screen</b>, not a dome around the scene: it does not
    /// turn as the camera does, which is what Goxel's background is and what
    /// makes it read as the window's own backdrop rather than as weather. It is
    /// drawn as the 3D background's canvas, so it is behind everything and
    /// reaches nothing — the sky the environment carries is untouched, and with
    /// it every reflection, which means the theme now reaches no part of a model
    /// at all.
    /// <para>
    /// Hand it the same colour twice and the void is flat, which is what the
    /// dark theme does.
    /// </para>
    /// </remarks>
    public Color VoidHigh { get; }

    public Color VoidLow { get; }

    /// <summary>
    /// The darker square of the transparency chequer, and <see cref="CheckerLight"/>
    /// its lighter one. What a render with no backdrop is shown on, so that the
    /// picture's own edges can be told from the card behind it.
    /// </summary>
    public Color CheckerDark { get; }

    public Color CheckerLight { get; }

    /// <summary>Whether the surfaces are darker than the ink written on them.</summary>
    public bool IsDark { get; private init; }

    public static EditorPalette Dark() => Dark(DefaultAccent);

    public static EditorPalette Dark(Color accent) =>
        new(
            DarkSurface,
            accent,
            White,
            DarkContrast,
            pressDepth: 0.125f,
            disabledSink: 0.1f,
            structurePull: 0.3f,
            recessionPull: 0.55f,
            disabledFade: 0.35f,
            accentTextPull: 0f,
            semanticPull: 0f,
            dimOverlay: new Color(0.5f, 0.5f, 0.55f, 0.28f),
            sceneRoom: new Color(0.42f, 0.44f, 0.48f),
            // The flat void the dark theme has always had, said as a gradient
            // with no gradient in it.
            voidHigh: new Color(0.12f, 0.12f, 0.14f),
            voidLow: new Color(0.12f, 0.12f, 0.14f))
        {
            IsDark = true
        };

    public static EditorPalette Light(Color accent) =>
        new(
            LightSurface,
            accent,
            Black,
            LightContrast,
            // Hovering already darkens here, so a press has to go further or the
            // two states are one colour.
            pressDepth: 0.2f,
            // Barely: a light widget is already close to its panel, and the dark
            // theme's own sink would put a disabled one exactly on a live one.
            disabledSink: 0.03f,
            // Less than the dark theme's, because it is measured against a pale
            // pane rather than a dark one — 0.3 there is a legible grey and here
            // is a smudge.
            structurePull: 0.18f,
            // Shallower than the dark theme's, and for the same compression: a
            // quiet label has to land further from a pale surface than from a
            // dark one to read as equally quiet-but-legible. Measured against
            // the sunken track it sits on as well as the panel, which is where
            // 0.45 left an unchosen segment at 2.2.
            recessionPull: 0.3f,
            disabledFade: 0.5f,
            // Far enough that a ticked checkbox's label and a chosen segment's
            // read at least as well as they do on the dark theme — measured, 4.5
            // and 5.0 against 4.9 and 3.1.
            accentTextPull: 0.6f,
            // Far enough that the signal colours read as themselves on a pale
            // surface; not so far that gold turns brown.
            semanticPull: 0.4f,
            // Darker than the dark theme's haze and no more opaque: a pale grey
            // laid over a pale interface says nothing at all.
            dimOverlay: new Color(0.12f, 0.13f, 0.16f, 0.26f),
            // Light, and bounded above by arithmetic rather than by taste. The
            // room is lit by the same key light the model is, so its floor comes
            // out about 1.16 times its albedo and its shadowed walls about 0.6
            // — measured off the rendered frame, not assumed. A voxel model's
            // default material is a light grey near 0.70, so an albedo past 0.60
            // puts the floor brighter than the model and the model sinks into
            // it. That is the ceiling; this sits just under it.
            //
            // Tinted rather than merely bright: what separates a grey model
            // from a lavender room is hue, which is what lets the room be this
            // light at all — a neutral one is bounded above by the model's own
            // albedo and a tinted one is not. The colour and nothing else: the
            // grid, the dashes and the shading are the room's own.
            sceneRoom: new Color(0.72f, 0.72f, 0.88f),
            // And the window's own backdrop behind it: a CAD grey running from a
            // mid grey at the top down to dark, with the faint cool cast those
            // greys carry rather than the room's lavender. Which way round is
            // the one part that is taste; everything else about it is measured.
            //
            // The backdrop has two jobs and each bounds one end. It is what the
            // room is seen against, so it must be darker than the room or the
            // depth cue inverts — and it is what the panels stand on, so a pane
            // composed over it has to come out as a card rather than as more of
            // the same. Both were failing before this: 1.12 against the room's
            // lit floor and 1.01 under a pane, which is one colour said twice.
            //
            // The top stop is where the bound bites: it is also what the far
            // walls are silhouetted against, and they render near 0.16, so a
            // grey much lighter than this puts a wall's own edge on the same
            // value as the sky behind it — measured, 1.00 at a top of 0.45. And
            // the bottom is the dark theme's own void exactly, so the light
            // theme's backdrop ends where the dark theme's whole backdrop sits.
            voidHigh: new Color(0.34f, 0.35f, 0.38f),
            voidLow: new Color(0.12f, 0.12f, 0.14f))
        {
            IsDark = false
        };

    public static EditorPalette For(EditorTheme theme, Color accent) =>
        theme == EditorTheme.Light ? Light(accent) : Dark(accent);
}
