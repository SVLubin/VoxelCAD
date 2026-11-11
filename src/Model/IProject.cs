using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Root of the document. Owns the material registry, the scenes, and one undo
/// history covering all of it.
/// </summary>
/// <remarks>
/// The history is deliberately project-wide rather than per scene: undo should
/// reverse the last thing the user did, whatever it was, without them having to
/// work out which stack the current panel belongs to.
/// </remarks>
public interface IProject
{
    event Action<ProjectChange> Changed;

    IHistory History { get; }

    IReadOnlyList<IMaterial> Materials { get; }

    /// <summary>
    /// The materials the editor ships with. Present in every project, saved in
    /// none, and resolved by <see cref="TryGetMaterial"/> like any other — a cell
    /// that wears one goes on wearing it in whatever document it is opened in,
    /// because the id is derived from the name rather than minted.
    /// </summary>
    IReadOnlyList<IMaterial> StandardMaterials { get; }

    /// <summary>
    /// Both lists, for whoever has to answer "what does this cell wear". Anything
    /// that resolves a material a voxel names reads this; anything that means
    /// "what does this document define" reads <see cref="Materials"/>. The two
    /// questions look alike and are not — the project tree, a <c>.vox</c> palette
    /// and a copy between files all want the second.
    /// </summary>
    IEnumerable<IMaterial> AllMaterials { get; }

    /// <summary>
    /// In project order. Which of them the editor is showing is not recorded
    /// here: that is session state, and the document is the same file whichever
    /// scene happens to be open.
    /// </summary>
    IReadOnlyList<IScene> Scenes { get; }

    /// <summary>
    /// The prefab library, shared across the scenes. In project order, like the
    /// scenes and the materials; which of them is open for editing is editor state
    /// and is not recorded here.
    /// </summary>
    IReadOnlyList<IPrefab> Prefabs { get; }

    bool TryGetScene(Guid id, out IScene scene);

    bool TryGetPrefab(Guid id, out IPrefab prefab);

    /// <summary>
    /// Whether any instance of this prefab stands anywhere — in a scene or inside
    /// another prefab's contents. What <see cref="DeletePrefab"/> would unpack,
    /// asked before it is offered.
    /// </summary>
    bool IsPrefabInUse(Guid id);

    /// <summary>
    /// Whether an instance of <paramref name="prefabId"/> may stand inside
    /// <paramref name="hostSceneId"/>. False only for the one case there is: a
    /// prefab placed inside itself, directly or through another. An ordinary scene
    /// takes anything.
    /// </summary>
    /// <remarks>
    /// Published for the reason <see cref="IsMaterialInUse"/> is: a surface offers
    /// the placement — a drag showing its answer in the air, a button drawn live or
    /// greyed — and has to know before it offers. One rule, so what a drop
    /// indicator says and what the model would refuse cannot disagree.
    /// </remarks>
    bool CanContainPrefab(Guid hostSceneId, Guid prefabId);

    /// <summary>
    /// Removes a prefab and unpacks every instance of it, everywhere, as one
    /// undoable record. Unlike a material in use this is not refused: an unpacked
    /// instance keeps the geometry it was showing, so nothing is lost but the link.
    /// </summary>
    void DeletePrefab(Guid id);

    /// <summary>
    /// Renames a library entry; instances carry their own names and are untouched.
    /// Refused when another entry already has the name — the library is addressed
    /// by name, and two prefabs sharing one would make either unaddressable.
    /// </summary>
    void RenamePrefab(Guid id, string name);

    Guid CreateScene(string name, Vector3I size);

    /// <summary>Refused for the last one — a project always has somewhere to build.</summary>
    void DeleteScene(Guid id);

    /// <summary>
    /// Takes a deep copy of a scene: its buildable region, every element in it, and
    /// the materials their cells wear. Records nothing, and refers to nothing.
    /// </summary>
    SceneClip CaptureScene(Guid id);

