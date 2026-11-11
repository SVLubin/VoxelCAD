using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One transform cluster as a single control: a caption with something at its
/// far end, and the three axis boxes under it, on one quiet card.
/// </summary>
/// <remarks>
/// The parts were laid out loose — a title adrift at the left, a control adrift
/// at the right, and three boxes each with a letter floating beside it — so a
/// channel read as five things that happened to be near each other rather than
/// as the one thing it is. A card is what says they belong together, and it says
/// it without a border: the section already has one, and a frame inside a frame
/// is two lines saying what one fill says better.
/// <para>
/// The fill is translucent on purpose. This sits on glass, and the panel's rule
/// is one screen read per pane — everything inside tints the blur that has
/// already been taken rather than taking another.
/// </para>
/// <para>
/// Shared by the transform channels and the pivot because their shape is the
/// same shape, and it was written out twice before this. Each subclass still
/// makes its own boxes: what is common is where the parts stand, not what a box
/// does when it is typed into.
/// </para>
/// </remarks>
internal partial class AxisBlock : PanelContainer
{
    public const int AxisCount = 3;

    private static readonly string[] AxisNames = { "X", "Y", "Z" };

    /// <summary>
    /// Room for one letter, so the three boxes below start at the same place
    /// whatever is written in them.
    /// </summary>
    private const float AxisLabelWidth = 12f;

    /// <param name="trailing">
    /// The one control the caption carries at its far end — a mode toggle, a
    /// button. Passed in rather than switched on here: the block is about where
    /// things stand, and what belongs at the end of a caption is the caller's.
    /// </param>
    protected AxisBlock(string title, Control trailing, IReadOnlyList<SpinBox> axes)
    {
        ThemeTypeVariation = "FieldGroup";

        HBoxContainer values = Ui.HBox();

        for (var axis = 0; axis < AxisCount; axis++)
        {
            values.AddChild(AxisLabel(axis));
            values.AddChild(axes[axis]);
        }

        AddChild(Ui.VBox(Ui.HBox(Ui.Label(title).Expand(), trailing), values));
    }

    /// <summary>
    /// The letter, in the colour its axis wears everywhere else in the editor.
    /// The handles are red, green and blue; a panel that names the same three
    /// axes in the same grey is a second vocabulary for one set of facts.
    /// </summary>
    /// <remarks>
    /// The colour comes from the theme rather than from
    /// <see cref="EditorAxisColors"/> directly, because it is not quite the
    /// gizmo's: on a light card the three are darkened until they can be read.
    /// It is an override rather than a lookup, so it is re-applied when the
    /// theme is repainted — see <see cref="TintAxisLabels"/>.
    /// </remarks>
    private static Label AxisLabel(int axis)
    {
        var label = new Label
        {
            Name = $"Axis{axis}",
            Text = AxisNames[axis],
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        return label.MinSize(AxisLabelWidth, 0f);
    }

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            TintAxisLabels();
    }

    public override void _Ready() => TintAxisLabels();

    /// <summary>
    /// Writes each letter its axis colour. Walked rather than held in a list,
    /// because the boxes are built by the subclass and this base only knows that
    /// the letters are there — the names are the contract.
    /// </summary>
    private void TintAxisLabels()
    {
        for (var axis = 0; axis < 3; axis++)
        {
            if (FindChild($"Axis{axis}", recursive: true, owned: false) is not Label label)
                continue;

            label.AddThemeColorOverride(
                "font_color",
                GetThemeColor(EditorAxisColors.NameOf(axis), EditorThemeFactory.SemanticType));
        }
    }
}
