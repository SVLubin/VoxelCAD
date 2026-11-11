# The renderer (canon)

This is **canon**, not description: the code implements it, and where the two
ever disagree it is the code that is wrong.

The editor has two ways of showing a model, and they answer different questions.
The **viewport** says what you are working on: it is real-time, it is what every
tool draws its affordances over, and it is shaded by one directional light with
ambient occlusion baked into the vertices (`docs/architecture.md`, *Shading*).
The **renderer** says what the model looks like: it traces light, so a wall
lights the floor beside it, the sun's shadow is soft because the sun has a size,
glass refracts and a lamp lights what stands next to it.

Neither is the other's preview. A render is not a better viewport and the
viewport is not a draft render — the viewport is graded by the engine's own tone
mapper under a fixed exposure because it is a working surface, and a render is
graded by a film curve with an exposure of its own because it is a picture.

## Where it lives

`VoxelCAD.Render` references CommonTypes, Geometry and Model, and holds **no
Godot nodes** — a `RenderingDevice` is not one. So it builds under the plain SDK
like Export, and everything in it that decides something is testable without an
engine.

Two steps, public, and separate because they cost and fail differently:

```csharp
RenderScene scene = RenderFactory.CompileScene(project, scene);
RenderFactory.TryCreateTracer(scene, settings, camera, out IPathTracer? tracer, out string error);
```

Compiling walks the document and can say what it had to leave out. Starting a
tracer touches a device and can find there is not one — which is a machine's
answer rather than a fault, so it is reported and never thrown.

`RenderScene` is **opaque outside the layer**, the way a clip is opaque outside
the model: a caller carries one from the compile to the tracer and never looks
inside. What it does say out loud is what it could not carry.

## The scene is compiled, not watched

A render is a **snapshot**. The document is walked once, when the render starts,
into voxel grids in their elements' own cells plus the materials those grids
wear; nothing in the layer subscribes to the model, and a render does not follow
later edits. That is what a picture is.

- **Every element keeps its own frame.** The ray is carried into it — the
  transform stored is the *inverse* of where the element stands — rather than the
  cells being baked into a shared world grid. That is what makes a rotated or
  scaled model trace exactly, and it is the reason this side does not have the
  hole the exporter records as `UnbakeableElements`.
- **The direction is carried across unnormalised.** The ray parameter then stays
  world distance, so a hit inside a half-scale element and a hit inside a double
  one can be compared against each other without either being converted.
- **Visibility composes down the tree** exactly as it does everywhere else: what
  is hidden, by its own flag or an ancestor's, is not in the scene.
- **Colour crosses into linear light.** The document's colours are sRGB — it is
  what a picker offered and what the viewport's materials are given — and light
  is not. Alpha does not: opacity is not a colour and transferring it like one
  would change how much every pane of glass lets through.

## What light does

**The sun is aimed at. Nothing else is.**

- The sun is sampled directly, over the cone its angular radius subtends, which
  is where both its soft shadow and its highlight come from.
- Its intensity is **irradiance** — what it delivers to a surface facing it — and
  the radiance handed to the shader is that divided by the cone. The two cancel
  in the estimator, so **widening the sun softens its shadow and changes nothing
  else**. Handing the tracer the intensity as radiance instead is how the first
  render of this project came out lit by nothing but its own sky.
- **There is deliberately no sun disc in the sky.** Nothing can find the sun by
  chance, so no path can find it twice and no multiple-importance weighting is
  needed to say so. The price is a roughness floor: a lobe of zero width cannot
  be aimed at a light, and the sun is most of the light here.
- **Emissive materials are aimed at too.** Every emissive cell with a face on the
  outside is a light — one walled in by its own neighbours lights nothing — and
  the sphere that bounds it is sampled. The sample is kept **only when the ray
  lands on that very cell**, which is what makes it exact with more than one
  light in the scene: a direction drawn towards this one that lands on another is
  the other one's light, at the wrong density.
