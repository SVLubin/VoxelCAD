# Architecture (canon)

This section is **canon**, not description: the code implements it, and where
the two ever disagree it is the code that is wrong. The prototype it replaced
(`src/Document/`, `src/Editor/`) has been deleted.

The code below is **illustrative of shape, not of layout** — every type still
gets its own file, file-scoped namespaces and four-space indentation, per the
C# Coding Style Guide. Where the two could ever conflict, that guide governs
formatting and naming; this one governs structure.

## Layers and assemblies

Every layer is its own `.csproj`, so the dependency arrows below are enforced
by the compiler rather than by convention.

| Assembly | Holds | May reference |
|---|---|---|
| `VoxelCAD.CommonTypes` | structs + enums, nothing else | — |
| `VoxelCAD.Geometry` | engine-free geometric predicates shared across the seam | — |
| `VoxelCAD.Model` | document data, hierarchy, history | CommonTypes, Geometry |
| `VoxelCAD.View` | Godot nodes, UI widgets, 3D rendering | CommonTypes, Geometry |
| `VoxelCAD.Export` | model ↔ IR ↔ external formats (out: OBJ/STL/FBX/vox; in: vox/qb) | CommonTypes, Geometry, Model |
| `VoxelCAD.Render` | path traced pictures: scene compile, tracer, tone map | CommonTypes, Geometry, Model |
| `VoxelCAD.Commands` | command dispatcher over the model | CommonTypes, Geometry, Model, Export |
| `VoxelCAD.Mcp` | MCP protocol + HTTP transport | Commands |
| `VoxelCAD.Preferences` | application settings + the client config file | CommonTypes |
| `VoxelCAD.Agent` | the endpoint's lifecycle: listener, client config, `ListenerStopped` | Commands, Mcp, Preferences |
| `VoxelCAD.Presenter` | presenters, `EditorSession`, control | CommonTypes, Geometry, Model, View, Commands, Preferences, Render |
| `VoxelCAD.csproj` (main) | composition root, nothing else | all of the above |

- **Model never references Presenter or View. View never references Model or
  Presenter.** Model talks outward only through events.
- **The file-format assembly reads as well as writes**, which is why its table
  row has an arrow both ways. `ExportProjection` is the one file that knows the
  model on the way out and `ImportProjection` the one that knows it on the way in;
  writers and readers see the IR and never a document. The name is the exporter's
  because the exporter came first, and renaming it would touch every layer above
  for nothing. Import's own canon — the placement arithmetic, the material
  mapping and what is deliberately dropped — is `docs/import.md`.
- **`VoxelCAD.Geometry` exists for the questions both sides of that wall ask.**
  `CommonTypes` may hold no logic and `View` may not see `Model`, so a predicate
  needed by the viewport *and* the exporter *and* the command surface had nowhere
  to live but three copies. Whether an element's cells sit on the world's own cell
  grid is the first of them: the exporter decides whether a volume bakes exactly,
  the viewport decides whether the region's walls take paint, and `element`
  reports it. The mirror-cell reflection is the second: the brush writes the whole
  orbit of a click, and the viewport previews that very orbit under the cursor —
  one formula, or the preview lies. The box a set of cells occupies is the third,
  and it is the one **`Model` depends on this layer for**: the viewport draws the
  selection box by it, `bounds` reports it, and a Centre pivot is its middle. That
  last customer is why the table above gives Model a Geometry reference at all —
  the arrow is new, and it is safe because Geometry is a leaf that references
  nothing, so nothing about "Model talks outward only through events" is touched.
  The face machinery is the fourth (`VoxelFaces`): the six neighbour offsets and
  the opacity rule deciding which faces of a model exist, asked by the viewport's
  mesher and the exporter's — two meshers that may wind a face each in their own
  convention but must never disagree about which faces there are. That is the bar
  for adding here — several layers, one answer, and disagreement between them
  would be a bug. Pure functions over Godot's arithmetic types; it references
  nothing.
- Main project mechanics: a `ProjectReference` per library **plus**
  `<Compile Remove="src/<Layer>/**" />` for each — otherwise Godot.NET.Sdk
  compiles those files a second time.
- Libraries are plain `Microsoft.NET.Sdk` with a `GodotSharp` PackageReference
  and `EnableDynamicLoading=true`. Godot arithmetic types (`Vector3`, …) are
  allowed in every layer; Godot *nodes* are View-only.
- **The assembly holding Godot nodes must use `Godot.NET.Sdk`, not
  `Microsoft.NET.Sdk`.** This is not cosmetic and it is not optional. Under the
  plain SDK a library's `Node` subclasses render and receive signals, but the
  engine **never calls their virtual callbacks** — `_Ready`, `_Process`,
  `_Input` silently do nothing, because the source generator that binds a type
  to a script resource only runs under the Godot SDK. Measured both ways on
  4.7.1: switch the SDK and the same code starts firing.

  Only `VoxelCAD.View` needs it, which is a useful signal in itself: a layer
  built with the plain SDK is one that holds no engine nodes. Its build drops a
  `.godot/` folder beside the sources — gitignored.

  Library nodes do not appear in the editor's Add Node dialog. Irrelevant:
  everything is built in code, and the project has exactly one `.tscn`, the
  composition root.

  **Verified:** the macOS export builds and launches (see
  `tools/export_macos.sh` — the plain ad-hoc re-sign it performs is what makes
  the exported bundle launchable, and the script says why). **Not yet
  verified:** hot reload, and exports for the other platforms.

## Visibility

- Public interface + `internal sealed` implementation at every assembly seam.
- One public factory per assembly, **for roots only**. Everything below a root
  is created by methods on already-obtained interfaces — a factory must never
  become a catalogue of every object in the layer.
- Inside an assembly this does not apply: use plain internal classes unless
  there is a concrete reason for an interface.
- `CommonTypes` has **no logic and no interfaces** — structs and enums only.
  Working rule: no method bodies. If it needs behaviour, it belongs in Model.
- Where an interface exposes a sequence of behavioural objects, the element
  type is an interface too (`IReadOnlyList<IHierarchyElement>`). Plain data
  structs from `CommonTypes` cross as themselves.
- Identity is always `Guid`, never an object reference — references do not
  survive undo or reload.

## Model

**`IProject` is the root** — the material registry, the scenes, the prefab
library, and **one undo history covering all of it**. History is deliberately project-wide rather than
per scene: undo should reverse the last thing the user did, whatever it was,
without them having to work out which stack the focused panel belongs to.

**Scenes live in the project too**, as a named list with the same shape as the
material registry: `CreateScene` / `DeleteScene` / `RenameScene`, undoable like
everything else, and a project always keeps at least one so there is always
somewhere to build. Each scene owns its own elements; nothing is shared between
them but the materials and the history. *Which* scene is being worked on is not
recorded here — that is editor state, and the document is the same file whichever
one is open.

**Materials live in the project**, referenced from voxels by `Guid`, so
recolouring one repaints every voxel using it. A material's appearance is a
single `Color` whose alpha is its opacity — a separate opacity field would be a
second way to say the same thing. Deleting a material still in use is refused
rather than leaving dangling references, and a project always keeps at least one
so voxels can always be placed. **The reason for that refusal is published**
(`IsMaterialInUse`), because a surface that offers the deletion has to know the
answer *before* it offers it — a bin greyed out beats a bin that throws — and two
copies of the rule could disagree about what "in use" means. The *current*
material is editor state, not document data: it belongs in `EditorSession`, not
here.

**Hierarchy.** Two roles, composition rather than inheritance:

```csharp
public interface IHierarchyContainer
{
    // The scene has an id too, so CreateElement/ReparentElement address the root
    // like any other parent — no Guid.Empty sentinel anywhere.
    Guid Id { get; }

    IReadOnlyList<IHierarchyElement> Children { get; }
}

public interface IHierarchyElement : IHierarchyContainer
{
    // Never null — a top-level element parents to the scene itself.
    IHierarchyContainer Parent { get; }

    // Null for ordinary elements, else the prefab instance root that owns this
    // element; anything non-null is read-only.
    Guid? PrefabInstanceId { get; }
}

// Has children, has no parent.
public interface IScene : IHierarchyContainer
{
    event Action<SceneChange> Changed;

    // Renaming goes through the project, which owns the list of scenes.
    string Name { get; }

    bool TryGetElement(Guid id, out IHierarchyElement element);

    Guid CreateElement(Guid parentId, ComponentKind kind, string name);
    void SetVoxels(Guid elementId, IReadOnlyList<VoxelEdit> edits);
    void DeleteElement(Guid id);
    void ReparentElement(Guid id, Guid newParentId, int index);
    void RenameElement(Guid id, string name);
    void SetTransform(Guid id, TransformChannel channel, Vector3 value);
    void SetFieldMode(Guid id, TransformChannel channel, FieldMode mode);
}
```

`HierarchyContainer` is an `internal sealed` **helper held in a private field**
by both `Scene` and `HierarchyElement`, not a base class. It owns list
mechanics (order, insert at index, maintaining `Parent`). `Scene` owns
integrity: the `Guid → element` registry, existence checks, cycle prevention
on reparent, and writing to history — only it sees the whole tree.

