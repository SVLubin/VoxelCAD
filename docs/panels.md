# Panels (canon)

This file is canon on the same terms as `docs/architecture.md`: the code
implements it, and where the two disagree the code is wrong. It governs the
editor's chrome layout — the panels, their columns, and how they share the
window with the viewport.

## The problem it answers

The old layout was two fixed docks, 300 and 330 canvas units, full height,
always present — 55% of an 1152-unit canvas spent on chrome whatever the window
size, with the inspector blank most of the time. The viewport is the product;
the chrome has to be able to get out of its way.

## The unit is a panel

A **panel** is a titled unit of chrome: an icon, a title, a content control,
and two orthogonal switches.

- **The pin is the X axis** — whether the panel stands in its column. Pinned,
  it occupies column space permanently. Unpinned, it collapses to its header
  (a tab), and its body appears only on demand.
- **The fold is the Y axis** — whether an open panel shows its body or only its
  header. A pinned, folded panel is a header row standing in the stack, and it
  is **exactly its header's square** tall — the separation below a header
  belongs to a body, and reserving it anyway left a strip of empty glass under
  the band. That is the same height a collapsed tab is, which is not a
  coincidence: both are the panel with nothing but its header left.

An unpinned panel is an **icon tab**: the panel shrunk to its own glyph. The
tab is not a button that opens something else — **it is the same control**, the
panel's own header, wearing the whole card because there is nothing else left
of it. There is no second node standing in for a collapsed panel and no moment
at which one is exchanged for the other; the surface simply moves between two
sizes, and what changes is only what no longer fits — the title, the chevron
and the pin stand down, and the header's band takes all four of the card's
corners instead of the two it caps a body with. So one press means both halves
of the gesture: it grows the panel, and pressed again it runs that backwards.
(A separate tab button is what this replaced, and it cost a visible pop: a flat
stylebox on one side of the moment it arrived and glass on the other.)

Showing the panel grows the rest of it **beside** the tab, so together they
make a Γ lying on its side. Three things follow, and they are the whole reason
for the shape:

- **The icon never moves.** A tab's glyph and a header's glyph land in the
  same pixel — the header is the tab's height and shares its inset, measured
  rather than assumed. Identity does not travel; the panel arrives around it.
- **A tab never moves either.** The body opens to the side of the run, never
  over it, so the tabs below stay where the eye left them. That is what kills
  the accordion wobble: without it, opening one panel pushes the next tab out
  from under the pointer and the hover chatters down the column.
- **The title arrives beside the glyph**, where the header's own words go, and
  the header drops its glyph while grown — the tab is standing right there
  wearing it, and drawing it twice would claim there were two things.

Pinning moves a panel up into the stack and the tabs close ranks below it;
unpinning sends it back down to the run.

There are four panels: **Toolbar** (one icon row — Open, Save, Undo/Redo,
Preferences, and a File popup only where the platform has no menu bar of its
own; the near-system actions a hand reaches for between edits), **Project** (the tree, whose rows answer a right press with a
glass context menu — the create kinds valid there, and the row's own Show,
Hide and Delete duplicated from the glyphs beside it), **History** on the
left; **Inspector** on the right. The tool strip is
not a panel — it lives inside the inspector, which is the decision "tools are
buttons of the current element's panel", stated rather than drifted into.
On macOS the menu bar is the platform's (`NativeMenuBar`): File and Edit in
the strip at the top of the screen, Settings in the application menu, and the
File button gone from the toolbar. The bar owns the chords it shows, so the
shell stops reading them — and being outside the window it is invisible to
`ui` and undrivable, the native file dialog's trap exactly.

## Columns

Each window edge (left, right) carries one column. **Pinned panels stack at
the top, in order; unpinned tabs sit below them.** A column with at least one
pinned panel is wide (`EdgeColumn.WideWidth`, derived — see **One panel, one
width** below); a column with none collapses to a thin rail. The viewport strip between the columns follows their widths live —
one method re-anchors every strip control, so the three cannot drift apart.

A panel's placement — side, order, pinned, folded — is a display preference,
stored per the `viewCubeCorner` template: flat keys with defaults beside them,
clamped or resolved rather than refused, applied live on `Changed`, reported
read-only by the `preferences` command, no history record ever. The ephemeral
"this panel is temporarily open" state is view state and is never stored.

## Height is a solve, not a negotiation

Every panel reports a **minimum** and a **natural** body height — natural is
live, measured from content (a tree with five rows asks for five rows). The
column's layout is a pure function (`PanelColumnLayout.Solve`) of the slots
and the container height. Per frame, because the inputs are cheap and a pure
function recomputed is state that cannot go stale.

Opening a temporary panel (clicking an unpinned tab) gets
`clamp(available, min, natural)`. Only when its **minimum** does not fit does
the cascade run, in order:

1. **Shrink** — pinned bodies give height continuously down to their minimums,
   bottom-up (nearest the opening slot first).
