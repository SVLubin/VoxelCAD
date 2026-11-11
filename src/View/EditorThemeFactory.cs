using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Builds a single Godot <see cref="Theme"/> from an <see cref="EditorPalette"/>
/// and <see cref="EditorMetrics"/>, styled to read like the Godot editor's own
/// chrome. This is the one place control appearance is decided; individual
/// widgets carry no colours of their own.
/// </summary>
/// <remarks>
/// The theme is meant to be assigned to the root <see cref="Window"/> so it
/// cascades to every panel, popup and dialog. Only <see cref="Control"/>-derived
/// nodes are affected — the 3D viewport, its clear colour, lighting and the
/// scene box are untouched by design.
/// </remarks>
internal static class EditorThemeFactory
{
    /// <summary>
    /// Theme type the app's semantic colours are registered under, mirroring how
    /// the Godot editor keeps its own colours in an "Editor" namespace. Controls
    /// read them with <see cref="Control.GetThemeColor(StringName, StringName)"/>
    /// so nothing hardcodes a warning/error/dimmer colour locally.
    /// </summary>
    public const string SemanticType = "VoxelCAD";

    /// <summary>
    /// A colour swatch whose colour cannot be translucent — see
    /// <see cref="StyleOpaqueSwatch"/>.
    /// </summary>
    public const string OpaqueSwatch = "OpaqueSwatch";

    /// <summary>
    /// The editor's typeface. Public because the view cube letters its faces
    /// in it too, and its world is a sub-viewport no Control theme reaches.
    /// </summary>
    public const string FontPath = "res://assets/fonts/InterVariable.ttf";

    /// <summary>How far a segmented row's thumb stands inside its track.</summary>
    private const int TrackInset = 2;

    /// <summary>
    /// A segment's own vertical padding. Nearly none: the track supplies the
    /// height, and a segment that padded itself as well came out taller than the
    /// spin boxes it stands beside.
    /// </summary>
    private const int SegmentMarginY = 1;

    /// <summary>
    /// What a caption's trailing button pads itself by, so it comes out the
    /// height of the segmented track standing in the same place on the rows
    /// above it. <see cref="TrackInset"/> exactly, and not a unit more.
    /// </summary>
    /// <remarks>
    /// A track is its text plus its inset, and that is less obvious than it
    /// looks: a <see cref="Button"/>'s minimum comes from its <b>normal</b>
    /// stylebox alone, not from the largest of its states. A segment's normal is
    /// empty, so a segment is exactly its text however much padding its lit
    /// state carries — <see cref="SegmentMarginY"/> shows when the thumb is
    /// drawn and never in the arithmetic. Measured rather than reasoned: with
    /// this at inset-plus-segment the button came out 26 against the track's 24,
    /// and no amount of setting the other five states moved it.
    /// </remarks>
    private const int CaptionMarginY = TrackInset;

    public static Theme CreateDark() => Create(EditorPalette.Dark());

    public static Theme Create(EditorPalette palette)
    {
        var theme = new Theme();

        Fill(theme, palette);

        return theme;
    }

    /// <summary>
    /// Writes a whole look into an existing <see cref="Theme"/>, replacing what
    /// was there. The same instance rather than a fresh one, and that is the
    /// whole of how a theme switch reaches the screen: every control looks its
    /// styleboxes, colours and constants up through the theme as it draws, so
    /// overwriting the entries repaints the editor with no control being told
    /// anything. What a fresh instance would need instead is every consumer
    /// re-pointed at it, and the ones that hold a value rather than a lookup —
    /// an icon assigned to a button, a colour override on a row — would be
    /// missed one at a time.
    /// </summary>
    public static void Fill(Theme theme, EditorPalette palette)
    {
        EditorMetrics metrics = EditorMetrics.Default();

        theme.DefaultFontSize = metrics.FontSize;

        StyleFonts(theme);
        StyleSemantic(theme, palette, metrics);
        EditorIconFactory.Register(theme, palette);
        StyleContainers(theme, metrics);
        StylePanels(theme, palette, metrics);
        StyleChromePanels(theme, palette, metrics);
        StyleInspectorGroups(theme, palette, metrics);
        StyleLabel(theme, palette);
        StyleButtons(theme, palette, metrics);
        StyleTree(theme, palette, metrics);
        StyleItemList(theme, palette, metrics);
        StyleLineEdit(theme, palette, metrics);
        StyleSpinBox(theme);
        StyleOpaqueSwatch(theme);
        StyleScrollBars(theme, palette, metrics);
        StylePopupMenu(theme, palette, metrics);
        StyleSeparators(theme, palette, metrics);
        StyleTabs(theme, palette, metrics);
        StyleWindows(theme, palette, metrics);
    }

    /// <summary>
    /// Inter, the same face the Godot editor's own chrome wears, as the one
    /// default font — and a tabular-figures variation for <c>LineEdit</c>,
    /// where every number in the inspector is actually drawn: digits sharing
    /// one width are what keep a value from jittering as it is dragged.
    /// </summary>
    /// <remarks>
    /// The engine's built-in font stays as the fallback, so a glyph Inter
    /// lacks degrades to the old look rather than to a tofu box.
    /// </remarks>
    private static void StyleFonts(Theme theme)
    {
        var inter = ResourceLoader.Load<Font>(FontPath);

        if (inter is FontFile file)
            file.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };

        theme.DefaultFont = inter;

        var numeric = new FontVariation
        {
            BaseFont = inter,
            OpentypeFeatures = new Godot.Collections.Dictionary
            {
                [TextServerManager.GetPrimaryInterface().NameToTag("tnum")] = 1
            }
        };

        theme.SetFont("font", "LineEdit", numeric);

