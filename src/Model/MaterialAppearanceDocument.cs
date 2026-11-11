namespace VoxelCAD.Model;

/// <summary>
/// Serialized form of a material's appearance. Every field is nullable so a
/// material that never left its defaults writes nothing at all, and a file from
/// before appearance existed reads back as exactly what it was.
/// </summary>
internal sealed class MaterialAppearanceDocument
{
    /// <summary>
    /// Whether the material is see-through at all. Absent in a file written before
    /// the flag existed, and the reader then takes the answer from the alpha the
    /// colour carries — which is exactly what that alpha used to mean.
    /// </summary>
    public bool? Transparent { get; set; }

    public float? Roughness { get; set; }

    public float? Metallic { get; set; }

    public float? Emission { get; set; }

    /// <summary>
    /// The finish by <b>name</b>. Not its number: a finish added between releases
    /// would renumber the ones already written, and a name that this build does
    /// not know reads back as no facture rather than throwing.
    /// </summary>
    public string? Finish { get; set; }

    public float? FinishScale { get; set; }

    public float? FinishRelief { get; set; }
}
