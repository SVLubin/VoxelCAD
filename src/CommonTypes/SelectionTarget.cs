using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One addressable row of the project tree: what sort of thing it is, and which
/// one. <see cref="Id"/> is <see cref="Guid.Empty"/> for the kinds that name a
/// place in the tree rather than an object in the document.
/// </summary>
/// <remarks>
/// Identity in the document is still a <see cref="Guid"/>. This pairs it with a
/// kind so that a single field can hold "the third material" and "that element"
/// without anyone having to ask every registry in turn whose id it is.
/// </remarks>
public readonly record struct SelectionTarget(SelectionKind Kind, Guid Id);
