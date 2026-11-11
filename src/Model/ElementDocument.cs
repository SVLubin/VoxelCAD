using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// Serialized form of one hierarchy element, nested exactly as the tree is.
/// </summary>
/// <remarks>
/// Field modes travel next to their values because a value alone cannot be
/// restored faithfully: writing it under the wrong mode would re-quantise it.
/// </remarks>
internal sealed class ElementDocument
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    /// <summary>
    /// The prefab a <c>PrefabInstance</c> element stands for; absent for everything
    /// else. It is the whole of what an instance is on disk: its contents are
    /// derived from that prefab and are expanded after the file is read, so
    /// <see cref="Children"/> is empty for one however much it shows on screen.
    /// </summary>
    public string? Source { get; set; }

    public float[] Position { get; set; } = { 0f, 0f, 0f };

    public float[] Rotation { get; set; } = { 0f, 0f, 0f };

    public float[] Scale { get; set; } = { 1f, 1f, 1f };

    public string PositionMode { get; set; } = string.Empty;

    public string RotationMode { get; set; } = string.Empty;

    public string ScaleMode { get; set; } = string.Empty;

    /// <summary>
    /// Written only for an element that is hidden, and read as "shown" when
    /// absent, which is what keeps files from before hiding existed reading back
    /// unchanged. Stored as
    /// the exception rather than as the state, so the common element carries no
    /// entry at all.
    /// </summary>
    public bool? Hidden { get; set; }

    /// <summary>Base64 payload from <see cref="VoxelBlobCodec"/>; absent unless the element holds voxels.</summary>
    public string? Voxels { get; set; }

    /// <summary>
    /// Doubled mirror-plane coordinates, [x, y, z], beside <see cref="SymmetryEnabled"/>.
    /// Both are absent while the slots have never left their default, so files
    /// from before symmetry existed read back unchanged.
    /// </summary>
    public int[]? SymmetryPlanes { get; set; }

    /// <summary>Which mirror-plane slots are on, [x, y, z].</summary>
    public bool[]? SymmetryEnabled { get; set; }

    /// <summary>
    /// Doubled pivot coordinates, [x, y, z] — the half-cell lattice stored the way
    /// the mirror planes are. Absent while the pivot has never been set, so files
    /// from before pivots existed read back unchanged.
    /// </summary>
    public int[]? PivotDoubled { get; set; }

    /// <summary>
    /// The lens a <c>Camera</c> element sees through; absent for everything else,
    /// and absent for a camera still at every default. Where it stands is the
    /// transform above, like any other element.
    /// </summary>
    public CameraDocument? Camera { get; set; }

    /// <summary>
    /// The file a <c>ReferenceImage</c> or <c>ReferenceModel</c> element stands
    /// for, and how it is drawn; absent for everything else. One section for both
    /// kinds — the component name above already says which, and two nearly
    /// identical sections would be an invitation for them to drift.
    /// </summary>
    public ReferenceDocument? Reference { get; set; }

    public List<ElementDocument> Children { get; set; } = new();
}
