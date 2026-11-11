# Driving the editor's interface

The editor can synthesize its own input. This is how UI and viewport behaviour is
**verified** — a transform handle can only be shown to work by being dragged — and
it is the answer to "Beckett's free edition cannot inject input", which is true
and has not been a blocker since this landed.

It is a **debug instrument, not a way to work.** To build a model use
`set-voxels`, `fill-box` and `set-transform`. To move an element use
`set-transform`. Clicking your way to either is how the semantic command surface
rots.

Everything below is a real transcript: the commands are what was run and the
answers are what came back.

## Switching it on

Off by default. Turn it on in **File → Preferences → "Let an agent drive the
interface (debug)"**, or set `inputInjectionEnabled: true` in
`preferences.json` beside the application (`user://` — on macOS
`~/Library/Application Support/Godot/app_userdata/VoxelCAD/preferences.json`)
and restart.

Ask whether it is on:

```
preferences
```
```json
"inputInjection": { "enabled": true, "note": "The 'input-*' commands drive the interface for testing it. Build models with 'set-voxels', 'fill-box' and 'set-transform'." }
```

Called while it is off, every `input-*` command refuses and names the setting.
They are always in `tools/list` — a tool that vanishes teaches nothing.

## The loop

```
take-control  →  ui  →  input-*  →  read the model back  →  release-control
```

`ui` and `window` need no control; the `input-*` commands mutate, so they are
gated like any edit. Without control they refuse with
`The editor is under the user's control. Call 'take-control'…`.

## Say who you are, and what you are doing

`take-control` takes a `name` and a `topic`, and both land on the plaque the
user is looking at while you hold their editor: the name as the title, the
topic under it. Pass them. A plaque reading "an agent" working on nothing is
what the user sees otherwise, and they decide whether to cancel the countdown
by exactly what it says.

`report-topic` restates the topic as the work moves through phases — one short
line, present tense, the way a status bar reads. It needs no control (it edits
nothing) and, like every command, it also counts as proof of life for the
silence clock. `control-status` echoes the current topic back.

## Coordinates

One space, and it is **not** the space a Godot `Control` measures its own rect
in. `ui` reports rects already converted into it, and a capture reports how to
get back to it:

```
window {"format":"jpeg","scale":"0.35"}
```
```json
{"width":1040,"height":624,"windowSize":[2972,1783],"origin":[0,0],"scale":[0.3499,0.35]}
```

`input = origin + pixel / scale`. The window-to-canvas factor has been measured at
both 1.002 and 2.58 on the same machine, differing only in how large the window
was — which is why **aiming by `target` is the documented way** and reading pixels
off a picture is the fallback.

## 1. Clicking a control by reference

Never by pixel. Ask `ui` what is there:

```
ui {"root":"UiRoot/LeftColumn/ProjectPanel"}
```
```json
{"windowSize":[2972,1783],"count":15,"controls":[
  {"ref":"UiRoot/LeftColumn/ProjectPanel","type":"ChromePanel",
   "rect":[7.7,103.2,851.4,799.9],"visible":true,"takesMouse":true},
  {"ref":"UiRoot/LeftColumn/ProjectPanel/ProjectHeader",
   "type":"Button","rect":[10.3,105.8,846.2,72.2],"visible":true,"disabled":false,
   "focused":false,"takesMouse":true,"text":""}]}
```

Then aim at the reference:

```
input-click {"target":"UiRoot/LeftColumn/ProjectPanel/ProjectHeader"}
```

A column also reports two decorations of its own — `DropHint`, the wash that
says a carried tab may be put down here, and `DropGhost`, the outline of where
it would land. Both are drawn while a panel is being dragged and neither takes
the mouse, so they are there to be read and never to be aimed at.
```json
{"at":[46.4,141.9],"target":"…/ProjectHeader","type":"Button","action":"Tap"}
```

Check the effect on the state, not on a screenshot — here, by asking `ui` again
and reading the panel's rect: a collapsed panel is its header's square, a grown
one is the column's width.

**A tab is dragged with `input-drag`, like anything else.** The gesture is a
press, motion polled per frame, and a release — not Godot's drag and drop — so
one `input-drag` from a tab to the other side of the window moves it there, and
a short drag along its own column reorders it. Read the result with
`preferences`, which reports every panel's side and order:

```
input-drag  {"at":"46,235","to":"2925,235","steps":"24"}
preferences   →  "History": { "side": "Right", … }
```