- A light that is aimed at must not also be counted when a path happens to land
  on it, so **emission is added only from the camera and after a bounce that
  could not be aimed anywhere** — a specular one, since the dielectric branch does
  no direct sampling. Past a cap the remaining cells are not aimed at, and then
  the whole scene goes back to finding them by chance rather than counting some
  twice.
- Everything else — the sky, and one voxel lighting another — arrives by the path
  carrying on, and **an indirect contribution is clamped** because of it: a path
  that stumbles onto something bright carries many times the radiance of its
  neighbours. Bias, taken deliberately against a picture that does not converge
  at all. The aimed paths are not clamped: clamping one that converges takes
  light out of a picture that was going to be right.

## A render is a snapshot, and now it says when it has gone stale

The scene is compiled once, when the render starts; editing the document with the
card open leaves the picture as it was. That is what a picture *is*, and it is why
nothing in the render layer subscribes to the model.

What the card does now is **say so**. While it is open the presenter watches the
scene *and* the project — a material is not in a scene, and recolouring one changes
every picture that wears it — and the first change marks the render stale: a line
on the card, and `stale` on `render-status` for a reader deciding whether to trust
what it is looking at.

It does not restart. Throwing a render away on a keystroke would be worse than a
line of text, and which of the two a person wants is not something the card can
know. "Render again" is the answer, and it is the only thing that clears the mark.

An edit that changes nothing marks nothing, because the model raises nothing —
which is the right answer and was worth checking: the first probe of this fired a
fill that wrote no cells and read the honest `false` as a failure.

**The card and the command surface say it at different lengths, from one pair of
constants.** A status bar is a few words beside a button, and the button completes
the sentence: the card says "The model has changed" and "Render again" stands next
to it. `render-status` has no button in front of its reader, so it reports the
whole of it. The first cut had one wording for both, and the card then had a
hundred-character sentence to fit into a strip it shares with a progress line —
which it did by clipping it to a single pixel, so the notice this section exists
for was on screen and unreadable for as long as it took to look.

## Transparency is a medium, not a surface

The march is told what the ray is currently **inside** and reports the first cell
that differs from it. So the far face of a glass block is found as an interface
rather than as another front face — which is what a plain "first filled cell"
march reports, and what would make glass a mess of nested entries.

- Whether the ray is entering or leaving is read off the medium, never off which
  way a normal happens to point: the face crossed always faces back along the ray,
  in both directions, so the normal cannot answer that question.
- The medium is tracked **per element**, because an element that is merely in the
  way must not have its own empty cells read as "the glass I am inside".
- A material's **alpha is its opacity**, and it is spent as the probability that a
  hit behaves as a surface rather than as an interface — the same bargain the
  viewport's own blending makes with that number.
- The part that behaves as a surface is **diffuse only**. Its highlight is the
  dielectric interface beside it, already counted; both would be the reflection
  twice, and once through a lobe as narrow as glass's, which is a field of bright
  speckles nothing converges away.
- A shadow ray is attenuated by what it passes through rather than stopped by it,
  or a glass model would stand in a solid shadow.

## A facture is the viewport's own, sampled by hand

A finish is a tiling height field, and the tracer is handed **the very field the
viewport's two maps are made from** rather than a second table of patterns — that
is what keeps a brick wall in a render from being a different brick wall. It
crosses as numbers (`FinishHeightField`) because a `Texture2D` cannot: the tracer
marches on a device of its own and has no sampler to hand one to. So the wrap and
the bilinear are in the shader, and the tiles are packed one after another into a
buffer with an offset per material.

- **The triplanar picks one plane rather than blending three.** A voxel's faces
  are axis-aligned, so the dominant axis of the face normal is exact and a blend
  would only soften what is already right.
- **The filtering is better than the viewport's, by construction.** The viewport
  picks a mip level and leans on anisotropic filtering; here every sample of a
  pixel lands somewhere else inside it, so the field is box-filtered exactly and
  a fine facture cannot shimmer.
