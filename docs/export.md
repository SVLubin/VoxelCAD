# Model export (OBJ, STL, FBX, MagicaVoxel)

Status: **complete** — all six stages are done and committed: 0 (IR + projection
+ registry + commands), 1 (OBJ + `.mtl`), 2 (binary STL), 3 (MagicaVoxel `.vox`),
4 (ASCII FBX 7.4) and 5 (GUI `File → Export…`). This document is the agreed plan;
the code implements it, and where the two disagree it is the code that is wrong —
the same standing `docs/architecture.md` has.

## 1. Goal and governing principle

Give VoxelCAD export to four external formats — **OBJ**, **STL**, **FBX**, and
**MagicaVoxel `.vox`** — such that:

- export is **reachable from the command dispatcher** (therefore from the
  headless CLI and from an agent), not only from the GUI. This is the canon's "a
  feature is not finished until it is reachable from the dispatcher";
- **a change to the editor or model does NOT force rewriting the serializers.**

The second requirement is central. It is met by a single architectural move: a
stable **intermediate representation (IR)** sits between the model and the
formats. Serializers know only the IR; they never see the model. Knowledge of the
model lives in exactly one place — the **projection `model → IR`**. Change the
model and only the projection changes. Add a format and only one writer class is
written, touching nothing around it.

```
IProject / IScene / IHierarchyElement / IVoxelModel   ← only the projection knows these
                 │
         ExportProjection  (the single point that knows the model)
                 │
                 ▼
        ┌──────  IR (stable contract)  ──────┐
        │  Facet A: ExportMesh   (triangles)  │
        │  Facet B: ExportVolume (voxel grid) │
        │  Shared:  ExportPalette, units      │
        └────────────────┬────────────────────┘
                         │  writers see ONLY this
        ┌───────┬────────┼────────┬────────────┐
       OBJ     STL      FBX      VOX         (future)
```

## 2. Assemblies and layering

A new assembly **`VoxelCAD.Export`** (`Microsoft.NET.Sdk`, `EnableDynamicLoading`,
a `GodotSharp` PackageReference for `Vector3`/`Vector3I` only — engine-free, holds
no Godot nodes).

| Assembly | May reference |
|---|---|
| `VoxelCAD.Export` (new) | `CommonTypes`, `Model` |
| `VoxelCAD.Commands` | `CommonTypes`, `Model`, **`Export`** (new edge) |

The `Commands → Export` edge is legal: `Export` references only `Model` +
`CommonTypes`, so the console transport (which reaches only `Model`/`CommonTypes`
through `Commands`) never pulls in `Presenter`/`View`/Godot nodes. **Export is
entirely headless** — FBX/STL/OBJ/vox can be produced from `VoxelCAD.Cli` with no
engine. That is "the agent does what the user does" made literal.

The dependency table in `docs/architecture.md` gains an `Export` row in the same
change — this is a canon edit.

**On `VoxelMesher` in `View`.** Its pure culling predicate
(`CollectVisibleFaces`) is engine-free, but `View` may reference only
`CommonTypes` per canon, so it cannot be reused from `Export` (logic in
`CommonTypes` is forbidden). The jobs also differ: `View` meshes **locally**
per element (the `Node3D` tree composes world transforms for display), whereas
`Export` must bake **world** geometry into one file. `Export` therefore has its
own mesher working in world space. The ~15-line culling predicate is duplicated;
this is deliberate minor debt, recorded in the ledger.

## 3. Intermediate representation

Two facets, because the formats split into two families with irreconcilable
demands.

**Shared — `ExportPalette`:** an ordered list of
`ExportMaterial { Guid Id, string Name, Color Rgba }`. Indices are assigned
stably (the order of `IProject.Materials`). Units: 1 voxel = 1 world unit, with a
configurable scale factor (for STL that is millimetres — printing).

**Facet A — `ExportMesh` (for OBJ / STL / FBX).** World-space, welded, indexed
geometry grouped by material:
- `IReadOnlyList<ExportSurface>`, where an `ExportSurface` is one material:
  `Vector3[] Positions`, `Vector3[] Normals`, `int[] Indices` (triangles),
  `int MaterialIndex`;
- per-element grouping metadata (element name → index range) so OBJ can emit
  `o`/`g` and FBX can emit per-element `Model` nodes;
