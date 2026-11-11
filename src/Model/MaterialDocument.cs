namespace VoxelCAD.Model;

/// <summary>Serialized form of one material.</summary>
internal sealed class MaterialDocument
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Red, green, blue, alpha as floats — alpha is the opacity. Not hex:
    /// eight bits per channel would quantise the colour on every save, and
    /// writing a file must not alter what it stores.
    /// </summary>
    public float[] Color { get; set; } = { 1f, 1f, 1f, 1f };

    /// <summary>
    /// How the surface takes light. Null for a material still at the defaults,
    /// which is what every material written before this existed was.
    /// </summary>
    public MaterialAppearanceDocument? Appearance { get; set; }
}
