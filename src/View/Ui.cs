using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// A thin fluent builder over Godot's Control system — not a replacement for it.
/// Keeps layout code declarative instead of a wall of imperative node assembly.
/// </summary>
internal static class Ui
{
    public static VBoxContainer VBox(params Control[] children) => Box<VBoxContainer>(children);

    public static HBoxContainer HBox(params Control[] children) => Box<HBoxContainer>(children);

    public static MarginContainer Margin(Control child, int all)
    {
        var margin = new MarginContainer();

        margin.AddThemeConstantOverride("margin_left", all);
        margin.AddThemeConstantOverride("margin_right", all);
        margin.AddThemeConstantOverride("margin_top", all);
        margin.AddThemeConstantOverride("margin_bottom", all);
        margin.AddChild(child);

        return margin;
    }

    public static Label Label(string text) => new() { Text = text };

    public static Button Button(string text, Action onPressed)
    {
        var button = new Button { Text = text }.MouseOnly();
        button.Pressed += onPressed;

        return button;
    }

    private static T Box<T>(Control[] children) where T : Container, new()
    {
        var box = new T();

        foreach (Control child in children)
            box.AddChild(child);

        return box;
    }
}