- built by a mesher parameterised with a **culling policy** (see §5).

**Facet B — `ExportVolume` (for MagicaVoxel).** A set of world-space integer
"bricks":
- `ExportBrick { Vector3I Origin, Vector3I Size, byte[] PaletteIndices }` — a set
  of bricks covers both multiple elements and the split of volumes larger than
  256³;
- palette is the shared `ExportPalette`, mapped to ≤255 colours;
- valid only for axis-aligned elements. The projection separately reports which
  elements it could not bake exactly (see §8, `.vox`).

## 4. The projection `model → IR` (the heart of the decoupling)

`ExportProjection` is the only class that knows
`IProject`/`IScene`/`IHierarchyElement`/`IVoxelModel`. It:

1. walks the chosen scene's tree, composing world transforms via
   `TransformHelper`;
2. reads each `IVoxelModel`'s cells;
3. assigns palette indices from `IProject.Materials`;
4. builds the requested facet (the mesher for mesh formats; grid baking for voxel
   formats).

Writers **never** touch `IProject`/`IScene`. That is the "do not rewrite the
serializers" guarantee: a model change lands in the projection (one file per
facet), and the formats are untouched.

## 5. Culling policy and the canonical coordinate frame

The mesher is parameterised by a policy:
- **Visual** (opacity-aware, for OBJ/FBX) — as `VoxelMesher` does today: a face
  between two differently transparent materials is kept so colour reads
  correctly;
- **Solid** (the "filled/empty" boundary, for STL/printing) — opacity ignored,
  only the outer boundary faces of the body are emitted, giving a watertight
  shell fit for 3D printing.

The IR fixes **one** canonical coordinate frame (right-handed, Y-up, CCW front
face — the neutral/OpenGL convention). Each writer applies its own axis
conversion for its format's convention. Winding correctness is decided in one
place, not four.

## 6. Format registry and writer interface

```csharp
internal interface IExportFormat
{
    string Extension { get; }      // ".obj"
    string Label { get; }          // "Wavefront OBJ"
    ExportFacet Facet { get; }     // Mesh | Volume
    void Write(ExportModel model, Stream output, ExportOptions options);
}
```

`ExportRegistry` maps an extension to its format. The assembly's single public
factory (canon: "one public factory per assembly, for roots only") exposes the
projection + registry as one entry point.

## 7. Command surface

In the **base** `CommandCatalog` (engine-free, therefore in the CLI too):

- **`export`** — `mutates: false`. The document does not change; this is a read
  that produces an external artifact — the same status as `capture`, which writes
  a PNG. Parameters: `path` (required; the extension picks the format), `scene`
  (optional; defaults to the active scene), and format options (`scale`,
  `encoding` ascii/binary where a format has both, a culling-policy override).
- **`export-formats`** — a query listing the available formats (extension, label,
  facet), so an agent can discover them. Reading is half the surface.

**GUI:** a `File → Export…` item (native file dialog, filters drawn from the
registry) invokes the **same** `export` command through the dispatcher. There is
no separate GUI export logic.

## 8. Per-format notes

**OBJ (`.obj` + `.mtl`)** — the reference mesh exporter, written first. Text:
`v`/`vn`/`f`, `usemtl`, `o`/`g` per element; a companion `.mtl` with `Kd`, `d`
(opacity), `illum`. Y-up right-handed, CCW, 1-based indices. Visual policy.

**STL (`.stl`)** — binary (80-byte header, `uint32` count, 50 bytes per
triangle). No materials, no shared vertices, one normal per facet. **Solid**
policy (watertight for printing), scale in millimetres.

**MagicaVoxel (`.vox`)** — Facet B. RIFF-like: `VOX ` + version 150, a `MAIN`
chunk holding `SIZE`+`XYZI` per model and an `RGBA` palette (256 entries). MV is
**Z-up**; `XYZI` stores x,y,z,index (1..255). Volumes larger than 256³ and
multiple elements become several models placed via `nTRN`/`nGRP`/`nSHP`. Requires
axis-aligned placement (integer position + Snap90). **A non-axis-aligned
(rotated or scaled) voxel element is refused with a clear message naming the
element** — resampling is deferred past stage 1. More than 255 materials is out
of scope for stage 1.

