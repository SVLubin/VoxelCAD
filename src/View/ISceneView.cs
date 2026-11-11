using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The 3D viewport. It mirrors the element tree as engine nodes so the engine
/// composes world transforms — the presenter only ever syncs local ones.
/// </summary>
public interface ISceneView
{
    /// <summary>
    /// A click landed in the scene. Where it landed, not what it means. Under the
    /// brush it is also every step of a stroke, one event per cell the hover
    /// proposed while the button was held, between <see cref="StrokeBegan"/> and
    /// <see cref="StrokeEnded"/>.
    /// </summary>
    event Action<ScenePick> Picked;

    /// <summary>
    /// The brush was pressed. What follows until <see cref="StrokeEnded"/> is one
    /// gesture — a <see cref="Picked"/> per cell, the first of them arriving
    /// straight after this — and a layer up it is one history record.
    /// </summary>
    event Action StrokeBegan;

    /// <summary>
    /// The stroke closed: the button came up, or Escape, or the tool or the target
    /// changed under it, or control changed hands. Every one of them commits what
    /// landed — the cells already went through the model, and a batch cannot be
    /// discarded — so there is no cancelled form of this.
    /// </summary>
    event Action StrokeEnded;

    /// <summary>
    /// Closes the stroke in flight, if any, on what it landed — the same closing
    /// a release does, asked from a layer up when the gesture's record has been
    /// filed under it (an undo reaching the shell through the platform's own menu
    /// bar, say). Raises <see cref="StrokeEnded"/> like every other closing.
    /// </summary>
    void EndStroke();

    /// <summary>
    /// A cursor click landed in the scene: which elements the ray crossed, nearest
    /// first, and where the click was. Which of them the click meant — and what to
    /// do about a click that crossed none — is decided a layer up.
    /// </summary>
    event Action<ElementPick> ElementPicked;

    /// <summary>
    /// The keyboard left the viewport — for a panel, or for another application.
    /// The click that brings it back is a first click, never the repeat of one made
    /// before the hand went elsewhere.
    /// </summary>
    event Action KeyboardLost;

    /// <summary>
    /// The focus key was pressed. Reported rather than acted on, because what is
    /// worth framing is the selection, and the viewport does not know what that
    /// is — the answer comes back as <see cref="FocusOn"/>.
    /// </summary>
    event Action FocusRequested;

    /// <summary>
    /// A tool key was pressed. Reported the same way framing is: which tool is
    /// active is session state, and the viewport is the last place that should
    /// keep its own copy of it — the answer comes back as <see cref="SetTool"/>.
    /// </summary>
    event Action<ToolKind> ToolRequested;

    /// <summary>A transform handle was grabbed. What follows is one gesture.</summary>
    event Action GizmoDragBegan;

    /// <summary>
    /// How far the grabbed handle has been pulled since the press, in the units of
    /// the channel the active tool edits. Cumulative from the start of the drag, so
    /// the value to apply is <c>start + delta</c> — never the current value plus
    /// this frame's step, which drifts once the model quantises.
    /// </summary>
    event Action<Vector3> GizmoDragged;

    event Action GizmoDragEnded;

    /// <summary>
    /// The drag was abandoned — Escape, or the tool changing under it. The viewport
    /// says only that it happened; what should be left behind is decided a layer up.
    /// </summary>
    event Action GizmoDragCancelled;

    /// <summary>
    /// Escape, offered to whatever gesture is held in the scene — a stroke, an
    /// extrude pull, a handle drag — before the shell dismisses anything else with
    /// the key. Answers whether there was one to end. The shell asks, because it
    /// answers Escape ahead of GUI routing and the scene's own input never sees
    /// the key while a hand is holding something.
    /// </summary>
    bool TryAnswerEscape();

    /// <summary>
    /// Puts a target on screen: the camera turns around its centre from then on,
    /// at a distance that fits it. The scene's own id frames the whole buildable
    /// region, which is also what an empty selection deserves.
    /// </summary>
    void FocusOn(Guid targetId);

    /// <summary>
    /// The turntable camera's whole state. What an agent reads before it aims, and
    /// what a handover records so the user's view can be restored on release.
    /// </summary>
    CameraPose GetCameraPose();

    /// <summary>
    /// Where the camera actually stands and how wide it sees — the pose above run
    /// through the orbit's own arithmetic.
    /// </summary>
    /// <remarks>
    /// Asked for rather than derived by the caller: a renderer that turned a
    /// turntable pose into a transform itself would hold a second copy of that
    /// arithmetic, and a picture framed a hair away from the viewport it was framed
    /// in is exactly the kind of wrong nobody can name from looking.
    /// </remarks>
    CameraView GetCameraView();

