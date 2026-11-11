namespace VoxelCAD.Model;

/// <summary>
/// Something lifted out of the document to be put back somewhere else: an element
/// and its subtree, a scene, or a material. Deep and self-contained — nothing in a
/// clip points back at what it was taken from, so the original may be renamed,
/// edited, or deleted without the copy noticing.
/// </summary>
/// <remarks>
/// Opaque on purpose. What a clip holds is the model's business; a caller carries
/// one from a copy to a paste and never looks inside, which is what lets the editor
/// keep one in session state without learning what a voxel blob is. The three
/// shapes are told apart by type, and the internal constructor is what keeps them
/// the model's to make.
/// </remarks>
public abstract class ClipContent
{
    internal ClipContent(string name)
    {
        Name = name;
    }

    /// <summary>
    /// What it was called where it was taken from. Published because a surface
    /// offering a paste may want to say what it would paste; where the copy
    /// actually lands, the name is composed against what is already there.
    /// </summary>
    public string Name { get; }
}