**FBX (`.fbx`)** — the hardest and highest-risk format. **Hand-rolled ASCII FBX
7.4 (7400) in pure C#**, no third-party library — chosen for portability (no
per-platform native binaries), a clean license, and auditability, which suits an
open-source project; the .NET FBX-writer landscape offers no strong pure-managed
alternative. Structure: `Objects` → `Geometry` (Vertices, PolygonVertexIndex,
LayerElementNormal), `Model`, `Material` (Lambert/Phong: Diffuse + Opacity),
`Connections`. `GlobalSettings`: UpAxis=Y, units. Binary FBX (deflate arrays,
footer) is deferred.

**glTF 2.0 / GLB (future).** Noted, not scheduled: an open, well-specified
format, trivial to write in pure C#, with first-class support in
Blender/Unity/Unreal. In this architecture it is simply another Facet A writer
(~a day), and is an attractive open-source-friendly alternative to FBX.

## 9. Tests

A new project **`VoxelCAD.Export.Tests`** (`net8.0`, `RollForward=LatestMajor`,
like the rest — no engine needed):
- projection: baking world transforms, palette assignment, both culling policies,
  the report of non-axis-aligned elements;
- per format: golden-file tests (a small model → known bytes/text);
- round-trip where practical (parse our own OBJ/`.vox` back and compare).

All six existing suites plus this one run in seconds.

## 10. Stages

0. IR + projection + registry + `export`/`export-formats` commands + scaffold
   tests.
1. **OBJ** (+ `.mtl`) — the reference, colours, Visual policy.
2. **STL** (binary, Solid policy) — geometry, printing.
3. **MagicaVoxel `.vox`** — Facet B, palette, axis-alignment, refusal path.
4. **FBX** (ASCII 7.4) — the hardest.
5. **GUI `File → Export…`** over the command.

## 11. Decisions taken

- FBX: hand-rolled ASCII FBX 7.4 in pure C#.
- Default export scope: the active scene; `scene` overrides.
- `.vox` with a non-axis-aligned voxel element: refuse with a clear message in
  stage 1; resampling deferred.

## 12. Implementation notes (decided while building; revisit if you disagree)

These are shape choices made under the spec's "where a detail is unspecified,
choose the simplest option" rule. None changes the architecture; all are cheap to
revise.

- **`ExportSurface` carries the element name as well as the material index.** The
  spec described a per-material surface plus separate "element name → index range"
  grouping metadata. Instead a surface is one *element × material* run, so the
  grouping is read straight off the surface list — one structure, not two. This is
  exactly what OBJ's `o`/`usemtl` interleaving and FBX's per-element `Model` nodes
  want, and it keeps welding correct (per element and material). If you would
  rather the IR weld globally per material and hold a separate element map, that is
  the alternative.
- **The IR is unscaled; the writer applies `Scale`.** Positions are in canonical
  world voxel units (1 cell = 1 unit, Y-up, CCW). `ExportOptions.Scale` and axis
  conversion are output concerns each writer applies, so the IR stays the single
  stable frame the spec asks for. `Scale` is meaningless for the volume facet
  (an integer grid) and is ignored there.
- **`Model.TransformHelper` is now `public`.** The projection composes world
  transforms with the very same `TransformValues → Transform3D` conversion the
  view uses; a second copy risked drifting from its YXZ Euler order and quietly
  mis-placing baked geometry. Widening a stateless pure-math helper is the
  narrowest fix and is consistent with the style guide (public only because
  another assembly needs it).
- **Volume baking is one brick per element for now.** Splitting on MagicaVoxel's
  256³ limit and placing several models is deferred to the `.vox` writer (stage
  3); recorded in the tech-debt ledger. The brick set is already the right shape
  for the split.
- **FBX is ASCII 7.4, per-element models, hierarchy flattened.** Each element
  becomes its own `Geometry` + `Model` (so it lands named in Blender/Unity), and
  because the geometry is world-baked, every model sits at the origin under the
  root — the group hierarchy is not reproduced. Every palette material is
  connected to every model in palette order, so a polygon's material index is just
  its palette index. Note ASCII FBX does not import into **Blender** (it reads only
  binary FBX); Autodesk tools, Unity and Unreal read ASCII. Binary FBX is deferred
  per the spec, so tests prove correctness by parsing the ASCII back rather than
  through Blender. Two adjacent elements that happen to share a name would merge
  into one model (grouping is by contiguous name) — a negligible edge case.
