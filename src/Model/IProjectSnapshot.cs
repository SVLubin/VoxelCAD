namespace VoxelCAD.Model;

/// <summary>
/// A project as it stood at one moment, detached from the live model and ready
/// to be written from any thread.
/// </summary>
/// <remarks>
/// Taking one is cheap — the tree is walked and the cells are copied, nothing is
/// encoded — and writing one is where the cost is: the voxel blobs are compressed
/// and the YAML is serialized on whatever thread calls <see cref="Write"/>. That
/// split is what lets a recovery snapshot be written without stalling the frame
/// that asked for it, and it is safe because nothing in here refers back to the
/// model: an edit made after the capture is not in the file.
/// <para>
/// One snapshot is written once. The write rewrites the reference paths it holds
/// relative to the directory it lands in, so a second write elsewhere would carry
/// the first one's paths; capture again rather than reuse.
/// </para>
/// </remarks>
public interface IProjectSnapshot
{
    /// <summary>
    /// Writes the captured project to <paramref name="path"/>, whole or not at
    /// all. Throws on a second call.
    /// </summary>
    void Write(string path);
}
