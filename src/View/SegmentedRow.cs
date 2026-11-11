using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>One choice in a <see cref="SegmentedRow"/>.</summary>
/// <param name="Name">
/// What the segment is called, which is the reference an agent aims at. Not
/// decoration: keeping every choice an ordinary named <see cref="Button"/> is
/// the whole reason this is a track around buttons rather than one drawn
/// control — see the class remarks.
/// </param>
internal readonly record struct SegmentSpec(string Name, string Label, string Hint);

/// <summary>
/// A row of choices that reads as one control: a sunken track with the segments
/// butted together inside it, and the chosen one lit as a thumb.
/// </summary>
/// <remarks>
/// It replaces a plain row of toggle buttons, which was three separate lumps
/// with gaps between them — nothing said they were a set, and nothing said only
/// one of them could be on. A track says both before a word of it is read: the
/// segments share a box, so they are one question, and exactly one of them is
/// filled in.
/// <para>
/// <b>Buttons in a track, not a control that draws itself.</b> Drawing it would
/// be less code and would cost the thing this editor keeps paying to have: an
/// agent aims at a <see cref="Control"/> by reference, and a segment drawn
/// inside a canvas is reachable only by guessing at a fraction of a rect — the
/// ledger's standing list-row gap. The context menu was built this way for the
/// same reason.
/// </para>
/// </remarks>
internal partial class SegmentedRow : PanelContainer
{
    /// <summary>
    /// The floor under an equalised row, for the case where every label in it is
    /// a character or two.
    /// </summary>
    private const float MinimumSegmentWidth = 30f;

    /// <summary>Breathing room over the widest label, so nothing sits on its edge.</summary>
    private const float SegmentSlack = 6f;

    private readonly HBoxContainer _row = new() { Name = "Segments" };
    private readonly List<Button> _buttonsList = new();

    private ButtonGroup _group = new();

    /// <summary>Whether the track spreads over the row it is given.</summary>
    private bool _fills;

    public SegmentedRow()
    {
        ThemeTypeVariation = "SegmentTrack";

        // Butted together: the track is what separates them from the world, and
        // a gap between two segments would make them two things again.
        _row.AddThemeConstantOverride("separation", 0);

        // The track hugs its segments rather than filling whatever row it is
        // dropped into: it is a control, not a band.
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

        AddChild(_row);
    }

    /// <summary>Which segment was pressed, by its place in the list it was given.</summary>
    public event Action<int>? Chosen;

    /// <summary>
    /// Spreads the track over the width it is given, its segments still equal —
    /// for a column of full-width fields, where a track that hugged its words
    /// would hang off one edge at a width nobody chose. The equal-width rule is
    /// untouched: what changes is only that they share the row rather than the
    /// widest label.
    /// </summary>
    public void Fill()
    {
        _fills = true;

        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        foreach (Button button in _buttonsList)
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public void SetSegments(IReadOnlyList<SegmentSpec> segments)
    {
        foreach (Button button in _buttonsList)
        {
            _row.RemoveChild(button);
            button.QueueFree();
        }

        _buttonsList.Clear();

        // A fresh group, or the freed buttons' membership outlives them and the
        // next selection has to argue with a ghost.
        _group = new ButtonGroup();

        for (var index = 0; index < segments.Count; index++)
        {
            int captured = index;
            SegmentSpec segment = segments[index];

            var button = new Button
            {
                Text = segment.Label,
                TooltipText = segment.Hint,
                ToggleMode = true,
                ButtonGroup = _group,
                ThemeTypeVariation = "Segment"
            }.MouseOnly().Named(segment.Name);

            button.Pressed += () => Chosen?.Invoke(captured);

            if (_fills)
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            _buttonsList.Add(button);
            _row.AddChild(button);
        }

        EqualiseSegments();
    }

    /// <summary>Reflects state without reporting it back as a choice.</summary>
    /// <remarks>
    /// Every segment is written, not only the chosen one, and the
    /// <see cref="ButtonGroup"/> above does not make that redundant:
    /// <c>SetPressedNoSignal</c> deliberately skips the group's unpressing —
    /// that is what "no signal" means — so a group only ever answers a real
    /// press. Setting one alone therefore <em>added</em> a lit segment. It
    /// showed wherever a row was told its value twice: the settings form builds
    /// its theme row at the class's own default and then fills it from the
    /// store, so opening the form on the light theme lit Dark and Light at once,
    /// and the pace row did the same on anything but Fast.
    /// </remarks>
    public void Select(int index)
    {
        for (var segment = 0; segment < _buttonsList.Count; segment++)
            _buttonsList[segment].SetPressedNoSignal(segment == index);
    }

    /// <summary>
    /// Whether the question is being asked at all. Refusing the press is the
    /// half that matters — a row dimmed by a modulate but still answering would
    /// be the worse of the two — and the caller dims the words beside it.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        foreach (Button button in _buttonsList)
            button.Disabled = !enabled;
    }

    /// <summary>
    /// The theme is only reachable once this is in the tree, and a font it has
    /// not seen is a width it cannot measure.
    /// </summary>
    public override void _Ready() => EqualiseSegments();

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            EqualiseSegments();
    }

    /// <summary>
    /// One width for every segment, taken from the widest label in the row.
    /// </summary>
    /// <remarks>
    /// A track whose segments were each as wide as their own word came out
    /// lopsided — "Free" against "1" is four characters against one — and a
    /// lopsided set of choices reads as a set of unrelated buttons again, which
    /// is the thing the track exists to stop. Equal segments also mean the thumb
    /// is the same shape wherever it lands.
    /// </remarks>
    private void EqualiseSegments()
    {
        if (!IsInsideTree() || _buttonsList.Count == 0)
            return;

        float widest = MinimumSegmentWidth;

        foreach (Button button in _buttonsList)
            widest = MathF.Max(widest, Measure(button) + SegmentSlack);

        foreach (Button button in _buttonsList)
            button.CustomMinimumSize = new Vector2(widest, 0f);
    }

    /// <summary>
    /// What one segment's label actually needs: the string measured in the font
    /// it will be drawn in, plus the padding of the box it will be drawn on.
    /// </summary>
    /// <remarks>
    /// Measured here rather than taken from <c>GetCombinedMinimumSize</c>,
    /// which under-reports these: pinned to what it answered, "90°" was drawn
    /// without its degree sign — the glyph was simply past the end of the
    /// segment. A button whose <c>normal</c> state is an empty stylebox is the
    /// suspect, that being where the engine looks for the padding to add.
    /// </remarks>
    private static float Measure(Button button)
    {
        Font font = button.GetThemeFont("font");
        int fontSize = button.GetThemeFontSize("font_size");
        StyleBox box = button.GetThemeStylebox("pressed");

        return font.GetStringSize(button.Text, HorizontalAlignment.Left, -1f, fontSize).X
            + box.GetMargin(Side.Left)
            + box.GetMargin(Side.Right);
    }
}