- **The normal is bent the way the viewport's normal map is built** — central
  differences over the same heights, at the same bump strength, with the depth
  applied as a blend towards flat because that is what the engine means by
  `normal_scale`. It is bent in the element's own frame and carried out through
  the transform the face's own normal took, so a rotated element wears its
  facture with it.

## The drawing, and what stands behind it

**Projection** is the camera's, and converging by default. Asked for a parallel one, the rays stop converging: parallel
edges stay parallel and a cube seen from its corner draws as an isometric cube,
which is what a drawing of a part looks like. The window's height defaults to
whatever keeps the shot the perspective camera was framing, so switching the
projection changes the drawing rather than what is in it. A lens has nothing to do
there — there is no cone to open — and is skipped.

**The backdrop** is what a camera ray that reaches nothing shows, and it is
deliberately *not* the same question as what lights the scene: the sky goes on
lighting it whichever is chosen, the way a renderer's transparent film has never
meant switching the world off.

- **Sky** — the environment itself, which is the default.
- **Plain** — one flat colour, which is what a specification sheet wants.
- **None** — an alpha channel, so the model can be composited onto whatever it is
  going to stand on. The floor goes with it: it is scenery rather than something
  that was built, so the camera does not see it either — though light still
  bounces off it, which is what keeps an underside lit rather than black.

Coverage rides in the accumulation buffer's fourth channel, and the resolve
divides the colour back out by it, because a file carries **straight** alpha
rather than premultiplied — otherwise every edge composites dark.

## The lens

A pinhole by default, because a CAD picture is usually one where everything is
legible and a blur nobody asked for is a picture of a mistake. Given a radius,
the ray still passes through the same point on the plane in focus but leaves from
somewhere on the lens rather than from its centre — so what is on that plane
stays sharp and everything else spreads by how far off it is.

The focus distance defaults to **the middle of what the scene holds**, which is
the point the camera was framed on. A default of zero would be the camera itself.

Both are the camera's — see *The picture is taken through a camera* — so they are
edited in the inspector and by `set-camera`, and a render takes them as it finds
them rather than offering an override of its own.

## The lens is focused by pointing at something

Typing a distance is the wrong instrument for a question about a picture: what a
person wants is *that*, sharp. Arming the Focus button and clicking a point of the
render does it, and so does `render-focus` with the point in the render's own
pixels.

**The distance is the one the render already measured.** Every sample writes how
far its camera ray reached into a buffer of one float a pixel, so focusing is a
buffer read rather than a second trace — which matters for more than speed: a
second trace would answer through a different jitter of the same pixel, and on an
edge that is a different surface.

The button that arms it is **refused while the camera's blur is nought**, and says
why: with no blur the whole picture is sharp and there is nothing to focus, so
arming a pick would move a number nothing reads. Greyed rather than hidden — a
control that vanishes takes its own explanation with it, and it would shift the
buttons beside it every time the value crossed nought. It is *pushed* rather than
read off a field: the blur lives on the camera, and the card holds no dial for it.

Nothing is there where the camera ray reached the sky, and that is answered as
such rather than by focusing on some default. Focus is one of the dials that decide
what the light does, so picking one restarts the render, exactly as moving it by
hand would — and what it writes is the camera's own lens, so it is one record on
the history like any other edit to the document.

## Progressive, in tiles, and resolved on the device

Every step adds one sample per pixel of **one tile** into a float buffer and
**nothing is divided until the picture is read**, so stopping early is a noisier
picture of the same render and never a different one. That is what lets one caller
run it to the end in a loop and another spend a slice of each frame on it without
the two being different renderers.

### Where the next sample goes

The picture is cut into tiles and the next sample goes to the tile with the least
`taken / weight` — weighted round robin by virtual time.

The weight is two questions multiplied: **where somebody is looking**, and **how
far from settled that tile still is**. They are different questions and a render
answers both — a noisy corner off screen is worth less than a noisy one under the
eye. The noise half is the standard error of each pixel's mean against how bright
it is, measured on the device a few times a second; it needs one extra number per
pixel, the sum of the squares, because the sum itself is already in the
accumulation and luminance is linear. It is judged against the average of the
picture rather than an absolute threshold, since what counts as noisy depends on
the scene, and it is bounded either way so one stubborn tile cannot take the whole
render.