**Elements are Unity-shaped:** transform + one optional component slot. An
empty slot is a grouping element (Unity's empty GameObject). A component
carries no `Guid` of its own — it is addressed through its element — and holds
a back-reference `Owner` set at attach time (not serialized; rebuilt from tree
structure). A component is fixed for the element's lifetime: "replacing" it
means creating a new element, and the `Guid` changes.

## Cameras

**A camera is an element**, `ComponentKind.Camera` filling the single component
slot beside `IVoxelModel` and `IPrefabInstance`. That is the whole of the design,
and everything else follows from it: it has a transform, so the gizmo aims it; a
place in the tree, so it rides on a group; a name, a hidden flag, undo, copy and
the file format. It holds no cells, so everything that walks the tree looking for
geometry — the mesher, the exporter, the render's own compile — passes over it
without being told to.

What it holds instead is the **lens** (`CameraLens`): the projection, the field of
view, the orthographic window, the blur and the focus distance. Those belong to
the camera rather than to the render form because they are part of the shot. A
picture taken through the same camera has to be the same picture, and a lens kept
in the application's preferences would make it depend on what somebody left a dial
at — including on a machine the file merely travelled to. The card keeps the film
and the light: the size, the samples, the exposure, the sun, the backdrop, the
floor and the glare.

**One camera per scene renders.** `IScene.ActiveCameraId` is *answered* rather
than stored: what `SetActiveCamera` records is somebody's **choice**, and the
scene falls back to the first camera it holds — depth-first, in tree order —
whenever that choice names nothing. Three things follow, and they are the reason
it is derived:

- The first camera created is the one that renders, with nobody having chosen.
- Deleting the chosen camera hands the picture to another rather than leaving the
  scene unrenderable, and **undoing that delete hands it straight back**, because
  the choice was never unmade.
- A file carries the choice and never the answer. Saving the fallback would turn
  a camera that merely happened to be first into a decision.

**A camera's position and rotation default to `FieldMode.Float`**
(`ElementTransformDefaults`). Integer and Snap90 are the CAD default because they
put an element's own grid on the scene's, which is what lets the brush place
against the region's walls — and a camera has neither a grid nor a brush. Under
Snap90 an aim of thirty degrees would arrive as nought, so a camera placed to keep
a shot would be quantised out of it with nothing saying what had happened.

**`IScene.CreateCamera` places one already standing where it belongs**, as one
record. Four edits would be four things to undo, and each would be quantised on
the way in — so the framing would have to survive a trip through the field modes
before they were set.

**A camera ignores scale.** It has no size of its own, and the tracer is handed
its *rotation* rather than its basis: a scaled basis would skew every ray. A
basis with no determinant is refused by name rather than guessed at, the rule
`SceneView.TryPick` already keeps — it is the place where an infinity would
otherwise become a direction.

## References

**A reference is an element**, and two members of `ComponentKind` fill the slot:
`ReferenceImage` is a plane wearing somebody else's picture, `ReferenceModel` is
somebody else's 3D model. Both are **placed and never edited** — the document owns
where one stands and how it is drawn, and the file it names belongs to the disk.
Everything else follows from being an element: a transform, a pivot, a name, the
hidden flag, undo, copy and paste, groups and prefabs.

**They hold no cells**, so the mesher, the exporter's projection and the render's
own compile pass over them without being told to — and so does every ray a cell
tool casts. A brush paints straight through a reference, which is what a reference
is *for*. Selecting one is the project tree's, as a camera's is.

**The asset is linked, not copied.** The component carries a path; the storage
layer writes it relative to the project's own directory whenever the asset sits
under it, and resolves it back on load, because that is the only layer that knows
where the project file is. A missing file is a state rather than a failure: the
element stays, draws as a placeholder, and says so.

**A picture's size in cells is the truth**, and the resolution it was last measured
at rides beside it so that "pixels per cell" is a division rather than a second
stored number. One pixel to one voxel is therefore a size, not a mode — and it is
kept honest by the quad spanning **from the element's own zero**, since
quantisation acts on the stored origin, so a corner at local zero is a corner on
the cell lattice. The document's own invariant is only the resolution:
`SetReferenceImage` measures a picture whose path changed and leaves the size
exactly as the caller asked, because how big the plane is is somebody's decision.

**Which reference an element is, is one push.** `ISceneView.SetElementReference`
carries both readings and "neither" at once; as two calls, an element carrying a
picture was pushed a picture and then pushed "not a model", and the second tore
down what the first had built.

The rest — the formats, the drawing, what a reference deliberately is not, and the
command surface — is canon of its own in `docs/references.md`.

**Every element can be hidden**, and the flag is document data — saved with the
project, undoable like any other edit, and reported by `element` and `tree`. It
sits beside the transform rather than in the component slot, for the same reason
the transform does: it is a fact about the element, not about what it holds, so a
group hides exactly as a voxel model does.

- **The flag is the element's own, and visibility composes down the tree** the
  way a transform does. Hiding a group writes nothing to its children — their own
  answer is untouched, which is what makes showing the group again restore
  precisely what was there. The composition is the consumer's: in the viewport it
  is free, because the nodes mirror the tree and Godot draws nothing under a
  hidden one; in the exporter it is the projection's walk skipping the subtree.
- **What is hidden does not leave the editor.** Both facets go through that one
  walk, so a mesh and a volume cannot come to disagree about it. That is the only
  reading of "hidden" worth having for an application whose product is a file.
- **Nothing else follows from it.** The cells, the transform and the place in the
  tree are untouched, and a hidden element is still selectable in the project
  tree, still inspectable, still transformable — the border and the handles hang
  in the parent's frame and do not go away with the geometry. Hiding is about
  what is drawn and what is written out, not about what may be edited.
- **The project tree shows both readings, and they are different things.** A row
  recedes when it reaches nothing — its own flag or an ancestor's — while the eye
  it offers is about the element's **own** flag, the only one clicking can change.
  So a row under a hidden group is greyed and still offers an open eye: pressing
  it hides that element in its own right rather than showing it. `RowVisibility`
  is the three states that distinction needs, and the presenter resolves it by
  carrying the line above a subtree down the walk.

**All hierarchy mutation goes through `IScene`, addressed by `Guid`.** Element
interfaces are read-only.

**Bounds are the scene's, and they do not bound the cells.** `IScene.Size` is the
buildable region: the room the user works in, the walls the picker tests against,
and what framing frames. It is **not** a limit on where a model's voxels may be,
and no longer a limit on where its elements may stand either. Cells are placed wherever they are
asked for, in the element's own coordinates, and neither the local coordinate nor
where it lands in the scene is tested.

It used to be tested, and the rule cost more than it bought. A model may
legitimately hang out of the box, and under the old rule the part that hung out
could not be extended, because the new cell landed outside. Worse, the same test ran over *every* edit rather than only
over additions, so the cells already out there could not be erased either: the
region quietly held geometry hostage outside itself. Nothing now bounds a
model's extent; if that ever needs bounding, it wants its own decision rather
than a rule that also breaks erasing.

**Element positions are not bounded either.** They were once — clamped per axis
to the region grown by half its size — and the leash went the same way the cell
test did, for the same reason: the region is the room and the ruler, not a fence.
A part staged well outside the build area is a legitimate thing to want, and the
clamp made it impossible while buying nothing the user could not see for
themselves. Rotation and scale were never constrained, and neither is
reparenting, which only re-expresses a position it is preserving.

Quantisation is what is left on the way in, and it is silent: the model turns a
request into the nearest representable value rather than throwing, so tools need
no rounding logic and user input never raises an exception. The one clamp that
remains anywhere is the **camera's focus point**, which is confined to the region
because a view orbiting a point outside the room is worse than an off-centre
subject — a rule about the camera, not about the document.

**The position channel names the pivot.** Every element carries one pivot — a
point in its own cells, always on the half-cell lattice, stored as doubled
integers the way a mirror plane is, so a point off the lattice is unrepresentable.
The element's frame composes as `T(pos) · R · S · T(−pivot)`: the number in the
position channel, the point the handles sit on, and the point a rotation or scale
acts about are **one and the same**. There is no mode. The pivot moves only when
told to — `IScene.SetPivot`, or the one-time default below — never by itself.

Internally the model stores the **local origin**, quantised exactly as before, and
derives the position (`P = origin + basis · pivot`) at the public seam. That split
is load-bearing three times over: the origin is what keeps cells on the world grid
(grid alignment is untouched), it is what snapshots and files hold (undo and
round-trips replay exactly, and old files load unchanged), and it is what the
composition walks — the pivot only re-labels which point the channel names, it
never changes the frame. Consequences:

- **Rotation and scale hold the pivot's location.** `SetTransform(Rotation)` and
  `SetTransform(Scale)` re-solve the stored origin so the pivot stands where it
  stood — against the *quantised* new value, or the element would slide while the
  angle snapped — inside the same record, since a snapshot pair carries the whole
  transform. Turning about the pivot is nobody's compensation arithmetic any more:
  not the gizmo's, not a script's.
- **An Integer position lives on the lattice the pivot shifts.** The cells must
  stay on the world grid, so the origin is what quantises, and the position lands
  on integers offset by the pivot's own fraction: with the pivot on a half cell,
  asking for 5 gives 5.5. The inspector's position boxes step by half a cell under
  Integer for the same reason — the values the model writes back live there.
- **Moving the pivot moves the number and nothing else.** `SetPivot` holds the
  origin, so the geometry stands still while the reported position follows the
  point; one undoable record. "Centre it" is a **one-shot operation** — the
  inspector's button and `set-pivot point=center`, both through `PivotCenter`, the
  middle of the element's cells (a group's subtree) as they stand at that moment.
  Nothing recomputes it later: a pivot that followed the cells could never be the
  number the channel names, because painting would then have to move either the
  geometry or the number.
- **The default is the first voxel edit's gift.** The first edit of an empty model
  sets the pivot to the middle of what it filled, inside the same history record —
  "first" is a fact about that moment that nothing could reconstruct later. Until
  then, and for groups, the pivot is zero and position means what it always meant.
  An explicitly set pivot — even zero — is a decision the default never overwrites.
- **The surfaces speak the pivot in offsets from the content's middle.** A fresh
  element's local cells are numbered from the scene's corner — a point nobody
  chose — so a pivot printed in them reads as noise. Every surface (`set-pivot`,
  the reports, the inspector row) therefore says where the pivot is *on the
  model*: 0 is the centre of what it holds, and a freshly painted model reads
  exactly that. The chain of anchors is deliberate — **position is about the
  room, the pivot is about the model, the mirrors are about the pivot** — each
  number measured from the thing its reader actually cares about. Storage stays
  absolute (`PivotUnits` in Model is the one conversion), so painting that grows
  the box re-labels the offset over an unmoved pivot, the same honesty a pivot
  move shows the mirror offsets.

**Anchoring never rounds the origin.** Its one promise is that the pivot does not
move, and a rounded origin breaks it — the next call would derive its anchor from
the rounded origin, so the error feeds back and the element walks with every turn
(measured: three quarter-turns about the middle of a 3×4×3 model left it a full
cell away, at the same rotation). Where the exact origin does not land on the
position mode's lattice — a quarter-turn about a pivot whose axes mix whole and
half cells genuinely puts the cells on half positions — **the mode degrades to
Float and the geometry stays**: the reparent rule, applied one more time. The
walls dash while the cells are off the grid, the mode chip says what happened,
and putting the element back on the lattice is the user's explicit act, never the
model's guess. The residue is one line: the *reported* position of a rotated
element is derived through the basis and can carry float epsilon; the stored
origin is exact.

**Voxel cells live in the model**, in `IVoxelModel`, as cell → material `Guid`.
The voxel model itself neither resolves material ids nor knows the region:
materials belong to the project registry and bounds to the scene. Edits arrive as a list of
`VoxelEdit` (a null material erases), are folded to one change per cell — later
edit wins, but *before* stays what the model actually started with, so a stroke
crossing itself still undoes exactly — and are recorded as a **delta**, never a
snapshot of the model.

**World transforms are never cached** in the model — local only, walk up when
needed. The View mirrors the element tree into `Node3D`, so Godot computes
world transforms for rendering; presenters sync local transforms only.

**Reparenting preserves world position.** When that conflicts with a field
mode (the new local transform cannot be integer / 90°-aligned), **the mode
degrades to Float — the geometry never moves.** Recorded in history with the
operation.