    /// <summary>
    /// Adds a clip back as a scene of its own and returns its id. One undoable
    /// record; the scene is not activated, which is the caller's decision.
    /// </summary>
    /// <param name="name">
    /// Null composes one against the project's scene names — see
    /// <see cref="IScene.PasteElement"/> for the rule, which is the same one.
    /// </param>
    Guid PasteScene(SceneClip clip, string? name = null);

    void RenameScene(Guid id, string name);

    bool TryGetMaterial(Guid id, out IMaterial material);

    Guid CreateMaterial(string name, Color color);

    /// <summary>
    /// Whether any voxel of any scene still carries this material — the reason
    /// <see cref="DeleteMaterial"/> would refuse, asked before rather than after.
    /// </summary>
    /// <remarks>
    /// Published so that a surface offering a deletion can grey it out instead of
    /// walking the user into a refusal, and an agent can see the answer before it
    /// asks. It is the same walk the refusal makes, which is the point: two copies
    /// of the rule could disagree, and then the button and the model would too.
    /// Walked on demand — deleting a material is rare, and a usage index that had
    /// to be maintained by every voxel edit would not be.
    /// </remarks>
    bool IsMaterialInUse(Guid id);

    /// <summary>Refused while any voxel still references the material.</summary>
    void DeleteMaterial(Guid id);

    /// <summary>
    /// Gives every cell wearing <paramref name="fromId"/> — in every scene and
    /// inside every prefab's contents — the material <paramref name="toId"/>, as
    /// one project-level record. Afterwards <see cref="IsMaterialInUse"/> answers
    /// false for the old one, which is what makes <see cref="DeleteMaterial"/>
    /// possible; the refusal there names this as the way out.
    /// </summary>
    void ReplaceMaterial(Guid fromId, Guid toId);

    /// <summary>Takes a copy of a material's name, colour and appearance. Records nothing.</summary>
    MaterialClip CaptureMaterial(Guid id);

    /// <summary>
    /// Adds a clip back as a material of its own, wearing a new id, and returns it.
    /// One undoable record. Nothing is repainted: voxels name a material by id, and
    /// this one is not the id they name.
    /// </summary>
    /// <param name="name">
    /// Null composes one against the registry's names — see
    /// <see cref="IScene.PasteElement"/> for the rule, which is the same one.
    /// </param>
    Guid PasteMaterial(MaterialClip clip, string? name = null);

    void RenameMaterial(Guid id, string name);

    void SetMaterialColor(Guid id, Color color);

    /// <summary>
    /// Sets how the material takes light and what facture it wears. Out-of-range
    /// values are clamped rather than refused — these arrive from sliders, and a
    /// control must not be able to raise an exception.
    /// </summary>
    void SetMaterialAppearance(Guid id, MaterialAppearance appearance);

    /// <summary>
    /// Applies a ready-made material: its appearance <b>and</b> its colour, as one
    /// undoable act. The colour is part of the bargain — a preset called Brick that
    /// left a material pale grey would be a preset of nothing — and it is why the
    /// preset a material matches is asked of its appearance alone, so that
    /// recolouring afterwards does not make it Custom.
    /// </summary>
    void SetMaterialPreset(Guid id, MaterialPreset preset);

    /// <summary>
    /// Writes the project to <paramref name="path"/>. The extension picks the
    /// storage mode, so converting between modes is a save under the other one.
    /// Whole or not at all: the file is replaced by a rename, never rewritten in
    /// place, so an interrupted save leaves the old file rather than half of one.
    /// </summary>
    void Save(string path);

    /// <summary>
    /// The project as it stands, detached from the model and writable from any
    /// thread — see <see cref="IProjectSnapshot"/>. <see cref="Save"/> is a capture
    /// written at once; a recovery snapshot is a capture written elsewhere.
    /// </summary>
    IProjectSnapshot Capture();
}
