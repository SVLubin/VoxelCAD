using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// A <c>.vox</c> file as its chunks say it, before any of it has been read as
/// geometry: the models, the palette, the material dictionaries and the scene
/// graph, each still in MagicaVoxel's own terms.
/// </summary>
/// <remarks>
/// Parsing and interpreting are separated on purpose. The byte-level walk has one
/// job — refuse a damaged file — and everything that needs judgement (which frame
/// of an animation, what a material property means, which palette entries anybody
/// wears) reads this instead of the bytes.
/// </remarks>
internal sealed class VoxDocument
{
    public VoxDocument(int version)
    {
        Version = version;
    }

    public int Version { get; }

    /// <summary>In file order, which is the order shape nodes index them by.</summary>
    public List<VoxModel> Models { get; } = new();

    /// <summary>
    /// Filled cells across every model, counted as they are read so that a file
    /// asking for more than a machine holds is refused while reading rather than
    /// after.
    /// </summary>
    public long Cells { get; set; }

    /// <summary>
    /// The 256 four-byte entries of the <c>RGBA</c> chunk, or null where the file
    /// carries none and the default palette stands in.
    /// </summary>
    public byte[]? Palette { get; set; }

    /// <summary>
    /// How many of those bytes the chunk actually carried. Past it the format's
    /// own default palette stands in, because an entry the file did not write is
    /// absent rather than transparent black.
    /// </summary>
    public int PaletteBytes { get; set; }

    /// <summary>
    /// Material properties by the colour index they describe. MagicaVoxel writes
    /// one for every entry whether or not anybody touched it, so presence here
    /// means nothing on its own.
    /// </summary>
    public Dictionary<int, IReadOnlyDictionary<string, string>> Materials { get; } = new();

    /// <summary>The scene graph by node id, or empty where the file carries none.</summary>
    public Dictionary<int, VoxNode> Nodes { get; } = new();

    /// <summary>One <c>SIZE</c> and the <c>XYZI</c> that follows it.</summary>
    internal sealed class VoxModel
    {
        public VoxModel(Vector3I size)
        {
            Size = size;
        }

        public Vector3I Size { get; }

        /// <summary>Cell and colour index, exactly as the file lists them.</summary>
        public List<(Vector3I Cell, byte ColorIndex)> Cells { get; } = new();
    }

    /// <summary>
    /// A node of the scene graph. One class rather than three, because a walk over
    /// the graph switches on the kind anyway and three types would only move the
    /// switch.
    /// </summary>
    internal sealed class VoxNode
    {
        public VoxNode(VoxNodeKind kind, string? name)
        {
            Kind = kind;
            Name = name;
        }

        public VoxNodeKind Kind { get; }

        /// <summary>The node's <c>_name</c>, or null where it has none.</summary>
        public string? Name { get; }

        /// <summary>Transform only: the single node below it.</summary>
        public int Child { get; set; }

        /// <summary>Transform only: the rotation of its first frame.</summary>
        public Basis Rotation { get; set; } = Basis.Identity;

        /// <summary>Transform only: the translation of its first frame, in cells.</summary>
        public Vector3I Translation { get; set; }

        /// <summary>Transform only: how many frames it carried, one of which was read.</summary>
        public int Frames { get; set; }

        /// <summary>Group only: the nodes below it, in order.</summary>
        public List<int> Children { get; } = new();

        /// <summary>Shape only: the models it stands for, one per animation frame.</summary>
        public List<int> Models { get; } = new();
    }
}