The press/move/release form works too and is the one to use when you want to
look at the carry in flight — `ui` then reports the carried card under the
pointer while the rest of the run has already closed ranks.

**A panel's header is also its tab.** Collapsed, the whole card *is* the header
— `{Kind}Header` is the control a press lands on, and there is no `{Kind}Tab`
anywhere; the same press grows it and puts it back. `{Kind}Pin` exists only
while the header is wide enough to hold it beside the glyph, so it is absent
from `ui` on a collapsed panel — grow the panel first, then aim at the pin.

`Undo` is the other good target — its effect is checkable in both directions.
A control that is missing, hidden, disabled, zero-sized or **covered by something
drawn over it** is a refusal naming the reason, never a click into empty space.

## 2. Dragging a transform handle

The case this exists for. Build the model with the semantic commands, then
exercise the handle.

```
fill-box {"element":"Model","from":"4,0,4","to":"9,5,9","material":"Default"}
input-click {"at":"1450,900"}          # focus the viewport — see "the first click" below
input-key   {"key":"W"}                # the Move tool, through the editor's own binding
```

Find the handle. It is 3D, so it has no control behind it: crop the window
around it and read the arrow off the picture.

```
window {"format":"png","region":"1200,100,700,500"}
```

Drag along the arrow, then read the model back:

```
input-drag {"at":"1600,428","to":"2000,536","steps":"20"}
```
```json
{"at":[1600,428],"to":[2000,536]}
```
```
element {"element":"Model"}     →  "position": [9.935957, 0, 0]     (was [0,0,0])
history                         →  one new record, "MoveElement"
undo                            →  "position": [0, 0, 0]
redo                            →  "position": [9.935957, 0, 0]
```

**One record for twenty motion steps**, undoing in one go: that is the gesture
batch working, and it is the thing worth checking after any change to the handles.

### Leaving the drag in flight

`input-drag` presses and releases in one command, so anything the editor shows
*while* a handle is held is over before you can look at it. Split the pair with
`action` — the mouse's version of what `input-key` does for held keys — and move with
`input-move` in between:

```
input-click {"at":"1590,955","action":"press"}     # grab the handle, keep it down
input-move  {"at":"1612,968"}                      # one frame of the drag
window      {"region":"760,600,2200,1180"}         # ← the ghost and the readout
input-move  {"at":"1637,982"}                      # past the snap point
input-click {"at":"1637,982","action":"release"}
```

The whole thing is still **one** history record: the gesture batch spans the
commands, not the call. What the readout says is the applied value and how far past
it the pointer is — `X 12 (+0.4)` while the inspector still reads 12, then
`X 13 (-0.2)` once it snapped. Under a quantised channel that is the only way to tell
"nothing is wrong, keep pulling" from "nothing is happening".

A press with nothing following it leaves the button down. Nothing breaks — the next
release ends it, and `release-control` clears the agent's held state on the next
grant — but the editor is in a drag until then.

### Plane patches

The move tool also has **three plane patches** — the small squares in the corner
between each pair of arrows, which slide both of their axes at once. Aim inside the
square, not at the corner where the arrows meet: the patch stands off the origin so
the shafts stay grabbable, and a press that lands on no handle at all leaves no
record rather than moving anything, which is how a mis-aimed drag reads. Two of the
three go away as the camera turns them edge-on, so read the picture again after any
`view` or `orbit`.

```
input-drag {"at":"1489,600","to":"1700,700","steps":"18"}
```
```
element {"element":"Block"}     →  "position": [6, 0, 1]     (was [0,0,0])
history                         →  one new record, "MoveElement"
```

## 3. Painting with the brush

```
input-key   {"key":"B"}          # the Paint tool
input-click {"at":"1487,228"}    # a face of the model
bounds {"element":"Model"}       →  voxels 216 -> 217, max [9,5,9] -> [9,6,9]
slice  {"element":"Model","axis":"y","index":"6"}
```
```
"grid": ["......","......","......","......","....D.","......"]
"legend": [{"char":"D","material":"Default","cells":1}]
```

**The first click into an unfocused viewport paints nothing** — it is what hands
the viewport the keyboard, and the brush only places what its preview already
offered. Taking control drops focus, so after `take-control` the first viewport
click is always an activation. Click once, then click to paint.

**A press-drag-release is a stroke, and a stroke is one record.** The press
lands its first cell, every cell the hover proposes while the button is down
lands as the pointer reaches it, and the release closes the record. Twelve-cell
slab, looked at from the top:

