using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// A transform vector together with the rule constraining it. Each mode has its
/// own implementation and its own backing storage, so a value that violates the
/// mode is unrepresentable rather than merely rejected.
/// </summary>
/// <remarks>
/// Quantisation is applied on write and again on read: the read side covers
/// paths that build elements without going through a setter (deserialization,
/// prefab expansion, undo restore, duplication). Raw values never leave the
/// model — callers only ever see <see cref="AsFloat"/> or <see cref="AsInt"/>.
/// </remarks>
internal interface IFieldVector3
{
    FieldMode Mode { get; }

    Vector3 AsFloat { get; }

    /// <summary>
    /// Rounded in <see cref="FieldMode.Float"/> mode, exact otherwise. Asking a
    /// float-mode vector for integers is lossy but allowed.
    /// </summary>
    Vector3I AsInt { get; }

    void Set(Vector3 value);

    void Set(Vector3I value);
}
