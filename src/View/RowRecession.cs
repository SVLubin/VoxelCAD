using Godot;

namespace VoxelCAD.View;

/// <summary>
/// How a tree row is drawn when it is there but not in effect: an undone history
/// record, an element hidden from the viewport and the export. Text fades toward
/// the background and the icon fades with it.
/// </summary>
/// <remarks>
/// The two halves live together because they are one look — a receded row whose
/// icon still shines at full strength reads as half-receded rather than as quiet,
/// which is how the history panel found out. The colours come from the theme
/// (<c>text_receded</c>, <c>icon_receded</c>), so a panel decides <em>which</em>
/// rows recede and never what receding looks like.
/// </remarks>
internal readonly struct RowRecession
{
    private readonly Color _text;
    private readonly Color _icon;

    public RowRecession(Color text, Color icon)
    {
        _text = text;
        _icon = icon;
    }

    public void Apply(TreeItem item)
    {
        item.SetCustomColor(0, _text);
        item.SetIconModulate(0, _icon);
    }

    /// <summary>
    /// Puts the row back to the theme's own colours. Clears the custom colour
    /// outright, so a panel must not use one for two purposes on a single row —
    /// the project tree's emphasis is on scene rows and its recession on element
    /// rows, which is what keeps those two apart.
    /// </summary>
    public static void Clear(TreeItem item)
    {
        item.ClearCustomColor(0);
        item.SetIconModulate(0, Colors.White);
    }
}
