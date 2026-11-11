namespace VoxelCAD.Model;

/// <summary>
/// One material of the registry — its name, its colour and its appearance —
/// taken out of a project. Pasted back through <see cref="IProject.PasteMaterial"/>
/// as a material in its own right, wearing a new id.
/// </summary>
/// <remarks>
/// Nothing is repainted by pasting one: voxels reference a material by id, and the
/// copy has an id of its own. That is the whole point of a copy being independent
/// of its original.
/// </remarks>
public sealed class MaterialClip : ClipContent
{
    internal MaterialClip(MaterialSnapshot state)
        : base(state.Name)
    {
        State = state;
    }

    internal MaterialSnapshot State { get; }
}