**A copy is a clip, and a clip refers to nothing.** `CaptureElement`,
`CaptureScene` and `CaptureMaterial` lift a `ClipContent` out of the document;
`PasteElement`, `PasteScene` and `PasteMaterial` put one back. Between the two the
clip is inert data with no link to what it came from, which is what lets it outlive
the original being edited, deleted, or **cut** — a cut being a copy and a delete,
one after the other. The clips are opaque outside the model: a caller carries one
from a copy to a paste and never looks inside, so the editor can keep one in
session state without learning what a voxel blob is.

- **Deep, and identity is what is left behind.** Fresh `Guid`s throughout the
  subtree, and the cells are copied rather than watched. Everything else travels:
  the stored transform with its field modes, the pivot, the hidden flag and
  symmetry.
- **The materials the cells wear travel with the clip, and are re-created only if
  they have gone.** A material the registry still has means itself — a copy shares
  the original's materials, which is the whole point of a registry, and recolouring
  one repaints both. The window that makes carrying them necessary is narrow and
  real: cutting a model takes its cells out of the document, which can leave the
  material they were the last users of deletable, and deleted before the paste. A
  rebuilt material gets a **new** id, because the old one may still be named by the
  record that deleted it.
- **One record per paste, whatever the subtree holds** — and the materials rebuilt
  for it are attached by that same command, so undo takes the two away together.
  The prepared subtree is held by the command rather than rebuilt, the discipline
  `DeleteElementCommand` already keeps: redo re-attaches the same instances.
- **The name follows one rule that reads as two.** A name nobody is using is kept,
  so a cut and a paste put it back unchanged; a name that is taken grows the next
  free number **from its own trailing one**, so a copy of "Model 2" is "Model 3".
  Only the root of a subtree is renamed — the inside of a copied assembly should
  read like the inside of the original, and two wheels called "Wheel" is what
  duplicating a car means. Element names are composed against the whole target
  scene, which is the scope a name has to be unambiguous in for anyone addressing
  an element by it.
- **An instance is captured as a reference**, which is the one identity a clip
  carries: an instance *is* a reference, so leaving it behind would leave nothing at
  all. Its children are not captured — they are derived from that source, and a
  paste expands them again. The prefabs a clip refers to travel with it, closure
  included, on the same terms as its materials: one the target document still has
  means itself, one it has never heard of is rebuilt under a new id. Half a library
  expands into half a model, which is why the closure and not just the entry.

**Field modes are model data** (Integer / Float / Snap90 per vector, saved with
the project). A new element starts **Integer / Snap90 / Integer** — whole cells,
right angles, unit cells. That is the CAD default, and it is also what puts a new
element's own grid on top of the scene's, which is what lets the brush place
against the region's walls; going off the grid stays available and is a choice.
Old files are unaffected: the three modes are always written and always read. One `internal` interface, three independent implementations, so
a non-conforming value is *unrepresentable* rather than merely rejected:

```csharp
internal interface IFieldVector3
{
    FieldMode Mode { get; }
    Vector3 AsFloat { get; }
    Vector3I AsInt { get; }

    void Set(Vector3 value);
    void Set(Vector3I value);
}
// FloatFieldVector3 stores Vector3; IntFieldVector3 stores Vector3I;
// Snap90FieldVector3 stores quarter-turns (Vector3I), so 90°-alignment
// is structural and full turns wrap without drift.
```

Rounding happens on write *and* on read — the read side is a safety net for
paths that build elements without the setter (deserialization, prefab
expansion, undo restore, duplication). **Raw values never leave the model.**

The three vectors are grouped by an `internal sealed ElementTransform` held by
the element. A mode is **never changed in place** — the object is replaced,
carrying its value across through `AsFloat`:

```csharp
public void SetMode(TransformChannel channel, FieldMode mode)
{
    ref IFieldVector3 slot = ref Slot(channel);
    slot = FieldVector3Factory.Create(mode, slot.AsFloat);
}
```

`FieldVector3Factory.Create` holds the only `switch` over `FieldMode` in the codebase.
`ElementTransform` stays dumb — no events, no history, no validation; `IScene`
calls it, records history and raises `TransformChanged`.

`TransformChannel { Position, Rotation, Scale }` (an enum in `CommonTypes`)
keeps `IScene` to one method instead of three, and maps one-to-one onto a row
of the inspector.

**Snap90 is valid for Rotation only** — this is a model invariant, not an
inspector setting, and `SetFieldMode` throws on violation (it is a programming
error, not user input). The rule lives in exactly one place, and the
`InspectorPresenter` builds its mode toggles from that same function, so the UI
can never offer a mode the model would refuse:

```csharp
// Pure functions — no interface, no state.
public static class FieldModeHelper
{
    public static IReadOnlyList<FieldMode> GetAllowedModes(TransformChannel channel);
    public static bool IsAllowed(TransformChannel channel, FieldMode mode);
}
```

**Quantised drags must compute `Quantize(startValue + totalDelta)`**, never
`Quantize(current + frameDelta)` — the latter drifts. The tool keeps the raw
accumulator; the model applies the rule.

## History

`ICommand` is `internal` — presenters call ordinary model methods and the model
records history itself. Public surface:

```csharp
public interface IHistory
{
    // The payload names the record that moved and the scene it belongs to.
    event Action<HistoryChange> Changed;

    // Records and CurrentIndex feed the history panel; everything past the
    // index has been undone.
    IReadOnlyList<HistoryRecord> Records { get; }
    int CurrentIndex { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }

    void Undo();
    void Redo();
    void UndoTo(int index);
    IDisposable CreateBatch(ActionKind kind, Guid sceneId, Guid targetId, string targetName);
}
```

`HistoryRecord` is an immutable struct in `CommonTypes` carrying `ActionKind`
(enum), the target `Guid`, a **snapshot** of the target's name (the object may
no longer exist), and a timestamp. Human-readable labels are built by the
presenter from `ActionKind` — never stored in the model, so localisation stays
in the UI layer.

**Every record says which scene it happened in**, stamped by the history from the
scene that ran the command rather than carried by the command — `Guid.Empty` for
a project-level action such as a material edit. The editor follows it: undoing
something in a scene nobody is looking at **makes that scene the active one**,
because the alternative is an undo that appears to do nothing. This is the reason
`Changed` carries a payload at all, and the reason `CreateBatch` has to be told
which scene the gesture belongs to.

- Undo must go through the same mutators as normal edits so the same change
  events fire; otherwise views desynchronise.
- A gesture is one record: open a `CreateBatch` scope when the drag starts. A
  batch that changed nothing leaves no record; nested batches are refused.
- An edit that changes nothing records nothing — including one that changes
  nothing *after quantisation*, so dragging inside a single grid cell does not
  fill the history with identical steps. A refused edit records nothing either.
- Reached through `IScene.History`. A command's `Do` runs again on redo, so
  commands hold the element instances they created rather than re-creating them
  — that is what keeps ids stable across undo/redo. For the same reason a
  reparent works out its resulting local transform once and replays it, instead
  of recomputing and accumulating float error each cycle.
- Deleting an element removes its whole subtree; the command **retains the
  detached original** (same `Guid`s) rather than a copy, so undo restores
  identity, not a look-alike.
- Editor session state (selection, active tool, camera) is **not** in history.

## Prefabs

A prefab instance is an element whose component is `IPrefabInstance`, carrying
`SourceId` — the prefab in the project-level library (`IProject.Prefabs`, shared
across scenes; cycle-checked, a prefab cannot contain itself).

**A prefab is a scene.** `IPrefab` is a name and an `IScene` — its own buildable
region, its own element tree, the same mutators and the same history — and the
content scene carries the **prefab's own id**. Three things follow from that one
decision, and they are the reason it was taken:

- **Editing a prefab is activating a scene.** `CommandContext.SetActiveScene`
  takes a prefab's id and every scene-bound presenter is rebuilt around its
  contents, exactly as for an ordinary scene. There is no second editing mode, and
  the way back is the same gesture — another row in the project tree.
- **A record made inside a prefab names it.** Every edit is stamped with the scene
  that ran it, so an edit to a prefab's contents is stamped with the prefab. The
  editor's existing rule — undoing something in a scene nobody is looking at makes
  that scene the active one — then opens the prefab, and undoing past it comes back
  out into the scene. Neither direction is code of its own.
- **A prefab has a room.** It is copied from the scene it was made in, and it is
  what the walls, the zoom limit and the framing are derived from while it is open.
  A prefab whose cells sat off the world lattice could not be painted against the
  walls of its own room, which is why its contents go in at identity and on the
  grid.

**No per-instance overrides.** An instance is *source + its own transform and
name*, which makes the instance subtree derived data: only `SourceId` and the
transform are saved, and the subtree is expanded on load. Editing the source
rebuilds every instance, everywhere — in every scene and inside every other
prefab.

- Element `Guid`s inside an instance are **deterministic** — a hash of
  (instance id, source element id) — so they survive rebuilds and reloads, and
  tree expansion state and selection do not reset when the source is edited.
- `IHierarchyElement.PrefabInstanceId` is `null` for ordinary elements, else
  the instance root's `Guid`. `IScene` refuses mutation of anything with a
  non-null value; the project tree gives such a row no bin, and a click on one in
  the viewport means the instance (see **The selection**).
- Instance contents are **visible in the hierarchy and read-only**: selectable
  and inspectable, not editable. The instance element itself is an ordinary
  element (move, rename, delete, reparent).
- **Read-only says so before anybody types.** Contents picked in the tree show
  their transform without taking input, carry a border and no handles, and are
  offered neither the tools nor the mirrors; in their place the inspector names
  the prefab they are part of and offers **Open prefab** — the same button, and
  the same word, an instance's own section uses. A refusal that only arrives
  after a field was typed into, or a handle that answers nothing, is a promise
  the panel then has to take back.
- **A rebuild is collected rather than run per event.** One edit to a source
  raises a stream of element events, and rebuilding every instance on each of them
  would be quadratic in the size of the edit. The sources whose instances are out
  of date are collected and drained once (`Project.MarkPrefabDirty`); the walk
  terminates because the library is acyclic, which is what the cycle rule is
  actually for.

**Making one is a transfer, not a copy.** "Make prefab" puts the element's subtree
in the library and leaves an instance standing in its place — and the instance
takes the element's **transform, field modes and pivot**, while the contents go to
the library at identity. So nothing appears to move, and the prefab's own room
shows its cells where they were painted. An element with no pivot of its own — a
group — is given the middle of what it holds, and so is a fresh instance: neither
has cells of its own, and handles at the local origin would sit in a corner of the
room rather than on the thing they move.

**Unpacking is one level.** The element stays where it is as a grouping element
and its contents become ordinary elements with fresh ids; a nested instance among
them goes on following its own source. Deleting a prefab is the same operation
applied everywhere at once — every instance of it unpacked, in every scene and
inside every other prefab, as **one** record. It is not refused the way a material
in use is, because nothing is lost by it: an unpacked instance keeps the geometry
it was showing. What it costs is asked in a dialog, and `IProject.IsPrefabInUse`
publishes the answer so the question can say it.

