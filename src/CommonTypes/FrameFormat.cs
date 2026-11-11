namespace VoxelCAD.CommonTypes;

/// <summary>
/// How a captured frame is encoded. PNG is lossless and large; JPEG and WEBP
/// trade fidelity for a fraction of the bytes, which is what an agent that looks
/// often wants.
/// </summary>
public enum FrameFormat
{
    Png,
    Jpeg,
    Webp
}
