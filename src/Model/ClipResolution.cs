using System;
using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// What the ids inside a clip mean in the document it is being pasted into: which
/// material each of its material ids names here, and which prefab each of its
/// source ids names here.
/// </summary>
/// <remarks>
/// One object rather than two dictionaries threaded through every call, and an
/// <see cref="Identity"/> for the common case — a copy that never left the
/// document, where every id still names itself.
/// </remarks>
internal sealed class ClipResolution
{
    private static readonly IReadOnlyDictionary<Guid, Guid> None = new Dictionary<Guid, Guid>();

    /// <summary>Everything still names itself: a copy inside one document.</summary>
    public static readonly ClipResolution Identity = new(None, None);

    private readonly IReadOnlyDictionary<Guid, Guid> _materialsByIdDictionary;
    private readonly IReadOnlyDictionary<Guid, Guid> _prefabsByIdDictionary;

    public ClipResolution(
        IReadOnlyDictionary<Guid, Guid> materialsByIdDictionary,
        IReadOnlyDictionary<Guid, Guid> prefabsByIdDictionary)
    {
        _materialsByIdDictionary = materialsByIdDictionary;
        _prefabsByIdDictionary = prefabsByIdDictionary;
    }

    public Guid Material(Guid id) => Map(_materialsByIdDictionary, id);

    public Guid Prefab(Guid id) => Map(_prefabsByIdDictionary, id);

    /// <summary>
    /// An id with no entry can only come from a document that was already carrying
    /// a dangling one, and passing it through preserves that rather than making the
    /// copy the thing that throws.
    /// </summary>
    private static Guid Map(IReadOnlyDictionary<Guid, Guid> mapByIdDictionary, Guid id) =>
        mapByIdDictionary.TryGetValue(id, out Guid mapped) ? mapped : id;
}