**Prefab names are unique**, because the library is addressed by name. Creation
composes a free one — there the name is a default — while `RenamePrefab` refuses a
taken one: a rename is the user's explicit choice of words, and quietly changing
an explicit choice is worse than refusing it.

**`IProject.CanContainPrefab` publishes the cycle rule** for the same reason
`IsMaterialInUse` publishes the deletion rule: a drag has to show its answer while
the row is still in the air, and a button has to be drawn live or greyed before it
is pressed. The model refuses by throwing, which is right for a programming error
and useless for either surface.

## View

Strictly passive, and **fed a spoonful at a time** — the presenter synchronises
it, the view never loads a model.

**The chrome layout — panels, columns, pins, folds, the height cascade and the
glass — is canon of its own in `docs/panels.md`.** Read it before touching how
the shell shares the window with the viewport.

**How a model is traced into a picture — the second way of seeing one, beside
this viewport — is canon of its own in `docs/render.md`.** The two answer
different questions and neither is the other's preview. One rule from it reaches
back here: **a rendering device may only be driven from the thread the engine
renders on**, local ones included, whatever the engine's own documentation
implies. Work that wants the card must be handed to that thread, not to a thread
of ours; what a frame can do instead is submit and pick the answer up next time
round.

- The view has no idea what a document is. Its API is setters and events:
  `AddItem(SelectionTarget target, SelectionTarget parent, string name, int index)`,
  `RemoveItem(SelectionTarget)`,
  `SetSelection(IReadOnlyList<SelectionTarget>, SelectionTarget active)`,
  `event Action<HierarchySelection> SelectionRequested`.
- **Incremental, not bulk** — `AddItem` per change, not `Populate(tree)`.
- **`Clear` is the whole of "the document is gone", and it is the panel's own
  responsibility to be complete.** A view is filled a spoonful at a time and
  outlives every document it is filled from, so anything it derives from one and
  does not drop here survives into the next — where nobody pushes a value for it,
  because the new document has nothing to say about it. Two shapes of that bug
  have been paid for: chrome parented to the *view* rather than to the element it
  describes (the symmetry lasers' wall traces, drawn across an empty room after a
  load), and state a refill does not touch (the history panel's folds, keyed by a
  session number that means nothing in the next document's history). The rule
  they leave behind: **a rebuild drops everything it owns before it decides
  whether to build anything** — a teardown reached only along the path that goes
  on to build covers exactly the cases that did not need it.
- Rows of the project tree are addressed by **`SelectionTarget` — a kind and an
  id** — not by `Guid` alone: the tree holds scenes, elements and materials, and
  its category rows correspond to nothing in the model at all. Identity inside
  the document is still a `Guid`; the kind says which registry it belongs to.
- The panel does not know what activating a row means. It reports
  `ItemActivated`, and a presenter decides that a scene becomes the one being
  worked on and a material becomes what the brush paints with.
- Signals are wiring between **Godot and the view** (`Button.Pressed`,
  `Tree.ItemSelected`, node lifecycle) — the view catches one and re-raises it
  as its own C# `event`. **Signals never cross the seam to a presenter.**
  Besides the interface issue (an interface cannot declare a signal), signal
  parameters must be `Variant`-compatible: `System.Guid` is not, and `Guid` is
  the basis of identity everywhere in this canon.
- **Godot offers an event to `_input`, then to the GUI, and only what the GUI
  left alone reaches `_shortcut_input` and the unhandled routes.** The name says
  otherwise and this project believed it for a long time: anything that must be
  answered before a focused control sees it — Escape, above all — goes in
  `_input` and nowhere else. Measured with a print in both, a focused number
  field eating the press and letting the release through.
- **A row event is re-raised outside the engine's dispatch** (`DeferredReport`,
  one frame later). A presenter may answer it by rebuilding the very panel it
  came from — undo from the history list walks the document and the list is
  rebuilt around it; activating a scene row rebuilds the scene-bound
  presenters, whose constructors refill the project tree — and Godot's `Tree`
  refuses `Clear` and `CreateItem` while it is delivering a mouse event. Raised
  inline, the rebuild fails with nothing but a log line: the panel keeps its
  stale rows and its half-cleared lookups swallow every later click. Caught
  live exactly that way. This is the dialogs' own "an answer is delivered one
  frame later, not inline" rule applied at the panel seam, and it is the rule
  for the *class*: what a presenter does with a row event must never be
  constrained by which engine dispatch happened to deliver it.
- Small controls (fields, rows, dock panels) stay internal; only the panel
  surfaces an interface.
- Input is reported, not interpreted: the viewport says "cell (x,y,z), face +Y
  was clicked", and a presenter decides what that means.

### Shading

The voxel surface is lit by **real lights and nothing else** — a standard PBR
material answering a directional key and a fill, so a face is dark because it
faces away from the light rather than because a table said so. The one thing
added on top is **ambient occlusion baked into the vertex colours**, computed
from the cells crowding each face corner (`VoxelAmbientOcclusion`), which
multiplies the albedo and so attenuates the ambient term exactly as much as the
direct one. That is what occlusion means, and it is the difference between a
model that reads as solid and one that reads as flat paint.

**How far the shadows carry is the room's answer, not the engine's.** Godot
budgets a directional shadow over a distance measured from the camera and
defaults it to 100; the zoom limit is already derived from the region, so the
shadow budget is derived from the same place — otherwise backing away from a
large scene walks out of the budget and the shadows disappear, which reads as
them being broken. One number per room, not per frame: a budget that followed
the camera would keep the shadow sharpest at every zoom and would therefore
change as the camera moved.

The numbers are Goxel's, deliberately: occlusion at 0.4, the cast shadow weak
rather than absent (`ShadowOpacity` 0.3), the key light high. This is the look
the editor is held to, and it is reproduced by *computing* the same thing Goxel
computes — not by approximating it with per-axis shade coefficients, which is
the exact fake the sacred rules forbid.

Two consequences of where the geometry lives, both intentional. **The occlusion
is an element's own**, because an element meshes from its own cells; nothing
composes the scene before meshing. And **the region is furniture, not
geometry** — `SceneBox` neither casts nor receives, like Goxel's ground grid —
so shadows only ever land on voxels.

**A transparent material is an instance of its own.** Godot's alpha pass orders
instances and never the surfaces inside one, so an element's geometry is split:
the opaque materials share a mesh, because the depth buffer orders them however
they arrive, and each transparent material gets a `MeshInstance3D`. Without it,
two panes of different transparent materials in one element blended in the order
their material ids happened to hash into a dictionary — the same result from
either side, which the eye reads as the panes jumping as the camera turns. It
costs no draw calls, a surface being one wherever it lives. What it cannot reach
stays: two panes of the same material are one instance again, and interpenetrating
objects have a single distance where the truth varies per pixel.

What casts is decided by what a thing *is*, not by whether it is in the
document: the brush's place preview wears the brush's own material and casts
like the voxel it stands for, because a box floating in empty space says nothing
about where in space it is and its shadow does. The wireframe markers and the
drag ghost do not, being lines and a deliberately unshaded ghost.

### The camera and the view cube

The camera, the focus marker and the view cube interpret their own input, and
that is not the rule above being bent. **Camera state is not model state.** A
brush click has to reach a presenter because only a presenter knows what a cell
means to the document; where the camera is looking means nothing to the document
at all, so there is no presenter to ask and nothing for one to decide. The
viewport therefore says "cell (x,y,z), face +Y was clicked" and reports it, and
turns the camera itself.

The cube is a widget over the viewport, not a second camera. It asks for a pose
through `ISceneView.SetCameraPose` — the one setter the aim commands and the
handover's restore also drive — so the pitch clamp, the region clamp on the
focus point, the marker and `CameraActivity` all apply to it with no machinery
of its own. It follows from the last of those that a tool is held off while the
cube's turn is running for exactly the reason it is held off during an orbit
drag, rather than because anything suppresses it.

**One pure function turns a zone into a pose, and both the mouse and the
dispatcher call it.** `ViewCubeGeometry.GetPose` is what the widget uses when a
face is clicked and what the `view` command uses when an agent names a
direction, which is why the two cannot come to disagree about where "front" is.
It is public for that reason and no other — the widget itself is internal like
every other panel view.

The cube is chrome, so it is in `window` and not in `frame`, and the corner it
sits in is a display preference the shell applies; it is not an aiming channel
for an agent, whose route to a standard view is `view`.

## Presenters

Plain C# objects (never Godot nodes), in a tree:

```
EditorRoot (Godot script — the composition root; creates the roots, runs the
            agent endpoint from the preferences, and stops)
  └── ShellPresenter ── IShellView (menus, docks, window title, dialogs)
        owns EditorSession, ControlPresenter, AutosavePresenter,
        SettingsPresenter, and one document at a time
        ├── AutosavePresenter ── the recovery snapshot of the bound document
        ├── SettingsPresenter ── ISettingsView (application preferences)
        └── DocumentPresenter ── the open IProject
              ├── HierarchyPresenter ── IHierarchyView
              ├── InspectorPresenter ── IInspectorView
              ├── HistoryPresenter   ── IHistoryView
              ├── ViewportPresenter  ── ISceneView
              ├── BrushPresenter     ── ISceneView
              ├── SelectPresenter    ── ISceneView
              └── GizmoPresenter     ── ISceneView
```

`DocumentPresenter` exists so that "recreated when the document changes" is one
object being replaced rather than five, and so that the shell holds exactly one
nullable field instead of a parallel set of them. It also owns the answer to *is
there anything to lose* — read off the **identity of the history record the
document sits on**, never off the index: undoing past the saved point and editing
again lands on the same index with a different record.

**Switching the active scene is the same move one level down.** The presenters
bound to a scene — hierarchy, inspector, viewport, brush, gizmo — are thrown away
and rebuilt around the new one, and the selection is dropped with them because
its ids address the scene being left. `HistoryPresenter` belongs to the project
rather than to any scene and stays where it is.

**Several presenters share the viewport, and none of them knows the others.** The
viewport mirrors the scene into it, the brush turns a click into a voxel edit, the
cursor turns a click into a selection, and the gizmo turns a handle drag into a
transform edit. Which of the tool presenters a plain left click belongs to is not
arbitrated between them: the active tool is session state, and the view acts on
whichever one it is in.

**The cursor is a tool, not the absence of one, and it is where the editor
opens.** `ToolKind.Select` edits nothing — it aims at whole elements rather than
at cells or faces, and what a click does is say which element the pointer is
over. Making it a member of the same exclusive enum is what makes *leaving* a
mode the same gesture as entering one: one key, the same request, the same round
trip through the session, and no second mechanism meaning "no tool". A fresh
window points at things before it edits them, which `EditorSession` states
outright rather than leaving to the enum's zero — the two agree, and neither
stands in for the other.