2. **Auto-fold** — pinned panels fold to their headers, bottom-up, so the
   topmost (the toolbar) surrenders last. Auto-folds are *derived, not
   stored*: the user's fold flag is untouched, so closing the temporary panel
   restores the stack by mere recomputation.
3. **Scroll** — with everything folded and the minimum still not fitting, the
   opening panel takes what is left and scrolls inside. Opening never refuses.

The same cascade serves a window too short for the pinned stack alone.

## Temporary panels

One temporary panel at a time, and it always opens the same way wherever the
column stands: beside its tab, over whatever is to that side. The column
itself never reflows for it — pushing the viewport on a glance would jump the
camera's aspect every time the pointer wandered. It stops the mouse, so
occlusion stays honest, and the scene's polled affordances ask the shell
whether the pointer is claimed by chrome (`SceneView` pointer shield), the
generalization of the view cube's hard-coded exception.

**The last card in a run cuts no notch.** A panel grown from it has nothing
below to reveal, so there is none: cutting one anyway pushed the body aside for
a hole showing the bare room. It spreads into the space under its own stub
instead. The Γ is a shape for getting out of the way of something, and where
there is nothing it is just a bite. What it does **not** get is extra width — a
notch is width given away, not width earned, so a card that gives none away
carries the same surface as one that does, and its body is the body every other
panel has.

**The cut is what the shape is, not something the opening does to it.** A card
carries the same answer open and shut, so a rectangle is a rectangle from the
first frame — given one answer collapsed and another grown, the notch eased
between them and the last card grew a Γ only to close it again. Everything else
about a panel animates; this does not, because it is not a position, it is an
identity.

**Hover shows, a click holds, the pin commits.** A dwell on the tab
(`PanelHoverIntent.OpenDwellSeconds`, so crossing a tab on the way elsewhere
opens nothing) grows the panel for as long as the pointer is about; a click
on the tab asks for the same thing on purpose, so it stays until dismissed;
the pin in the header puts it in the column for good. Three degrees of
commitment on three gestures, and the header click is the way back — the
gesture that grew it, run backwards.

A press in progress is not a glance: while a panel is held, hover decides
nothing. Without that, holding a tab down longer than the dwell opened it under
the hand before the drag had travelled far enough to be one, and the panel was
then picked up at whatever size that had reached.

**Both the dwell and the grace are measured at the editor's pace, and go to
nought when animation is switched off.** A dwell is not a transition, and it was
left out of the pace setting for that reason — wrongly. With nothing easing, the
dwell is the only thing left that the eye reads as the interface taking its
time: a panel that lands at once, a fifth of a second after you asked for it,
reads exactly as "there are still delays in here", which is what the setting
exists to answer. Both go through `MotionTiming.ScaleFixed`, like the view
cube's turn. The cost is real and is the point: off, sweeping the pointer down a
rail opens each tab it crosses for the frame or two it is over it. A floor here
would be a number nobody chose standing in the way of a setting somebody did.

The hover is polled per frame from `IInputState`, never engine enter/exit,
which follow the machine's cursor and would let a bystander's mouse close a
driving agent's panel. **Every dismissal is a decision taken with the pointer
possibly still on the surface that answered it, so hover is latched off that
panel until the pointer leaves and returns** — not just the click that first
needed it. Escape, a press on the scene, the viewport taking the keyboard and
the header itself all go through the one close, and the latch lives there:
without it Escape closed the panel and the dwell handed it straight back a
fifth of a second later, which reads as Escape doing nothing.

It closes on: Escape, another tab opening, a pointer
button pressed on the scene itself (a real button — the wheel is not a click,
the same sentence the focus gate enforces), the viewport taking the keyboard,
or the pointer leaving the panel and its tab for a grace period
(`CloseGraceSeconds`) — and never while the hand is still in it by any
measure: keyboard focus inside, a mouse button down, a picker or dialog up.
Closing — and unpinning, and folding, and every hide the solver or a rail
transition performs — hands the keyboard to the viewport if the hidden
subtree held it: a hidden control's focus goes nowhere by itself, which this
codebase has measured twice.

**Nothing in the chrome is ever placed outright.** Every frame the column
works out where each thing *belongs* and hands that to a `RectMotion` — four
`Smoothed` numbers, one per edge. Growing, collapsing, pinning, unpinning,
folding, unfolding, reordering and the column's own width between rail and
wide therefore animate by construction rather than by anybody remembering to
animate them, and a target changed halfway is simply the next target.

**The surface is a mask. Nothing inside it moves.** Every control a panel holds
— the glyph, the words, the chevron, the pin, the tree, the inspector's form —
is laid out at the size it will stand at once the panel has arrived, pinned
against the edge the animation does *not* move, and the growing surface simply
uncovers it. Opening is a reveal, not a reflow.

Which edge stands still is the whole of the rule, and it is why half of this was
invisible on one side and glaring on the other: a left column's panel grows
rightward from a fixed left edge, so left-aligned content happens to stay where
it is, while a right column's grows **leftward** — and the same content flew the
width of the panel across the screen on every open, under a header that was
sitting perfectly still. The other half showed on both sides: anything anchored
to the *far* edge, which is the moving one, rode it — the pin and the chevron
travelled the whole way in on every open. One rule answers all of it, and it
also stops the tree and the form being laid out again on every frame.

