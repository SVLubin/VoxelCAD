using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// One gesture the command layer asked for, flattened so a test can assert
/// against whichever fields that kind of gesture uses.
/// </summary>
internal sealed class Injection
{
    public Injection(string kind)
    {
        Kind = kind;
    }

    public string Kind { get; }

    /// <summary>Where it starts — the click, the pointer, the drag's press.</summary>
    public Vector2 Point { get; init; }

    public Vector2 To { get; init; }

    /// <summary>Clicks, drag steps, or wheel notches, depending on the kind.</summary>
    public int Count { get; init; }

    public MouseButton Button { get; init; }

    public InputModifiers Modifiers { get; init; }

    public Key Key { get; init; }

    public HoldAction Action { get; init; }

    public string Text { get; init; } = string.Empty;
}
