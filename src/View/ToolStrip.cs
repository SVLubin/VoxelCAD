using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Row of tool buttons for the inspector. Deliberately not a
/// <see cref="ButtonGroup"/>: exclusivity belongs to the editor — the buttons
/// report what was asked for and mirror whatever
/// <see cref="SetActiveTool"/> pushes back in, so this panel, the tool keys and
/// anything else that switches tools can never disagree about which is on.
/// </summary>
internal sealed partial class ToolStrip : HBoxContainer
{
    private readonly Dictionary<ToolKind, Button> _buttonsByToolDictionary = new();

    private ToolKind _activeTool;

    public event Action<ToolKind>? ToolSelected;

    public ToolStrip()
    {
        AddButton(ToolKind.Select, "ToolSelect", "Select (Q) — click to pick, again to go deeper");
        AddButton(ToolKind.Paint, "ToolPaint", "Paint (B)");
        AddButton(ToolKind.Fill, "ToolFill", "Fill (G) — recolour the connected volume");
        AddButton(ToolKind.Eyedropper, "ToolEyedropper", "Eyedropper (I)");
        AddButton(ToolKind.Extrude, "ToolExtrude", "Extrude face (X) — Shift to carve in");
        AddButton(ToolKind.Move, "ToolMove", "Move (W)");
        AddButton(ToolKind.Rotate, "ToolRotate", "Rotate (E)");
        AddButton(ToolKind.Scale, "ToolScale", "Scale (R)");
    }

    /// <summary>
    /// Icons come from the theme, which is reachable only once the strip is in
    /// the tree — the buttons exist before that, so they are dressed here.
    /// </summary>
    public override void _Ready()
    {
        foreach ((ToolKind tool, Button button) in _buttonsByToolDictionary)
            button.Icon = GetThemeIcon(
                EditorIconCatalog.ForTool(tool), EditorThemeFactory.SemanticType);
    }

    /// <summary>Reflects session state without reporting it back as a request.</summary>
    public void SetActiveTool(ToolKind tool)
    {
        _activeTool = tool;

        foreach ((ToolKind kind, Button button) in _buttonsByToolDictionary)
            button.SetPressedNoSignal(kind == tool);
    }

    private void AddButton(ToolKind tool, string name, string tooltip)
    {
        ToolKind captured = tool;
        var button = new Button
        {
            ToggleMode = true,
            TooltipText = tooltip,
            ThemeTypeVariation = "ToolButton"
        }.Named(name).MouseOnly();

        button.Pressed += () =>
        {
            ToolSelected?.Invoke(captured);

            // The session is the authority. Its answer normally arrives through
            // SetActiveTool inside the invoke above; re-asserting afterwards is
            // what keeps a click on the active button from untoggling it, and a
            // click nobody answered from pretending it switched something.
            SetActiveTool(_activeTool);
        };

        _buttonsByToolDictionary[tool] = button;
        AddChild(button);
    }
}