**Measured**, on a scene of glass, steel and a lamp at 960×640 asking for 6000
samples, with the viewer fitted so that nothing is being aimed at and the spread is
the noise alone:

| after | worst tile | best tile | worst tile's error |
|---|---|---|---|
| 1 s | 211 | 1195 | 15.8% |
| 4 s | 354 | 3403 | 4.5% |
| 8 s | 645 | 6000 | 3.2% |
| 13 s | 1114 | 6000 | 3.1% |

The reading to take from it is not the spread but the plateau: past eight seconds
the worst *error* stops falling because the tiles that were noisy have had
everything they asked for, and what is left under-sampled is the flat sky. **The
render equalises noise rather than samples** — which is what makes a picture look
finished long before it is. What it does not do is finish sooner: the total is the
same work, and the last quiet tile is still owed its samples. Three properties fall out
of that one line and all three are what this had to have:

- In the long run each tile is sampled in proportion to its weight, **exactly** —
  no accumulator drifts, because the ratio *is* the state.
- **Nothing starves.** Every weight is above nought, so every tile's virtual time
  rises and the least of them is eventually somebody else.
- **The first sweep is complete.** Every tile starts at virtual time nought
  whatever its weight, so the picture is covered once before any of it is covered
  twice — which is why a prioritised render still *appears* whole immediately
  rather than growing out of the middle of the frame.

The weights come from what is on screen: a tile wholly inside the viewer's own
rectangle is worth eight times one nobody is looking at, and a tile half inside is
worth half way between. The interpolation is the point — a step at the boundary
would draw the region's own edge in the noise, which is the one artefact that
would make this worse than not doing it.

**Priority decides the order and never the total.** A tile that has had everything
asked of it drops out and the rest carry on, so two renders of the same scene at
the same settings are the same picture whatever was being looked at while they
ran. Measured on the running editor at 1920×1080: 899 samples over the whole
picture against 7192 in the part zoomed into — eight times, with the rest still
climbing.

Every pixel of a tile is traced together, so they have had exactly the same number
of samples: the resolve divides by a count **per tile** rather than per pixel — a
few hundred numbers instead of a million, and no counter for the tracer to
increment.

**Dividing, exposing, tone mapping and encoding happen on the device.** Not for
elegance: the preview is read many times during one render, and doing it on the
processor was measured to cost more than the tracing it was showing — eighty-eight
samples in 6.8 s, against 256 in 0.3 s once the resolve moved. Four bytes a pixel
come back instead of sixteen and nothing walks them.

The film curve is `ToneMap`'s, and the resolve shader is handed its constants
rather than repeating them: the shape of the expression is one line, the numbers
are what would drift.

### The frame does not wait for the card

A batch is **submitted at the end of one frame and waited for at the start of the
next**, so the frame's own drawing happens while the card is tracing and the wait
is usually already satisfied when it comes. Submitting was never the cost; the
`Sync` straight after it was, where the processor stood and watched.

**It is not a thread, and it cannot be one.** A *local* rendering device is
documented as being for work on a separate thread and is not: every call from a
thread of our own is refused with "can only be called from the render thread".
That was written, shipped to a drive, and looked as though it worked — the
scheduler counted samples nothing had traced and the card showed a bar filling and
an estimate counting down for a render doing no work at all. The engine's own log
is what said so.

What *is* threaded is the compile, where the work is plain arithmetic over the
document: measuring each element's cells and filling its own range of the grid
both run over several elements at once, each writing to a range nothing else
touches. The order of everything that comes out of it — which lamps are aimed at,
above all — is the tree's, not the order threads happened to finish in.

## The lens scatters, and that is the only post-process

