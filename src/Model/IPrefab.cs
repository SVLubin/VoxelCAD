using System;

namespace VoxelCAD.Model;

/// <summary>
/// One entry of the project's prefab library: a named subtree that instances of it
/// derive their contents from.
/// </summary>
/// <remarks>
/// A prefab is edited the way a scene is, because <see cref="Content"/> <b>is</b> a
/// scene — its own buildable region, its own element tree, the same mutators and
/// the same history. That is what makes "open the prefab" the move the editor
/// already knows how to make rather than a second editing mode, and why the
/// content scene carries the prefab's own <see cref="Id"/>: a history record
/// stamped with the scene it happened in then names the prefab without a second
/// field.
/// <para>
/// It is not in <see cref="IProject.Scenes"/>: a prefab is a library entry, and
/// what a scene row offers — activation, deletion, a place for the user to build —
/// is a different set of promises.
/// </para>
/// </remarks>
public interface IPrefab
{
    Guid Id { get; }

    string Name { get; }

    /// <summary>
    /// The subtree, as a scene of its own. Its top-level elements are what an
    /// instance expands into, under the instance's own element.
    /// </summary>
    IScene Content { get; }
}
