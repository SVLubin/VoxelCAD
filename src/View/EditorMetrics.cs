using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The editor's sizing vocabulary: font size, corner radius, border and content
/// margins, control separation, row height and icon size. Kept apart from
/// <see cref="EditorPalette"/> so density can be tuned without touching colour.
/// </summary>
/// <remarks>
/// Values mirror Godot's "Default" spacing preset at scale 1: base margin 4,
/// widget content margin (6, 5), corner radius 3, popup margin ~10, and a row
/// separation that lands tree and list rows near the editor's ~24px height.
/// </remarks>
internal sealed class EditorMetrics
{
    private EditorMetrics()
    {
    }

    public int FontSize { get; private init; }

    /// <summary>Rounding on panels, buttons and fields — Godot's default is 3.</summary>
    public int CornerRadius { get; private init; }

    /// <summary>Hairline border on inset controls (trees, lists, fields).</summary>
    public int BorderWidth { get; private init; }

    /// <summary>Accent ring drawn when a control holds keyboard focus.</summary>
    public int FocusBorderWidth { get; private init; }

    /// <summary>Content inset shared by panels and the base stylebox.</summary>
    public int PanelMargin { get; private init; }

    public int ButtonMarginX { get; private init; }

    public int ButtonMarginY { get; private init; }

    /// <summary>Spacing between children of box containers.</summary>
    public int Separation { get; private init; }

    public int PopupMargin { get; private init; }

    /// <summary>Vertical padding folded into tree and list rows.</summary>
    public int RowSeparation { get; private init; }

    public int IconSize { get; private init; }

    /// <summary>
    /// Width a scrollbar reserves when it shows, so its grabber is a bar the eye
    /// finds rather than a hairline. A scrollbar takes its width from its track
    /// stylebox, so this is folded into that as a content margin.
    /// </summary>
    public int ScrollBarWidth { get; private init; }

    public Vector2 DialogButtonMinSize { get; private init; }

    public static EditorMetrics Default() => new()
    {
        FontSize = 14,
        CornerRadius = 3,
        BorderWidth = 1,
        // A hairline, like every other border here: a focus ring says which
        // panel is listening, and two pixels said it louder than the thing it
        // was pointing at.
        FocusBorderWidth = 1,
        PanelMargin = 4,
        ButtonMarginX = 6,
        ButtonMarginY = 5,
        Separation = 4,
        PopupMargin = 10,
        RowSeparation = 8,
        IconSize = 16,
        ScrollBarWidth = 10,
        DialogButtonMinSize = new Vector2(105f, 34f)
    };
}