```
input-drag {"at":"1200,886","to":"1700,886","steps":"20"}
bounds {"element":"Slab"}        →  voxels 144 -> 150, max y 0 -> 1
history                          →  one new record, "EditVoxels"
undo                             →  voxels 144
```

Split the pair to look at a stroke while it is held. Nothing new lands while
the pointer rests, and `history` shows no record yet — the batch is open:

```
input-click {"at":"1200,886","action":"press"}
input-move  {"at":"1300,886"}
input-move  {"at":"1400,886"}
bounds {"element":"Slab"}        →  voxels 147, max y 1 — three cells, none stacked
history                          →  unchanged until the release
input-click {"at":"1400,886","action":"release"}
history                          →  one new record
```

**Shift is read at the press and kept.** `input-click {"action":"press",
"modifiers":"shift"}` opens an erasing stroke, and releasing Shift part-way
(`input-key {"key":"Shift","action":"release"}`) changes nothing until the next
press — a stroke is one mode for its whole length. Holding Shift with a separate
`input-key` press *before* the click does not make the click carry it; the
modifier belongs to the click.

**Escape closes a stroke on what it landed** — the cells are already the
model's — and further motion under the still-held button places nothing until
the next press.

## 4. Pulling an extrusion

The extrude tool (`X`) is a press-drag-release: grab a face, pull along its
normal, and the release writes as many whole layers as the pull travelled — one
history record. A plain click is the degenerate gesture and writes one layer,
Shift-click carves one in.

```
input-key   {"key":"X"}
input-click {"at":"1484,790","action":"press"}     # grab the top face
input-move  {"at":"1484,700"}                      # pull outward
window      {"region":"1100,400,800,600"}          # ← pending layers + "Extrude +2"
input-click {"at":"1484,650","action":"release"}
bounds {"element":"Slab"}        →  voxels 50 -> 100, max y 1 -> 3
history                          →  one new record, "EditVoxels"
```

A pull brought back to where it started resolves to **nothing** — release writes
no record — and `Escape` mid-pull walks away the same. While the button is down
the gesture owns the viewport: the camera does not answer, and every key but
Escape is swallowed.

## 5. Dragging a row of the project tree

An element row drags onto another element or onto the scene's own row, world
position preserved. **Use one `input-drag`** — a press, moves and a release as
separate commands highlights the target and then lets go without dropping.

```
input-drag {"target":"…/HierarchyTree","offset":"0.3,0.54","to":"122,333","steps":"20"}
tree        →  the row is now a child of the row it landed on
```

Two things to know. Only element rows travel — a material or a category row
starts no drag at all, by the presenter's own answer. And drops are reliable
from here: consecutive drags in one session — nest, move, un-nest, re-nest —
all land. (They did not always: Godot's `Tree` clears its drop flags after
every drag, and until the view re-asserted them per gesture, only the first
drag of a session could drop. If a drop ever refuses again, log the drop
*section* — a dead drag reads −100 at every position.)

## 6. Menus and dialogs

Popups and dialogs are windows drawn over the main one. They are in the snapshot,
they carry `"layer": 1`, and they are reached the same way — the menu items
themselves are not controls, so aim inside the popup with `offset`:

```
input-click {"target":"UiRoot/LeftColumn/ToolbarPanel/Body/Scroll/ToolbarBody/ToolbarRow/FileMenu"}
ui                                            # the popup is now listed, with its rect
input-click {"target":"…/FileMenu/PopupMenu@0","offset":"0.4,0.88"}   # Preferences…
ui {"root":"SettingsDialog"}                  # the dialog's own controls
```

`layer` is what makes this work: a popup is a child of whichever button owns it,
so tree order puts it *under* half the interface while the engine draws it *over*
all of it. Without the layer, a click on an open menu is refused as "covered by
the panel behind it".

## 7. The other commands

```
input-move  {"target":"…/Undo"}                         # hover, to check a highlight
input-click {"target":"…/HierarchyTree","offset":"0.5,0.1"}   # a row of a list
input-click {"target":"…/CreateModel","modifiers":"shift","clicks":"2"}
input-key   {"key":"Left","action":"press"}             # held, for the camera arrows
input-key   {"key":"Left","action":"release"}
input-key   {"key":"Escape"}                            # cancels a drag in flight
input-text  {"text":"Bracket"}                          # into whatever holds the keyboard
input-wheel {"at":"1500,900","notches":"-3"}
```