A render without glare reads as arithmetic: an emissive material comes out as a
bright patch with a hard edge where a camera — or an eye — would show it spreading
into what is beside it. It is computed on a copy reduced eightfold, blurred across
and then down, and mixed back into the **exposed linear** picture before the film
curve, because glare is something that happens to light on its way through a lens
rather than after the film.

**No threshold, and mixed rather than added.** The whole picture is convolved and a
small fraction of the result is mixed in, which is what a lens actually does —
scatter a little of everything. The bright-pass-and-add that most engines use
haloes edges and invents light that was never there; mixing moves light about
instead of making more of it.

It costs three passes over a sixty-fourth of the pixels, run on every read rather
than once, because exposure is a dial that turns without the render starting again.

**Nothing else is post-processed.** There is no denoiser: noise is answered with
samples, and at these speeds that is the honest answer rather than a filter that
guesses.

## The energy has been counted

A white surface in a uniform environment with no sun must come back at exactly
the environment's radiance. The way to ask is a **convex** white cube in a flat
sky: it should vanish into the background, both having gone through the same film
curve, and a convex body never sees itself so one bounce is the whole answer.

Lit once, and it found something. A white metal at roughness one came back **40%
darker than the room** — the single-scattering microfacet loss, which is why a
rough metal reads as dirty in a naive renderer. The missing light is returned now,
tinted by the surface because that is what a second bounce off it would have been.

**Lit a second time, across the roughness range, and it found the compensation
itself.** The first fix asked Karis's analytic `EnvBRDFApprox` how much a single
scattering returns — and that is a fit of the *split-sum environment* term, not of
the estimator this tracer runs. Measured on a ladder of white metals at roughness
0.1 through 1, the picture read **+2.6%, +8.0%, +7.6%, −5.9%, −17.5%** against the
room: too much light where none had been lost, too little where it had. One
measurement at one roughness had looked fine, which is how it survived.

What replaced it is the same integral done exactly: `SpecularAlbedoTable` walks a
Hammersley sequence through the tracer's own weight — `G·(V·H)/((N·V)(N·H))`, which
is what `f·cos/pdf` simplifies to when the half vector came from the distribution —
over a 32×32 table of view angle against roughness, uploaded once and read
bilinearly. It is deterministic, so a test holds it to numbers, and it was checked
against an independently written Monte-Carlo integration before it went in: the two
agree to a third of a per cent at every point.

The same ladder now reads **−0.02%, +0.01%, +0.04%, +0.02%, −0.20%** in radiance —
the cubes vanish into the sky, which is what the check looks like when it passes.

**And the diffuse residue went with it, once there was a number to look at.** A
white matte cube read −2.8%, and the cause was the other end of the same table: the
diffuse half was scaled by `1 − F(N·V)`, the Fresnel at the view angle, which for a
dielectric is four per cent — while the specular lobe it was giving that four per
cent to returns **1.2%** at roughness one. The arithmetic said −2.77% before the
render was asked, and the render said −2.80%.

What the diffuse half gives up is now what the specular half actually returns:
`A·f0 + B` from the same table, times the compensation, so the two sum to one by
construction. The cube reads **+0.29%**. Every body in the furnace is now inside a
third of a per cent, and the check holds all six to the same bound because none of
them has a residue of its own any more.

### Running it

The scene is checked in and so is the measurement, because the whole lesson of the
second lighting is that a check done once by hand is a check done at one point.

```
dotnet run --project tools/VoxelCAD.Cli -- --script tools/checks/furnace.txt
```

Then, **holding the editor throughout** — releasing control puts the user's camera
back, and the next render is of wherever they were looking — open `tmp/furnace.vcad`,
`view front`, `focus`, `zoom 70`, and take a locating picture with the backdrop
blacked out:

```
render width=1000 height=300 samples=64 sun-intensity=0 sky=0.5,0.5,0.5 ground=false glare=0 backdrop=plain backdrop-color=0,0,0 path=tmp/furnace-lit.png
```

That first render exists because of what passing looks like: the cubes vanish into
the sky, so nothing in the measured picture can find them. Finding six in it is the
framing check.

