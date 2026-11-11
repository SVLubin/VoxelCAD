using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// One element of a clip, nested exactly as the tree is: everything an element is,
/// with its identity left behind.
/// </summary>
/// <remarks>
/// The transform is the <b>stored</b> form — the local origin — not the public
/// pivot form, for the reason <see cref="TransformSnapshot"/> holds the same one:
/// a copy has to be replayed exactly rather than round-tripped through the pivot
/// arithmetic. Field modes travel beside their values because a value alone cannot
/// be restored faithfully.
/// <para>
/// Material ids travel as they stood in the source document. Whether they still
/// name anything is settled at paste time — see
/// <see cref="Project.ResolveClipMaterials"/> — which is why a clip carries
/// <see cref="ClipMaterial"/> entries beside the cells. <see cref="SourceId"/> is
/// the same arrangement for a prefab instance, and it is the one identity a clip
/// does carry: an instance <em>is</em> a reference, so leaving it behind would
/// leave nothing at all. Its children are not captured — they are derived from
/// that source, and a paste expands them again.
/// </para>
/// <para>
/// <see cref="Lens"/> is a camera's, <see cref="ImageSettings"/> and
/// <see cref="ModelSettings"/> are a reference's, and each is the default for
/// everything else — a clip carries every element's whole state rather than a
/// state per kind, which is what keeps a paste from having to ask what it is
/// putting back. A reference's path travels as it is, absolute, which is the whole
/// of what makes copying one into another document work.
/// </para>
/// </remarks>
internal sealed record ElementNode(
    string Name,
    ComponentKind ComponentKind,
    Guid? SourceId,
    TransformValues Values,
    TransformModes Modes,
    Vector3I? PivotDoubled,
    bool Visible,
    SymmetryValues Symmetry,
    CameraLens Lens,
    ReferenceImageSettings ImageSettings,
    ReferenceModelSettings ModelSettings,
    IReadOnlyDictionary<Vector3I, Guid> Voxels,
    IReadOnlyList<ElementNode> Children);
