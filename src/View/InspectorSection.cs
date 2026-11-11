using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Collapsible titled group — the inspector's basic unit. A framed panel with a
/// header band, so the dock reads as a set of groups rather than one run of
/// rows. The frame and band are theme type variations decided in
/// <see cref="EditorThemeFactory"/>; nothing here carries a colour.
/// </summary>
/// <remarks>
/// <b>Its header is a chrome panel's header one size down, and it behaves like
/// one.</b> Both are a band you press to fold the body under it, so both ease
/// the body away rather than switching it off, and both carry the same mark
/// turning the same way — the chevron points <em>up</em> while the body shows,
/// because it says what the press will do rather than what the group is. It was
/// a text arrow, <c>▾</c> and <c>▸</c>, glued to the front of the title: the one
/// text arrow left in the codebase, in the disclosure-triangle convention this
/// editor deliberately left behind, and instant where its own twin eased.
/// <para>
/// The body is folded by a <b>mask</b>, on the panels' own rule: the rows are
/// laid out at their full height and a clip closes over them, so nothing inside
/// re-flows on the way. A clipped child receives no input either, so a row under
/// the closing edge stops being clickable as it goes; the outright hide at the
/// end is for what is left after that — a body with nothing showing is nothing
/// to draw and nothing to measure.
/// </para>
/// <para>
/// <b>Taking the body's size over from a container means answering everything
/// the container answered.</b> A <c>MarginContainer</c> in a box was told its
/// height by the box whenever anything about it changed; measured once here and
/// left, the clip kept whatever the rows happened to be worth at
/// <see cref="_Ready"/> — before a theme has reached them — and cut the last row
/// off the bottom of the group for the rest of the session. So the two signals a
/// container lives on are answered here instead: the rows' own minimum changing,
/// and the room they are given changing.
/// </para>
/// </remarks>
internal sealed partial class InspectorSection : PanelContainer
{
    /// <summary>Inset of the body rows from the frame — the dock's own margin.</summary>
    private const int BodyMargin = 4;

    /// <summary>The mark's side, matching the header glyphs elsewhere.</summary>
    private const float ChevronSide = 12f;

    /// <summary>How far the mark sits in from the band's far edge.</summary>
    private const float ChevronInset = 6f;

    private readonly Button _header;
    private readonly TextureRect _chevron = new()
    {
        Name = "SectionChevron",
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        MouseFilter = MouseFilterEnum.Ignore
    };

    private readonly VBoxContainer _body = new();
    private readonly MarginContainer _bodyMargin;

    private readonly Control _bodyClip = new()
    {
        Name = "SectionBody",
        ClipContents = true
    };

    /// <summary>Nought folded, one open — the body's share of its own height.</summary>
    private readonly Smoothed _open;

    /// <summary>Nought points down, one points up. The chevron is one drawing mirrored.</summary>
    private readonly Smoothed _turned;

    public InspectorSection(string title, MotionTiming timing)
    {
        ThemeTypeVariation = "InspectorGroup";

        _open = new Smoothed(MotionSpeed.Opacity, timing);
        _turned = new Smoothed(MotionSpeed.Opacity, timing);

        // Snapped rather than left unaimed: the first target a Smoothed is ever
        // given snaps, so a group would arrive open with no turn on its mark and
        // only fold properly the second time.
        _open.SnapTo(1f);
        _turned.SnapTo(1f);

        _header = new Button
        {
            Text = title,
            ThemeTypeVariation = "InspectorGroupHeader",
            Alignment = HorizontalAlignment.Left
        }.MouseOnly();

        // At the far end of the band, which is where a chrome panel's header
        // carries its own — and it is also the one place a Button will not
        // argue about: the text's left inset comes from the stylebox, not from
        // any constant, so a mark placed at the head of the band lands on top
        // of the title rather than beside it. Found by looking.
        _header.AddChild(_chevron);

        // The margin wrapper is what folds away, not the body: a hidden child
        // still leaves a MarginContainer its margins, and a collapsed group
        // should be exactly its header band.
        _bodyMargin = Ui.Margin(_body, BodyMargin);
        _bodyClip.AddChild(_bodyMargin);

        _header.Pressed += ToggleExpanded;

        // The mark hangs on the band's far edge, so where it belongs changes
        // whenever the band does — and the band is as wide as the panel, which
        // moves every frame a panel opens. Placed only on the frames this node
        // happens to be animating, it stayed wherever the header's *minimum*
        // width had put it: hard against the end of the title, reading as part
        // of the word. Seen, not reasoned about.
        _header.Resized += PlaceChevron;

        // What a container would have answered. The rows' minimum is not final
        // when this is built — a control outside the tree has no theme, so no
        // font, so no height it will keep — and the room they are given follows
        // the panel. Either one moving without the clip being re-solved leaves a
        // group cut off at the bottom.
        _bodyMargin.MinimumSizeChanged += PlaceBody;
        _bodyClip.Resized += PlaceBody;

        AddChild(Ui.VBox(_header, _bodyClip));
    }