        // Published under the semantic type as well, for the readouts that are not
        // fields: anything whose digits change while the eye is on them wants the
        // same columns, and the alternative is each of them building its own
        // variation off a font it had to look up.
        theme.SetFont("numeric", SemanticType, numeric);
    }

    private static void StyleSemantic(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetColor("accent", SemanticType, palette.Accent);

        // The accent as text rather than as a fill. They are the same colour on
        // the dark theme and not on the light one, where the accent written on
        // its own selection tint is one colour said twice — measured at 1.3,
        // which is a row nobody can read.
        theme.SetColor("accent_text", SemanticType, palette.TextPressed);

        // The three axis colours, as a panel writes them rather than as the
        // gizmo floats them. Registered rather than read from
        // EditorAxisColors directly, so a control that names an axis follows a
        // theme change like everything else the theme carries.
        for (var axis = 0; axis < 3; axis++)
        {
            theme.SetColor(
                EditorAxisColors.NameOf(axis), SemanticType, EditorAxisColors.OnSurface(axis, palette));
        }

        // Published for anything that paints a sunken well of its own — the
        // material preview's window is the first — so a control that draws its own
        // ground takes the palette's rather than a colour of its own invention.
        theme.SetColor("inset_background", SemanticType, palette.InsetBackground);
        theme.SetColor("border", SemanticType, palette.Border);

        theme.SetColor("warning", SemanticType, palette.Warning);
        theme.SetColor("error", SemanticType, palette.Error);
        theme.SetColor("success", SemanticType, palette.Success);
        // Not "history_undone", which is what this was called while the history
        // panel was its only customer: the plaque's subtitle already wore it, and
        // a hidden element's row wears it now. What it means is "present, not in
        // effect", and three panels agree on that.
        theme.SetColor("text_receded", SemanticType, palette.TextReceded);
        theme.SetColor("icon_receded", SemanticType, palette.IconReceded);
        theme.SetColor("dim_overlay", SemanticType, palette.DimOverlay);
        theme.SetColor("checker_dark", SemanticType, palette.CheckerDark);
        theme.SetColor("checker_light", SemanticType, palette.CheckerLight);

        // The logical size row icons draw at, whatever the textures were
        // rasterised at — the panels read it rather than measuring a bitmap.
        theme.SetConstant("icon_size", SemanticType, metrics.IconSize);

        // Every panel that can hold the keyboard carries a focus ring on the
        // widget that holds it — a Tree, a list, a field. The viewport has no
        // such widget and no ring of its own either: it used to draw one along
        // the whole window's inner edge, and that is exactly what makes it
        // unnecessary, because a ring somewhere else on screen already says the
        // scene is not the one listening. See the ledger.

        // The ring a focused panel wears, drawn by FocusRingOverlay rather than
        // by the panel itself: a Tree paints its own focus stylebox under its
        // rows, so the rows scrolled against an edge would sit on top of it.
        theme.SetStylebox("panel_focus", SemanticType, FocusRing(palette, metrics));

        // The plaque that says an agent holds the editor is a glass card like
        // every other surface, ringed in the warning gold rather than the focus
        // blue. Border only: the glass behind it is the fill.
        StyleBoxFlat plaque = BorderOnly(
            palette.AgentBorder, metrics.BorderWidth, metrics.CornerRadius);
        plaque.SetContentMarginAll(metrics.PanelMargin);
        theme.SetStylebox("agent_plaque", SemanticType, plaque);

        // There is no second ring for the countdown. It was a darker, half
        // transparent gold — which is what the ring above looks like part way
        // through fading in, so the banner animates the one colour's strength
        // instead and the two states became one number. See ControlBanner.
    }

    private static void StyleContainers(Theme theme, EditorMetrics metrics)
    {
        // BoxContainer covers both HBoxContainer and VBoxContainer through theme
        // type inheritance.
        theme.SetConstant("separation", "BoxContainer", metrics.Separation);
        theme.SetConstant("separation", "HBoxContainer", metrics.Separation);
        theme.SetConstant("separation", "VBoxContainer", metrics.Separation);
    }

    private static void StylePanels(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        // Docks sit flush against the screen edge, so they stay square like the
        // editor's own panels rather than floating with rounded corners.
        StyleBoxFlat dockStyle = Flat(palette.PanelBackground, cornerRadius: 0, margin: metrics.PanelMargin);
        theme.SetStylebox("panel", "PanelContainer", dockStyle);

        theme.SetStylebox("panel", "Panel", Flat(palette.WidgetBackground, metrics.CornerRadius, metrics.PanelMargin));
    }

    /// <summary>
    /// The chrome panels of <c>docs/panels.md</c>: a glass card with a thin
    /// opaque frame, a translucent header band over the glass, and the rail
    /// tab a collapsed panel becomes. All type variations — the opaque
    /// <c>PanelContainer</c> base stays what every dialog and readout wears.
    /// </summary>
    private static void StyleChromePanels(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        // Nothing at all: the surface draws its own fill, border and outline
        // from one distance field, because the shape a panel takes when it
        // grows from its tab is not a rectangle and no stylebox can be one.
        // See GlassPanelMaterial.
        theme.SetTypeVariation("GlassPanel", "PanelContainer");
        theme.SetStylebox("panel", "GlassPanel", new StyleBoxEmpty());

        // The radius the surfaces round to and the hairline they wear, both
        // published so the shader and the controls beside it cannot disagree
        // about them. The shader carries defaults for each; a value it is never
        // handed is a number living in two places.
        theme.SetConstant("corner_radius", SemanticType, metrics.CornerRadius);
        theme.SetConstant("border_width", SemanticType, metrics.BorderWidth);

        // The header band is translucent so the glass reads through it — a
        // shade over the pane, not a solid strip pasted on top.
        theme.SetTypeVariation("PanelHeader", "Button");

        StyleBoxFlat headerBand = HeaderBand(new Color(palette.WidgetBackground, 0.55f), metrics);
        StyleBoxFlat headerHover = HeaderBand(new Color(palette.WidgetBackgroundHover, 0.6f), metrics);

        theme.SetStylebox("normal", "PanelHeader", headerBand);
        theme.SetStylebox("hover", "PanelHeader", headerHover);
        theme.SetStylebox("pressed", "PanelHeader", headerHover);
        theme.SetStylebox("hover_pressed", "PanelHeader", headerHover);
        theme.SetColor("font_color", "PanelHeader", palette.Text);
        theme.SetColor("font_hover_color", "PanelHeader", palette.TextHover);
        theme.SetColor("font_pressed_color", "PanelHeader", palette.TextHover);
        theme.SetColor("font_hover_pressed_color", "PanelHeader", palette.TextHover);

        // The same band a grown panel wears, with its bottom corners rounded
        // too. A panel showing nothing but its header — folded in the stack, or
        // shrunk all the way to its own glyph — has no body for the band to cap,
        // so it caps the whole card and takes all four of its corners. Same
        // colours, deliberately: a tab is the panel shrunk, and the two must not
        // read as different materials on either side of the moment it arrives.
        theme.SetTypeVariation("PanelHeaderAlone", "Button");

        var aloneRadius = metrics.CornerRadius - (int)ChromePanel.FrameInset;

        StyleBoxFlat aloneBand = HeaderBand(new Color(palette.WidgetBackground, 0.55f), metrics);
        aloneBand.SetCornerRadiusAll(aloneRadius);
        StyleBoxFlat aloneHover = HeaderBand(new Color(palette.WidgetBackgroundHover, 0.6f), metrics);
        aloneHover.SetCornerRadiusAll(aloneRadius);

        theme.SetStylebox("normal", "PanelHeaderAlone", aloneBand);
        theme.SetStylebox("hover", "PanelHeaderAlone", aloneHover);
        theme.SetStylebox("pressed", "PanelHeaderAlone", aloneHover);
        theme.SetStylebox("hover_pressed", "PanelHeaderAlone", aloneHover);
        theme.SetColor("font_color", "PanelHeaderAlone", palette.Text);
        theme.SetColor("font_hover_color", "PanelHeaderAlone", palette.TextHover);

        // A context menu's rows: flat over the card's glass, lighting up under
        // the pointer like a popup's own hover, and nothing when idle.
        theme.SetTypeVariation("ContextMenuRow", "Button");

        var quiet = new StyleBoxEmpty();
        quiet.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        quiet.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        quiet.SetContentMargin(Side.Top, metrics.ButtonMarginY);
        quiet.SetContentMargin(Side.Bottom, metrics.ButtonMarginY);

        StyleBoxFlat lit = Widget(palette.Highlight, palette, metrics);

        theme.SetStylebox("normal", "ContextMenuRow", quiet);
        theme.SetStylebox("disabled", "ContextMenuRow", quiet);
        theme.SetStylebox("hover", "ContextMenuRow", lit);
        theme.SetStylebox("pressed", "ContextMenuRow", lit);
        theme.SetStylebox("hover_pressed", "ContextMenuRow", lit);

        // The answer a question expects. Accent-filled rather than merely
        // ringed: a dialog's three buttons are the same shape and the same
        // words' worth of grey, and which of them goes through is the one thing
        // a hand needs to read without reading.
        theme.SetTypeVariation("PrimaryButton", "Button");

        StyleBoxFlat primary = Widget(palette.SelectionFill, palette, metrics);
        StyleBoxFlat primaryHover = Widget(palette.Highlight.Lerp(palette.SelectionFill, 0.5f), palette, metrics);

        theme.SetStylebox("normal", "PrimaryButton", primary);
        theme.SetStylebox("hover", "PrimaryButton", primaryHover);
        theme.SetStylebox("pressed", "PrimaryButton", primary);
        theme.SetStylebox("hover_pressed", "PrimaryButton", primary);
        theme.SetColor("font_color", "PrimaryButton", palette.TextHover);
        theme.SetColor("font_hover_color", "PrimaryButton", palette.TextHover);

        // And no focus ring on top of it. It is the only focusable thing on a
        // question — that is what makes Enter answer — so the ring conveys
        // nothing the fill has not already said, and an accent ring drawn round
        // an accent fill reads as a button marked twice.
        theme.SetStylebox("focus", "PrimaryButton", new StyleBoxEmpty());

        // A question's own heading. One step up in size and in weight of
        // colour from the sentence under it — the engine's dialog said the same
        // thing by making the *message* bold, which put the emphasis on the
        // wording of the question rather than on what it is about.
        theme.SetTypeVariation("DialogTitle", "Label");
        theme.SetColor("font_color", "DialogTitle", palette.TextHover);
        theme.SetFontSize("font_size", "DialogTitle", metrics.FontSize + 2);

        // The accelerator written at the far end of a menu row. Godot spells
        // this "font_accelerator_color" on its own PopupMenu; these rows are
        // ordinary buttons, so it is a Label variation instead — same colour,
        // same meaning.
        theme.SetTypeVariation("ContextMenuShortcut", "Label");
        theme.SetColor("font_color", "ContextMenuShortcut", palette.TextDisabled);
        theme.SetFontSize("font_size", "ContextMenuShortcut", metrics.FontSize);

        // The settings rail's sections: flat like a menu row, but the chosen
        // one wears the selection tint — it is state to read across the card,
        // not a hover.
        theme.SetTypeVariation("SettingsTab", "Button");

        var railQuiet = new StyleBoxEmpty();
        railQuiet.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        railQuiet.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        railQuiet.SetContentMargin(Side.Top, metrics.ButtonMarginY);
        railQuiet.SetContentMargin(Side.Bottom, metrics.ButtonMarginY);

        StyleBoxFlat railHover = Widget(palette.Highlight, palette, metrics);
        StyleBoxFlat railOpen = Widget(palette.SelectionFill, palette, metrics);

        theme.SetStylebox("normal", "SettingsTab", railQuiet);
        theme.SetStylebox("hover", "SettingsTab", railHover);
        theme.SetStylebox("pressed", "SettingsTab", railOpen);
        theme.SetStylebox("hover_pressed", "SettingsTab", railOpen);

        // The trees inside glass panels wear no frame of their own: a pure-list
        // panel is its tree, and an inset drawn there is a frame inside the
        // panel's frame with nothing else beside it. The rows sit straight on
        // the pane — margins only, and no engraved row guides either, which the
        // selection rectangle used to collide with. A variation, so the trees
        // in dialogs keep their solid inset.
        theme.SetTypeVariation("GlassTree", "Tree");
        var bare = new StyleBoxEmpty();
        bare.SetContentMarginAll(metrics.PanelMargin);
        theme.SetStylebox("panel", "GlassTree", bare);
        theme.SetConstant("draw_guides", "GlassTree", 0);
    }

    private static StyleBoxFlat HeaderBand(Color background, EditorMetrics metrics)
    {
        // The band caps the surface, so its top corners take the surface's own
        // radius and its bottom ones stay square against the body.
        //
        // Less the inset it sits at, which is the rule for anything drawn
        // inside the glass frame: concentric curves keep an even gap only when
        // the inner radius is the outer one minus the distance between them,
        // and a band rounded at the full radius one unit in pinched towards the
        // corner it was meant to follow.
        StyleBoxFlat band = Flat(
            background, metrics.CornerRadius - (int)ChromePanel.FrameInset, margin: 0);
        band.SetCornerRadius(Corner.BottomLeft, 0);
        band.SetCornerRadius(Corner.BottomRight, 0);
        band.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        band.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        band.SetContentMargin(Side.Top, metrics.PanelMargin);
        band.SetContentMargin(Side.Bottom, metrics.PanelMargin);

        return band;
    }

    /// <summary>
    /// The inspector's groups: a hairline frame around each section, and a
    /// darker band for its header — the same widget fill buttons use, so the
    /// band reads as furniture rather than a new colour. Type variations, so
    /// the section opts in and every other panel and button stays untouched.
    /// </summary>
    private static void StyleInspectorGroups(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetTypeVariation("InspectorGroup", "PanelContainer");

        StyleBoxFlat frame = BorderOnly(palette.Border, metrics.BorderWidth, metrics.CornerRadius);
        frame.SetContentMarginAll(metrics.BorderWidth);
        theme.SetStylebox("panel", "InspectorGroup", frame);

        theme.SetTypeVariation("InspectorGroupHeader", "Button");

        // Square bottom corners: the band caps a frame it is flush with, and a
        // rounded seam against the body would read as a gap. Translucent, per
        // the one-blur rule of docs/panels.md: a band inside a glass panel
        // tints the pane, it does not paint a solid strip over it.
        StyleBoxFlat band = Widget(new Color(palette.WidgetBackground, 0.6f), palette, metrics);
        StyleBoxFlat bandHover = Widget(new Color(palette.WidgetBackgroundHover, 0.7f), palette, metrics);

        foreach (StyleBoxFlat style in new[] { band, bandHover })
        {
            style.SetCornerRadius(Corner.BottomLeft, 0);
            style.SetCornerRadius(Corner.BottomRight, 0);
        }

        theme.SetStylebox("normal", "InspectorGroupHeader", band);
        theme.SetStylebox("hover", "InspectorGroupHeader", bandHover);
        theme.SetStylebox("pressed", "InspectorGroupHeader", bandHover);
        theme.SetStylebox("hover_pressed", "InspectorGroupHeader", bandHover);

        // An icon button draws its texture at native size. The catalogue states
        // that size itself now (see EditorIconFactory.Render), so this is belt
        // and braces rather than the load-bearing cap it used to be — and it
        // still holds for any icon that arrives from somewhere else.
        theme.SetTypeVariation("ToolButton", "Button");
        theme.SetConstant("icon_max_width", "ToolButton", metrics.IconSize);

        // The active tool wears the selection tint, not the ordinary pressed
        // shade: which tool the editor is in is state worth reading across the
        // room, and a slightly darker grey is not.
        StyleBoxFlat toolActive = Widget(palette.WidgetBackground, palette, metrics);
        toolActive.BgColor = palette.SelectionFill;
        theme.SetStylebox("pressed", "ToolButton", toolActive);
        theme.SetStylebox("hover_pressed", "ToolButton", toolActive);

        StyleSegment(theme, palette, metrics);
    }

    /// <summary>
    /// One question with a handful of answers — the transform channels' field
    /// modes, the animation pace — drawn as a sunken track with the answers
    /// butted together inside it and the chosen one lit as a thumb.
    /// </summary>
    /// <remarks>
    /// What it replaced was a row of <c>Flat</c> toggle buttons, and <c>Flat</c>
    /// in Godot means no stylebox is drawn in <em>any</em> state: the chosen one
    /// was told from the rest by the colour of its text, so a mode row read as
    /// two or three little words adrift rather than as a control. Giving each of
    /// them a box of its own was better and still wrong — three lumps with gaps
    /// between them say nothing about being a set, or about only one of them
    /// being on. The track says both before a word of it is read.
    /// <para>
    /// The track is the field's own inset shade, so the row reads as a thing to
    /// operate like the spin boxes beside it, and the thumb is the selection
    /// tint the tool strip already uses for "this is the one you are in".
    /// </para>
    /// </remarks>
    private static void StyleSegment(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetTypeVariation("SegmentTrack", "PanelContainer");

        StyleBoxFlat track = Flat(palette.InsetBackground, metrics.CornerRadius, margin: 0);
        track.SetBorderWidthAll(metrics.BorderWidth);
        track.BorderColor = palette.Border;

        // The gap the thumb is inset by, all the way round: without it the lit
        // segment's corners land on the track's own and the two read as one
        // rectangle with a coloured end.
        track.SetContentMarginAll(TrackInset);

        theme.SetStylebox("panel", "SegmentTrack", track);

        // One transform cluster's card. No border: the inspector section around
        // it already has one, and a frame inside a frame is two lines saying
        // what one fill says better. Translucent, because it sits on glass and
        // the pane's blur has already been read once.
        theme.SetTypeVariation("FieldGroup", "PanelContainer");

        StyleBoxFlat group = Flat(
            new Color(palette.WidgetBackground, 0.35f), metrics.CornerRadius, margin: 0);
        group.SetContentMarginAll(metrics.PanelMargin);

        theme.SetStylebox("panel", "FieldGroup", group);

        theme.SetTypeVariation("Segment", "Button");

        // Nothing at all when it is not the answer: the track is behind it, and
        // a box drawn over a box is one box too many.
        theme.SetStylebox("normal", "Segment", new StyleBoxEmpty());
        theme.SetStylebox("disabled", "Segment", new StyleBoxEmpty());

        StyleBoxFlat hovered = Segment(palette.WidgetBackgroundHover, metrics);
        StyleBoxFlat chosen = Segment(palette.SelectionFill, metrics);

        theme.SetStylebox("hover", "Segment", hovered);
        theme.SetStylebox("pressed", "Segment", chosen);
        theme.SetStylebox("hover_pressed", "Segment", chosen);

        // The unchosen ones stay quiet: a row of equally lit answers says
        // nothing about which one is the answer.
        theme.SetColor("font_color", "Segment", palette.TextReceded);
        theme.SetColor("font_hover_color", "Segment", palette.Text);
        theme.SetColor("font_pressed_color", "Segment", palette.Text);
        theme.SetColor("font_hover_pressed_color", "Segment", palette.Text);
        theme.SetColor("font_disabled_color", "Segment", palette.TextDisabled);

        StyleCaptionButton(theme, palette, metrics);
    }

    /// <summary>
    /// A button at the far end of a caption row — the pivot's "Center" — sized to
    /// the segmented track that stands in exactly that place on the three rows
    /// above it.
    /// </summary>
    /// <remarks>
    /// An ordinary <see cref="Button"/> pads itself by
    /// <see cref="EditorMetrics.ButtonMarginY"/> top and bottom; a track pads by
    /// its own inset plus its segments', which is less. So the two came out at
    /// different heights on rows that are otherwise the same row, which reads as
    /// one of them having been forgotten. Nothing else about it changes: it is
    /// the ordinary button's shades, at the caption's height.
    /// </remarks>
    private static void StyleCaptionButton(
        Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetTypeVariation("CaptionButton", "Button");

        // Focus among them, and it is the one that matters: a Button's minimum
        // is its *largest* stylebox, so the inherited focus ring's ordinary
        // padding set the height however small the four visible states were
        // made. Transparent, because this button is MouseOnly and never takes
        // the keyboard — it is here to be measured, not to be seen.
        (string State, Color Background)[] states =
        {
            ("normal", palette.WidgetBackground),
            ("hover", palette.WidgetBackgroundHover),
            ("pressed", palette.WidgetBackgroundPressed),
            ("hover_pressed", palette.WidgetBackgroundPressed),
            ("disabled", palette.WidgetBackgroundDisabled),
            ("focus", new Color(0f, 0f, 0f, 0f))
        };

        foreach ((string state, Color background) in states)
        {
            StyleBoxFlat style = Flat(background, metrics.CornerRadius, margin: 0);

            style.SetContentMargin(Side.Left, metrics.ButtonMarginX);
            style.SetContentMargin(Side.Right, metrics.ButtonMarginX);
            style.SetContentMargin(Side.Top, CaptionMarginY);
            style.SetContentMargin(Side.Bottom, CaptionMarginY);

            theme.SetStylebox(state, "CaptionButton", style);
        }
    }

    /// <summary>
    /// A segment's own box: the ordinary widget padding sideways and almost none
    /// of it vertically, because the track around it supplies the height and a
    /// segment that padded itself as well came out a third taller than the spin
    /// boxes it sits beside.
    /// </summary>
    private static StyleBoxFlat Segment(Color background, EditorMetrics metrics)
    {
        StyleBoxFlat style = Flat(background, metrics.CornerRadius, margin: 0);

        style.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        style.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        style.SetContentMargin(Side.Top, SegmentMarginY);
        style.SetContentMargin(Side.Bottom, SegmentMarginY);

        return style;
    }

    private static void StyleLabel(Theme theme, EditorPalette palette)
    {
        theme.SetColor("font_color", "Label", palette.Text);
        theme.SetColor("font_shadow_color", "Label", new Color(0f, 0f, 0f, 0f));
    }

    private static void StyleButtons(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat normal = Widget(palette.WidgetBackground, palette, metrics);
        StyleBoxFlat hover = Widget(palette.WidgetBackgroundHover, palette, metrics);
        StyleBoxFlat pressed = Widget(palette.WidgetBackgroundPressed, palette, metrics);
        StyleBoxFlat disabled = Widget(palette.WidgetBackgroundDisabled, palette, metrics);
        StyleBoxFlat focus = FocusRing(palette, metrics);

        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        theme.SetStylebox("hover_pressed", "Button", pressed);
        theme.SetStylebox("disabled", "Button", disabled);
        theme.SetStylebox("focus", "Button", focus);

        theme.SetColor("font_color", "Button", palette.Text);
        theme.SetColor("font_hover_color", "Button", palette.TextHover);
        theme.SetColor("font_focus_color", "Button", palette.TextHover);
        theme.SetColor("font_pressed_color", "Button", palette.TextPressed);
        theme.SetColor("font_hover_pressed_color", "Button", palette.TextPressed);
        theme.SetColor("font_disabled_color", "Button", palette.TextDisabled);
    }

    private static void StyleTree(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetStylebox("panel", "Tree", Inset(palette, metrics));

        // Empty on purpose: the engine draws this one before the rows, so a row
        // scrolled against the edge would paint over it. FocusRingOverlay draws
        // the "panel_focus" ring above the rows instead.
        theme.SetStylebox("focus", "Tree", new StyleBoxEmpty());
        theme.SetStylebox("selected", "Tree", Flat(palette.SelectionFill, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("selected_focus", "Tree", Flat(palette.SelectionFill, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("hovered", "Tree", Flat(palette.Highlight, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("cursor", "Tree", BorderOnly(palette.Accent, metrics.BorderWidth, metrics.CornerRadius));
        theme.SetStylebox("cursor_unfocused", "Tree", BorderOnly(palette.Border, metrics.BorderWidth, metrics.CornerRadius));

        theme.SetColor("font_color", "Tree", palette.Text);
        theme.SetColor("font_selected_color", "Tree", palette.TextHover);
        theme.SetColor("font_hovered_color", "Tree", palette.TextHover);
        theme.SetColor("guide_color", "Tree", palette.Separator);
        theme.SetColor("relationship_line_color", "Tree", palette.Separator);
        theme.SetColor("drop_position_color", "Tree", palette.Accent);

        theme.SetConstant("v_separation", "Tree", metrics.RowSeparation);
        theme.SetConstant("h_separation", "Tree", metrics.Separation);
        theme.SetConstant("item_margin", "Tree", metrics.PanelMargin * 3);
        theme.SetConstant("inner_item_margin_left", "Tree", metrics.PanelMargin);
        theme.SetConstant("inner_item_margin_right", "Tree", metrics.PanelMargin);
        theme.SetConstant("draw_guides", "Tree", 1);
        theme.SetConstant("draw_relationship_lines", "Tree", 1);
    }

    private static void StyleItemList(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetStylebox("panel", "ItemList", Inset(palette, metrics));
        theme.SetStylebox("focus", "ItemList", FocusRing(palette, metrics));
        theme.SetStylebox("selected", "ItemList", Flat(palette.SelectionFill, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("selected_focus", "ItemList", Flat(palette.SelectionFill, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("hovered", "ItemList", Flat(palette.Highlight, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("cursor", "ItemList", BorderOnly(palette.Accent, metrics.BorderWidth, metrics.CornerRadius));
        theme.SetStylebox("cursor_unfocused", "ItemList", BorderOnly(palette.Border, metrics.BorderWidth, metrics.CornerRadius));

        theme.SetColor("font_color", "ItemList", palette.Text);
        theme.SetColor("font_selected_color", "ItemList", palette.TextHover);
        theme.SetColor("font_hovered_color", "ItemList", palette.TextHover);
        theme.SetColor("guide_color", "ItemList", palette.Separator);

        theme.SetConstant("v_separation", "ItemList", metrics.RowSeparation);
        theme.SetConstant("h_separation", "ItemList", metrics.Separation);
        theme.SetConstant("icon_margin", "ItemList", metrics.PanelMargin);
        theme.SetConstant("line_separation", "ItemList", metrics.PanelMargin);
    }

    private static void StyleLineEdit(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat normal = Field(palette, metrics);
        StyleBoxFlat readOnly = Field(palette, metrics);
        readOnly.BgColor = palette.WidgetBackgroundDisabled;

        theme.SetStylebox("normal", "LineEdit", normal);
        theme.SetStylebox("focus", "LineEdit", FocusRing(palette, metrics));
        theme.SetStylebox("read_only", "LineEdit", readOnly);

        theme.SetColor("font_color", "LineEdit", palette.Text);
        theme.SetColor("font_selected_color", "LineEdit", palette.TextHover);
        theme.SetColor("font_uneditable_color", "LineEdit", palette.TextReadonly);
        theme.SetColor("font_placeholder_color", "LineEdit", palette.TextDisabled);
        theme.SetColor("caret_color", "LineEdit", palette.Caret);
        theme.SetColor("selection_color", "LineEdit", palette.SelectionFill);
        theme.SetColor("clear_button_color", "LineEdit", palette.Text);
        theme.SetColor("clear_button_color_pressed", "LineEdit", palette.Accent);
    }

    /// <summary>
    /// The inspector's numeric fields are plain text fields: a value is typed and
    /// read back, so the spin buttons are removed rather than hidden — zero
    /// button width, and the minimum that would otherwise be taken from the arrow
    /// icons turned off, so no dead strip is left beside the text. The inner
    /// <c>LineEdit</c> already carries the field's own styling and fills what is
    /// left, which is now the whole control.
    /// </summary>
    private static void StyleSpinBox(Theme theme)
    {
        theme.SetConstant("set_min_buttons_width_from_icons", "SpinBox", 0);
        theme.SetConstant("buttons_width", "SpinBox", 0);
        theme.SetConstant("field_and_buttons_separation", "SpinBox", 0);

        // Zero width removes the buttons' footprint, but the arrow glyphs would
        // still draw, overflowing into the field. Modulating them to fully
        // transparent is what actually erases them — every state, so none of the
        // eight reappears on hover, press or disable.
        var invisible = new Color(0f, 0f, 0f, 0f);
        foreach (string side in new[] { "up", "down" })
        {
            theme.SetColor($"{side}_icon_modulate", "SpinBox", invisible);
            theme.SetColor($"{side}_hover_icon_modulate", "SpinBox", invisible);
            theme.SetColor($"{side}_pressed_icon_modulate", "SpinBox", invisible);
            theme.SetColor($"{side}_disabled_icon_modulate", "SpinBox", invisible);
        }
    }

    /// <summary>
    /// A colour swatch for a colour that cannot be translucent, with the
    /// transparency chequer taken out from under it.
    /// </summary>
    /// <remarks>
    /// Godot draws that chequer as its own command <em>beneath</em> the colour,
    /// which is invisible only while the button is fully opaque: fade the card a
    /// swatch sits on and the chequer comes through between the scene and the
    /// colour, because the two are composited separately rather than as one
    /// picture. The settings card leaving turned its accent into a chessboard,
    /// which is how this was found.
    /// <para>
    /// A variation rather than a change to <c>ColorPickerButton</c> itself,
    /// because the chequer is doing real work on the other swatch in this
    /// editor: a material's alpha <em>is</em> its opacity, so a translucent
    /// material is meant to show the chequer through its own colour. The accent
    /// has no alpha at all — its picker is built with <c>EditAlpha</c> off — so
    /// under it the chequer says nothing and is replaced with nothing.
    /// </para>
    /// </remarks>
    private static void StyleOpaqueSwatch(Theme theme)
    {
        theme.SetTypeVariation(OpaqueSwatch, "ColorPickerButton");

        var clear = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        clear.Fill(new Color(0f, 0f, 0f, 0f));

        theme.SetIcon("bg", OpaqueSwatch, ImageTexture.CreateFromImage(clear));
    }

    private static void StyleScrollBars(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        int radius = metrics.CornerRadius;

        // A scrollbar's thickness comes from its track ("scroll") stylebox, not
        // its grabber — so the track, though transparent, carries a content margin
        // that reserves the width a vertical bar's grabber then fills (and a
        // horizontal bar's height). Without it the bar collapses to a hairline and
        // overflow reads as a hard clip.
        StyleBoxFlat track = Flat(new Color(0f, 0f, 0f, 0f), radius, metrics.ScrollBarWidth / 2);

        foreach (string type in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", type, track);
            theme.SetStylebox("scroll_focus", type, track);
            theme.SetStylebox("grabber", type, Flat(palette.WidgetBackgroundHover, radius, margin: 0));
            theme.SetStylebox("grabber_highlight", type, Flat(palette.BorderHover, radius, margin: 0));
            theme.SetStylebox("grabber_pressed", type, Flat(palette.Accent, radius, margin: 0));
        }
    }

    private static void StylePopupMenu(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat panel = Flat(
            palette.PanelBackground, metrics.CornerRadius, metrics.PopupMargin);
        panel.SetBorderWidthAll(metrics.BorderWidth);
        panel.BorderColor = palette.BorderHover;

        theme.SetStylebox("panel", "PopupMenu", panel);
        theme.SetStylebox("hover", "PopupMenu", Flat(palette.Highlight, metrics.CornerRadius, margin: 0));
        theme.SetStylebox("separator", "PopupMenu", Line(palette.Separator, metrics.BorderWidth, vertical: false));
        theme.SetStylebox("labeled_separator_left", "PopupMenu", Line(palette.Separator, metrics.BorderWidth, vertical: false));
        theme.SetStylebox("labeled_separator_right", "PopupMenu", Line(palette.Separator, metrics.BorderWidth, vertical: false));

        theme.SetColor("font_color", "PopupMenu", palette.Text);
        theme.SetColor("font_hover_color", "PopupMenu", palette.TextHover);
        theme.SetColor("font_accelerator_color", "PopupMenu", palette.TextDisabled);
        theme.SetColor("font_disabled_color", "PopupMenu", palette.TextDisabled);
        theme.SetColor("font_separator_color", "PopupMenu", palette.TextReadonly);

        theme.SetConstant("v_separation", "PopupMenu", metrics.PanelMargin);
        theme.SetConstant("item_start_padding", "PopupMenu", metrics.ButtonMarginX);
        theme.SetConstant("item_end_padding", "PopupMenu", metrics.ButtonMarginX);
    }

    private static void StyleSeparators(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        theme.SetStylebox("separator", "HSeparator", Line(palette.Separator, metrics.BorderWidth, vertical: false));
        theme.SetStylebox("separator", "VSeparator", Line(palette.Separator, metrics.BorderWidth, vertical: true));
        theme.SetConstant("separation", "HSeparator", metrics.Separation);
        theme.SetConstant("separation", "VSeparator", metrics.Separation);
    }

    private static void StyleTabs(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        // Content sits under the tab strip; keep the top corners square so the
        // selected tab reads as continuous with its panel.
        StyleBoxFlat content = Flat(palette.PanelBackground, metrics.CornerRadius, margin: metrics.PanelMargin);
        content.SetCornerRadius(Corner.TopLeft, 0);
        content.SetCornerRadius(Corner.TopRight, 0);

        StyleBoxFlat selected = Flat(palette.PanelBackground, metrics.CornerRadius, margin: metrics.ButtonMarginX);
        selected.SetCornerRadius(Corner.BottomLeft, 0);
        selected.SetCornerRadius(Corner.BottomRight, 0);
        selected.SetBorderWidth(Side.Top, metrics.FocusBorderWidth);
        selected.BorderColor = palette.Accent;

        StyleBoxFlat unselected = Flat(palette.WindowBackground, metrics.CornerRadius, margin: metrics.ButtonMarginX);
        unselected.SetCornerRadius(Corner.BottomLeft, 0);
        unselected.SetCornerRadius(Corner.BottomRight, 0);

        StyleBoxFlat hovered = Flat(palette.WidgetBackgroundHover, metrics.CornerRadius, margin: metrics.ButtonMarginX);
        hovered.SetCornerRadius(Corner.BottomLeft, 0);
        hovered.SetCornerRadius(Corner.BottomRight, 0);

        foreach (string type in new[] { "TabContainer", "TabBar" })
        {
            theme.SetStylebox("tab_selected", type, selected);
            theme.SetStylebox("tab_unselected", type, unselected);
            theme.SetStylebox("tab_hovered", type, hovered);
            theme.SetStylebox("tab_disabled", type, unselected);
            theme.SetStylebox("tabbar_background", type, Flat(new Color(0f, 0f, 0f, 0f), 0, margin: 0));

            theme.SetColor("font_selected_color", type, palette.TextHover);
            theme.SetColor("font_unselected_color", type, palette.TextDisabled);
            theme.SetColor("font_hovered_color", type, palette.Text);
        }

        theme.SetStylebox("panel", "TabContainer", content);
    }

    /// <summary>
    /// The engine's own windows — dialogs and the File popup. They wear the
    /// surface language's corners and hairline, but not its glass: a
    /// <see cref="Window"/> paints its panel itself, outside any child's reach,
    /// so glass there means rebuilding the dialog as an in-shell card the way
    /// the context menu was. Worth doing, and its own decision — see the ledger.
    /// </summary>
    private static void StyleWindows(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat panel = Flat(
            palette.PanelBackground, metrics.CornerRadius, metrics.PopupMargin);
        panel.SetBorderWidthAll(metrics.BorderWidth);
        panel.BorderColor = palette.BorderHover;

        theme.SetStylebox("panel", "PopupPanel", panel);
        theme.SetStylebox(
            "panel",
            "AcceptDialog",
            Flat(palette.PanelBackground, metrics.CornerRadius, metrics.PopupMargin));

        theme.SetColor("title_color", "Window", palette.Text);
        theme.SetColor("title_outline_modulate", "Window", new Color(0f, 0f, 0f, 0f));
        theme.SetConstant("title_height", "Window", metrics.FontSize + metrics.PopupMargin);

        StyleTooltip(theme, palette, metrics);
    }

    /// <summary>
    /// The hint that follows the pointer. It was the one surface in the editor
    /// still wearing the engine's stock theme — a flat slab in Godot's own grey,
    /// square where everything else rounds — and it turns up over every panel
    /// pin, every tool button and every field, which makes it the most-seen
    /// thing nobody had dressed.
    /// </summary>
    /// <remarks>
    /// Opaque rather than glass, and that is the one place the surface language
    /// is deliberately not followed. A tooltip's whole job is to be read at a
    /// glance over whatever it happens to land on, and glass gives it whatever
    /// contrast the thing underneath allows. It takes the family's radius, its
    /// hairline and its text colour instead, which is what makes it belong.
    /// <para>
    /// The tooltip is a <see cref="PopupPanel"/> whose label is a
    /// <see cref="Label"/>; Godot resolves them through the "TooltipPanel" and
    /// "TooltipLabel" type variations, and gives neither a fallback worth
    /// keeping.
    /// </para>
    /// </remarks>
    private static void StyleTooltip(Theme theme, EditorPalette palette, EditorMetrics metrics)
    {
        // Tighter than a popup's margin: a hint is one line, and the padding a
        // menu needs around its rows reads as a box drawn round a word.
        StyleBoxFlat panel = Flat(palette.PanelBackground, metrics.CornerRadius, margin: 0);
        panel.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        panel.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        panel.SetContentMargin(Side.Top, metrics.ButtonMarginY);
        panel.SetContentMargin(Side.Bottom, metrics.ButtonMarginY);
        panel.SetBorderWidthAll(metrics.BorderWidth);
        panel.BorderColor = palette.Border;

        theme.SetTypeVariation("TooltipPanel", "PopupPanel");
        theme.SetStylebox("panel", "TooltipPanel", panel);

        theme.SetTypeVariation("TooltipLabel", "Label");
        theme.SetFontSize("font_size", "TooltipLabel", metrics.FontSize);
        theme.SetColor("font_color", "TooltipLabel", palette.Text);

        // The engine draws a shadow behind tooltip text by default, which on a
        // panel this dark is a smear rather than a lift.
        theme.SetColor("font_shadow_color", "TooltipLabel", new Color(0f, 0f, 0f, 0f));
        theme.SetColor("font_outline_color", "TooltipLabel", new Color(0f, 0f, 0f, 0f));
        theme.SetConstant("outline_size", "TooltipLabel", 0);
        theme.SetConstant("shadow_offset_x", "TooltipLabel", 0);
        theme.SetConstant("shadow_offset_y", "TooltipLabel", 0);
    }

    private static StyleBoxFlat Widget(Color background, EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat style = Flat(background, metrics.CornerRadius, margin: 0);
        style.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        style.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        style.SetContentMargin(Side.Top, metrics.ButtonMarginY);
        style.SetContentMargin(Side.Bottom, metrics.ButtonMarginY);

        return style;
    }

    private static StyleBoxFlat Field(EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat style = Widget(palette.InsetBackground, palette, metrics);
        style.SetBorderWidthAll(metrics.BorderWidth);
        style.BorderColor = palette.Border;

        return style;
    }

    private static StyleBoxFlat Inset(EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat style = Flat(palette.InsetBackground, metrics.CornerRadius, margin: metrics.PanelMargin);
        style.SetBorderWidthAll(metrics.BorderWidth);
        style.BorderColor = palette.Border;

        return style;
    }

    private static StyleBoxFlat FocusRing(EditorPalette palette, EditorMetrics metrics)
    {
        StyleBoxFlat style = BorderOnly(palette.Accent, metrics.FocusBorderWidth, metrics.CornerRadius);
        style.SetContentMargin(Side.Left, metrics.ButtonMarginX);
        style.SetContentMargin(Side.Right, metrics.ButtonMarginX);
        style.SetContentMargin(Side.Top, metrics.ButtonMarginY);
        style.SetContentMargin(Side.Bottom, metrics.ButtonMarginY);

        return style;
    }

    private static StyleBoxFlat BorderOnly(Color borderColor, int borderWidth, int cornerRadius)
    {
        var style = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = borderColor
        };

        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(cornerRadius);

        return style;
    }

    private static StyleBoxFlat Flat(Color background, int cornerRadius, int margin)
    {
        var style = new StyleBoxFlat { BgColor = background };

        style.SetCornerRadiusAll(cornerRadius);
        style.SetBorderWidthAll(0);
        style.SetContentMarginAll(margin);

        return style;
    }

    private static StyleBoxLine Line(Color color, int thickness, bool vertical) => new()
    {
        Color = color,
        Thickness = thickness,
        Vertical = vertical
    };
}