- **`.vox` merges every element into one model, rotated Z-up.** MagicaVoxel is
  Z-up, so the writer rotates the Y-up world grid with the proper cell rotation
  `(x, y, z) → (x, -z - 1, y)` (a +90° turn about X), which keeps the model
  upright and unmirrored. All bricks are merged into a single model translated to
  the origin, rather than emitted as several `nTRN`/`nGRP`/`nSHP`-placed models —
  simpler, and correct for any scene inside MagicaVoxel's 256-per-model limit. A
  scene larger than that in any axis is refused with its size named, which is the
  same deferred work as the 256³ split. Overlapping voxels from different elements
  collapse, later wins.
- **The GUI export is a thin client of the command surface.** `File → Export…`
  reads the formats from `export-formats` to build the native save dialog's
  filters (one per format), then runs `export path=…` — the shell holds a base
  dispatcher over the same document, ungated because the user always has control.
  It adds no export logic of its own, per the spec. To offer several filters the
  dialog request now carries a `FileDialogFilter` list instead of a single
  filter/label pair; Open and Save build a one-element list. The presenter's flow
  is unit-tested against the fake shell (filters, the export call, the error path,
  cancel, and the stale-document guard); the menu item itself is Godot UI, so like
  every other menu entry it is confirmed by an error-free launch rather than a unit
  test — and the Beckett Lite in this environment cannot open the dropdown to
  screenshot it.
- **A writer is handed an `IExportSink`, not a single `Stream`.** §6 sketched
  `Write(model, Stream, options)`, but OBJ writes a companion `.mtl` beside the
  geometry, which one stream cannot express. So a writer opens named files on a
  sink (`sink.Open(sink.BaseName + ".obj")`, then `+ ".mtl"`), and `IExporter`
  takes a *path* and answers with every `ExportArtifact` it wrote. The exporter
  buffers the whole set and commits it only if the writer returns, keeping the
  "a refusal leaves no file" guarantee across a multi-file export. STL and `.vox`
  will simply open one file each.

## Open questions (for you, in the morning)

- **STL is exported Z-up.** The IR is Y-up; the STL writer rotates it to Z-up
  because a slicer stands a model on the XY bed with Z as height, so a Y-up STL
  imports lying on its side. It is a proper rotation `(x, y, z) → (x, -z, y)`, so
  winding stays outward and the model is watertight either way. If you would
  rather STL keep the IR's Y-up frame and let the user rotate in the slicer, this
  is a one-line change. OBJ stays Y-up (its de-facto convention).
- `export` answers with `scene`, `format`, and a `files` list (each `path` +
  `bytes`) — OBJ reports two files, STL one. It does not yet report surface or
  triangle counts, nor name elements a volume export will skip; say if you want
  those on the payload. (The `.vox` refusal in stage 3 will name the offending
  element in its error regardless.)

## 10. The other direction

`.vox` and `.qb` are **read** as well, into the same assembly and through the
mirror of this pipeline: `IImporter.Read` produces an import IR, and
`ImportProjection` — the counterpart of `ExportProjection` — turns it into
elements, cells and materials as one undoable record. The placement arithmetic,
the complete MagicaVoxel material table with every dropped key and its reason,
and what an import deliberately does not do are canon of their own in
`docs/import.md`.

One asymmetry belongs here rather than there, because it is the **writer's**:

- **`VoxFormat` emits no `MATL` chunk.** It writes each material's colour into
  the `RGBA` palette, opacity included, and says nothing else about it. So a
  material of ours that is declared transparent does not show as transparent in
  MagicaVoxel — MagicaVoxel reads transparency from `MATL`, never from the
  palette's alpha, and always writes 255 there itself. Our own round trip is
  unaffected, because the reader knows that a file with no materials at all means
  its alpha; but a file of ours opened in MagicaVoxel loses the glass.
  <br>The fix is small and self-contained — emit `_type _blend` with `_trans`,
  `_metal`, `_rough` and `_emit` for the materials that need one, and force the
  palette alpha to 255 — and it was deliberately not done alongside the importer,
  so that the round-trip test measures one thing rather than two.