**And it is both axes, not one.** A fold is the same reveal turned ninety
degrees: the body's contents keep the height they had and the clip closes over
them. Written against the animating height instead, a folding panel re-flowed
its body all the way down — and a scrolling one said so out loud, its bar
arriving as the room ran out and leaving again on the way back up, with every
row shifting by the bar's width as it came and went. So a slot holds **the last
size that had room for the contents** on both axes: a fold aims the surface at
the header alone and a collapse aims it at the tab's square, and neither is a
statement about how big the body is.

**There is no container inside a panel — not one, and that is the rule rather
than a preference.** `PanelLayoutSolver` computes every rect — header, body,
glyph, title, chevron, pin, content pane — and the panel assigns all of them in
the same call that was handed its own. The last one to go was the header's own
row, and it cost the glyph twice: a row wider than the box it is given grows
away from the near edge, and the glyph — placed last on a mirrored column so
that it lands against the stub — was carried clean out of the header and clipped
to nothing, leaving the right column's tab blank on every fresh launch. A container will not do: it sizes its children
from a sort deferred to the message queue while their *position* follows the
parent at once, so on every frame the panel's rect changes the two disagree by a
step. On a left column nothing shows, because the parts are measured from the
near edge, which is the position. On a mirrored column they are measured from
the far edge — position **plus** size — and the whole header shivered a unit per
tick for the length of every animation. Godot's pixel snapping is the other half
of the same trap, so a surface is handed a rect with **both edges already on
whole units** (`RectMotion.Snapped`): rounding the size while leaving a
fractional position produces the same sawtooth.

Two smaller ones fall out of the same rule. **Nothing inside a panel may push
back on its size** — a container's margins and its children's minimums become a
minimum the panel cannot go below, and at a margin of one card a collapse stuck
a few units wide and then snapped the rest of the way. And **a wrapper that only
exists for layout must ignore the mouse**, or, spanning the cut, it eats the
presses meant for the tabs the notch was cut away to reveal.

`Smoothed` is a **sine ease-out over a duration derived from how far there is
to go** — distance over a speed per kind of quantity, clamped into 0.05…0.20 s.
It replaced a fixed 0.12 s, which replaced an
exponential approach that this paragraph used to describe. The exponential had
the better story — no start value, no elapsed time, so nothing a new target
could restart — and the wrong tail: an approach never arrives, so the last
letter of a title went on fading for several frames after the motion was
visually over, which is exactly the part of a transition the eye is still
watching. The curve restarts from wherever the value stands when it is
retargeted, so being aimed somewhere new mid-flight is still the normal case
and still costs nothing; it cannot overshoot; and the step comes from the
frame's own length, so the same wall-clock time lands in the same place at any
frame rate. The first target a fresh motion is ever given snaps: a panel
appearing for the first time has nowhere to have come from. The logical state
changes instantly; only the pixels linger.

**Why a speed and not a duration.** Under one duration the launch speed was a
function of how far the thing happened to be going: a panel opening covered 328
units and the tabs below it reordered by 36, side by side during the same drag,
at a nine-fold difference in pace. And a reversal cost a whole transition for
whatever little was left — one frame into an open, coming home took eight more
frames to travel the ground one frame had covered. With `T = D / speed` both
leave at the same speed and stop at different times, the launch speed is `π/2`
times the constant for every distance, and the per-frame step is bounded at
`(π/2)·speed·dt` — 72 units at 60 Hz — instead of growing without limit with the
distance. A reversal costs what is left, because the distance is measured from
where the value *stands*.

**The pace is a setting, and it multiplies the finished duration.** Three
positions — Fast is what ships, Medium is one and a half times it, Slow two and
a quarter — and a switch that turns animation off entirely, in which case every
motion is placed rather than eased. The multiplier is applied to the **clamped**
answer, clamps included, and that is the whole of why it works: the constant-
speed band is 137.5 to 550 units, and the chrome's two commonest motions sit
outside it at both ends — the 36-unit tab reorder on the floor, the 653-unit
slide on the ceiling. A multiplier on the distance-over-speed term alone would
leave exactly those unchanged and would land the 328-unit panel open on the same
ceiling as the slide. Multiplying the whole answer preserves every ratio between
motions at every setting. Off is a flag and never a factor of nought: nought
would leave a duration of nought, `IsSettled` true from the first frame and the
value standing where it was for ever — a freeze rather than a crash, and nothing
logged.

**The two clamps are global; the speed is not.** The floor, 0.05 s, is three
frames at 60 Hz: below it a motion is not one, and without it a 36-unit tab
reorder would snap on a 60 Hz machine and animate on a 120 Hz one — the
frame-rate independence failing at the other end. The ceiling, 0.20 s, is there
because a canvas unit is not a physical size, so distance alone would make the
chrome animate more slowly on a bigger monitor. The constant-speed band is 137.5
to 550 units, and the only motion above it is a full-height slide.

