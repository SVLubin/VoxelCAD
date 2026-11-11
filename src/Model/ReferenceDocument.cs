namespace VoxelCAD.Model;

/// <summary>
/// Serialized form of a reference, image or model — one document for both, since
/// the element's own component name already says which it is and a file with two
/// nearly identical sections would invite them to drift.
/// </summary>
/// <remarks>
/// Every field but the path is nullable and written only where it differs from
/// the default, the bargain a camera's lens and a material's appearance both
/// strike: a file should say what was chosen and stay quiet about what was not.
/// </remarks>
internal sealed class ReferenceDocument
{
    /// <summary>
    /// Relative to the project's own directory wherever the asset sits under it,
    /// absolute otherwise, and written with forward slashes either way — see
    /// <see cref="ReferencePaths"/>.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Width and height in cells; a plane's only geometry.</summary>
    public float[]? SizeCells { get; set; }

    /// <summary>
    /// The picture's own resolution as it was last read, so the pixels-per-cell
    /// readout survives the asset going missing and a headless run answers what the
    /// editor answers.
    /// </summary>
    public int[]? PixelSize { get; set; }

    public float? Opacity { get; set; }

    public bool? Filtered { get; set; }

    public bool? Lit { get; set; }

    public bool? Wireframe { get; set; }
}
