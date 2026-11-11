# Import (canon)

Somebody else's voxel file read into this document. Two formats: MagicaVoxel
`.vox` and Qubicle Binary `.qb` — between them, the files the voxel world
actually holds.

**An import is an edit, not an open.** What arrives is ordinary elements with
ordinary cells, wearing ordinary materials, in the scene that was already there.
The file is *read*, not linked: editing it elsewhere afterwards changes nothing
here. That is the opposite of a reference (`docs/references.md`), and the two are
opposite on purpose — a reference is scaffolding you trace over, an import is
your model now.

**It is one undoable record.** Elements, cells and the materials the cells wear,
all in one, so a user who did not mean it has one undo to press and a redo that
brings the same ids back. It is a history batch opened inside the command's own
execution — the canon's "local gesture with a guaranteed close", not a scope held
across round trips.

**And a refused import leaves nothing behind, which is structural rather than
checked.** Most of what can go wrong is over before the batch opens: a reader
either produces a whole `ImportedFile` or throws, and that is what `IImportPlan`
is — the read file, checked over, waiting. But not all of it, because the caller
names the parent, and a parent that refuses children (prefab contents, an
instance, an id that has gone) refuses when the first element is created. A batch
can be closed and never discarded, so one that threw part way would file a
half-finished import as a record and leave its materials in the registry.

So the elements are created **first**, before a single material: the one thing
left that can fail is the batch's first act, and a batch that collected nothing
leaves no record. Materials come second, because the cells name them; the cells
come last. Three passes over a handful of nodes, for a property worth more than
the pass.

## The shape of the layer

The mirror of the export side, and deliberately its shape:

```
IImporter.Read(path)     →  IImportPlan          the reader, which knows a format
IImporter.Apply(plan, …) →  ImportResult         the projection, which knows the document
```

`ImportProjection` is the one file that knows `IProject`/`IScene`, exactly as
`ExportProjection` is on the way out. Readers see bytes and produce the IR;
they never see a document. Adding a format touches its own reader and one line
of `ImportRegistry`.

Reading and applying are two steps because two callers need them apart. Opening
a file as a new document has to size the room before there is a scene to import
into, and it cannot size it without knowing how big the model is.

## Placement is done in cell indices

Every transform either format states is a **signed permutation and an integer
offset** — a set closed under composition. `CellTransform` is that, and the
whole placement is composed in it. Points are avoided because a half-cell
carried through a rotation stops being a half-cell: `R · (½,½,½)` has components
of either sign. So the half from a cell's corner to its centre is introduced
exactly once, in `ImportPlacement`, where a cell index becomes an element's
local origin — and it is always a whole number of cells there.

That is what buys the property worth stating plainly:

> **An imported element lands on `Integer` position and `Snap90` rotation
> exactly. No placement either format can state degrades a field mode.**

That is a property of the arithmetic *and* of the order the channels are written
in, and the second half was learned the hard way. A rotation written against a
pivot is **anchored** — the model re-solves the origin so the pivot does not move
— out of a basis rebuilt from Euler angles, so the answer carries a float error
that grows with the pivot's distance from the origin. Past the model's own
tolerance the position channel degrades to Float in silence and the cells stop
sitting on the world's grid. **A model of 44 cells a side was enough.** So the
rotation is written first, while the element has no pivot to anchor to and there
is nothing to re-solve; the position is written last, and a position write
quantises rather than anchoring, so it absorbs the error instead of tripping over
it.

Held by asking the document, not the arithmetic: all twenty-four rotations over
five grid-and-translation pairs up to the format's own 256-cell ceiling, asserting
the modes survived, that every cell corner is on the world grid, and that the turn
the element took is the one the file asked for.

### Where MagicaVoxel puts a model

```
world cell = t + R · (v − c),    c = floor(size / 2) componentwise
```

