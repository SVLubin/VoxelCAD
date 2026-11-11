using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Draws its parent's keyboard-focus ring over the parent's content instead of
/// under it. Added as a child of the panel that holds the keyboard; the panel's
/// own "focus" stylebox stays empty.
/// </summary>
/// <remarks>
/// Exists because the engine's <see cref="Tree"/> paints its focus stylebox
/// right after the background and before the rows, while clipping rows to its
/// full rect rather than to the content margin — so a row scrolled against the
/// edge paints over the ring. A child canvas item draws after everything its
/// parent drew, which puts the ring on top without touching how the rows are
/// drawn. Same move the viewport already makes for its own edge.
/// </remarks>
internal sealed partial class FocusRingOverlay : Control
{
    private Control? _host;

    public override void _EnterTree()
    {
        _host = GetParent<Control>();

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _host.FocusEntered += QueueRedraw;
        _host.FocusExited += QueueRedraw;
        _host.Resized += QueueRedraw;
    }

    public override void _ExitTree()
    {
        if (_host == null)
            return;

        _host.FocusEntered -= QueueRedraw;
        _host.FocusExited -= QueueRedraw;
        _host.Resized -= QueueRedraw;
        _host = null;
    }

    public override void _Draw()
    {
        if (_host?.HasFocus() != true)
            return;

        // The host's rect, not this control's: a Tree meddles with a child's
        // size (measured 16 units short each way), and a canvas item may draw
        // beyond its own bounds — the host's own clip still contains the ring.
        DrawStyleBox(
            GetThemeStylebox("panel_focus", EditorThemeFactory.SemanticType),
            new Rect2(-Position, _host.Size));
    }
}