**`Quiet` is what the output cannot show, and it is per quantity.** Half a unit
for a length, because that is what `RectMotion.Snap` rounds away; 1/255 for an
alpha, because that is what an eight-bit channel rounds away. It does two jobs —
a target restated unchanged every frame must not restart the clock, and targets
measured from live content jitter by a fraction — and half a unit applied to an
alpha broke both silently and totally. See the ledger.

**A rectangle moves on one clock**, set by the edge with the furthest to go
*among those actually restarting*. Four clocks would let a panel finish growing
sideways and go on growing downward. Counting an edge that is merely coasting
would let it set the pace for a small change beside it. The furthest single
edge, never a corner distance: a left column's panel opens with its near edge
fixed and a right column's with its far edge fixed, so a norm over all four
would make the same gesture take visibly different times on the two sides.

**One panel, one width — and the body is the primitive.** The number written
down anywhere in the chrome is `ChromePanel.BodyWidth`, the room a panel gives
its contents. Everything else about how wide something is is solved backwards
from it: the surface is the body plus its frame (`ChromePanel.SurfaceWidth`), a
surface grown over a run of tabs is that plus the notch it gives away, and the
column is the surface plus its own margins (`EdgeColumn.WideWidth`). So a panel
is one width pinned, grown, on the left, on the right, first in the run or last.

It was written the other way round once — a width per dock, 300 on the left and
330 on the right, with the body left over after subtracting a frame and
sometimes a notch — and that gave one panel **three** body widths per column,
290/298/334 against 320/328/364, six numbers nobody had chosen. The body is the
primitive because it is the only one of the four a person can judge: it is what
holds the inspector's rows and the tree's names, and a dock is just what has to
be built around it.

**The width is the side's, set from a corner grip — and the seam stayed dead.**
A resizable column was built once before and taken out, and the objection that
removed it was never the *number*: it was the grab strip along each inner seam,
a dead zone exactly where the scene begins. The grip is the same capability
without that cost — a small mark on the panel's inner-bottom corner, pulled like
any other drag in this chrome (polled, so an injected pointer performs it the
way a hand does; snapped while held, because an edge that eased toward the
pointer would trail it; Escape puts everything back).

One number per **side**, not per panel: every panel of a column — pinned, grown
from its tab, carried — wears its side's width, which is what keeps one panel
one width in every state it has. Bounds live in `PanelWidths` and are shared
with the preferences' clamp, so what a file may say and what a pull may ask for
cannot come apart. The **height** is the panel's own, stored on its placement
(`0` = as tall as the content asks), floored at the panel's minimum and fed to
the cascade as its request — so a panel dragged shorter than its content simply
scrolls, and one dragged taller keeps the room. A panel with nothing to be tall
about — the toolbar — carries a width-only grip. Both are stored like every
other placement fact and reported by `preferences`; writing stays the GUI's.

**A press belongs to the topmost panel covering that point, and to nothing
behind it.** Both polled gestures — picking a panel up by its header, pulling
its grip — go through one answer (`PanelPressTargets`), and that answer asks the
same depth the column already solves for drawing (`PanelDepthOrder`). It has to,
because panels here overlap by design: one grown out of a run, one still
retracting while its neighbour opens, one carried across the window. A walk that
took the first panel in *layout* order whose header or grip happened to contain
the point is a hit test that cannot see what is drawn over it — and it shipped
that way: a temporary panel's own pin stood across the pinned panel's grip
underneath, so the pin was answered as a resize of the panel behind it. The
notch is part of the same rule rather than an exception to it: a cut is a hole,
so what shows through it owns the press, which is what `ChromePanel._HasPoint`
tells the engine — through the very same function, so the two cannot come to
disagree about who a press belongs to.

**It is constant in canvas units, which is not the same as constant.** Under
`window/stretch/mode="canvas_items"` with `aspect="expand"` and no explicit base,
the canvas-to-window factor is derived from the window's own width — measured at
2.5799 in a 2972-pixel window and 1.002 in a small one. So 320 units is 825
pixels in one window and 320 in another, and so is every other number here: the
28-unit header, the 16-unit glyph, the 14-point font, the 96-unit cube. The
chrome is internally consistent and externally a fraction of the window. Making
it a constant physical size is a change to the **ruler**, not to these numbers:
`stretch/mode="disabled"` plus a `ContentScaleFactor` of the platform's own scale
times a user setting. It is deliberately a separate decision — under
`canvas_items` a scale setting multiplies *on top of* the window fit, so it only
picks which fraction of the window the chrome is, and looks as though it works.

**A tab is a tab while it travels.** What a collapsed panel hides — its title,
its chevron, its pin — is hidden because the surface is a mask and its contents
are still laid out at the width they last settled at, not re-flowed into the
tab. So "collapsed" must be a question about the panel's **size**, never about
whether its rectangle has stopped moving: asked the second way, a tab sliding
to a new place in the run stopped being one, and two units of its title's first
letter showed past the tab's own clip until it arrived.