    /// <summary>
    /// Aims the camera. The single setter the aim commands and the handover's
    /// restore both drive, so there is one place camera state is put and one set
    /// of clamps it obeys.
    /// </summary>
    void SetCameraPose(CameraPose pose);

    /// <summary>
    /// The viewport's rendered frame, encoded per <paramref name="options"/> —
    /// by default with none of the editor chrome in it, which is the agent's
    /// primary way to see what it has built and the counterpart to reading a model
    /// as a slice. Asking for the chrome instead answers the other question: what
    /// the interface looks like, docks, dimmer, plaque and all.
    /// </summary>
    /// <remarks>
    /// Carries the pixel size back so a follow-up crop has a space to measure in,
    /// and the mapping to input coordinates so a pixel read off the picture can be
    /// turned into a point to aim at. Reading coordinates off an image is the
    /// fallback, though — the supported way to aim is a reference from
    /// <see cref="IUiDriver.ReadControls"/>.
    /// </remarks>
    FrameCapture CaptureFrame(FrameCaptureOptions options);

    /// <summary>
    /// One cube wearing the given material, lit and photographed on its own. The
    /// document and the scene are untouched: this is a viewport of its own, and it
    /// exists because a material's whole point is how it looks — a slice cannot
    /// show a facture, and the main viewport shows what the user built rather than
    /// the material by itself.
    /// </summary>
    /// <remarks>
    /// The turn is given rather than remembered, so a picture asked for twice is
    /// the same picture: this stage is not the panel's, and what somebody left the
    /// panel's block at must not decide what an agent sees.
    /// </remarks>
    FrameCapture CapturePreview(
        string name,
        Color color,
        MaterialAppearance appearance,
        MaterialPreviewTurn turn,
        int size,
        FrameFormat format,
        float quality);

    /// <summary>
    /// Whether an agent holds the editor. While it does, the focus marker is off,
    /// so the clean frame the agent captures is the scene and nothing of the
    /// user's camera furniture; the user gets it back when control returns.
    /// </summary>
    void SetAgentControlled(bool controlled);

    /// <summary>
    /// Which element clicks are resolved against. Picking happens in that
    /// element's own space, so a hit is a cell it can be told to fill.
    /// </summary>
    void SetPickTarget(Guid? elementId);

    /// <summary>
    /// Which material the brush preview wears, so what is shown under the cursor
    /// matches what a click would leave behind.
    /// </summary>
    void SetBrushMaterial(Guid materialId);

    /// <summary>
    /// What the drag in flight is asking for, against what the model made of it:
    /// a ghost of the element where the pointer has pulled it, and a line of
    /// numbers saying how far past the applied value that is. Null while no drag is
    /// in flight, which is what clears both.
    /// </summary>
    /// <remarks>
    /// Pushed rather than derived here, because the viewport holds one of the two
    /// numbers and the model the other. It is also the only way a quantised channel
    /// can be honest mid-gesture: the element must keep snapping to the lattice the
    /// model rounds it to, so the pointer's own position has to be shown as
    /// something else rather than by letting the element follow it.
    /// </remarks>
    void SetDragPreview(DragPreview? preview);

    /// <summary>
    /// What is shown as selected: a wireframe box around each element's voxels
    /// when anything is asking for one, plus whatever handles the current tool
    /// draws on <paramref name="activeId"/>. An empty list and a null id clear
    /// both — what the viewport is told about a scene or a material being
    /// selected.
    /// </summary>
    /// <remarks>
    /// Two arguments rather than one because they answer different questions.
    /// <paramref name="elementIds"/> is what is selected, and every one of them
    /// gets a border; <paramref name="activeId"/> is the one the editor speaks for,
    /// and it alone carries the handles and the mirror lasers — a drag writes one
    /// channel of one element, and a mirror plane belongs to the model the brush
    /// would paint into. It is always one of the ids, or null when nothing in this
    /// scene is selected — or when the active one is part of a prefab instance,
    /// which is bordered like the rest and carries no handles, since nothing can
    /// move it where it stands.
    /// <para>
    /// <paramref name="pivotPoint"/> is where on the active element the handles
    /// sit, in its own local space, and it travels with the selection because the
    /// two are pushed together and never derived here: the rule that places the
    /// pivot is the model's, and resolving it means walking the element's cells.
    /// Ignored when the id is null.
    /// </para>
    /// </remarks>
    void SetSelectionHighlight(
        IReadOnlyList<Guid> elementIds, Guid? activeId, Vector3 pivotPoint);

