namespace VoxelCAD.Model;

/// <summary>
/// Serialized form of a camera's lens. Every field is nullable and written only
/// where it differs from the default — a file should say what was chosen and stay
/// quiet about what was not, the same bargain a material's appearance strikes.
/// </summary>
internal sealed class CameraDocument
{
    public string? Projection { get; set; }

    public float? Fov { get; set; }

    public float? OrthographicHeight { get; set; }

    public float? Aperture { get; set; }

    public float? FocusDistance { get; set; }
}
