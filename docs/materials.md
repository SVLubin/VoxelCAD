# Materials: presets and fine tuning (canon)

What a material looks like, where that is stored, and how it is edited. Written
before the work so the decisions are decisions rather than residue; where the
code and this file disagree, the code is wrong.

## 1. The shape of the decision

Three questions were open, and they are answered here.

**Does the document store the choice or the parameters it expands to?**
**The parameters.** A preset is a shortcut that writes several of them at once,
and afterwards it is not referred to again. The alternative — storing "this is
Brushed Steel" and expanding it at read time — makes fine tuning impossible
without inventing a second, overriding layer, and fine tuning is half of what
was asked for.

**Then how does the interface know which preset is selected?** It asks: the
preset whose parameters equal the material's is the one shown, and where none
does the row reads **Custom**. Nothing is stored, so nothing goes stale, and a
material tuned back onto a preset's values shows that preset again.

**Does a preset carry a colour?** Yes, and it applies it. "Brick" that leaves a
material pale grey is not a preset of anything. The colour is one row below and
is changed in a click, so the cost of being wrong is small; the cost of the
other choice is a preset list that does nothing visible.
**But the preset is matched on appearance alone** — recolouring a brick leaves
it Brick.

## 2. What a material is

```
Material = Name + Colour + Appearance
```

`Colour`'s alpha is the opacity, unchanged and still the only opacity there is.

`MaterialAppearance` (CommonTypes) is six numbers and an enum:

| field | range | means |
|---|---|---|
| `Roughness` | 0…1 | 0 a mirror, 1 chalk |
| `Metallic` | 0…1 | 1 tints the reflection instead of the diffuse |
| `Emission` | 0…1 | self-lit strength; 0 is not self-lit at all |
| `Finish` | enum | which facture, `Smooth` for none, `Ripples` for water |
| `FinishScale` | 0.1…16 | voxels one **unit** of the facture spans |
| `FinishRelief` | 0…2 | how much of the facture's own relief is applied |

There is no transparency field and no "kind": **transparency is the colour's
alpha**, which is canon already, and the kinds the spike had — matte, plastic,
metal, glass, frosted, emissive — are exactly points in this space. Glass is a
low roughness and an alpha below one; frosted is the same with the roughness up.
Making them an enum as well would be a second way to say the same thing, and the
one that could not be tuned.

**Water and Mirror are the two presets that are about the render rather than the
surface**, and neither needed a new dial. A mirror is the one metal that goes all
the way to metallic one — everywhere else stopping short of it is deliberate,
since a fully metallic surface has no diffuse term and a face turned from every
light goes black, which is exactly what a mirror does. Water is a smooth
dielectric that lets light through, tinted by what it absorbs, with the swell on
its surface as a facture (`Ripples`); the tracer already reflects a dielectric by
Fresnel and refracts what gets past, so what makes water read as water is being
smooth enough for the reflection to survive and rippled enough for it to move.
Measured on the pool: at roughness 0.03 the water's own surface carries **2.9
times the local contrast** it has at roughness 1, and reads 30% brighter — that
contrast is the reflected scene.

**In the viewport both are simpler, and that is the seam rather than a defect.**
Godot reflects the environment sky, which here is a flat colour, so a mirror is a
grey panel and water is flat cyan with a rippled highlight. Reflection of the
scene itself is what a path tracer is for; the viewport says where things are.

**Metallic is not a switch.** Full metal has no diffuse term, so a face turned
from every light goes black; the metal presets sit at 0.85 for that reason, and
the slider says so by being a slider.

## 3. Presets

A closed list, in four groups, each a bundle of the six values plus a colour.
The list lives in `Model` (`MaterialPresetTable`), because applying one is a
model operation and the command surface and the inspector must not each have
their own copy.

- **Mineral** — Stone, Concrete, Sand, Plaster, Brick, Tile, Mosaic
- **Timber** — Timber, Plank floor, Parquet, Bark
- **Metal** — Steel, Brushed steel, Hammered metal, Tread plate, Corrugated,
  Knurled grip, Gold, Mirror, Copper
- **Synthetic** — Plastic, Rubber, Painted
- **Glass and light** — Glass, Frosted glass, Glass block (wave), Glass block
  (cross), Glass block (bubble), Water, Lamp

