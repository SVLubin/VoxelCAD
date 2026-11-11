using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The line of numbers a transform drag shows while it is in flight — the applied
/// value of each axis being dragged, and how far past it the pointer has been
/// pulled.
/// </summary>
/// <remarks>
/// A corner of the viewport rather than a label pinned to the handles, which is the
/// CAD reading of it: a coordinate readout has a place it always is, and an eye that
/// knows where to look does not have to find text that moves with the hand. It also
/// leaves the handles unobscured at the one moment they are being used, and needs no
/// unprojection to place.
/// <para>
/// It ignores the mouse and cannot hold the keyboard: it sits over the viewport
/// surface, and a click meant for the scene must reach the scene.
/// </para>
/// </remarks>
internal sealed partial class DragReadout : PanelContainer
{
    private readonly Label _label = new() { Name = "Text" };

    public DragReadout()
    {
        Name = "DragReadout";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;

        ThemeTypeVariation = "GlassPanel";

        AddChild(new GlassBackdrop());
        AddChild(Ui.Margin(_label, 6));
    }

    /// <summary>
    /// Tabular figures, taken from the theme rather than chosen here: a readout
    /// that changes every frame reflows on every digit in a proportional font, and
    /// the numeric variation is the same one the inspector's fields already wear.
    /// </summary>
    public override void _Ready()
    {
        if (HasThemeFont("numeric", EditorThemeFactory.SemanticType))
            _label.AddThemeFontOverride("font", GetThemeFont("numeric", EditorThemeFactory.SemanticType));
    }

    /// <summary>
    /// The text to show, or empty to go away. Empty rather than a separate hide
    /// call, because "nothing has been pulled yet" and "no drag is in flight" should
    /// look the same and are both the presenter saying there is nothing to report.
    /// </summary>
    public void SetText(string text)
    {
        Visible = !string.IsNullOrEmpty(text);

        if (Visible)
            _label.Text = text;
    }
}