`offset` runs 0..1 across the target's rect. It is how a **row of a `Tree` or an
`ItemList`** is reached: rows are not `Control`s, so they have no reference of
their own.

## 8. The view cube

**Use `view` to look from a direction.** `view {"direction":"top-front-right"}`
does instantly and by name what clicking the cube does in a third of a second,
and the two share the same function, so they cannot disagree. What follows is how
the widget itself was exercised, not a way to work.

The cube is one control with 26 zones inside it, and the zones are 3D geometry
rather than `Control`s — so they are reached with `offset`, like a list's rows,
and unlike a list's rows **they move as the cube turns**. Aim from a known pose:

```
view        {"direction":"top-front-right"}      # yaw 45, pitch -35.264
input-click {"target":"UiRoot/ViewCubeArea/ViewCube","offset":"0.308,0.611"}
camera                                            # yaw 0, pitch 0 — the +Z face
```

From the isometric pose the near corner is dead centre (`0.5,0.5`), the top
face's centre is `0.5,0.297`, and the +Z face's is `0.308,0.611`. From a face-on
pose the visible face's nine cells are on a grid at `0.304 / 0.5 / 0.696`.

**Read the pose back, not the picture.** Each zone has its own yaw and pitch, so
the answer to `camera` says which zone was actually hit — which is the only
reliable check, since a guessed offset near a boundary can land next door.

The turn is animated, so wait for it before reading, and note what interrupts it:

```
input-click {"target":"…/ViewCube","offset":"0.696,0.304"}
input-wheel {"at":"1447,890","notches":"-1"}      # cancels it where it stands
camera                                            # a pose short of the target, and it stays there
```

Any camera input cancels a turn in flight; a second press retargets it, and
pressing the same zone again stops it. While it runs the camera counts as busy,
so a brush click during one places nothing — the same rule as clicking mid-orbit.

Modifiers are genuinely held for the length of the gesture, not merely flagged —
the editor reads some off the event and some off the live keyboard, and half a
modifier satisfies neither.

## What the editor sees while you drive

Worth knowing, because it decides what you can rely on.

While an agent holds control, the editor's tools read **your** pointer and keys,
not the machine's. Whoever is sitting in front of it can lean on Shift, wave the
mouse across the viewport and click — none of it reaches the brush, the handles
or the camera. Verified by measuring, with a person doing exactly that.

Two consequences for you:

- **You have no pointer until you move one.** After `take-control` the brush
  offers nothing, because there is nowhere to point. `input-move` or the first
  frame of any `input-click` gives you one, and it stays where you left it.
- **What you see in `window` is what you are about to do.** The preview under
  your pointer is yours. Before this, a capture showed the marker under the
  user's mouse, which made screenshots during agent work actively misleading.

The keyboard is still the editor's own: the viewport reads keys only while it
holds focus, and taking control drops focus. So the first `input-click` into the
viewport is an activation — click once, then work.

## Limits

- **Off by default**, and debug-only. Nothing may be reachable only this way.
- A drag is capped at 120 steps (two seconds of frames), a click at 3, a wheel at
  32 notches, typed text at 256 characters. Out of range is clamped, not refused.
- Points outside the window are clamped to it.
- **A native OS file dialog cannot be driven.** It is an operating-system window,
  not a Godot `Control`, so it is invisible to `ui` and unreachable by injection.
  Running embedded in the Godot editor the picker is Godot's own and *is*
  reachable — which makes this a trap: an open/save flow that works under the
  editor will not work in the shipped binary. Drive files with `open` and `save`.
- **Selection and the active tool are not restored on release.** Clicking changes
  the selection, a tool key changes the tool, and a handover puts back only the
  camera. Leave them where the user would want them.
- Rows of a `Tree` or `ItemList`, and items of a `PopupMenu`, are not controls;
  reach them with `offset`.
- The occlusion check approximates Godot's hit test: same three questions
  (visible, takes the mouse, contains the point) in draw order, but it does not
  model a container clipping its contents.

## When something does nothing

In order:

1. `ui` — is the control visible, enabled, and where you think? Is
   `UiRoot/ViewportSurface` `focused`, if you are driving the viewport?
2. The command's own answer — it echoes `at` and the `target` it resolved to.
3. `history` — did anything land at all, and as how many records?
4. `window` — what does the screen actually show?