## 4. The interface

One section, two depths, chosen by a segmented row and remembered as an editor
preference rather than as document data — it is how somebody likes to work, not
a fact about the model. The colour notation and the preview's fold are stored on
the same terms, and for the render card's own reason: a preference that reset with
the window would have to be chosen again every morning. All three shipped as
session state, which is not what this file said, and the file was right.

```
Material
  [ Basic | Advanced ]
  Preset     [ Brushed steel        ▾ ]
  Colour     ■  [ HEX ▾ ] [ #8A8F99FF        ]
  ── Advanced ────────────────────────────
  Roughness  [——●———————]  0.30
  Metallic   [————————●—]  0.85
  Emission   [●—————————]  0.00
  Finish     [ Brushed              ▾ ]
  Scale      [———●——————]  2.0 cells
  Relief     [—————●————]  1.0
```

**The colour row is one line.** A swatch that opens the picker, a format chooser,
and a single field carrying the value in that format — hex, 0–255 RGB, or HSV.
The picker itself is stripped to the wheel, the alpha and the eyedropper: the
numbers belong in the dock where they can be read without opening anything, and
Godot's own sliders, mode tabs and swatch drawers were the clutter this is
meant to remove. The format is an editor preference, stored like the depth above
it.

**Typing quantises, and that is honest.** A hex field cannot express the float
the wheel produced; a value the user typed is a value the user chose. The field
writes only on commit — Enter or focus out — and a refresh from the model never
round-trips through the text.

**A colour is one gesture from the moment the field takes the keyboard.** Focus
opens it, focus out closes it, and everything between — a wheel dragged across a
gradient, a hex typed and submitted — is one history record. So the document
changes on Enter while the record arrives when the caret leaves, which is right:
what somebody is doing to a colour is one thing they did. What it needs from
everything else is the rule the canon already states — **anything that starts a
new action ends the one in flight first** — and the shell says that for its own
actions while the agent dispatcher had to be taught to. The injected input is
exempt, and has to be: a click, a select-all and a typed value are three commands
making one gesture rather than three actions.

## 5. The command surface

A feature is not finished until it is reachable from the dispatcher.

- `set-material-preset material preset` — one record, appearance and colour.
- `set-material-appearance material [roughness] [metallic] [emission] [finish]
  [scale] [relief]` — any subset; one record.
- `material-presets` — the list, with what each is.
- `materials` reports every field and the preset it matches, or `custom`.
- `preferences` reports how the section is being worked in — the depth, the
  colour notation, whether the preview is unfolded — because a `window` capture
  shows those and an agent reading one has to know which way round it is drawn.
  Reading only, like every other display preference.

## 6. Storage

`MaterialDocument` gains an optional appearance object; absent means the default,
which is what every existing file means. `Finish` is written as a **name**, so
adding a finish cannot renumber the ones already saved, and an unknown name reads
back as `Smooth` rather than throwing. Schema version 4.

## 7. What is still not done

**Export sees the colour and nothing else.** Roughness, metal and the facture do
not leave the editor; carrying them out means UVs in the export IR and image
files beside the mesh. Recorded in the ledger, not resolved here.

## 8. The agent's surface, and a preview

Beyond the writes in §5, an agent needs to *see* a material without building
anything: `material-preview material [yaw] [pitch] [size]` renders one cube
wearing it — its own camera, its own light, its own viewport — and answers with
the image. A
material is the one thing in this editor whose whole point is how it looks, so
"read it back" has to mean a picture; a slice cannot show a facture and the main
viewport shows whatever the user has built rather than the material alone.

The preview never touches the document or the scene: it is a `SubViewport` that
exists only while a preview is asked for.

**And it can be turned.** `material-preview` takes `yaw` and `pitch`, and answers
with the ones it actually used — the pitch stops short of the poles and the yaw
wraps, through `MaterialPreviewTurns.Clamp`, which is **the same function the
panel's own drag goes through**, so the two cannot come to disagree about where
the poles are. Without it an agent could see one three-quarter view of a block a
user can walk right round, which is half of what a facture does: a brushed streak
is a mark from the front and a sheen from the side. The command's own block is not
the panel's, so what somebody left the panel turned to cannot decide what a
picture comes back showing.