- **It picks across elements, which no other tool does.** The viewport reports
  every element the ray crossed, nearest first (`ElementPick`), and only what is
  actually on screen: visibility composes down the node tree the way it composes
  down the document's, so what is hidden — by its own flag or an ancestor's — is
  not offered. Distances are compared in world units, because each element's ray
  is normalized in its own frame and a scaled element would otherwise sort against
  its neighbour in a different unit.
- **Which of them the click meant is the presenter's**, and it is the one decision
  here worth its own object: a click repeating a click in the same spot means the
  element behind the one selected now, so a stack of parts is walked through by
  clicking the same pixel again (`SelectionCycle`). It steps from the *live*
  selection rather than from what it last answered — and the walk **ends** the
  moment the hand has been anywhere else: a selection made elsewhere (a tree row,
  an undo) or the keyboard leaving the viewport makes the next click a first click,
  whatever pixel it lands on.
- **A plain click on what is already selected changes nothing.** It is, more often
  than not, the click that takes the keyboard back from a panel — the viewport
  answers its activating click as a tool click — and it must not collapse a
  selection of several to one. "Selected" is asked of what the click *means*: the
  nearest hit, or the instance it is the contents of, so a model picked inside an
  instance in the tree does give way to the instance — the instance is what can be
  moved, and the contents say where they can be changed. The spot is remembered,
  so a second click there walks on from the selection.
- **A click on a prefab instance's contents means the instance**
  (`SelectableElements`). Those elements refuse every edit, so pointing at one and
  getting it would be a selection with nothing to offer — no transform to type
  into, no bin, no prefab row. The walk is outward rather than one hop, since
  inside a nested instance the contents belong to the inner instance, which is
  itself the outer one's contents; a run of hits collapsing to one instance is one
  entry, or clicking the same pixel again would "cycle" between two names for the
  same element.
- **A click past everything selects the scene**, rather than clearing the
  selection to nothing: the room is what the click landed on, and it is a row of
  the project tree like any other. A modifier click that crossed nothing is
  different and leaves the selection alone; see **The selection** below.
- **A modifier click adds to the selection or takes something out of it.** The
  viewport reads the modifiers off the click that carried them rather than polling
  for them — by the time a presenter answers, the key may well be up — and what
  the selection becomes goes through the one function every surface uses.
- **It writes `EditorSession.Selection` and nothing else.** The tree's rows, the
  inspector, the selection borders and the brush's target all follow through the
  ordinary channel — clicking a model in the viewport is the same event as
  clicking its row, arriving from the other side. It leaves no history record,
  because selecting is not editing.
- **It is not the only tool that selects, only the one that does nothing else.**
  The three transform tools answer the same click (`SelectingTools`), and they can
  because of where the viewport's routing already puts them: a press is offered to
  the handles first, so one that missed them is free to mean "pick what is under
  it" — grab or select, decided by what the press hit rather than by a second rule.
  That is what every editor with a gizmo does, and it is what makes reaching for
  Move before choosing a part a way of working. What stays out is the tools that
  aim at cells: their click already means place, erase, sample, pull or fill,
  and having it also change which model is being edited is the complaint
  auto-select has earned everywhere it exists.

**A tool is asked for, not assigned**, and more than one panel may ask: the
viewport's own key, and — with an element selected — the project tree's, so that
picking a row and reaching for the tool that acts on it is one gesture rather than
two. `EditorSession.RequestTool` is the single way through, because activating a
tool is two things and only one of them is state: the tool is recorded, *and* the
viewport takes the keyboard, since that is where the tool acts and where the keys
that drive it have to arrive. The second half is why a request is distinct from
`ToolChanged` — asking for the tool already active changes nothing and still has
somewhere to send the keyboard. Which panel may ask is the panel presenter's
decision and not the panel's: the tree reports the key like any other input, and
the presenter refuses it for a row the tool has nothing to act on, because sending
the keyboard away from a panel someone is browsing is worse than a key that does
not answer. The cursor is the exception, and it is the exception *because* of that
rule rather than despite it: it takes hold of nothing, so there is nothing for it
to need selected, and leaving whatever mode you are in has to work from a row no
other tool would answer on.

**The editor is never in a tool that has nothing to act on.** A tool that aims at
cells needs an element with cells in it; the tools that select need nothing, since
their own click is what finds the something — `ToolSubjects` is that rule, asked by
everything that can put the editor into a tool. The state it closes off is silent,
which is why it is a rule rather than a habit: with no subject the viewport
proposes no preview and answers no click, and nothing anywhere says why, so it
reads exactly like selection having stopped working. It is reached without doing
anything odd — deleting what was being painted, clicking a material row, switching
scenes, or reaching for the brush before picking a model.

- **It and `SelectingTools` are one decision read twice.** Widening which tools
  select narrows what the fallback is for, and it did: the transform tools were in
  its scope until their click began to pick, and now nothing can put the editor
  into a transform tool it cannot use. A tool added tomorrow settles both questions
  at once — whether its click finds things, and what it needs before it may act.
- **The fallback watches, it does not ask.** `ToolFallbackPresenter` writes
  `ActiveTool` rather than calling `RequestTool`, and the difference is why those
  two exist: nobody asked for the cursor, so the viewport must not take the
  keyboard for it. A user whose selection just went away is working somewhere else.
- **Falling back is not the same as being refused.** A panel key that names a tool
  with no subject is refused *before* the request, because the request would move
  the keyboard on its way to a tool that then fell back anyway. Same question, two
  different things being protected. Which is also why a transform key is answered
  on any row now and a cell key is not: one of them can make its own subject.
- **It is not a memory.** A tool that fell back stays fallen when a subject turns
  up again; restoring it would make the same click put the editor into different
  modes depending on what came before.
- **The tool is still session state, not the scene's.** Switching scenes drops a
  cell tool only because the selection goes with the scene-bound presenters — it is
  the subject that went away, not the tool that was scoped to a scene.

Every presenter derives from `PresenterBase`, which carries one thing: `IsAlive`,
false once disposed. File operations are `async` and panel presenters do not
survive a document change, so **after every `await`, a presenter checks it before
touching a view** — and the shell checks that the document it started on is still
the open one, since a picker can outlive the project it was raised for.

The same guard belongs at the top of a **model-event handler**. Deleting the
active scene rebuilds the scene-bound presenters from inside the very change
event they are subscribed to, and .NET goes on delivering that event to the
handlers it snapshotted — including the ones just disposed, which would then
write over what their successors had already put in the view.

- **Sibling presenters never know each other.** Coordination happens through
  shared state: the hierarchy writes `session.Selection`, the inspector reacts
  to `SelectionChanged`. `ShellPresenter` **must not relay messages between
  panels** — that is how it turns into a god object. If two panels need to
  agree on something, it is state, and it belongs in `EditorSession`.
- `ShellPresenter` handles lifecycle operations (new / open / save / close),
  not inter-panel plumbing.
- A sub-presenter appears when a sub-view has its own independent binding to
  the model (inspector sections qualify). Same rules recursively: the parent
  creates, owns and disposes its children.
- **Panel presenters are recreated when the document changes**; views persist.
  Invariant: *a presenter's constructor fills its view from scratch*, starting
  with `Clear()`. `ShellPresenter`, `EditorSession`, `ControlPresenter` and
  `SettingsPresenter` outlive the document — a handover in progress is not
  interrupted by opening a file, only the history the actor is stamped onto
  follows it, and preferences are not part of a project at all.
- **Which document is open is `CommandContext`'s answer, not a field in the
  shell.** The user's File menu and an agent's `open` then become the same event
  arriving from two directions, and neither can leave the other looking at a
  project that is no longer loaded. The shell rebinds on `ProjectChanged` and
  clears the unsaved mark on `ProjectSaved`, whichever raised them.
- **Which scene is being worked on is `CommandContext`'s answer too**, for
  exactly the same reason: the user activating a scene row and an agent's
  `activate-scene` are one event, and an agent editing a scene the user is not
  looking at would otherwise be invisible. The shell rebinds on
  `ActiveSceneChanged`, and the context falls back on its own when the active
  scene is deleted or undone away. It is the one piece of session state that
  does not live in `EditorSession`.
- `EditorSession` lives in the Presenter assembly, not the Model — it is not
  part of the project and is never saved. The selection is dropped on a document
  change: its ids address nothing in the new one.

**The selection is a set, and one of it is active.** This was the open question
the list at the end of this file used to carry, and it is settled here:
`EditorSession.Selection` is an ordered `SelectionState` — everything selected,
plus the one the editor speaks for. Almost nothing downstream reads the set. The
inspector, the handles, the mirror lasers, the brush's target, the bin and the
clipboard all read `ActiveTarget`; the two surfaces that *draw* a selection read
the set, and they are the project tree's rows and the viewport's borders.

- **A selection holds one kind.** Nothing downstream has a meaning for an element
  and a material held at once, so a click naming another kind replaces rather than
  mixes, and a Shift range that genuinely crossed a category keeps the kind of the
  row actually clicked. The active target is what says which kind that is.
- **One function decides what a click does to it** — `SelectionEdit`, asked by
  every surface that can change a selection. A modifier meaning one thing in the
  scene and another in the list is a thing nobody could learn, and two copies of
  the rule is how that happens. The surfaces report **what a click named and what
  it asked for**, never the selection it should result in: the project tree knows
  which of its rows are drawn selected and so could work one out, and that is
  exactly the second copy.
- **The modifiers are one answer too** — `SelectionModifiers`. The command
  modifier toggles and Shift extends, which is the platform's convention for a
  list and the CAD one for a viewport at once. Extend means whatever the surface
  can offer: a list has an order, so it is the run from the anchor; the scene does
  not, so it is the one thing under the pointer, added and never taken out. **On
  macOS the command modifier is not a preference** — the engine's macOS window
  turns a Ctrl+left-click into a right button press before any of this is reached,
  so Cmd is the only modifier a left click can carry there.
- **A modifier that named nothing leaves the selection alone.** The hand was
  adding to it and missed, which is not the same sentence as "select nothing" — so
  only a plain click past everything lands on the room.
- **Walking a stack is a plain click's affair.** `SelectionCycle` reaches the
  element behind the one selected when a click repeats a click in the same spot; a
  modifier click means the thing you can see, and clicking the same pixel again
  with the command modifier has to take it back out rather than reach past it, or
  the modifier stops meaning toggle. The cycle is told to forget its spot when one
  happens, so the plain click after it starts at the front rather than resuming a
  walk nobody was on.
- **What is selected and what is left of it are different questions.** Deleting
  one of several drops that one and leaves the rest, and the active role is handed
  on rather than left naming something gone — `SelectionEdit.Remove`, which is
  also what a toggle taking out the active target goes through.

**A gesture over a selection acts on each of them, about itself, as one record.**
That is the whole rule for a group, and it is chosen rather than fallen into.

