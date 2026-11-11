using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// A scene: the hierarchy root, and the only way to change anything in it.
/// Elements are addressed by <see cref="Guid"/> rather than by reference, because
/// references do not survive undo or reload.
/// </summary>
/// <remarks>
/// The scene owns integrity — existence checks, cycle prevention, field-mode
/// rules, prefab read-only enforcement — because it is the only object with a
/// view of the whole tree.
/// </remarks>
public interface IScene : IHierarchyContainer
{
    event Action<SceneChange> Changed;

    /// <summary>
    /// What the project tree calls it. Renaming goes through the project, which
    /// owns the list of scenes the way it owns the material registry.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Size of the buildable region in cells: the room being worked in, the walls
    /// the viewport picks against, and what framing frames.
    /// </summary>
    /// <remarks>
    /// It bounds <b>nothing</b> in the document. Voxels are placed where they are
    /// asked for, inside the region or not — a model may legitimately hang out of
    /// the box, and what hangs out has to be extendable and erasable like the rest
    /// of it — and an element may stand wherever it is put. The room and the ruler,
    /// not a fence.
    /// </remarks>
    Vector3I Size { get; }

    /// <summary>
    /// The camera this scene's picture is taken through, or <see cref="Guid.Empty"/>
    /// where it holds none. Only one camera renders at a time, which is the whole
    /// of what "active" means here.
    /// </summary>
    /// <remarks>
    /// Answered rather than stored: what <see cref="SetActiveCamera"/> records is
    /// somebody's <em>choice</em>, and the scene falls back to the first camera it
    /// holds whenever that choice names nothing — the same shape as the editor
    /// falling back to another scene when the active one is deleted. So the first
    /// camera created is active with nobody having chosen it, deleting the chosen
    /// one hands the picture to another rather than leaving the scene unrenderable,
    /// and undoing that delete hands it straight back.
    /// </remarks>
    Guid ActiveCameraId { get; }

    bool TryGetElement(Guid id, out IHierarchyElement element);

    /// <summary>
    /// Creates an element as the last child of <paramref name="parentId"/>.
    /// <see cref="ComponentKind.None"/> gives a grouping element.
    /// </summary>
    Guid CreateElement(Guid parentId, ComponentKind kind, string name);

    /// <summary>
    /// Creates a camera already standing where it is meant to stand, as one
    /// undoable record, and returns its id. The values are in the parent's frame,
    /// like any element's transform.
    /// </summary>
    /// <remarks>
    /// Its own method rather than a create followed by three edits, because a
    /// camera is placed to keep a shot: over four records a shot is four things to
    /// undo, and each of those edits would be quantised on the way in — so the
    /// framing would have to survive a trip through the field modes before they
    /// were set. A camera's position and rotation start
    /// <see cref="FieldMode.Float"/> for that reason.
    /// </remarks>
    Guid CreateCamera(
        Guid parentId,
        string name,
        Vector3 position,
        Vector3 rotationDegrees,
        CameraLens lens);

    /// <summary>
    /// Creates a reference already pointing at its file, as one undoable record,
    /// and returns its id. The kind follows the extension
    /// (<see cref="ReferenceFormats.TryGetKind"/>) and is refused where nothing in
    /// this editor could draw the file.
    /// </summary>
    /// <remarks>
    /// Its own method for the reason <see cref="CreateCamera"/> is: an import is
    /// one thing the user did, and a create followed by a settings edit would be
    /// two things to undo — the second of which would leave a reference pointing at
    /// nothing standing in the scene in between. A picture is measured here, so a
    /// plane arrives at one pixel to one voxel rather than at a size nobody chose.
    /// </remarks>
    Guid CreateReference(Guid parentId, string name, string path);

    /// <summary>
    /// Applies cell edits to an element's voxel model as one undoable action.
    /// Edits that change nothing are dropped; a list of only those records nothing.
    /// </summary>
    void SetVoxels(Guid elementId, IReadOnlyList<VoxelEdit> edits);

    /// <summary>
    /// Gives every cell wearing <paramref name="fromId"/> the material
    /// <paramref name="toId"/> — in one element, or across every voxel model of
    /// this scene when <paramref name="elementId"/> is null — as <b>one</b>
    /// undoable record however many elements it reached. Nothing wearing it
    /// records nothing. Prefab contents shown in this scene are read-only and
    /// are passed over; the prefab's own scene is where they are replaced.
    /// </summary>
    void ReplaceMaterial(Guid fromId, Guid toId, Guid? elementId);

    /// <summary>Removes the element and its whole subtree.</summary>
    void DeleteElement(Guid id);

    /// <summary>
    /// Takes a deep copy of the element and everything under it. Records nothing —
    /// copying is not editing — and the clip refers to nothing, so the original may
    /// be edited or deleted afterwards without touching it.
    /// </summary>
    /// <remarks>
    /// The materials the subtree's cells wear travel with it, so that a paste can
    /// put back one the document has lost in the meantime. A prefab instance is
    /// captured as a reference — its source id travels, its derived children do
    /// not — and the prefabs it needs travel with the clip, closure included.
    /// </remarks>
    ElementClip CaptureElement(Guid id);

    /// <summary>
    /// Puts a clip back as a new subtree under <paramref name="parentId"/>, at the
    /// end of its children, and returns the new root's id. One undoable record,
    /// whatever the subtree holds.
    /// </summary>
    /// <param name="name">
    /// What the pasted root is called. Null composes one against the names already
    /// in this scene: the clip's own where nothing is using it — so a cut and a
    /// paste put a name back unchanged — and the next free number where it is
    /// taken. Only the root is renamed; what is under it keeps its own names.
    /// </param>
    /// <remarks>
    /// The clip may come from another scene or another document. Any material its
    /// cells wear that this project no longer has is re-created as part of the same
    /// record, rather than the cells being written wearing an id that names nothing.
    /// </remarks>
    Guid PasteElement(Guid parentId, ElementClip clip, string? name = null);