And it must be asked of the **surface**, never of a flag beside it. The slot
kept one, raised by the three retarget paths; a panel carried across the
window's middle is rebuilt as a fresh slot in the column it arrives in and
reaches none of them — only `Carry` — so it was born with the flag lowered and
wore a tab's dress for the length of the crossing: no title, no chevron, no pin,
on a card three hundred units wide. The question is "has the surface settled at
the tab's own square", answered by the shape motion, which every path writes.
It is the third time this chrome has paid for a fact about how something looks
being kept where only some of the paths that draw it set it.

**The hole is drawn, in the shape the panel will actually wear.** The column
already reserves the carried panel's place and lets the others part around it;
an outline is put in that hole so the reservation is visible instead of implied.
It is the **shape** the panel will occupy — the cascade's plan for it in the
stack, a tab card in the run, and the grown surface **with the notch it will
cut** if it is the open one. If the landing is a Γ, the outline is a Γ. So a tab
carried into a pinned stack shows the whole panel it is about to become, which
the card under the hand cannot say. Accent, outlined over a faint wash of the
same colour, drawn above the cards and never glass: it marks where a surface
*will be* and is not one, and the only card its rect can ever meet is the
carried one — **which the notch is what made true**. A rectangular outline hung
on a tab's corner lay over the very tabs the cut exists to reveal, washing them
and running its border across them, while `EdgeColumn`'s own constructor said it
could not. It eases like
everything else, and for a sharper reason than consistency — the hole is a gap
between neighbours that are easing, so an outline that snapped would stand at
the destination while the gap was still closing.

**A column solves its own depth.** Godot reads draw order off the child list,
and that was the one output of the column's solve nobody had ever written down:
a panel is appended when it is reparented and never moved again, so depth was a
residue of which panel arrived last and drifted from the layout the moment a
panel was reordered without changing columns — a panel dragged past a sibling
passed *behind* it. The answer is the layout's own order with the carried panel
last (`PanelDepthOrder`), because the thing under the hand sweeps over its
neighbours. The columns are siblings under one root and neither clips, so the
same rule applies one level up: the column a drop is aimed at is raised above
the other for the length of the gesture, and put back on release.

**A drag is judged against the layout as it was when the tab was picked up —
the list *and* the ruler.** The placements are taken from the moment of the
pick-up so a move is never applied to its own output; the geometry those
placements are measured against has to be frozen on exactly the same terms, and
for a sharper reason than accumulation. The preview reserves the carried panel's
hole at the top of the stack, and the span offered to a drop leaves that panel
out of what it reports — so the span begins *below the panel's own hole*, and a
pointer parked in it reads as outside the stack. The next frame withdraws the
preview, the hole comes back, and the answer flips. One frame per state, each of
them a valid layout: a screenshot shows one, a recording sampling one phase shows
one, and an eye integrating both sees the whole column doubled. Measured over a
691-unit column before the fix: **148 of 692 pointer positions never reach a
fixed point** (`PanelDragLoopTests`). The height cascade is what gives the loop
its teeth — it shrinks and folds bottom-up, so moving a panel one place up the
stack changes every height below it, and with it every boundary the decision was
made on.

The class rule: **a decision taken once a frame from a pointer must be measured
against something that decision cannot move.** Half a snapshot is not a snapshot.

## A tab is dragged, not dropped on anything

A tab travels: pressed, carried under the hand, let go. It changes which side
it is on and where it stands in the run, and nothing else — **`PanelPlacement`
is `Side` and `Order`, with no anchor and no corners.** Corner-anchored runs
were designed and then withdrawn.

- **A run hangs on the window's own edge, not on the column's inner one.** On
  the left those are the same place, which is why a run inset from the column's
  left looked right for as long as nothing was pinned on the right — and then a
  wide right column put its tabs, and everything grown from them, a column's
  width inside the window with the scene showing past them. Mirrored, like the
  header.
- **Which side is which half of the window the hand is in.** There is no drop
  zone to find and nothing to hit; carry a tab across the middle and it belongs
  to the other column. **Where in the run** is how many tabs the pointer has
  passed, measured against the middle of each, and the carried tab is left out
  of that count — a run that measured itself would answer differently depending
  on where it had just put the thing being measured, and chatter between two
  places.
- **The others part for free.** A drop target is applied as a *preview* — the
  placements are changed but not stored — so the rest of the run re-solves and
  glides to its new spots through the same motion every other transition uses.
  Nothing animates the parting because nothing has to.
- **The carried tab is snapped, not eased.** It is the one thing that must be
  exactly under the hand; a thing being dragged that eased towards the pointer
  would lag behind it.
- **Nothing is stored until the hand lets go**, so a cancelled drag needs no
  undo of its own — Escape simply puts back the placements captured when the
  tab was picked up. A drop renumbers both sides dense: a list that only ever
  has values inserted into it drifts into ties, and a tie is settled by whatever
  order the panels happen to arrive in.