```
python3 tools/checks/furnace.py locate tmp/furnace-lit.png
```

It prints a `sample=` argument — the sky first, then one rectangle per body. Render
again with it, and hand the `sampled` array back:

```
render width=1000 height=300 samples=1600 sun-intensity=0 sky=0.5,0.5,0.5 ground=false glare=0 bounces=16 sample=<what locate printed>
python3 tools/checks/furnace.py check '<the sampled array>'
```

**What is measured is radiance, not pixels**, and that is not a detail: the same
diffuse cube reads −1.5% off the encoded picture and −2.8% out of the tracer,
because the film curve compresses. Every energy number in this document is the
radiance one.

Two things the table says that the fit could not. A mirror returns everything, at
every angle — the fit said 94.5%, so the old compensation brightened a polished
surface by 5.8% for nothing. And near edge on the albedo is **not monotone in
roughness**: at a twentieth from grazing it falls to about two thirds by roughness
0.2, climbs back to three quarters by 0.6, and only then falls away, because the
lobe tilts far enough to catch more than the smoother one did. The first test
written for the table asserted monotonicity everywhere and the numbers refused;
what is asserted now is what is true.

### What the clamp costs

A path tracer is linear in the light it is given, so any deviation from
proportionality is something clamping — which makes the indirect clamp measurable
without a second build or a reference renderer. A white floor and a white wall,
the sun straight down so the wall is lit by bounce alone, the same two patches
sampled in radiance at two intensities:

| | wall (bounce only) | floor (direct) |
|---|---|---|
| sun 1 → 8, an eightfold | **100.0%** | **100.0%** |
| sun 2 → 100, a fiftyfold | **30.9%** | 95.8% |

So at any brightness a scene actually uses, the clamp takes nothing. It bites only
where indirect radiance passes six, which needs a sun fifty times the default —
and there it does exactly what it was written for.

## Two ways in, and they are the same renderer

- **`render`** on the command surface, `mutates: false`. A render reads the
  document and touches nothing in it, so it is free on the same terms `slice`,
  `bounds` and `frame` are: looking is not editing. What it does take is *time*,
  which is why the sample count is an argument and not a constant.
- **File → Render…** (⌘R / Ctrl+R), a glass card holding the picture as it
  arrives. A card rather than a dock panel because a render is not something you
  work beside.
- **`render-settings`**, which reports the defaults and every bound, so a render
  can be asked for without finding the edges by being refused. Both halves come
  off one set of constants: the descriptions interpolate them, and a test checks
  that no reported default falls outside its own reported limit.
- **`render-open`, `render-status`, `render-image`, `render-view`, `render-focus`,
  `render-close`** — the card itself, on the surface like everything else. A render asked for over the wire is
  one picture answered into a conversation; the card is the other way of having
  one — it keeps going, it can be looked into, and what it is looking at decides
  where the samples go.

**Neither reaches the headless CLI.** `tools/VoxelCAD.Cli` does not reference this
layer at all, and could not use it: a render wants a live camera and a device that
runs compute, and `--headless` has neither — the same reason the camera and capture
commands are editor-only. So `voxelcad --mcp` publishes no `render*` tool, and an
agent that needs a picture needs the editor. What would change that is a CPU
reference tracer, which does not exist and which the energy check would want
anyway.

### The picture can be looked into

The viewer scales about the pointer — by wheel, by trackpad pinch, and by
two-finger scroll, which zooms rather than pans because that is already what the
same fingers do over the scene; the conversion is the viewport's own, so the two
move at the same rate. Godot's macOS backend sends a pan gesture *and* synthesized
wheel events for one physical scroll, so a wheel notch arriving just after a pan
gesture is ignored, or a trackpad would zoom twice as far as it asked for. It pans
by dragging, snaps to Fit or 1:1, and past one to one draws the render's own pixels
as squares — a render *is* pixels,
and somebody judging noise wants to see them. Under a backdrop of None it draws the
transparency chequer, which is the only thing that says where an empty backdrop's
edges are.