- **Each element about its own pivot.** A drag applies one delta to each selected
  element's own channel — position, or Euler degrees, or scale — and the model
  already holds each element's pivot still through a rotation or a scale, so
  turning about itself costs this layer no arithmetic at all. Deliberately **not**
  about the selection's centre: wanting a common centre is what a group is for, and
  a second, invisible centre that existed only while several rows happened to be
  picked out would be a worse thing to explain than "put them in a group".
- **A selected element under a selected ancestor is dropped** (`TransformTargets`).
  The ancestor's move carries the whole subtree, so writing the child's channel as
  well moves it twice — at double speed, in a case a Shift range reaches
  immediately, since a group and its children are adjacent rows.
- **The pull is quantised once, and the granted step is what everybody gets.** A
  group has to move *rigidly*: a selection that changes shape while it is being
  dragged is not a selection being dragged. Handing each element the raw pull and
  letting each round it its own way does not do that, which was measured rather
  than reasoned — over one cell of pull an Integer element stood still and then
  jumped a whole cell while a Float one beside it glided, so the gap between them
  swung by a full cell and back. So one element quantises and the rest are given
  what it was granted, and **no rounding rule is restated to do it**: the
  pace-setter is written with the raw pull and the model is asked what it made of
  it. Snapping stays the model's, exactly as it is for a drag of one.
- **A constrained element paces the group** (`GroupDragPace`), because the step has
  to be one every member can take — give a group with an Integer element in it a
  fractional step and that element rounds what it was handed, and the deformation
  is back. Within one channel there are exactly two modes, one free and one not, so
  "constrained" is the whole of the ordering this needs. The cost is the right one:
  a Float element dragged together with an Integer one moves in whole cells for as
  long as they are dragged together, which is what moving as a group means.
- **One record, however many it touched.** A gesture is one record, and that is the
  point rather than a detail: a group move that undid an element at a time would be
  the opposite of what the user watched happen. The same holds for a delete over a
  selection, which is the one operation besides the drag that acts on the set.
- **The wording is composed, not counted.** A record names the active target and
  the remainder — "Move Alpha and 2 more" — because "3 elements" would read as
  "Delete material 3 materials" once the kind's own template wrapped it.
  `ActionLabelHelper` is the one place that decides it.
- **The ghost and the readout speak for one element.** They say where the hand is
  against what the model made of it, and the delta they describe is shared by the
  whole group; the one they name is the active element, or the first the drag
  actually writes to when the active one was dropped as a passenger.

**The clipboard is session state, and the shell is what puts things on it.**
`EditorSession.Clipboard` holds a `ClipContent` and belongs to the person at the
keyboard rather than to the document, like the selection beside it — and unlike
the selection it is **deliberately not dropped on a document change**: a clip
refers to nothing and carries the materials its cells wear, so copying in one
project and pasting into another is not a case to defend against but the one way
this editor has of moving a model between files.

- **A paste goes into what is selected**, and where that reading has no sense — an
  element on the clipboard with a material selected — it lands where its kind
  belongs instead: elements in the active scene, scenes and materials in the
  project, which is the only place either can live. **A paste is never refused for
  want of a matching selection**; landing somewhere obvious beats nothing
  happening at all. Prefab content falls back the same way, being read-only
  wherever it appears.
- **Whatever lands is selected**, so the next thing the user does is about the
  copy. A pasted scene is also *activated*, which has to happen first: switching
  scenes drops the selection.
- **Duplicate is the same gesture with the copy beside the original**, and it does
  not touch the clipboard — a duplicate is about the row that is selected, so
  losing a clip somebody put there on purpose would be a second thing happening
  that nobody asked for.
- **A cut writes the clipboard only once the removal has happened**, so a refused
  one leaves both the document and the clipboard as they were.
- **None of this is on the command surface**, on the same terms as the selection
  and the active tool: an agent holds no session state and addresses things by
  name, so a clipboard for it would be an indirection with nothing on either side.
  What it gets instead is the whole gesture in one sentence — `duplicate-element`,
  `duplicate-scene`, `duplicate-material`, each naming what to copy and where to
  put it.

## Dialogs

`IDialogService` is a narrow interface implemented by the shell view; panel
presenters receive only it, never the full `IShellView`, so they cannot reach
menus or the window title.

```csharp
public interface IDialogService
{
    Task<string?> AskPathAsync(FileDialogRequest request);
    Task<ConfirmResult> AskConfirmAsync(ConfirmRequest request);
    void ShowError(string title, string message);
}
```

- File picking uses the **native OS dialog** (`FileDialog.UseNativeDialog` /
  `DisplayServer.FileDialogShow`) — on desktop CAD users expect their own file
  browser. Confirmations and errors are **in-app** Godot dialogs, themed with
  the rest of the UI.
- **Running embedded in the Godot editor, the picker is Godot's own, and that is
  not a bug.** The editor's embedded game window swaps in a DisplayServer that
  reports neither `NativeDialogFile` nor `Subwindows`, so the engine falls back —
  measured on 4.7.1: embedded gives `ds=embedded, nativeFile=False`, the same
  build launched standalone gives `ds=macOS, nativeFile=True` and leaves its own
  `FileDialog` hidden. Do not "fix" a screenshot taken through the editor. To see
  what ships, turn embedding off or run the binary with `--path`.
- Only `CommonTypes` structs cross this boundary — no Godot types.
- A dialog **holds an interaction token** for as long as it is up, the same one
  a focused inspector field holds. Seizing control out from under an open
  question is as rude as seizing it mid-edit.
- An answer is delivered **one frame later** (`CallDeferred`), not inline. A
  continuation runs on whatever thread completed the task, and here that is the
  main thread inside the dialog's own close handling — where opening the next
  dialog is not safe, and "save, then pick a path" opens the next dialog.
- Presenters are `async`, and presenters are recreated on document change:
  **after every `await`, check that the presenter is still alive** before
  touching the view — `PresenterBase.IsAlive`. The shell checks more than that:
  a picker can outlive the document it was raised for, so what comes back is
  dropped unless the document it started on is still the open one.
- The presenter never learns the file format — it gets a path and hands it to
  the model. The one thing a dialog cannot avoid naming is an extension, and the
  model publishes that (`ProjectFormat`) rather than letting the shell guess.

## AI collaboration

**Not a side feature.** Driving the editor with an agent is one of its central
functions, and that has consequences for how everything else is designed.

### One command dispatcher, several transports

```
CommandDispatcher            commands over IProject / IScene
   ├── console transport     argv or a script file, separate process, no engine
   └── MCP transport         inside the running editor
```

Tests are written once, against the dispatcher. The transports differ only in
extensions: camera and screenshot commands make sense solely in a live editor.

**A feature is not finished until it is reachable from the dispatcher.** The
dispatcher is a peer of the GUI, not an adapter bolted to its side; anything
that can only be done through GUI-specific code is invisible to an agent. This
is the same discipline as accessibility, and it decays just as quietly without
a rule.

It is a rule and not a preference: **reaching the surface is the default, and
every omission is an entry in the ledger giving its reason.** Two kinds of
reason have ever been good enough. The first is state an agent does not have —
the selection, the active tool, the clipboard — because an agent names what it
acts on and holds no session, so a command for it would be an indirection with
nothing on either side. The second is a rope worth not handing over: the input
injection switch, which must not be switchable by the instrument it enables, and
preference writes, which outlive the session with nothing to put them back.
Neither difficulty nor "nobody asked for it" is a reason.

**Reading matters as much as writing.** "Do what the user does" implies "see
what the user sees": query the tree, the selection, the history, capture a
frame. Read commands are half the surface, not a convenience.

A voxel model is read as text rather than as a picture: `bounds` says where the
cells are, and `slice` draws one layer as a character grid with a legend. That is
the precise reading — a screenshot says whether something looks right, a slice
says which cell is wrong. Both are `mutates: false`, so an agent can look before
it holds control; looking is not editing, and making it wait would be absurd.

The grid is shaped for a reader that has to be certain. Characters come from the
material's own name, so it is legible before the legend is consulted, and they
are assigned over the whole model rather than the layer in front of it, so two
slices never disagree about what a letter means. Rows carry both ends of their
range instead of a convention to remember. One answer is capped, and past the cap
the refusal names the arguments that window it: a 32-wide slice is a thousand
characters and costs nothing, a 256-wide one is not something to read.

**A name means the element that can be acted on.** An instance's contents keep the
names of the source they came from — that is what makes the inside of a copied
assembly read like the original — so every instance would otherwise make its own
name ambiguous the moment it was created, and `element=Wheel` would stop resolving
for the very element it names. `CommandContext` therefore prefers the matches that
are not prefab content, and falls back to the ambiguity report only when every
match is derived. Derived elements stay reachable by id and through `tree`, which
is where a reader meets them anyway.

The console transport references `Model` and `CommonTypes` **only** — never
`Presenter`, which would drag `View` and Godot behind it. If it stops
compiling, something has leaked into the model.

### Driving the interface is an instrument, not a way to work

The editor can synthesize its own input: click its buttons, drag its handles,
press its keys. It exists for one reason — **some behaviour can only be shown to
work by being exercised.** A transform handle is the clearest case: the
arithmetic under it is testable and the node it lives in is not, so before this
existed no handle had ever been dragged by anyone but the user, and the ledger
said so.

It is **not part of the semantic surface, and must never become a way of doing
things that surface can do.** An agent that clicks where it could have called
`set-voxels` or `set-transform` is an agent making the surface everything else is
built around redundant, one convenience at a time. So:

- It is **off by default**, behind a preference, reported by `preferences`.
- The commands are **registered even while it is off**, and refuse with a message
  naming the setting. A tool that vanishes is worse than one that explains
  itself: clients cache the list they were given at connection.
- Every description names the semantic command to use instead.
- Nothing may become reachable *only* this way. The dispatcher rule stands
  unchanged: a feature is not finished until it is reachable from the dispatcher,
  and clicking a button is not reaching it.

**Two commands read the interface, and they answer different questions.**
`window` captures the whole window, chrome included — that is *looking*, and it
is for judging whether something looks right. `ui` walks the `Control` tree and
reports each control's reference, type, text, state and rect. **`ui` is the
aiming channel.** A reference survives a relayout; a pixel read off a screenshot
survives nothing. Coordinates are the fallback, for what has no control behind it
— a point in the 3D scene, a gizmo handle — and the capture carries the mapping
back to input coordinates so that fallback is at least honest. Both are
`mutates: false`, on the same terms as `slice` and `frame`: looking is free.

**Three coordinate spaces meet here, and two of them are not the same.** A
control's rect is in canvas units; input is aimed in window pixels; the captured
image is in window pixels too. The canvas-to-window factor is the window's own
stretch transform, and it is *not* near one — measured at 1.002 and at 2.58 on
the same machine, differing only in how big the window was. So `ui` reports rects
already converted, a capture reports its origin and scale, and the transform is
read live on every snapshot. A cached one is a click in the wrong place after the
first resize.

