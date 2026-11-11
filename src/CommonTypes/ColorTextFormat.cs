namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which way a colour is written in the one field beside the swatch. One at a
/// time, deliberately: Godot's own picker shows every representation at once,
/// and reading three numbers to find the one you wanted is the thing this row
/// exists to remove.
/// </summary>
/// <remarks>
/// Here rather than with the field that shows it, because the choice is stored:
/// the preferences layer may not see the view, so an enum both of them name has
/// to be one neither of them owns.
/// </remarks>
public enum ColorTextFormat
{
    /// <summary>Six digits, or eight where the colour is not opaque.</summary>
    Hex,

    /// <summary>Nought to 255 per channel, which is what a colour pasted from elsewhere is in.</summary>
    Rgb,

    /// <summary>Degrees and two percentages — how a shade is reasoned about rather than measured.</summary>
    Hsv
}