`v` is the voxel's index in the model's own grid, `t` and `R` the `_t` and `_r`
of the transform node above it, composed down the chain. **The specification does
not state this**; it was measured against MagicaVoxel's own files. In `cars.vox`
the two cars have grids 13 and 14 deep and both land with their bottom exactly on
the ground, which is only true of integer division — an odd side and an even one
in the same file.

### The `_r` byte, and why row-major

Bits 0–1 give the column of the non-zero entry in the first row, bits 2–3 the
column in the second, and the third row takes what is left; bits 4, 5 and 6 are
the three signs. Bit 7 is unused, so every matrix has two encodings.

Read the other way round the same eight bits give the **transpose**, which for a
quarter turn is the opposite turn. Both readings stand a model upright, so a
symmetric subject cannot tell them apart — which is how implementations come to
disagree about it quietly. What separates them is where the model's *grid* lands:
MagicaVoxel stands a model on the ground. `Gomez.vox` carries `_r = 40` on a flat
sprite; row-major puts its grid on the ground, column-major eleven cells above
it. Row-major it is, and the spec's own wording agrees.

The byte can also encode 24 **reflections**, which MagicaVoxel does not write and
the format allows. A reflection on a *shape* is folded into the model's cells and
the transform keeps the proper rotation that is left — the model lands exactly
where the file asks, and cannot be un-mirrored by editing the transform
afterwards. A reflection on a **group** is refused by name: there are no cells to
fold it into.

### The axis convention is one answer, not two

`VoxAxis` holds both directions of the Y-up ↔ Z-up turn, and the writer and the
reader both go through it. Two copies of a convention are two chances to
disagree, and a disagreement here is a model that comes back mirrored from its
own file. It is a proper rotation, so nothing is flipped; the `−1` in the index
form is what makes it an index map rather than a point map, since a flipped axis
sends the cell at `k` to the cell at `−k−1`.

Qubicle is Y-up like this document, so nothing turns. Its one axis question is
the header's handedness flag — see below.

### The scene graph keeps its shape

A MagicaVoxel group becomes a grouping element holding its parts, transform and
all. Composing per node and composing the whole chain down to each shape were
checked to give the same cells over the whole corpus.

Several transform nodes may point at one shape node: that is how the format
states an **instance**. Each arrival becomes its own element — an independent
copy. Mapping them onto this editor's prefabs is a decision of its own and is
deliberately not taken here.

An **animation** — a shape node naming several models, or a transform node with
several frames — is read at its first frame, and the import says so in its notes.

### Where an import lands

**With its own corner on the scene's origin.** A file may place its models
anywhere, including well into negative cells, and a model arriving outside the
room reads as a broken import. The offset is one translation over the whole
import, so nothing moves relative to anything else — which is the half that has
to be exact.

Several top-level parts go under one grouping element named after the file, so an
import is one thing in the tree and one thing to move. A file holding a single
part needs no such wrapper and does not get one.

**Two imports into one document therefore stand on each other**, both corners on
the origin. That is deliberate on the same grounds a duplicated element lands
inside its original: an offset would be a number nobody chose, and the import is
one element, already selected, already one drag from anywhere.

Names are the file's own where it gives them, and the file's own name where it
does not; a name already taken grows the next free number from its own trailing
one. That matters more for materials than for elements: two materials sharing a
name make either of them unaddressable by name for the life of the document.

## Materials

**Only the palette entries some cell actually wears become materials.** A palette
is not a scene. MagicaVoxel stores 256 colours whether or not anything wears them
— a tank makes fourteen, a sheet of unit sketches thirteen, a heavily painted
sample a hundred and nineteen, never 256.

### The trap

MagicaVoxel writes a `MATL` chunk for **all 256 entries**, whether or not anybody
touched one, and it writes keys that its own type does not admit. `teardown.vox`
carries `_alpha 0.5` on all 235 of its plain diffuse materials. A reader that
trusts a key because it found it turns that file half transparent.