    /// <summary>
    /// Which tool the viewport is in, and so which handles it draws and what a
    /// plain left click does. <see cref="ToolKind.Paint"/> is the brush and no
    /// handles at all.
    /// </summary>
    void SetTool(ToolKind tool);

    /// <summary>
    /// Hands the viewport the keyboard. What a tool asked for from another panel
    /// needs: the tool acts here, so the keys that drive it have to arrive here
    /// too. A no-op when the viewport is already the panel listening.
    /// </summary>
    void TakeKeyboard();

    void Clear();

    /// <summary>
    /// Establishes the scene root that top-level elements parent to, sets up the
    /// buildable region's backdrop, and frames the camera on it.
    /// </summary>
    void SetRoot(Guid sceneId, Vector3I size);

    void AddElement(Guid id, Guid parentId);

    /// <summary>Removes the element and everything nested under it.</summary>
    void RemoveElement(Guid id);

    /// <summary>
    /// Places the element's node. <c>values.Position</c> names where the element's
    /// <b>pivot</b> stands in the parent frame — the public meaning of the channel
    /// — so the node's own origin is composed here as <c>position − basis · pivot</c>,
    /// which is the one place the view has to know the pivot at all.
    /// </summary>
    void SetElementTransform(Guid id, TransformValues values, Vector3 pivotPoint);

    /// <summary>
    /// Whether the element is drawn. Pushed per element and never composed here:
    /// the nodes mirror the document's tree, so a hidden parent takes its whole
    /// subtree off screen the way it takes it out of an export.
    /// </summary>
    void SetElementVisible(Guid id, bool visible);

    /// <summary>
    /// The element's cells, and the box of them the change touched — null for
    /// everything, which is what a fresh document and a change of material are.
    /// </summary>
    void SetElementVoxels(
        Guid id,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        CellRegion? region);

    /// <summary>
    /// The element's mirror-plane slots, pushed so the brush preview can offer
    /// the whole orbit a click would write. Nothing else follows from it here:
    /// symmetry modifies edits, never the cells already shown.
    /// </summary>
    void SetElementSymmetry(Guid id, SymmetryValues values);

    /// <summary>
    /// Draws, redraws or takes away an element's camera: its frustum in the
    /// viewport, told apart by whether it is the camera the scene renders through.
    /// A null lens means the element is not a camera.
    /// </summary>
    /// <remarks>
    /// A camera holds no cells, so nothing else in this interface would ever put
    /// one on screen — and a shot nobody can see is a shot nobody can aim.
    /// </remarks>
    void SetElementCamera(Guid id, CameraLens? lens, bool active);

    /// <summary>
    /// The shape of the picture a render would produce, so a frustum encloses what
    /// the render will contain. Pushed rather than assumed: how big the picture is
    /// is the render form's answer, and a square frustum would be a promise it does
    /// not keep.
    /// </summary>
    void SetCameraAspect(float aspect);

    /// <summary>
    /// What an element stands for, both readings at once: a picture, a model, or
    /// neither — the same "this is what this element is" push a camera's lens
    /// makes. Everything is re-derived from it: the plane's size, the texture, the
    /// filtering and the fade.
    /// </summary>
    /// <remarks>
    /// One call carrying both rather than one per kind, and that is not tidiness.
    /// As two, an element carrying a picture was pushed a picture and then pushed
    /// "not a model" — and the second call tore down what the first had built. It
    /// shipped, and no test could see it: a fake records what it is told, and what
    /// went wrong was the order two truths arrived in. A single answer cannot be
    /// half-applied.
    /// </remarks>
    void SetElementReference(
        Guid id,
        ReferenceImageSettings? picture,
        ReferenceModelSettings? model);

    /// <summary>
    /// Whether references are drawn at all. One switch for the whole scene, beside
    /// the per-element eye: it is for looking at the model clean for a moment, so
    /// it touches no element's own flag and putting it back shows exactly what was
    /// shown before.
    /// </summary>
    void SetReferencesVisible(bool visible);

    /// <summary>
    /// The material's colour and how its surface takes light, both from the
    /// document; how an appearance becomes a picture is decided in this assembly
    /// and nowhere else. The name is what a shipped material's atlas layer is
    /// found by.
    /// </summary>
    void SetMaterial(Guid id, string name, Color color, MaterialAppearance appearance);

    /// <summary>
    /// Says which of the viewport's optional effects are running. None of them is
    /// what a material is, so switching one off changes how the scene is drawn and
    /// never what the document says.
    /// </summary>
    void SetViewportEffects(ViewportEffects effects);

    void RemoveMaterial(Guid id);
}