**A gesture spans frames, and the queue knows it.** Godot accumulates input:
several motions parsed in one frame arrive as one, so a drag squeezed into a
single frame is a teleport that exercises none of the per-frame logic a drag is
meant to exercise. Injection therefore steps once per frame, and
`MainThreadWorkQueue` grows a matching rule — the command that started a gesture
does not answer until it drains, and **nothing behind it runs meanwhile**, so a
read issued after a drag cannot report the state from halfway through.

Everything goes through `Input.ParseInputEvent`, never `Viewport.PushInput`, and
the two are not interchangeable: the gizmo polls `Input.IsMouseButtonPressed`
every frame and cancels a drag when it says no, and the camera polls
`Input.IsKeyPressed` for its modifiers. A pushed event updates neither, so a
pushed drag would cancel itself on its first frame.

### Control is exclusive on three channels, not one

Input reaches the editor three ways, and each needed its own answer to "whose is
this?". Two were obvious and one was not, and the one that was not leaked for as
long as it went unnamed.

1. **Commands** — gated by `ControlGatedDispatcher`. Settled from the start.
2. **Events** — `InputEvent` routed through the tree. Gated by the banner, below.
3. **Polled device state** — `Input.IsKeyPressed`, `Input.IsMouseButtonPressed`,
   the viewport's pointer. **Not gated by either of the above, and not gateable
   by them:** Godot writes `Input`'s state before dispatching the event, so
   marking an event handled does not unwrite it, and the viewport recomputes the
   pointer and its hover each frame from the machine's cursor regardless.

The tools poll because they need *state*, not messages — nobody sends a held Alt,
and the answer has to be right on a frame where no input arrived. That reasoning
is sound, and it was complete for exactly as long as one party drove the editor.

So the tools read **`IInputState` — the pointer and keys of whoever is driving**,
never the machine's devices. Two implementations, one selector:
`PhysicalInputState` wraps Godot, `InjectedInputState` is filled in by the
injector as it sends, and `EditorInputState` chooses between them off the signal
the control presenter already sends the viewport. Each grant starts the agent
from nothing held and **no pointer at all** — a tool with nowhere to point offers
nothing, which is the correct answer for an agent that took control and only
issued semantic commands.

Measured before and after, with a human at the keyboard and an agent holding the
editor: a held Shift used to flip the agent's brush to erase, the preview used to
track the user's mouse across the window, and releasing their button could end the
agent's drag. None of the three now reaches the tools, and everything the user
does with the editor back is unchanged.

`ViewportSurface.IsPointerInside` follows from the same rule: it asks the driving
party's pointer against its own rect rather than tracking the engine's enter and
exit signals, which fire from the machine's cursor whatever else is going on.

What stays outside this is Godot's own GUI cosmetics — a button lighting up under
the user's cursor — computed by the engine per frame and not reachable from here.
It is paint, and the dimmer covers it.

### The banner blocks the user's input, not input as such

While an agent holds control the interface is suspended. That rule used to read
"no input", and it could, for as long as an agent reached the editor only over the
command surface. It cannot now: **the right to inject exists precisely when input
is blocked**, because injecting is something an agent does while holding control.

So the rule is restated rather than worked around. Injected events carry a mark;
the banner passes marked events and swallows everything else, with one carve-out
— its own Stop button, which is how a user interrupts an agent. Two consequences
follow, and both are load-bearing:

- **The dimmer no longer blocks by hit test.** A hit test cannot ask where an
  event came from, so a dimmer that ate clicks would have eaten the agent's too;
  and waving it away around an injection would be switching a safety mechanism off
  to make a feature work. It is paint now, and `ControlBanner._Input` is the single
  chokepoint for pointer and keyboard alike.
- **The mark is metadata, not `InputEvent.Device`.** The device would be the
  obvious carrier and it survives the trip, but Godot 4 matches an InputMap action
  only when the event's device equals the binding's — this project's keymap is on
  device 16, and nothing else matches. Tagging the device would silently stop `W`
  switching tools and the arrows turning the camera, which are exactly the keys
  worth injecting. A synthesized key therefore wears the device the keymap was
  recorded with, read off the InputMap rather than written down.

The mark's failure mode is one-sided by construction: nothing physical carries it,
so a lost mark costs an agent a gesture and never costs the user their protection.

**What injection does not restore.** Clicking changes the selection and pressing a
tool key changes the active tool, and release puts back only the camera — the same
gap the ledger already records for an agent's ordinary session, widened by the fact
that an instrument driving the interface touches both far more often.

### The agent and the user never work at the same time

Control is exclusive, and held by one of them. The agent gets the real editor:
the user's own camera, the user's selection, the user's history. That is what
makes "the agent does what the user does" literal rather than approximate — and
it is why no second viewport or offscreen camera is needed.

While the agent holds control the interface is dimmed, with a plaque and two
buttons: stop, and undo everything the agent did. The second is free —
`EditorSession` remembers `History.CurrentIndex` at handover, so the agent's
whole session is one `UndoTo` away.

**What the plaque can show is published, not left to be discovered.** It holds the
agent's name, two lines of its topic and a third line for the agent queued behind
it; `View.PlaqueBudget` says how many characters that is, and the `topic` parameters
quote it, so an agent is advised of the length before it writes one rather than
finding out by watching its own words end in an ellipsis. A recommendation, never a
limit — nothing refuses a long topic, it is simply not all read.

**Session state is restored on release** — camera, selection, active tool. The
agent's *edits* stay, obviously; it is the view onto them that returns.

### And no two agents at the same time either

The rule above says "the agent", and for as long as the editor could only tell the
user from *an* agent, that was a hope rather than a rule. Two clients on the
endpoint shared one identity: the second one's `take-control` reported the first
one's grant, which it could only read as its own; its edits passed the gate the
moment the first was granted control; its `release-control` ended a session it had
never opened; its `report-topic` wrote on the plaque the user was reading. So:

- **Every agent transition names the client asking for it**, and one that names
  anybody but the holder is refused. `ControlArbiter` holds the holder's identity
  beside its name, and identity is what every decision is made on — a name is what
  the plaque shows, and two agents may share one.
- **The gate asks whether the editor is held by *this* client**, not whether it is
  held. That is `ControlGatedDispatcher`, and it is why the surface is handed out
  **per client** (`IAgentDispatcherProvider`): the control commands close over the
  client they speak for, so no agent can name another one.
- **A refusal is a refusal, not a status.** `take-control` behind another agent
  fails, naming the holder, its topic and how long it has been there; the newcomer's
  `control-status` reads `HeldByAnotherAgent` with `youHoldIt: false`. There is no
  wording an agent can mistake for permission to edit.
- **A verdict belongs to the agent it was aimed at.** `Denied` and `TimedOut` are
  reported to that client; to anybody else the editor is simply the user's.
- **Identity comes from the transport, and the transport is the only place that
  could know it.** The editor's HTTP endpoint answers the `initialize` handshake
  with an `Mcp-Session-Id` to adopt and treats whatever id a request carries as
  that agent's identity; a client that carries none is filed under one shared
  anonymous identity, which is the old single-agent behaviour rather than a
  failure. The stdio transport passes a constant: one pipe pair, one agent, nobody
  to tell apart.

  The offer belongs on the handshake **and nowhere else**, which was measured
  rather than reasoned out. Offered a fresh id on every request that lacked one, a
  client that adopts what it is offered changes identity between its first call and
  its second — and the grant its first call had been given was then stranded on an
  identity it no longer had, leaving it unable to release control it held. The
  identity a session starts with is the identity it keeps.

### Waiting for a turn is a line, kept deliberately dull

An agent that meets a held editor may join a line: `take-control` with
`wait=<seconds>` blocks until the request is settled and comes back the moment the
editor is the caller's. The line is **arrival order and nothing else** — the only
fairness rule that needs no explaining to whoever loses by it — and it is **bounded**,
because a handful of agents wanting one editor is a case worth serving in order and a
hundred is a case worth refusing plainly. Past the bound a newcomer is told how long
the line is instead of being promised a turn that far off; that also keeps a rude
client from growing the line without limit, since a client id is whatever a header
says.

- **The head is promoted when the holder lets go** — releases, or falls silent — and
  it becomes an ordinary pending request from there: countdown, plaque, a user who may
  still refuse it. Nobody skips: the second in line moves up, it is not granted
  anything.
- **Never when the user takes the editor back.** Stop and Cancel mean "not now", and
  handing the editor to the next agent in line a frame later would make that button a
  joke. It means it for everybody, so a refusal lets the **whole line** go and tells
  each agent in it: the user refused agents, not one agent's turn.
- **Asking again renews a place rather than taking a second one**, or a polling agent
  would push itself to the back of the queue it was politely holding.
- **The call is capped, the waiting is not.** A blocked call is answered at the cap
  with the status — including the caller's place in line — and the place still
  standing, so an agent waits an hour by asking again rather than by holding one
  request open for an hour, which no client's own tool-call timeout would survive
  anyway.
- **A place nobody is holding open goes stale**, on the same silence terms a holder's
  control does. A call in flight is proof enough that somebody is there.
- **Withdrawing is `release-control`** — the same sentence as handing the editor
  back, one step earlier: "I no longer want it." It has to be sayable, or an agent
  that changed its mind is the one handed the editor when the holder lets go.
- **A blocked call must not hold the work queue.** It is the one command whose answer
  arrives later (`CommandResult.Pending`), and it lets the main thread go before it
  waits — otherwise a waiting agent would block the holder's own commands, and the
  holder could not release the editor the waiter is waiting for. A transport that
  cannot wait ignores the pending answer and reports the immediate one, which is
  complete on its own: "still busy, you are queued."
- **The user is told who is queued**, on the plaque — the next agent by name and how
  many are behind it — or the editor being taken again the moment it comes back reads
  as the same agent refusing to let go.

### Handing over is a negotiation, not a switch

```
UserControl ──request──▶ Pending ──grant──▶ AgentControl ──release / stop / time-out──▶ UserControl
                            │                      │
   while Pending:           │        while AgentControl holds it:
     ├ waiting:  the user is busy       ├ working: a running clock        (plaque + Stop)
     │           (quiet plaque)         └ silent:  nothing sent in a while (plaque + Stop)
     ├ counting: 3… 2… 1                       ▲
     │           (plaque + Cancel)              │ promoted on release or time-out,
     └ at once:  window unfocused          Reserved  never on the user's refusal
                                          (one agent, waiting its turn)
```

- **"Busy" is wider than "a history batch is open."** Typing in a field,
  dragging a tree item, an open dialog — none are batches, and seizing control
  during any of them is equally rude. Interacting code holds a token from
  `EditorSession`; the handover waits while any is held.
- A busy user holds the handover, but not forever: past a **maximum wait** the
  countdown runs whether or not they are still interacting, and they watch it the
  same way. So a leaked token delays a handover instead of stalling it until
  restart, and a real one still gets a visible countdown before losing the editor.