- **Any panel travels, by its header, as the thing it is.** A tab is dragged by
  its glyph and an open panel by its title bar, which are the same control and
  so the same gesture — but only once it has **stopped moving**: a panel caught
  mid-flight has no settled place to be picked up from, and the grab would be
  measured against a rect that is somewhere else by the next frame. **Nothing
  about it changes while it is carried**: an open panel travels open and lands
  open, a pinned one stays pinned and rejoins the stack, a tab travels as a tab.
  Only the position is taken over, and it is snapped rather than eased, because
  a dragged thing that lagged behind the pointer is not being dragged. A drag
  moves a panel; it does not edit it — forcing the pin off on a drop made a drag
  silently undo a pin, which is a second thing happening that nobody asked for.
  **Which end of the card the glyph stands at travels with it too**, frozen at
  the pick-up. Following the column the panel is currently over turned the
  header round at the window's invisible middle, and turned it the wrong way:
  the end that hangs off the screen is always the one away from where the hand
  came from, so a panel carried *to* the right edge put its glyph and its title
  over that edge, and the same on the left. Where it will land is the ghost's
  sentence, and the ghost still says it.
- **While it is in hand its header is cargo, not a button.** A `Button` takes
  its draw mode from the engine's pointer against its own rect, and this chrome
  places that rect from `_Process` — a frame after the motion event the engine
  tested it with. So the local point the header is asked about is the grab
  offset plus the pointer's travel since the last frame, against a band 28 units
  tall: drag briskly and it leaves the band on some frames and not others, and
  the header shivers between its idle and its lit shade for the length of every
  drag. Crossing to the other column is the same defect outright — the reparent
  drops the engine's grip on the press, measured over four frames as pressed,
  normal, normal, hover. So a carried header wears one band, the lit one,
  because the card is under the hand. The paint is what was wrong; the press
  itself was already answered correctly.
- **A drop commits the glance that was carrying the panel.** Where the pointer
  comes to rest says nothing about whether the panel is still wanted — the hand
  has just moved it on purpose, which is at least what a click on its tab means,
  and a click already lands it held. Without that, a panel let go somewhere the
  pointer does not cover was drawn open at its new place for exactly **one
  frame** and was a tab on the next: the tick that decides its fate cannot run
  until the columns have laid it out, so "is the pointer on it" has no answer on
  the frame of the drop. With animation on that read as a retraction; with
  animation off, the grace being nought, it is a flash — and a state that lasts
  one frame is not a state. It is also the canon read literally: a panel travels
  as the thing it is and lands as the thing it was, so one that was open lands
  open rather than landing open and then shutting itself. Dropped into the
  pinned stack it is nobody's temporary at all, which `SetPanelLayout` already
  says the moment the preview pins it.
- **What a panel *is* follows where it is put.** Dropped between the pinned ones
  it is pinned; dropped in the run it is a tab. The block the hand is over is
  read from the stack's own span, so a column with nothing pinned in it has no
  span and can never pin by accident. This is the only reading under which the
  preview can be honest: carried into the stack the panel is **drawn** in the
  stack and the panels there make room, carried back out it is a tab again, and
  a preview that showed one thing and stored another would be a lie.
- **The places it may go say so.** Both columns light faintly while anything is
  being carried and the one it would land in lights more — the places, so the
  hand knows where it may go, and the answer, so it knows where it is going.
  Faded in and out over 50 ms — the floor, since a hint's whole range is 0.14 of
  an alpha and there is no speed at which that is three frames. **This did not
  work until it was fixed**: the fade was carried by a number whose "beneath
  noticing" was half a unit, so every step the hint could take was an assignment
  and it had never faded at all, in either direction, since it was written. A
  hint that blinked on would be one more thing snapping during a gesture that is
  otherwise all movement.
- **A press that does not travel is still a click**, and past the threshold it
  is a drag and stays one however far back towards the start the hand comes. A
  gesture that changed its mind about what it was halfway through would drop a
  panel *and* toggle it, depending on where the hand happened to stop.

**Godot's drag and drop is not used, and that is deliberate.** The gesture is a
press, some motion polled from `IInputState` per frame, and a release — which is
exactly what `input-drag` sends, so the gesture an agent performs is the gesture
a hand performs. A `_GetDragData` handshake is reachable only by a real mouse,
and the engine's `Tree` clearing its drop flags after every gesture is a bill
this codebase has already paid once.

Deferred, deliberately: a top strip / menu bar (its own decision; the ledger's
customers are queued on it, and on macOS the likely answer is the platform's own
menu).

## Glass is the surface language, everywhere

**Every floating surface in this editor is a rounded glass card.** Not a panel
style — the app's one look, and this is the canon that says so. A surface is:
a `GlassBackdrop` behind its contents, a hairline opaque border in the
`GlassPanel` variation, and the corner radius every other piece of chrome
uses. The panels wear it, the context menu wears it, the drag readout wears
it, and the agent plaque wears it with a gold ring instead of a grey one.
Anything new that floats over the scene wears it too; the burden is on a
surface that does **not**, and there are two ways to carry it. A control that
must stay legible with nothing behind it, which nothing so far is — and a mark
*about* a surface rather than a surface itself, of which the drag's landing
ghost is the first: made of glass it would be a second panel on screen for one
panel's worth of intent, and a second screen read per frame for the length of
every drag.