    private bool IsExpanded => _turned.Target > 0f;

    public void AddRow(Control row) => _body.AddChild(row);

    /// <summary>
    /// Opens or folds it outright, with no animation. What a section is built
    /// already folded needs — easing a section shut on the frame the card it lives
    /// on appears would read as the card moving rather than as a fold.
    /// </summary>
    public void SnapExpanded(bool expanded)
    {
        _open.SnapTo(expanded ? 1f : 0f);
        _turned.SnapTo(expanded ? 1f : 0f);

        SetProcess(true);
    }

    /// <summary>Whether the body is showing, or on its way to.</summary>
    public bool Expanded => IsExpanded;

    /// <summary>The fold changed, by a press on the band.</summary>
    public event Action<bool>? Folded;

    /// <summary>
    /// The mark, and the idling. Both belong here rather than in the
    /// constructor: a node outside the tree cannot see the theme, and Godot
    /// turns processing on for a script that overrides <c>_Process</c> as it
    /// sets the node up — after any constructor.
    /// </summary>
    public override void _Ready()
    {
        LoadChevron();
        PlaceBody();
        PlaceChevron();
        Idle();
    }

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            LoadChevron();
    }

    public override void _Process(double delta)
    {
        var step = (float)delta;

        _open.Tick(step);
        _turned.Tick(step);

        PlaceBody();
        PlaceChevron();

        if (_open.IsSettled && _turned.IsSettled)
            Idle();
    }

    private void ToggleExpanded()
    {
        bool expanded = !IsExpanded;

        _open.Retarget(expanded ? 1f : 0f);
        _turned.Retarget(expanded ? 1f : 0f);

        SetProcess(true);

        Folded?.Invoke(expanded);
    }

    /// <summary>
    /// The mask. The rows keep their full height throughout and the clip closes
    /// over them — nothing inside a folding group moves.
    /// </summary>
    /// <remarks>
    /// The rows are measured on every call rather than remembered: this is the
    /// one place their height is asked for at all, so a stale answer here is a
    /// group that clips its own last row and never stops.
    /// </remarks>
    private void PlaceBody()
    {
        float open = _open.Value;
        float natural = _bodyMargin.GetCombinedMinimumSize().Y;

        _bodyClip.CustomMinimumSize = new Vector2(0f, natural * open);
        _bodyMargin.Position = Vector2.Zero;
        _bodyMargin.Size = new Vector2(_bodyClip.Size.X, natural);

        // Nothing showing is nothing to draw: the clip already refuses the rows
        // it covers, and the last of them goes with the body.
        _bodyClip.Visible = open > 0.001f;
    }

    private void PlaceChevron()
    {
        _chevron.Size = new Vector2(ChevronSide, _header.Size.Y);
        _chevron.Position = new Vector2(_header.Size.X - ChevronSide - ChevronInset, 0f);
        _chevron.PivotOffset = _chevron.Size * 0.5f;

        // One drawing mirrored through its own middle: +1 is the artwork, down,
        // and -1 is the same mark pointing up. A sign change rather than a swap,
        // which is what makes it a turn instead of two pictures.
        _chevron.Scale = new Vector2(1f, 1f - 2f * _turned.Value);
    }

    private void LoadChevron()
    {
        _chevron.Texture =
            GetThemeIcon(EditorIconCatalog.ChevronDown, EditorThemeFactory.SemanticType);

        _chevron.CustomMinimumSize = new Vector2(ChevronSide, ChevronSide);
    }

    private void Idle() => SetProcess(!_open.IsSettled || !_turned.IsSettled);
}
