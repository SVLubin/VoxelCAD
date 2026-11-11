namespace VoxelCAD.CommonTypes;

/// <summary>
/// A row dragged onto a place in the project tree, reported rather than acted on:
/// the panel says what was dropped where, and a presenter decides whether that
/// means anything to the document.
/// </summary>
/// <param name="Target">The row being dragged.</param>
/// <param name="Parent">The row it is being dropped into.</param>
/// <param name="Index">
/// Where among that row's children it lands, counted in the tree <b>as displayed
/// at the moment of the drop</b> — before anything has moved. Turning that into
/// the model's index is the presenter's job, and it is not the identity: an
/// element dragged further down inside its own parent leaves a gap behind it.
/// </param>
public readonly record struct HierarchyDrop(
    SelectionTarget Target,
    SelectionTarget Parent,
    int Index);
