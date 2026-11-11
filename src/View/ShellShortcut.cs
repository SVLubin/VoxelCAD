namespace VoxelCAD.View;

/// <summary>
/// What a chord pressed anywhere in the shell asks for. The shell reports it and
/// stops there; what undoing or saving means is a presenter's business.
/// </summary>
internal enum ShellShortcut
{
    None,
    Undo,
    Redo,
    New,
    Open,
    Save,
    SaveAs,
    Copy,
    Cut,
    Paste,

    /// <summary>Copy and paste in one press, which is why it is a chord of its own.</summary>
    Duplicate,

    /// <summary>Remove what is selected — all of it, not the one row it names.</summary>
    Delete,

    /// <summary>
    /// The settings form. Bound here as well as on the platform's own menu,
    /// because on Windows and Linux there is no platform menu to bind it on.
    /// </summary>
    Preferences,

    /// <summary>
    /// The render card. Every application whose product is a picture has a chord
    /// for this, and its menu row was the only one in the File menu with no
    /// shortcut printed beside it.
    /// </summary>
    Render
}
