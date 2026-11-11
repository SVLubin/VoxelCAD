using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a right press on a project-tree row asks for: a menu here, about this
/// row, with the visibility facts the row is already drawing.
/// </summary>
/// <param name="CanToggleVisibility">
/// Whether the row carries an eye at all — only elements do, so only they are
/// offered the hide and show.
/// </param>
/// <param name="Hidden">
/// The element's <em>own</em> flag, which is what the eye means and what a
/// click on it changes — not whether an ancestor is hiding it.
/// </param>
internal readonly record struct HierarchyMenuRequest(
    Vector2 Position,
    SelectionTarget Target,
    bool CanToggleVisibility,
    bool Hidden);