- Cancelling the countdown is a **refusal**, reported to the agent as such and
  distinguishable from a lost connection. The agent accepts it without
  arguing — the user may explain, and need not.
- The agent's request carries a **timeout**; it must not hang because someone
  left a text field focused and went for coffee.
- **Losing the transport releases control** — the endpoint switched off, the port
  moved, the editor closing. Otherwise a crashed agent leaves the interface dimmed
  until restart. `McpEndpoint` says so the moment a listener that was up goes down,
  and the shell abandons whatever handover was in flight.

### A live agent and a dead one

Long work is not the thing to protect against; silence is. An agent filling a
large model legitimately holds control for minutes, and one deciding on its next
call is quiet for tens of seconds — both are alive. What is dead is an agent that
has stopped issuing commands at all. So the signal is **time since the last
command, not total time held.**

- **Every command is proof of life, reads included.** An agent polling
  `control-status` while it thinks is alive and must not be cut off. The gated
  dispatcher already sees every agent command, so that is the one place liveness
  is stamped — a command refused for want of control still counts.
- **Graduated, and shown before it is taken.** Past a first threshold the banner
  says the agent is not responding, the interface still suspended; only past a
  further grace period does control return on its own. The grace is an amount
  *after* the warning, so control never snaps back without a warning first,
  whatever the two are set to.
- **A time-out is distinct from a refusal.** Both end with the agent holding
  nothing, and they mean opposite things: a refusal is the user's decision, a
  time-out is the agent's own silence. `control-status` reports `TimedOut` where a
  cancelled countdown reports `Denied` — the same argument that separates a
  refusal from a lost connection, one step further.
- **The thresholds are preferences**, beside the countdown duration, not
  constants — the maximum wait, the silence warning, and its grace. Clamped like
  every other setting, so the form cannot raise an exception.
- **The banner carries a running clock** of how long the agent has worked, formed
  in the presenter (seconds, then minutes and seconds) so the view renders a value
  and decides nothing. It is repainted only when the displayed second changes, not
  on every frame the countdown presenter ticks.
- Time reaches all of this only through the presenter's `Tick` — no `DateTime.Now`
  in the logic — so the arbiter and the presenter stay engine-free and the whole
  handover is tested by advancing a number.

### Batches are unreachable from a transport

`CreateBatch` returns a scope, which only works when the code that opens it is
guaranteed to reach the close. That holds for a mouse drag — press and release,
one call stack — and cannot hold for a remote driver, whose scope would span
round trips with a counterparty free to vanish.

So the remote surface has **no begin/end batch at all**: every command is
atomic, one history record or none. There is nothing to leave open. Grouping at
agent-session scale comes from `UndoTo`, not from batching.

The scope survives only where it is honest — local gestures that live across
frames — inside `try`/`finally`. Taking or releasing control closes any open
batch as a backstop, **committing** rather than discarding: the edits already
went through the model and the user already watched them happen.

**A gesture that spans frames is held by `EditorSession`, not by the panel that
opened it.** A colour picker left open, a drag still under way — the press and
the release arrive as two separate events, so there is no call stack to put a
`finally` on, and an open scope goes on collecting whatever happens next. That
is the failure to design against: a scene created while the picker is open would
be filed inside a record called "change material" and vanish as an entry of its
own. So the rule is **anything that starts a new action ends the one in flight
first** — `session.EndGesture()` at the top of the shell's create, delete, undo,
redo and file operations — and the session is where that reaches a gesture whose
owner it knows nothing about. Ending commits, for the same reason a handover
does.

Three gestures open one today: the colour picker, the handle drag, and the
**brush stroke** — the press opens the batch, every cell the hover proposes while
the button is down is one more `SetVoxels` inside it, and the release closes it.
A stroke has no cancelled form: its cells are already the model's by the time
anything could cancel, so Escape, a tool change, the target changing under it,
control changing hands, a camera moving under it, and the gesture being ended by
anything else first — an undo through the platform's own menu bar — all close it
on what landed. The stroke also decides one
thing the hover does not: while the button is down the picker reads the target's
surface **as it stood at the press** — what the stroke placed is read through,
what it erased is still met — so a stroke paints along the surface it began on
rather than climbing its own track or digging through it (`BrushStroke`).

### Application preferences

Countdown duration, whether to count down at all, whether the MCP server runs —
these are neither document data nor `EditorSession`, which dies with the
process. They live in `VoxelCAD.Preferences`, stored beside the application.

- **The layer is handed a directory, it does not find one.** Where "beside the
  application" is on a given platform is the engine's answer — `user://` — and
  only the composition root globalizes it. The layer itself is files and
  numbers, so it is testable like everything else.
- **The token is the reason this exists.** An endpoint that invented a new one
  every launch would invalidate the client config before it was ever used, which
  is what made the central feature impractical. It is generated once, persisted,
  and replaced only when asked for.
- **Out of range is clamped, not refused** — the same bargain the model makes
  with a transform, for the same reason: a settings form must not be able to
  raise an exception. A damaged file falls back to defaults rather than stopping
  the editor; it is settings, not the document.
- **`IEditorPreferences.Changed` is raised after values reach disk**, and the
  composition root acts on it: the endpoint is rebuilt to match and reports back
  through `IEditorShell.ReportAgentEndpoint`. So what the settings form says
  about the endpoint is what the endpoint actually did — listening where,
  switched off, or refused the port — rather than what was asked of it.
- **A first run is the absence of the file** (`IsFirstRun`), asked before the
  first load writes one, so it is true exactly once. A damaged file is not a
  first run: somebody has been here.

**The first launch opens a sample, not an empty room.** The samples are ordinary
project files in `assets/samples/`, embedded in the model assembly so the editor,
the CLI and the tests read the same bytes and an export needs no filter for them;
`ModelFactory.OpenSample` gives one back as a new project with an empty history.
The shell opens `castle` when `IsFirstRun` is true, **untitled and clean** —
closing it asks nothing and saving asks where, so the shipped copy is never
written over — and frames it on the first tick, the view a new document starts
on being pitched at an empty room's floor. `new sample=castle` is the same door
for an agent. A sample is generated, not edited by hand: `tools/samples/` holds
the script that builds it.

**The client config is the connection.** `McpClientConfigWriter` writes the same
`mcpServers` shape Beckett drops into a project, with the bearer token filled in,
so connecting an agent is pointing it at a file. It **merges** rather than
overwrites: the obvious place to point it is a repository whose `.mcp.json`
already names other servers. It is written only once the listener is actually
up — a config naming a port nothing is listening on turns a visible failure into
a silent one.

`PreferenceValues` lives in `CommonTypes` rather than in the preferences layer,
because it crosses the view seam: the settings dialog reports what the user
filled in, and a view knows nothing about where preferences are kept.

## Autosave and recovery

**A recovery snapshot is an ordinary `.vcad` written into the application's own
data directory, and the user's file is never touched by it.** `AutosaveStore`
owns `<user://>/autosave/`: one `.vcad`, one JSON sidecar (the original path, the
time, the original's own modification stamp, the record count, the byte count)
and one lock file per open document, keyed by an id minted when the shell binds
the document. The lock is taken at the first write and held open with
`FileShare.None` for as long as the document stays bound — a document nobody has
edited claims nothing; being held is the whole of "a running editor owns this",
so a crash releases it and the next launch offers the pair. Nothing sweeps the
directory: a scan never deletes, and what is removed is removed by somebody
asking. Every document write is atomic, so a length the sidecar did not record
is never a truncated document — it is a sidecar older than the document beside
it, and the document is offered untitled, as a lone one is; the old sidecar is
removed before the new document lands so that a crash in the window leaves
exactly that.

**The resting invariant: a snapshot exists iff, at the last interval that looked,
the document had unsaved work.** `AutosaveRule.Decide` is that sentence as a
table: on the interval (a preference, 120 s by default), only when the history
record identity moved since the last snapshot — the same identity
`DocumentPresenter.IsModified` is read off, through `RecordIdentity` — and not
while a gesture is in flight or the user is busy, except that a deferral is
bounded at one further interval, past which the snapshot is written through the
gesture without ending it: every edit already went through the model, and a
picker left open must not leave an hour unprotected. A document undone back to
its saved point drops its snapshot. A successful save by any route
(`CommandContext.ProjectSaved`) and the user's own Discard delete it. A document
replaced without either — an agent's `new`, `open` or `autosave-recover` — is
written on the way out and kept: nothing an agent can do on the command surface
loses the user's work, and only the user's Discard deletes.

**The write is two halves on two threads, and the seam is the model's.**
`IProject.Capture` walks the tree and copies the cells — one to three
milliseconds on a model of 179,000 cells — and hands back an `IProjectSnapshot`
that owes the model nothing; `IProjectSnapshot.Write` compresses the blobs,
serializes the YAML and lands the file, which is where a save spends its time,
and it runs on a worker. `IProject.Save` is the same capture written at once.
One write is in flight per document, and every path that acts on the files — a
save, a discard, an unbind — waits for it first, so a stale write can never land
after a delete.

**Every file the model writes is written whole or not at all** (`AtomicFile`: a
sibling in the same directory, flushed, renamed over the target) — the ordinary
save included, because a truncated `.vcad` is the one loss recovery cannot
repair: the snapshot is deleted after a save and holds an older state.

**Recovery opens the snapshot as a modified document under its original path** —
born modified, so the title carries the bullet through every undo until a save
writes where the user meant — unless the original's modification stamp is not
the one the snapshot recorded (read on the main thread at the capture, never on
the worker), in which case it opens untitled and a save asks where: a snapshot
never silently aims a save at a file that moved on after it.
The recovered document adopts the snapshot's id and its lock, so the work exists
on disk continuously until a save replaces it. A snapshot opened as if it were a
document (`open` on a path inside the store) is redirected to a recovery on the
next tick, so a snapshot is never the open document under its own path.

**The offer is one card per snapshot, newest first, Recover / Discard / Later**,
asked on the shell's first tick through the same gate every file operation runs
behind, and again from File → Recover unsaved work…; more than three pending and
a summary card (Review / Discard all / Later) comes first. Later loses nothing,
ever, and a document changing under a card — an agent granted the editor while
it stands — reads as Later. Damaged snapshots are never in the dialog: a
destructive answer does not belong under the key a hand presses to dismiss
things. **The surface**: `autosave-status`, `autosave-now`, `autosave-recover`,
`autosave-discard` (this session's own orphans only — an earlier session's
snapshot is the user's), and `preferences` reports the switch, the interval and
the directory, reading only, on the standing rule for preference writes.

## Deliberately still open

Not decided yet, and none of it disturbs the boundaries above: how picking
reports hits from the viewport, the binary layout inside the voxel blob, and the
command vocabulary itself.

The selection's cardinality used to be on this list and is settled — see **The
selection** above.