So **every key is gated on `_type`**, and which keys a type admits is not guessed
— MagicaVoxel ships its own material panel as a text UI definition, and it says:

| MagicaVoxel type | what its panel offers |
|---|---|
| `_diffuse` | nothing |
| `_metal` | Roughness, IOR, **Metallic** |
| `_glass` | Roughness, IOR, **Transparency**, Density, Phase |
| `_emit` | **Emission**, Power, Ldr |
| `_blend` | all of the above |
| `_media` (Cloud) | Density, Phase |

A material with no `_type` at all is diffuse — MagicaVoxel 0.99.7 writes none for
an ordinary one.

### The table

| MagicaVoxel | ours | rule |
|---|---|---|
| `RGBA[c−1]` | `Color` RGB | colour index `c` reads the entry before it; the format's own note |
| `_rough` | `Roughness` | **only** for `_metal`, `_glass`, `_blend`; halved on the way in |
| `_metal`, or `_weight` | `Metallic` | `_metal` and `_blend` |
| `_emit`, or `_weight` | `Emission` | `_emit` only |
| `_trans`, else `_alpha`, else `_weight` | `Transparent` + `Color.A` | `_glass` and `_blend`; **`A = 1 − t`** |
| `_flux`, `_ldr` | dropped | Power is a decade; ours is one dial clamped to 0..1 |
| `_ior`, `_ri`, `_spec`, `_sp`, `_att` | dropped | we have no index of refraction and no separate specular lobe |
| `_d`, `_g`, `_media`, `_media_type` | dropped | no participating media at all |
| `_plastic`, `_spec_p`, `_g0`, `_g1`, `_gw` | dropped | legacy or undocumented |
| — | `Finish`, `FinishScale`, `FinishRelief` | always default; MagicaVoxel has no factures |

Two of these are worth their own sentence.

**A diffuse material's `_rough` is not read at all.** MagicaVoxel offers no
roughness for one, so the `0.1` on every untouched entry is a default nobody
chose — and ours is a single GGX dial on which 0.1 is very nearly a mirror. Read
literally, every stock MagicaVoxel file would arrive glossier than this editor's
Steel preset. Where roughness *is* read it is halved, which is a borrowed visual
calibration rather than a measurement of ours: MagicaVoxel's slider at its top
does not look like our chalk.

**`_alpha` is transparency, not opacity.** MagicaVoxel's own panel labels the
dial Transparency, so what this document stores is its complement. The value
everybody tests with is 0.5, which is its own complement — which is how three
independent implementations came to disagree about it without noticing. A fully
transparent material keeps the last two per cent of its opacity: faithful would
be invisible, and an invisible import reads as a broken one rather than as glass.

`_weight` is the older spelling of whichever strength the type names.
MagicaVoxel up to 0.99.5 wrote it; later versions gave each effect its own key.
Measurable on one file saved by both: `room.vox` says `_type _emit, _weight 0.23`
in one version and `_emit 0.23` in the other.

**It is a fallback for the key its type names, never a value in its own right**,
and the difference between those two readings is expensive. `_blend` is the
modern general type whose dials say everything, so a `_blend` carrying no
`_metal` means *not metal*; `_metal` is the other way round, because in that
older spelling naming the type was the only thing there was to say. Read the
absent key as full strength and every `_blend` material becomes a mirror —
measured on MagicaVoxel's own material showcase, where all three plainly
dielectric samples and all four glass ones came in fully metallic.

### Which colour a `MATL` describes

Its id, read as the colour index directly. The format is ambiguous about this —
older files number their material chunks 0..255 and newer ones 1..256, and both
spellings agree on 1..255, which is every index a voxel can actually wear. The
two ends are the unusable ones: index 0 is the format's word for empty and 256
cannot fit in a voxel's byte, so neither is ever resolved.