    /// <summary>
    /// Lifts the element into the project's prefab library and leaves an instance
    /// of it in its place, as one undoable record. Returns the new prefab's id.
    /// </summary>
    /// <param name="name">
    /// What the library entry is called; null takes the element's own name. Composed
    /// against the names already in the library, so two prefabs never share one.
    /// </param>
    /// <remarks>
    /// The instance takes the element's transform, field modes and pivot, and the
    /// contents go to the library at identity — so nothing appears to move. An
    /// element with no pivot of its own is given the middle of what it holds, which
    /// is where its instance's handles will sit.
    /// </remarks>
    Guid CreatePrefab(Guid elementId, string? name = null);

    /// <summary>
    /// Adds an instance of a prefab as the last child of
    /// <paramref name="parentId"/>, and returns its id. Its contents are derived
    /// from the source and read-only; the element itself is ordinary.
    /// </summary>
    /// <remarks>
    /// Refused when it would put a prefab inside itself, which is only reachable
    /// while editing one — an ordinary scene can hold any instance.
    /// </remarks>
    Guid CreateInstance(Guid parentId, Guid prefabId, string? name = null);

    /// <summary>
    /// Cuts an instance's link to its source: the element stays where it is as a
    /// grouping element, and its contents become ordinary elements with fresh ids.
    /// One level — a nested instance inside them stays an instance.
    /// </summary>
    void UnpackInstance(Guid elementId);

    /// <summary>
    /// Moves the element under a new parent, preserving its world transform. If a
    /// channel's field mode cannot represent the resulting local value, that mode
    /// degrades to <see cref="FieldMode.Float"/> — the geometry never moves.
    /// </summary>
    /// <param name="index">
    /// Where it lands among the new parent's children, counted <b>after</b> it has
    /// left wherever it was — so moving an element down inside its own parent asks
    /// for one less than the row it is dropped below. Out of range is clamped, not
    /// refused.
    /// </param>
    void ReparentElement(Guid id, Guid newParentId, int index);

    void RenameElement(Guid id, string name);

    /// <summary>
    /// Hides or shows an element as one undoable action; a value equal to the
    /// current one records nothing. Nothing else changes — the cells, the
    /// transform and the place in the tree are untouched — and what is nested
    /// under a hidden element is hidden with it without being written to.
    /// </summary>
    void SetVisible(Guid id, bool visible);

    void SetTransform(Guid id, TransformChannel channel, Vector3 value);

    /// <summary>Throws when the mode is not valid for the channel (see <see cref="FieldModeHelper"/>).</summary>
    void SetFieldMode(Guid id, TransformChannel channel, FieldMode mode);

    /// <summary>
    /// Sets a voxel model's mirror-plane slots as one undoable action. The cells
    /// are untouched — symmetry only modifies future edits — and a value equal
    /// to the current one records nothing.
    /// </summary>
    void SetSymmetry(Guid elementId, SymmetryValues values);

    /// <summary>
    /// Moves the element's pivot to <paramref name="point"/>, in its own cells, as
    /// one undoable action. The geometry does not move — the local origin is held —
    /// but the position channel names the pivot, so the reported position changes
    /// with it. Rounded to the half-cell lattice on the way in; a value equal to
    /// the current one records nothing.
    /// </summary>
    void SetPivot(Guid elementId, Vector3 point);

    /// <summary>
    /// Sets a camera's whole lens as one undoable action. Nothing in the scene
    /// moves — where a camera stands is its element's transform — and a lens equal
    /// to the current one records nothing. Values are clamped on the way in rather
    /// than refused.
    /// </summary>
    void SetCameraLens(Guid elementId, CameraLens lens);

    /// <summary>
    /// Chooses which camera this scene renders through, as one undoable action. A
    /// choice equal to the current one records nothing.
    /// </summary>
    /// <remarks>
    /// A camera inside a prefab instance may be chosen even though nothing about
    /// it may be edited: choosing is a fact about the scene rather than a change to
    /// the element, and refusing would leave a scene whose only camera is one it
    /// cannot use.
    /// </remarks>
    void SetActiveCamera(Guid elementId);

    /// <summary>
    /// Sets a reference plane's whole settings as one undoable action — which file
    /// it shows, how big it is in cells and how it is drawn. Nothing in the scene
    /// moves, and settings equal to the current ones record nothing. Values are
    /// clamped on the way in rather than refused.
    /// </summary>
    /// <remarks>
    /// A path that differs from the current one has its picture measured here, so
    /// the resolution the document carries can never contradict the file it names.
    /// The <em>size</em> is left exactly as the caller asked: how big the plane is
    /// is somebody's decision, and only a fresh reference has no decision to
    /// respect — which is why <see cref="CreateReference"/> is the one that sizes
    /// one pixel to one voxel. A file that is missing or unreadable leaves the last
    /// known resolution standing: a stale number a reader can compare against a
    /// missing-file mark is worth more than a nought that says nothing.
    /// </remarks>
    void SetReferenceImage(Guid elementId, ReferenceImageSettings settings);

    /// <summary>
    /// Sets a reference model's whole settings as one undoable action — which file
    /// it shows and how it is drawn. How big it is and where it stands is the
    /// element's own transform.
    /// </summary>
    void SetReferenceModel(Guid elementId, ReferenceModelSettings settings);
}