`render-status` reports that measurement as `noise` — the worst tile's relative
error, so a tenth means the average pixel there is about ten per cent out. It is
what says whether more samples would help, where the count alone cannot: a flat sky
is settled at four and a caustic is not at four thousand.

**`render-image` is how a card's picture gets out**, and it is not a convenience:
the Save button opens the platform's own file picker, which is outside the window
and unreachable by injection, so without it a render an agent had asked for and
watched could only be looked at. It answers the picture as it stands rather than
waiting for the end — a render is the same render however long it runs, so asking
early gives a noisier picture of it and never a different one — and takes a `path`
so an agent can keep one without a picker. It is deliberately a command of its own
rather than a field on `render-status`: a megabyte of base64 on every poll of a
progress line is not a status.

**A job always has a picture.** The first batch runs when the render starts rather
than on the first frame after it, so a card shows something on the frame it appears
and `render-open` followed straight away by `render-image` is answered instead of
being told to come back. Found by a probe doing exactly that in one frame.

**What is on screen is what the renderer traces first**, so aiming the viewer and
aiming the render are one act rather than two — and a picture nobody has zoomed
into asks for nothing in particular, which is plain round robin. That is why
`ImageView.VisibleRegion` answers *empty* for a fitted picture rather than a
rectangle that happens to be the whole of it: a rounding of half a pixel at the
edge must not read as somebody aiming at the middle.

**The card is resized from a corner grip and remembers the size**, and the settings
stand in a column beside the picture rather than under it — a render window is a
picture with its dials to one side, and a band across the bottom takes height from
the one part of the card anybody is looking at. The column hides from a toggle in
the header, and whether it is showing is remembered. The foot carries the progress:
a bar across the whole width, then how far along on the left and what to do with
the render on the right, with a gap the width of the grip so a press meant for one
cannot land on the other.

**Nothing inside the card asks for width or height as its text changes.** The card
is as big as the larger of what it was dragged to and what its contents need, so a
progress line that grows a digit, or a note that wraps to a second line, drags the
whole card with it — which is a window that changes size while a render runs. The
progress line is clipped and takes the row's slack, so nothing it prints is ever a
width. The note beside it is one clipped line asking for exactly what its own words
need, up to a ceiling — measured in the font it will be drawn in, and it is only
ever given one when something has happened, so it cannot move while a render is
running. Held by measurement rather than by eye: the card's rect is identical, to
the unit, across a render.

The measuring is not fussiness. The width of that note was first *chosen* — a round
150, and the theme's font wanted 157, so what the card showed was "The model has
change". A picture caught it; nothing else could have. **The same rule settles the
settings column's width**: it is measured from the widest dial in it (a track
reading "Perspective | Parallel") with the round number kept only as a floor, so the
column is as wide as its contents and no wider. Written down instead, it cut its own
fields off at the frame.

**The column is cut softly at the top and bottom**, the panels' own `ScrollFade`,
driven off the scroll bar rather than a frame tick — the strength only changes when
the body moves or the card is resized, and this card stops processing the moment it
has finished arriving. Its two segmented tracks fill the column rather than hugging
their words: in a stack of full-width boxes a track that hugged sat against the
right edge at whatever width its labels happened to need, so two questions one under
the other were two different shapes hanging off the same edge.

**A wheel notch over the card is swallowed, not read as a press.** A card is
dismissed by a press on the glass behind it, and an `InputEventMouseButton` with
`Pressed` true is what a wheel notch is — so a scroll that nothing inside the card
answered (a settings column already at its top) closed the card *and* then zoomed
the scene behind it, both from one gesture. Found by driving, in the one state that
produces it. The card is over the editor, so nothing behind it may answer a gesture
aimed at the card.

**A card that opens shows the whole picture.** The canvas outlives the card, so
reopening it used to restore the last zoom — a fresh render arriving already cropped.
"Render again" is the other case and keeps what the user had zoomed into: opening is
a new picture, re-rendering is the same view of a new one. The render's own resolution deliberately does not follow: the picture is
a deliverable, and one whose size depended on how big the window happened to be
would be a different file on every machine. The card is how much of it can be seen
at once, which is a different question.

