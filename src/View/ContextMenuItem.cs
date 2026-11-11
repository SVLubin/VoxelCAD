namespace VoxelCAD.View;

/// <summary>One line of a context menu, or — with no label — a separator.</summary>
/// <param name="Icon">
/// The catalogue glyph the row wears — the same one its button wore before the
/// action moved into the menu, so the picture a hand learned stays with it.
/// </param>
/// <param name="Shortcut">
/// The chord that does the same thing without opening the menu, written the
/// platform's way, or empty where there is none. A menu is where a chord is
/// learned; one that hides its accelerators teaches nobody, and the platform's
/// own menu bar was already showing them.
/// </param>
internal readonly record struct ContextMenuItem(
    int Id,
    string Label,
    string Name,
    string Icon,
    bool Enabled,
    string Shortcut = "")
{
    public static ContextMenuItem Separator() =>
        new(-1, string.Empty, string.Empty, string.Empty, false);

    public bool IsSeparator => Label.Length == 0;
}