**Frosting is the user's to switch off, and it is a whole second shader.** A
panel is glass by default and the family above is unchanged by the setting; what
"off" gets is the same distance field, the same hairline and the same modulate
rule, painted in one flat colour — `GlassTint.Over` the backdrop the pane usually
stands on, so what changes is how much of the scene shows through and not what
colour the chrome is. It is a second shader rather than a uniform because Godot
copies the back buffer for a canvas item whose shader **declares**
`hint_screen_texture`, whether or not a branch went on to sample it: a switch
inside one shader would have turned the blur off and gone on paying for it. What
is swapped is `_shared.Shader`, so no surface is re-pointed at anything and a pane
built after the switch is born wearing the right one.

**The ghost shares the shape and not the paint.** One distance field lives in
`PanelShapeField` and two materials are built over it — the glass, which
declares its own screen sampler, and the outline, which declares none. The
field carries no sampler at all, so one screen read per glass panel is
untouched and a ghost costs no backbuffer copy. Sharing it is not tidiness: an
outline promising a rectangle where the panel would draw a Γ is a mark about a
surface that is not the surface, and that is exactly what it was.

**The shader is the surface, not a fill behind one.** It samples the screen
(`hint_screen_texture`, mip-filtered), draws it blurred and tinted, and draws
the shape's own outline and hairline from one distance field. A stylebox
could not: a panel grown from its tab is a rectangle with a notch bitten out
below the stub, and no per-corner radius rounds a *concave* corner. A field
does it for nothing — the shape is a rounded box minus a rounded box, and the
subtraction leaves the fillet the inner corner wants. Two controls faked into
looking like one is what this replaced, and it left a seam wherever their
corners met.

**Every corner of that shape wears the same radius, convex ones included.**
Subtracting with a plain `max` rounds only the concave corner the notch's own
box brings with it; the two *convex* corners the cut creates — where the stub's
underside meets the surface's edge, and where the body's does — came out as
right angles against a card that is round everywhere else, and it was the first
thing anyone noticed. The subtraction is a **rounded** intersection instead,
which is exact for perpendicular edges and changes nothing further than one
radius from either boundary, so the concave fillet is untouched.

**The cut clears the whole collapsed square, frame and all, and then the same
gap again.** Measured against the header alone it fell a frame's width short,
and the surface lay over the last unit of every tab it was supposed to be
revealing; measured against the card exactly, it stopped *on* the tab's own
edge and laid its hairline along it, so the two read as welded rather than as
one thing standing clear of another. It reaches one card plus one `TabClearance`
— the same number the run puts between one tab and the next, so a tab standing
in a notch is framed evenly on every side. Everything a tab is measured against
is the card, not the header inside it: where the run steps, how the rail centres
it, what rect the pointer is tested against, and how far the notch reaches. And
the notch is cut only while there is a body to cut it past — with none, there is
nothing to reveal, and cutting anyway takes a bite out of the tab's own
underside.

The 3D layer renders full-window under the chrome, so the glass shows the
room; a surface over other chrome blurs that instead, which is what makes the
context menu read as sitting above the panel it covers. The notch is not the
panel's to answer for either: `_HasPoint` refuses it, so a press there reaches
the tab the shape was cut away to reveal.

The base `PanelContainer` style is untouched, so the engine's dialogs and
popups keep their opaque panels until they are brought across deliberately.
Four have been: the hierarchy's context menu, the settings form, the File
menu and the question card — each rebuilt as an in-shell card for the same
reason, that a `Window` paints its own panel outside any child's reach, so
glass there means not being a window. What that also buys, every time, is a
surface whose controls are ordinary named ones an agent can aim at by
reference; a `Window`'s contents are invisible to `ui` outright.

What is left as a `Window`, and deliberately: the **file picker**, which is
the platform's own and cannot be drawn here, and the **colour picker's
popup**, which is Godot's composite widget rather than ours. Both take the
family's radius, hairline and text colour; neither is glass. The **tooltip**
is the one surface that could be brought across and should not be — a hint's
whole job is to be read at a glance over whatever it lands on, and glass
gives it whatever contrast the thing underneath allows.

**Two themes, one derivation.** The palette is a surface colour, an accent and
an **ink** — the direction more contrast lies in, white on a dark surface and
black on a light one — so hovering, text, hairlines and separators turn round
together rather than one at a time. Dark is what the editor ships with and is
pinned by test; light runs the same formulas from a light surface with a gentler
contrast. The accent is the user's on both, and it is a setting rather than a
third theme: a colour is better asked for as a colour.

How light the light theme is, is decided by the **glass** more than by the
palette: a pane is its own tint over the scene behind it, and the scene is not
themed. A light tint at the dark theme's own strength composes to mid-grey over
this editor's grey room, so the strength is per theme and the tint moves away
from the ink rather than always down. What follows from that is the rule for
adding anything else: **a colour is legible against what it actually lands on**,
which for a panel is the composed pane and not the palette's panel colour.