Under the picture: a bar that eases, the sample count, the count *in view* where
the two differ, and an estimate of what is left. Spans are printed at the precision
they are worth reading at — tenths while it is seconds, whole seconds past ten,
minutes past a minute and **hours past an hour**, every boundary decided by what
would be printed rather than by the raw value. The hours came from a sweep that
produced "about 1666 m 40 s left", which is the same complaint the tenths rule was
written for, one unit up; and the rounding is away from zero, because .NET rounds
halves to even and an hour and ten and a half minutes read as "1 h 10 m". Both counts are shown because both
are honest — the whole picture is worth what its least converged part is worth, and
showing only that would make a visibly clean corner read as noisy. What is left is
estimated from the **recent** rate rather than the average, or the first slow
seconds of a render would still be dragging the estimate down at the end of it.

**The card's dials are remembered** — in the preferences, beside the panel
widths and for the same reason they are not in `PreferenceValues`: the settings
form does not edit them, and a form that carried a value it never shows would
write back whatever it happened to be holding. A render setup is a thing somebody
arrived at, and losing it on Close would make every second render start from the
beginning. `preferences` reports them, so an agent can see what a person would
get by pressing Render.

The **command does not** read them: it has arguments and defaults of its own, so
one agent's render cannot depend on what somebody left the card at. The two can
differ, and both reports say so — `preferences` for the card's, `render-settings`
for the command's. Those defaults live in one place inside the command, because
`render-settings` promises them and `render` has to keep the promise: reported
from the renderer's own while three were overridden beside them, the report named
a width, a height and a sample count no render would ever have used.

Two of the card's dials deliberately do not start again: **exposure** is applied
when the picture is read, and **samples** only says how long the same render may
carry on for. Everything else changes what the light does. That is stated as
"everything except these two" (`RenderRestart`) rather than as a list of what
restarts, so a dial added tomorrow restarts until somebody decides it should not.

**"Render again" is not a dial**, and sharing an event with them was a defect that
shipped: with nothing changed it re-graded instead of tracing. A render is compiled
from the document as it stood when it started, so the button is the only way to
take a second picture of a scene that has since moved, and it restarts whatever the
dials say.

## The picture is taken through a camera

A render is taken through the scene's **active camera** — an element in the
document, with a transform saying where it stands and a `CameraLens` saying what
it sees through. `SceneCamera.TryRead` is the one answer, asked by the card and by
the one-shot `render` alike, so a picture cannot come out differently depending on
which of them asked.

**The lens is the camera's; the film and the light are the card's.** The
projection, the field of view, the orthographic window, the blur and the focus
distance come out of the document. The size, the samples, the exposure, the sun,
the backdrop, the floor and the glare come off the form. That line is where it is
because a picture taken through the same camera has to be the same picture: a
lens kept in the application's preferences would depend on what somebody left a
dial at, and the file is the only thing that travels between machines.

**Opening the card in a scene with no camera places one**, where the viewport is
looking, as one undoable record. That is the honest reading of "a picture is taken
through a camera": the camera it would otherwise have asked somebody to make is
exactly the camera they were already framing. The one-shot `render` refuses
instead and names the fix — it is a read, and a read that invented an element
would be worse than a sentence.

**Focusing writes the camera's own lens.** Arming the Focus button and clicking a
point of the picture sets the active camera's focus distance, which is an
undoable edit to the document rather than a dial being turned; the button itself
is refused while that camera's blur is nought, and the card is *told* whether to
offer it rather than reading a field it no longer has.

The viewport's own camera is still asked for rather than derived — `SetActiveScene`
and the aim commands go through `ISceneView.GetCameraView` — but only to place a
camera from it. A renderer that turned a turntable pose into a transform itself
would hold a second copy of the orbit's arithmetic.