The ambiguity is therefore real in the format and unobservable in practice, and
that is measured rather than assumed: across the whole reference corpus, **every
file carrying a material anybody edited numbers from one**, and every file that
numbers from zero carries nothing but MagicaVoxel's untouched defaults.

### The colour's own alpha

- **A file with `MATL` chunks**: the alpha is **ignored**. MagicaVoxel always
  writes 255 and means nothing by it.
- **A file with none**: the alpha **is** the opacity. That is the only thing
  that could carry one, and this editor's own `.vox` writer puts one there — so
  ignoring it would break a round trip through our own format.

### Qubicle has no materials

A `.qb` stores four bytes per voxel and no palette at all. Colours are
deduplicated **exactly, across the whole file** — a Qubicle model is one object
split into named part matrices, and deduplicating per matrix would make
seventeen copies of one skin tone — and each becomes a material named `#RRGGBB`.
The name is the identity, and it is unique by construction. Past 1024 distinct
colours the file is refused with the count: a file painted from a photograph or
a mesh has no small set of materials to make.

**The alpha byte is never an opacity.** With the visibility mask off it is 0 or
255; with it on it is a bitmask of which sides Qubicle would draw. The
specification's own test is `mask == 0` for an empty voxel, so anything non-zero
is a filled cell and nothing else — every material a `.qb` produces is opaque.

### Handedness

The `.qb` header says whether the file is left- or right-handed. Right-handed
matches this document and nothing turns; left-handed measures depth the other
way, so the whole placement is mirrored — the matrix's position with it, not only
the cells inside it. Mirroring inside each matrix instead would shuffle the parts
of a model along the depth axis the moment it has more than one, which is exactly
what a seventeen-part figure has.

This is the one rule here **not** settled against a file whose contents are known
independently. It follows the specification's wording and matches Goxel's reader,
which adds the position first and turns the axis afterwards; six real Qubicle
files agree cell for cell with a reference implementation that does the same. The
cross-format check — one model exported from MagicaVoxel as both `.vox` and `.qb`
— is what would settle it against ground truth.

## The routes

- **`import path=… [parent=…]`** — into the open document, under the scene root
  or under a named element.
- **`import-formats`** — what can be read.
- **`open path=…`** on a voxel file — a new document instead: sized to what
  arrived, never smaller than a default room on any axis, and **untitled**, so
  Save asks where rather than writing this editor's format over somebody else's
  file.
- **File → Import…**, in the platform's menu bar and in the in-window card.
- **The Open picker** offers the voxel formats beside `.vcad`, because that is
  the gesture somebody arriving from MagicaVoxel reaches for first.

Both pickers take their formats from the command surface, not from the format
layer: the shell is not entitled to say what this editor can read.

## Refusals

Named, and never half an import. Everything is read and checked before the
document is touched, so a refusal leaves it exactly as it was — including
`open`, which does not replace the document until the file has been read.

