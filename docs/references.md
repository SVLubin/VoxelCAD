# References (canon)

A reference is something somebody else made, standing in the scene so you can
build around it: a 3D model to trace the silhouette of, or a plane wearing a
picture to trace over. It is **placed and never edited**. The document owns where
it stands and how it is drawn; the file it stands for belongs to the disk.

## It is an element, which decides almost everything

Two members of `ComponentKind` — `ReferenceImage` and `ReferenceModel` — fill an
element's single component slot, on exactly the terms `Camera` does. So a
reference has a transform, field modes, a pivot, a name, a hidden flag, a place in
the tree, undo, copy and paste, and it can sit in a group or inside a prefab.
None of that is written for it; it is what being an element already means.

And it **holds no cells**, so everything that walks the tree looking for geometry
passes over it without being told to: the mesher, the exporter's projection and
the render's own compile. That is the whole of "a reference does not leave the
editor" — a property of where it sits rather than a rule anybody enforces.

## The asset is linked, not copied

The component carries a **path**, and the file stays where it is. A `.vcad` is
YAML text and voxel blobs already ride in it as base64; a 30 MB glTF embedded
there would be 40 MB of text in a document that is opened by reading the whole
string. A reference is also scaffolding one throws away, and one that follows
edits made in another application is worth more than one frozen at import.

- **In memory the path is absolute.** The *file* carries it relative whenever the
  asset sits under the project's own directory, and absolute otherwise. Only the
  storage layer knows where the project file itself is, so only the storage layer
  relativizes on write and resolves on read.
- **A missing asset is a state, not a failure.** The element stays, keeps its
  transform and its parameters, draws as a placeholder, and says so in the
  inspector, on its row and in `references`. Re-pointing it at a file is an
  ordinary edit.
- **A clip carries the path**, so copying a reference between documents works with
  nothing added — the one thing link-by-path gives away for free.

## What a picture is measured in

The **size in cells is the truth** and is what the file stores. Beside it the
inspector shows **pixels per cell**, which is that size divided by the image's own
resolution, and writing into it rewrites the size; a **1:1** action sets one image
pixel to one voxel, which is the whole point for pixel art.

Two rules keep 1:1 honest rather than approximate. The quad spans **from the
element's local origin** rather than being centred on it, so an Integer position
puts its corner on the cell lattice and pixel centres land on voxel centres — and
that holds however the pivot is placed, because quantisation acts on the stored
origin rather than on the reported position. And the pivot defaults to the plane's
middle, inside the import's own record, the way a model's first voxel edit gives it
one: the grip belongs on the picture, and a quarter-turn about its bottom corner
would swing the whole drawing out of the room.

**Resizing does not move the pivot**, so the offset the inspector shows re-labels
itself over an unmoved grip — the same honesty painting already shows on a voxel
model, and for the same reason: the position channel *names* the pivot, so
re-centring it would change where the plane reports itself to be. The Centre button
puts it back.

**What the document promises about a picture is its resolution, not its size.**
`set-reference path=…` measures the file that is arriving, and leaves the size the
caller asked for exactly as asked. What re-fits a re-pointed plane is the command
above the model: with nothing said about size it keeps the width and follows the new
picture's proportions, so a reference never comes back stretched, and where the two
pictures share a shape nothing moves at all.

The resolution is read from the file's own header by
`ImageHeader` — a few dozen lines of PNG, JPEG, WebP and BMP header
parsing in the model, deliberately **not** the engine's image loader, because the
headless CLI has no engine and a plane whose size nobody could compute would
default to a number nobody chose. What is stored beside the size is the last known
resolution, so the readout survives the file going missing.

A 3D model is loaded at **one file unit to one cell** and scaled by the element's
own `Scale` channel, which defaults to `Float` for a reference the way a camera's
position does — Integer scale cannot express the 0.4 a two-metre glTF character
needs to become sixteen cells tall. Nothing about the model's geometry is known to
the model layer, which holds a path and parameters and nothing else.

**"Fit to N cells" is deliberately absent**, and the reason is that seam: the only
layer that knows how big the loaded file is is the one that loaded it, so the
convenience costs a new view-to-presenter question for a number the Scale channel
already expresses. Framing one (`F`, `focus`) does work — the viewport measures
whatever a reference draws — so a model that arrives two cells tall is found rather
than hunted for, and then scaled by the handles or by typing.

