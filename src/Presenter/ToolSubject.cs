namespace VoxelCAD.Presenter;

/// <summary>
/// What the selection offers a tool to take hold of. Not the selection itself —
/// the tools do not care whether a material or a scene row is what is selected,
/// only that neither of them is something to act on.
/// </summary>
internal enum ToolSubject
{
    /// <summary>
    /// Nothing a tool can act on: no selection, a scene, a material, or an element
    /// the document no longer has.
    /// </summary>
    Nothing,

    /// <summary>
    /// An element, so there is a transform to take hold of. Nothing currently
    /// tells this apart from <see cref="VoxelModel"/> — the tools that want a
    /// transform select as well, so they are a yes to anything — and it is kept
    /// because it says what the selection <em>is</em> rather than what some tool
    /// makes of it.
    /// </summary>
    Element,

    /// <summary>An element with cells in it, so there is a surface to aim at too.</summary>
    VoxelModel
}