**And there is a ceiling on how much an import will make**, counted across
everything in the file rather than per model, because a per-model bound is no
bound at all — a file may hold as many models as it likes, and a run-length
stream states a filled million in twelve bytes. Two kilobytes of Qubicle can
otherwise ask for tens of millions of cells and be given them. The ceiling is
256³, the largest volume either format calls a single model, which is the one
number in the neighbourhood that is not arbitrary; what it costs is measured
(about 320 bytes a cell, from 0.14 GB of process for a 20-cell model against 1.33
GB for the corpus's largest at 3,958,985), so raising it is a decision with a
figure attached.

What is refused: a file that does not begin with the format's magic; a truncated
one, at any point (swept, byte by byte, in the tests); a chunk declaring more
content than the file holds, including a length that would only fit by wrapping
a 32-bit offset; chunks or a scene graph nesting deeper than a model could; a
graph naming a node it never defines or referring back to itself; a shape naming
a model the file does not hold; a mirrored group; a `.qb` matrix with a zero
side, a size too large to be a model, a run longer than its slice, or more
colours than the cap. A chunk this reader has never heard of is **not** refused —
it is stepped over by its declared length, which is what a length-prefixed format
is for.

Two of those bound a **recursion** rather than a value, and they are the reason
this section says "refused" rather than "handled": both walks over a `.vox` are
recursive, and fifty thousand nested chunks or nodes took the process down with a
stack overflow before the bounds went in. A crash is not a refusal. The limits
are generous by two orders of magnitude — MagicaVoxel's own graphs are four deep
and only `MAIN` ever holds chunks — and a test asserts an ordinary depth still
reads, so they cannot quietly tighten onto real files.

## What this deliberately does not do

- **No prefabs from instances.** A shape reached twice becomes two elements.
- **No animation.** First frame, and it says so.
- **No `.gox`, no `.qbt`, no `.vxl`.** Two formats cover the market.
- **No import of layers, cameras, render settings or notes.** `LAYR`, `rOBJ`,
  `rCAM`, `NOTE`, `IMAP` and `META` are stepped over. `IMAP` is worth one
  caveat: it re-orders how MagicaVoxel *shows* a palette, so a material called
  `Palette 014` may not be the 14 the user saw.
- **No file drop.** Dragging a `.vox` onto the window does nothing; the drop
  path is the reference importer's.
- **Nothing is written back.** Export already writes `.vox`; it emits no `MATL`,
  so a transparent material of ours does not show as transparent in MagicaVoxel.
  That asymmetry is the exporter's to fix and is recorded in the ledger.
- **A file can import and then refuse to export.** The writer merges a whole
  scene into one model, so a `.vox` whose parts are spread further than 256 cells
  apart comes in whole and cannot go back out — MagicaVoxel's own material
  showcase is exactly that, 342 cells wide once its twelve models are placed. The
  import is right and the refusal is the exporter's own, named and already in the
  ledger: splitting a scene into several placed models is not built. Worth knowing
  because "open it and save it back" is the first thing anyone tries.

## How it is verified

The reading is held by tests over files built byte by byte, so each states the
file it is about. The *placement* is held by something stronger, because a
convention cannot be checked by reading either half of it:

- **Round trip.** A model written to `.vox` and read back, with the cells
  compared in world coordinates. That is what locks the axis convention.
- **The corpus.** Every single-model file in a corpus of real MagicaVoxel files
  round-trips through export and back with an identical cell set and identical
  colours per cell — up to `monu1.vox`'s 156,942 cells, with a re-exported
  file coming back the same size to the byte. Every multi-model file agrees cell for cell
  with an independently written reference implementation, including nested
  groups carrying transforms and 104 shapes reached by shared nodes.
- **Qubicle.** Six real `.qb` files agree the same way, covering both handedness
  settings, both compression modes and the visibility mask on and off.

- **Driven**, on the running editor: the file imported, `slice` read back
  against the palette, one history record, undo taking the cells and the
  materials together, redo bringing them back, and a second import landing
  beside the first.

The corpus lives outside the repository — those files belong to other people —
so the committed tests are the byte-built ones, and the corpus runs are how a
change to any of this is checked before it ships.

## What it costs

Measured through the headless CLI, warm, wall clock for the whole process
including its own start-up:

| file | cells | |
|---|---|---|
| a MagicaVoxel scene | 161,547 | **0.60 s** |
| a Qubicle file | 3,958,985 | **1.93 s** |

Four million voxels in under two seconds is not a number that needed defending,
so nothing here is tuned for it. What keeps it there is one rule: **the cells of
a model go in as a single `SetVoxels` call.** A cell at a time would remesh the
model once per voxel, which is the difference the ledger already records as 400
milliseconds for placing one voxel. Nothing else about the import is on a hot
path, and the numbers above are headless — in the editor the view work of
receiving a large model dominates, and that is the viewport's cost rather than
the reader's.