## How it is drawn

- **Unshaded by default**, with a `Lit by the scene` checkbox per reference. A
  photograph already carries its own light and a blueprint reads worse under a key
  light; a 3D reference sometimes reads better with one. This is a deliberate use
  of an unshaded material and not the fake the sacred rules forbid: nothing about
  the *voxel* shading model is being imitated or worked around, and the user chose
  it explicitly.
- **Neither casts nor receives shadows**, either way round. A reference is
  furniture, on `SceneBox`'s own terms.
- **Opacity is per instance** — `GeometryInstance3D.Transparency` — so a model's
  own materials are never touched, copied or overridden to fade it.
- **A model may be drawn as a wireframe**, which is an edge mesh built from the
  triangle indices rather than a render mode: Godot 4 has no per-instance
  wireframe. There is no x-ray mode; depth is ordinary, what is nearer is what is
  seen.
- **A model wears its own materials and textures**, so OBJ carries MTL and the
  images beside it, glTF carries its own, and STL has none by definition.
- **An image plane is double-sided** and honours the image's own alpha. Filtering
  is nearest or linear, a checkbox, because a pixel-art reference smoothed into
  mush is the first thing that goes wrong.

## What it is not

- **Not exported.** No OBJ, STL, FBX or `.vox` sees a reference; it is not the
  model, it is what the model was traced from.
- **Not in the traced render.** The tracer marches voxel grids and has no
  triangle intersector at all. Putting references in a render is a feature of its
  own, not a line.
- **Not clickable in the viewport.** Selection is the project tree's, as a
  camera's is today. A cell tool's ray passes through a reference as if it were
  not there, so the brush paints over a silhouette rather than fighting it — which
  is what a reference is for, and why this is a property rather than a shortfall.
- **Not editable.** No vertex, pixel or material of the asset is touched by this
  editor, ever. Everything that can be done to it is done to the *element*.

## Hiding them all at once

Whether references are drawn is **session state, not document data**: it is not
written to the file, leaves no history record, and does not touch any element's
own hidden flag, so putting them back shows exactly what was shown before. One
switch for the whole scene — a toggle in the toolbar, beside the per-element eye
every element already has — and it reports rather than acts, so the button and the
command surface's own switch are one answer.

## The command surface

Reading is half of it, and a reference's product is a picture, so reading means a
picture.

| Command | Does |
|---|---|
| `import-reference` | Path, kind derived from the extension, optional parent and name. One record. |
| `set-reference` | Opacity, lit, filtering, wireframe, size in cells or pixels per cell, and re-pointing the path. |
| `references` | Every reference in the scene: path, whether the file is there, resolution or bounding box, and the parameters. |
| `reference-preview` | The image itself, or a captured view of the model — the material rule, one component further on. |
| `references-visible` | Whether references are drawn at all. |
| `set-references-visible` | The global switch above. Editor-only, and gated on holding the editor: it changes what the user is looking at. |

Everything else a reference needs is already there: `set-transform`, `set-pivot`,
`set-visible`, `rename-element`, `delete-element`, `reparent-element`,
`duplicate-element`.

## Where the decisions live

- **Model** — the path, the parameters, the undo, the file, and the image header
  read. No engine, so all of it is tested without one.
- **View** — decoding and caching, keyed by path so two elements pointing at one
  file load it once; the nodes; the wireframe mesh; the placeholder.
- **Presenter** — the inspector's rows, the import flow through `IDialogService`,
  the dropped-file route, and the global switch.

**Which reference an element is, is one push.** `ISceneView.SetElementReference`
carries the picture, the model and "neither" at once. As two calls it was a bug
waiting: an element carrying a picture was pushed a picture and then pushed "not a
model", and the second tore down the node the first had built — which no test could
see, a fake recording what it is told rather than the order two truths arrive in.

## Deliberately deferred

The **orthographic reference mode** — a view locked square to a plane so a picture
is traced pixel for pixel on screen — is what the size-in-cells rule and the
lattice-aligned corner exist to make possible, and is its own piece of work.