The look is repainted into **one** `Theme` instance rather than replaced, because
a control looks its styleboxes and colours up as it draws. Three things are
outside that and are pushed instead — the icon textures (repainted in place, so
every button already holding one follows), the two shader materials, and any
colour a control cached in a field. The 3D scene is not themed: the room is the
document's furniture, not the chrome.

**One screen read per panel.** The backdrop is the panel's single
`hint_screen_texture` read; everything inside — trees, section frames, header
bands — paints flat translucent fills over the already-blurred pane
(`GlassTree` and friends), never a second screen read. Each read is a
backbuffer copy, and a nested one would blur the blur.

**A body that scrolls is cut softly.** The last 16 units at an edge dissolve
rather than ending on a line — `ScrollFade`, an alpha mask
(`ClipChildrenMode.Only`) between the clip window and what scrolls inside it.
The strength follows **what is actually cut** (`ScrollFadeEdges`), not how
close a row happens to be to the line: at rest the top is whole however small
the content's own margin is, and a body scrolled to its end has a whole bottom.
Both halves of "where does it stand" come off one ruler (`ScrollExtent`) —
a `ScrollContainer`'s own bar, or, for the two trees, the bar they crank
(`TreeScrollExtent`). A measured content height is *not* that ruler: it carries
a row of headroom, and a maximum overstated by it dissolves an edge that has
nothing past it.

A mask is the one mechanism that works over glass. A gradient of the background
colour laid over the rows would be a haze band, because what is behind a row is
a blur rather than a fill. It costs a canvas group per scrolling body and **not**
a second screen read, so the rule above stands.

## How an animation is checked

Not by looking. A transition here lasts between 0.05 and 0.20 s; a screenshot
round trip is seconds and `ui` gives one sample per call, so both instruments
see a settled panel and report that all is well. Three defects in a row were
shipped that way, each found by a human watching the screen.

- **A per-frame trace, printed from the running editor and read back through
  `game_logs`.** Every frame, exact numbers, for every part that could move.
  Then the invariants are *computed* rather than judged: which quantities must
  be constant across the whole sweep, which must be monotonic. A quantity that
  goes forward, back, forward is a shake, and it is obvious in a column of
  numbers and invisible in two screenshots.
- **Read the trace before anything is assigned, not after.** Read afterwards it
  reports what was *set* this frame, and the deferred sort then corrects some of
  it before the frame is ever drawn — a lag that is real in the log and absent
  on screen. Read at the top of the frame it reports the previous frame's
  settled state, which is what was drawn.
- **Both columns, every time.** Mirroring is where the asymmetry lives: of the
  defects found here, the ones that mattered existed only on the right, and
  every one of them was missed by checking the left and generalising.
- **A fresh launch, not only the state you drove into.** A panel that has never
  been grown is the state a user starts in, and it is the one an agent never
  sees, because driving is what it does first. The right column's glyph was
  missing in exactly that state.
- **And what can be a pure function must be one.** Every defect in this file's
  arithmetic was unreachable by a test while it lived inside the node. It lives
  in `PanelLayoutSolver` now, and the sweep over every width from zero to grown
  is what found the pin's clearance being reckoned two units short — a squeeze
  of the glyph at one width, which no amount of looking would have caught.

## Agents

**Everything a hand can do here, the instrument can do.** That is a
requirement on the interaction model, not a hope: hover-open is polled from
`IInputState`, so `input-move` plus a wait is a real dwell; a tab click, a
right press and a menu row are ordinary clicks by reference; and the menu is a
card of named buttons precisely so its rows are addressable, which a
`PopupMenu`'s items never were. Any new gesture must keep that true — if a
feature can only be reached by a physical mouse, the model is wrong, not the
instrument.

The one thing that stays out of reach is Godot's own hover paint: the engine
recomputes it from the machine's cursor each frame, so an injected pointer
does not light a button up, and a bystander's mouse lights one up through the
agent's hold. Paint, never a changed click — the dimmer stops the machine's
clicks by origin, and **every piece of chrome must therefore sit under the
banner layer**, which is where the context menu had to be moved after it was
found hovering happily above it.

Layout is chrome, off the command surface like the active tool and the
selection: no collapse/resize commands. The read half applies in full — the
`preferences` command reports every placement, and `ui` must keep telling the
truth: a collapsed body is `Visible = false`, never clipped (UiHitTest does
not model clipping, and a clipped control reads as hittable); every header and
pin is a real, Named control an agent can aim at by reference. **The header is
the tab**, so there is one reference for both, and the pin is simply absent
while the header is too narrow to hold it.

The header's own contents are the one thing that *is* clipped, deliberately:
a collapse narrows it to the tab's side, and the words and the chevron slide
out of its right-hand edge rather than spilling over the scene. That is safe
for the reason the rule above gives — `ui` intersects every rect with its
clipping ancestors, so a title clipped to five pixels is reported as five
pixels and one clipped away entirely collapses to nothing an `input-*` will
accept.
