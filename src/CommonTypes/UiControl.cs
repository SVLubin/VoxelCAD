using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One control of the editor chrome, as an agent sees it: something to address,
/// something to read, and somewhere to aim.
/// </summary>
/// <remarks>
/// <see cref="Reference"/> is the load-bearing field. It is what the injection
/// commands take, so it is an identifier first and a label second: a path of node
/// names, with a generated <c>Type@index</c> segment wherever a control was never
/// given a name of its own. Godot forbids <c>@</c> in a name a caller chose, so
/// the two forms can never be confused for one another.
/// </remarks>
/// <param name="Rect">
/// Where it is, in input coordinates — the same space <c>window</c> reports and
/// the injection commands take, which is <em>not</em> the canvas space a control's
/// own rect is measured in.
/// </param>
/// <param name="Disabled">
/// Whether it would refuse to be interacted with: a disabled button, an
/// uneditable field. Distinct from invisible, and a refusal either way.
/// </param>
/// <param name="TakesMouse">
/// Whether a click stops here rather than passing through. What decides which
/// control a point actually lands on, and so what the occlusion check reads.
/// </param>
/// <param name="Layer">
/// How many windows deep it sits: zero in the main window, one inside a popup or
/// a dialog drawn over it, and so on. A window is drawn above everything in the
/// window that owns it whatever its position in the tree, so this — not tree
/// order — is what decides which of two overlapping controls is on top.
/// </param>
public readonly record struct UiControl(
    string Reference,
    string Type,
    string Text,
    Rect2 Rect,
    bool Visible,
    bool Disabled,
    bool Focused,
    bool TakesMouse,
    int Layer);
