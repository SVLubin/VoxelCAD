using System;

namespace VoxelCAD.View;

/// <summary>
/// What a menu can ask the shell for. One record rather than a reference to the
/// shell: a menu bar has no business reaching anything else the shell owns, and
/// the platform's menu and the in-window popup are handed the same set.
/// </summary>
internal sealed record ShellMenuActions(
    Action New,
    Action Open,
    Action Recover,
    Action Save,
    Action SaveAs,
    Action ImportReference,
    Action Import,
    Action Export,
    Action Render,
    Action Preferences,
    Action Undo,
    Action Redo,
    Action Cut,
    Action Copy,
    Action Paste,
    Action Duplicate,
    Action Delete);