In the panel it is **dragged**: a pull across the whole of the preview is one full
turn, which is Goxel's rule and what the scene's own orbit already answers a hand
by, so neither carries a degrees-per-pixel number chosen by feel. A gesture is what
a hand wants and a number is what an agent wants; the clamp is shared.

**The cube is as big as the facture needs.** One cell more than the pattern's
period, never fewer than three — so a face carries the whole pattern *and* the
start of the next repeat, which is what makes a tiling facture legible as one
rather than as a decoration that happens to end at the edge. A fixed three cells
is what shipped first and it showed three quarters of a four-cell plank pattern:
Brick, Timber and Parquet are period four and PlankFloor is period five, so on
every one of them the placed model wore the part the preview had cropped.

**The scale measures a unit, not the tile, and that is what makes the family
proportional.** A tile holds one panel, four boards, six hammer marks — so a dial
that meant the tile gave two materials set to the same number features six times
apart, which is the one thing a tiling number exists not to do. It means cells to
one *unit* of the pattern; the recipe carries how many units its tile holds
(`UnitsPerTile`) and the period is the product. Then the ready-made materials can
be sized against each other the way the real things are, anchored on a brick
course of two cells: a pool's mosaic 0.7, a parquet block 2, a floor board 4, a
glass block 5, a ceramic tile 8, with a knurl at 0.25 and a hammer mark at 0.4
under all of them. `MaterialProportionTests` holds the chain.

**A feature must be bigger than a voxel.** The pattern is anchored to the
element's own lattice, so a facture whose feature comes out exactly one cell wide
puts the same seam against every cell boundary — and therefore against every edge
and corner of every model. PlankFloor shipped that way: four boards to a tile,
four cells to a tile, one board to a cell. It is eight cells now, so a board is
two — **on the lattice**, which is where a voxel editor's numbers belong. Taking
the period off the lattice also removes the seam and was tried first; it is the
wrong lever, and dishonest in a way this application should not be. Measured, a
board of one cell reads 23 points worse than the flat material at a concave
corner and a board of two, three or four reads within a few points of it: what
was wrong was never that the period was whole, it was that the board *was* the
cell. Brick is still four and four, and is recorded in the ledger rather than
retuned.

**What no size can promise is the phase.** A facture is a field over the element's
own cell lattice — that is what makes it ride with the model instead of sliding
across it as the model is moved — so a model shows the window of the pattern its
own cells sit at. The same cube painted at cells 0..2 and at 8..10 wears different
parts of the plank. The preview contains everything a model can show; which part
of it a given model shows is that model's own position in the timber.

## 9. The preview's ground, and why it is a chequer

A preview of a **transparent** material over a flat ground is a lie in one
specific way: what you see is the material composited over that colour, and you
cannot tell how much of what you see is the material and how much is the ground.
Two panes at 0.3 and 0.6 alpha over the same dark grey differ only in how dark
they are — exactly the reading a flat ground destroys.

The chequer is the standard answer, and it is standard for a reason worth
stating: a ground that is **two** colours turns opacity into something you can
see rather than infer — the strength of the pattern showing through *is* the
transparency, read directly, with no reference sample needed.

- **Automatic, not a switch.** The chequer appears when the colour's alpha is
  below one and not otherwise, because a flat ground is the better reading for an
  opaque material and a chequer under it is chartjunk. There is no preference,
  because there is no case where somebody wants the wrong one.
- **Quiet.** Two shades a little either side of the panel's own inset colour, not
  the black-and-white of an image editor: this sits inside a dock, and a loud
  chequer would be the brightest thing in the window.
- **Fixed to the window, not to the block.** The squares stay put as the block
  turns, because they are the ground the block is seen against rather than part
  of the scene.
- **The command's picture keeps its alpha.** `material-preview` renders on a
  transparent background and always has; the chequer is the panel's ground,
  painted under the viewport. An agent compositing the result wants the alpha,
  not a chequer baked into it.

## 10. Antialiasing in the preview

The block is nearly all straight edges at 45° to the pixel grid, which is the
worst case for a jagged silhouette, and it is small — so the edges are a large
share of what there is to look at. The viewport renders at twice the size it is
shown at and is filtered down, which is supersampling by another name: it costs a
buffer four times the area of a 132-unit square and nothing per frame beyond
that, and it is the one technique that needs no render pipeline changes at all.
