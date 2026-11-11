using System;
using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// A picture standing in the scene to be traced over: which file it is, how big
/// it is in cells, and how it is drawn. Where it stands is the element's own
/// transform, the same as for anything else in the tree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SizeInCells"/> is the truth and <see cref="PixelSize"/> is what the
/// file happened to be, so "pixels per cell" is a division rather than a second
/// stored number that could come to disagree with the first. One pixel to one
/// voxel — the whole point for pixel art — is therefore a size, not a mode.
/// </para>
/// <para>
/// The resolution is carried in the document rather than being asked of the file
/// each time, so the readout survives the asset going missing and the headless CLI
/// answers the same as the editor.
/// </para>
/// </remarks>
/// <param name="Path">
/// Where the picture is. Absolute in memory; the file carries it relative when the
/// asset sits under the project's own directory, which is the storage layer's
/// business and nobody else's.
/// </param>
/// <param name="SizeInCells">How much of the scene the plane covers, width by height.</param>
/// <param name="PixelSize">
/// The picture's own resolution as it was last read. Zero where nothing has ever
/// read it — a file that was already missing when it was named.
/// </param>
/// <param name="Opacity">One is solid, nought is drawn not at all.</param>
/// <param name="Filtered">
/// Smoothed between pixels, or not. Off is what pixel art needs and is why this is
/// a switch rather than a quality setting.
/// </param>
/// <param name="LitByScene">
/// Whether the scene's lights fall on it. Off by default: a photograph already
/// carries its own light, and a drawing reads worse under a key light.
/// </param>
public readonly record struct ReferenceImageSettings(
    string Path,
    Vector2 SizeInCells,
    Vector2I PixelSize,
    float Opacity,
    bool Filtered,
    bool LitByScene)
{
    /// <summary>
    /// A plane nobody has configured: square, opaque, unfiltered and unlit. The
    /// import replaces the size the moment it knows the picture's resolution.
    /// </summary>
    public static ReferenceImageSettings Default => new(
        Path: string.Empty,
        SizeInCells: new Vector2(
            ReferenceLimits.DefaultSizeInCells,
            ReferenceLimits.DefaultSizeInCells),
        PixelSize: Vector2I.Zero,
        Opacity: 1f,
        Filtered: false,
        LitByScene: false);

    /// <summary>
    /// How many of the picture's own pixels fall on one cell, or nought where the
    /// resolution is not known. Derived rather than stored, so the two can never
    /// contradict each other.
    /// </summary>
    public Vector2 PixelsPerCell => new(
        SizeInCells.X > 0f ? PixelSize.X / SizeInCells.X : 0f,
        SizeInCells.Y > 0f ? PixelSize.Y / SizeInCells.Y : 0f);

    /// <summary>
    /// The size that puts one pixel of the picture on one voxel, or the size
    /// unchanged where the resolution is not known — there being nothing to
    /// compute it from and a guess being worse than leaving it alone.
    /// </summary>
    public Vector2 OneToOneSize => PixelSize.X > 0 && PixelSize.Y > 0
        ? new Vector2(PixelSize.X, PixelSize.Y)
        : SizeInCells;

    /// <summary>
    /// The same settings with every value inside its bounds. Called on the way in
    /// from anywhere a person, an agent or a stored file could have named one.
    /// </summary>
    public ReferenceImageSettings Clamped() => this with
    {
        Path = Path ?? string.Empty,
        SizeInCells = new Vector2(
            Math.Clamp(
                SizeInCells.X,
                ReferenceLimits.MinimumSizeInCells,
                ReferenceLimits.MaximumSizeInCells),
            Math.Clamp(
                SizeInCells.Y,
                ReferenceLimits.MinimumSizeInCells,
                ReferenceLimits.MaximumSizeInCells)),
        PixelSize = new Vector2I(Math.Max(0, PixelSize.X), Math.Max(0, PixelSize.Y)),
        Opacity = Math.Clamp(Opacity, ReferenceLimits.MinimumOpacity, ReferenceLimits.MaximumOpacity)
    };
}
