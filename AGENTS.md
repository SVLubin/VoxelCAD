# VoxelCAD — Agent Instructions

## Product

VoxelCAD is a **voxel editor** with a **CAD-like** UI/UX orientation — precision, structure, and tool clarity — not the feel of artistic/sculpting voxel apps.

## Stack & platforms

- **Engine:** Godot 4.x
- **Language:** C# (primary). Prefer C# over GDScript for game/editor logic unless a Godot API or tooling path clearly requires GDScript.
- **Targets:** Desktop only (Windows / macOS / Linux as relevant).

## MCP

Two servers, and they do different jobs.

**Beckett** (`letterett` at `http://127.0.0.1:8770/mcp`) builds and runs the
project and lets you look at it.

- Prefer it for scene/node inspection, running the project, console/logs, C#
  tooling, and screenshots of a running scene.
- Do not assume Unity MCP tools apply here; this is a Godot project.
- If it is unreachable, say so and fall back to filesystem edits — ask the user to
  start Godot with Beckett enabled when editor actions are needed.
- **Beckett's free edition cannot inject input. That is not a blocker** — VoxelCAD
  injects its own; see the next section.

**VoxelCAD's own endpoint** (`voxelcad`, written to `user://mcp.json` when the
editor starts) is the editor's command surface: it builds and reads models, and it
drives the interface. Use it for anything about the *document* or the *interface*,
and Beckett for anything about the *engine*.

## Driving the editor, and verifying UI work

**The editor can drive its own interface.** Buttons, transform handles, the
viewport, the keyboard — all reachable, so UI and viewport behaviour is verified
by exercising it, not only by screenshotting it. If you are about to write "I
could not verify this because input cannot be injected", stop: you can.

It is a **debug instrument, not a way to work.** Build models with `set-voxels`,
`fill-box` and `set-transform`; move things with `set-transform`. An agent that
clicks where a semantic command exists is rotting the surface this project is
built around.

- **Off by default.** Turn it on in File → Preferences → "Let an agent drive the
  interface (debug)", or set `inputInjectionEnabled: true` in `preferences.json`
  under `user://` and restart. `preferences` says whether it is on.
- The loop: `take-control` → `ui` → `input-*` → read the model back
  (`element`, `tree`, `slice`, `history`) → `release-control`.
- **Only one agent may hold the editor.** `take-control` is refused while another
  agent has it — `wait=<seconds>` puts you in line, served in the order agents
  asked, and answers the moment the editor is yours — and nothing you send meanwhile
  edits the document. `youHoldIt` in the status is the field that says you may edit;
  no other wording does.
- **Introduce yourself.** `take-control` takes `name` and `topic`, and the
  plaque the user watches is built from them; restate the topic with
  `report-topic` as the work moves through phases. A plaque reading "an agent"
  doing nothing in particular is what they see if you skip this.
- `ui` lists every control with the **reference** the `input-*` commands aim at,
  and its rect in the coordinates they take. Aim by reference, not by pixels read
  off a screenshot. `window` pictures the whole window and is for looking.
- Commands: `input-move`, `input-click`, `input-drag`, `input-key`, `input-text`,
  `input-wheel`. `input-click` and `input-key` both take
  `action: tap|press|release`, which is how a gesture is left in flight — the only
  way to look at what the editor shows *while* a handle is held.
- While you hold control the tools read **your** pointer and keys, not the
  machine's — a person at the keyboard cannot change what your brush is about to
  do. You have no pointer until you move one.

**Worked examples, limits and the coordinate contract: `docs/agent-input.md`.**

## How to work

- Combine solid **C# practices** with idiomatic **Godot** patterns (nodes, scenes, signals, resources, lifecycle).
- Prefer Godot’s native architecture over Unity-shaped workarounds (no “fake MonoBehaviour” layer unless there is a clear reason).
- Keep UI/UX decisions CAD-like: explicit tools, predictable transforms, clear selection/feedback, precision over freeform art workflows.
- **A feature is reachable from the command surface, or it is not finished.**
  Not "ask whether" — the default is yes, and leaving it off is the thing that
  needs a reason. The reason goes in the ledger beside the feature, in words,
  and the bar is high: an agent is meant to do what the user does and see what
  the user sees, so anything a person can reach through the interface an agent
  can reach through the dispatcher.
  **What has ever cleared that bar**, so the shape of a real exception is on
  record rather than guessed at: state an agent does not have (the selection,
  the active tool, the clipboard — an agent names what it acts on and holds no
  session), and a rope worth not handing over (the injection switch, which
  cannot be switchable by the instrument it enables; preference writes, which
  outlive the session with nothing to put them back). Difficulty is not on that
  list, and neither is "nobody asked".
  **Reading is half of it, and for anything whose product is a picture, reading
  means a picture.** A material's whole point is how it looks, so it answers with
  a rendered preview and not only with its numbers.
  **Every time you add a feature, ask what the command surface must reach:** This is the canon's "a feature is not finished until it is reachable
  from the dispatcher" (see `docs/architecture.md`), turned into a reflex rather
  than an afterthought: an agent is meant to do what the user does and see what
  the user sees, and any capability that lives only in GUI-specific code is
  invisible to it. If exposing it is obviously worth doing — it is the same
  discipline as accessibility — do it in the same change without asking, and say
  so in your report. If it is genuinely doubtful whether it belongs on the
  surface, ask first, before building, rather than guessing. Reading counts as
  much as writing: a way to query the new state is half of "reachable".

## Language

**Everything you write into the repository MUST be in English.** Source, comments,
identifiers, tests, docs, and commit messages all land in English regardless of the
language of the conversation. Talk to the user in whatever language they use, but
nothing in a tracked file or in git history should be in another language.

**Two carve-outs, and only two**, both because the interface is localized:

- **`src/Localization/Locales/*.po`** — the catalogues. Their `msgid`s are the
  English the source is written in; their `msgstr`s are the translations, and
  every comment in them is a translator's, not ours.
- **The endonym table in `LanguageCodes.GetEndonym`** — what each language calls
  itself. A list of languages is read by recognising your own, so those are the
  one set of words that must not be translated into anything.

Everything else stays English, and that emphatically includes **the agent-facing
surface**: the MCP command names, their parameter names and their descriptions in
`src/Commands` and the `*Commands.cs` presenters are read by a model rather than
by the user. Localizing them would make the editor's vocabulary depend on a
display setting, so no string that reaches an agent goes through
`Localized(...)`.

## Scratch space

Put throwaway files — sample `.vcad` projects, build scripts, one-off output — under
`tmp/` at the repository root. It is git-ignored, so nothing there is committed by
accident, but it is **not** cleaned up automatically: delete what you no longer need
by hand.

## Tests

Ten projects, none of which needs an engine — the layers under them are plain
C# — so together they run in well under a minute (the two that open real
loopback sockets, Mcp and Agent, are the slow ones):

```bash
dotnet test tests/VoxelCAD.Model.Tests/VoxelCAD.Model.Tests.csproj
dotnet test tests/VoxelCAD.Geometry.Tests/VoxelCAD.Geometry.Tests.csproj
dotnet test tests/VoxelCAD.Commands.Tests/VoxelCAD.Commands.Tests.csproj
dotnet test tests/VoxelCAD.Export.Tests/VoxelCAD.Export.Tests.csproj
dotnet test tests/VoxelCAD.Mcp.Tests/VoxelCAD.Mcp.Tests.csproj
dotnet test tests/VoxelCAD.Agent.Tests/VoxelCAD.Agent.Tests.csproj
dotnet test tests/VoxelCAD.Presenter.Tests/VoxelCAD.Presenter.Tests.csproj
dotnet test tests/VoxelCAD.View.Tests/VoxelCAD.View.Tests.csproj
dotnet test tests/VoxelCAD.Preferences.Tests/VoxelCAD.Preferences.Tests.csproj
dotnet test tests/VoxelCAD.Render.Tests/VoxelCAD.Render.Tests.csproj
```

- **After any change under `src/Model/`, `src/CommonTypes/`, `src/Geometry/`,
  `src/Commands/`, `src/Export/`, `src/Mcp/`, `src/Agent/`, `src/Presenter/`,
  `src/View/`, `src/Preferences/` or `src/Render/`, run them.** Not at the end of a batch of
  edits — before reporting anything about that change.
- **A test loop must capture the failing test's name.** Grepping a run for
  `Passed!`/`Failed!` alone throws away the one thing a rare failure gives you:
  one Model run in thirty-five failed here and the name went with the summary
  line, after which thirty-four green runs said nothing at all. Match
  `^  Failed ` too, or an intermittent failure is unrecoverable.
- A model change that adds or alters an invariant lands **with** its test.
- These tests earned their keep on the first run: they caught Godot's
  `Vector3.Round` using banker's rounding (`2.5 → 2`), which would have made grid
  snapping look arbitrary. Assume the same class of surprise is still out there.
- The test project targets `net8.0` like the rest, with `RollForward=LatestMajor`
  because only the .NET 10 runtime is installed on this machine.

## Headless CLI

`tools/VoxelCAD.Cli` drives a project without an engine — it references
`Commands`, and through it `Model` and `CommonTypes`, but never `Presenter`.
Results are one JSON object per line; a failing command stops the run and the
process exits non-zero.

```bash
dotnet run --project tools/VoxelCAD.Cli -- --help
dotnet run --project tools/VoxelCAD.Cli -- --in part.vcad tree
dotnet run --project tools/VoxelCAD.Cli -- --script build.txt --out part.vcad
```

`--in` and `--out` are sugar over the ordinary `open` and `save` commands, so a
one-shot invocation and a script speak the same language. `--mcp` speaks MCP
over stdin/stdout instead — the same command surface, reached by an agent that
launches the CLI as its subprocess. In-editor MCP over HTTP reuses the same
`src/Mcp` server; only the transport differs.

**Do not run `dotnet build` on `VoxelCAD.csproj`** — use Beckett's
`build_csharp`. A plain build leaves intermediate output in a path the editor
does not use, and the generated `AssemblyInfo.cs` then gets globbed as source
and collides. The `.csproj` now excludes those paths explicitly, but the habit
is still worth keeping. Building the libraries and tools directly is fine.

**`build_csharp` checks that the code compiles; it does not refresh what the
editor runs.** It builds into a scratch directory on purpose, so it is safe with
the editor open — and so `.godot/mono/temp/bin/Debug`, the assemblies a play
loads, keep whatever was built there last. Those are rebuilt by Godot's own
build: Beckett's `play_scene` builds before it plays, and with no editor open
`Godot --headless --path . --build-solutions --quit` does the same. Measured
2026-11-11: after `build_csharp` the loaded `VoxelCAD.Presenter.dll` still
carried the previous build's timestamp and hash; after `play_scene` it did not.

## Architecture (canon)

Layers, assemblies, dependency directions, and the Model / View / Presenter
contract are canon and live in their own file — read it before designing or
writing any code:

@docs/architecture.md

## Sacred rules (hard constraints — do NOT break without explicit consent)

These exist because prior agents repeatedly masked symptoms instead of fixing
root causes, broke fundamentals to make a screenshot look right, and never
actually ran the project to verify. That is now forbidden.

### 1. Fix the root cause, never fake a fundamental

You may NOT replace, disable, or fake a **fundamental subsystem** to hide a bug.
Fundamentals include (non-exhaustive):

- **Lighting & shading model** — e.g. do NOT switch a material to `unshaded` and
  bake in hardcoded per-axis "shade" coefficients to imitate light. If lighting
  looks wrong, find why (usually normals, winding, or light setup).
- **Depth / culling / winding** — do NOT flip `NORMAL`, disable depth, or toggle
  cull modes to paper over inside-out or z-fighting artifacts. Find the real
  geometry/winding/order cause. (Reminder: **Godot uses clockwise winding for
  front faces** — the opposite of the OpenGL/right-hand CCW convention.)
- **Transforms & coordinate systems** — no ad-hoc offsets/negations to "make it
  line up".
- **Material / shader model** — no throwaway shaders that contradict the planned
  resource-based material system.
- **Scene architecture** — do NOT violate the product model for local
  convenience (e.g. making `VoxelSet` hard-code knowledge of the scene box /
  bounds when models are supposed to be independent).

### 2. No symptom patches or magic numbers

- No values tuned to the *current* scene scale to hide a defect (e.g. shrinking a
  preview cube to `0.996` to dodge z-fighting). If you need a fudge factor, the
  underlying cause is probably wrong.
- A workaround is allowed ONLY when its root cause is understood, it is documented
  as tech debt (see below), and — if it touches a fundamental above — the user
  has explicitly approved it.

### 3. Verify by running, don't trust confidence

- When the change is visual/behavioral and **Beckett is available, you MUST run
  the project and observe** (screenshot / logs / remote tree) before claiming it
  works. "I'm confident it's correct now" is not verification — prior agents were
  confident and wrong three times in a row on the same bug.
- **Observing is the floor, not the ceiling: a change to something the user
  interacts with is verified by driving it.** A screenshot says a button is drawn;
  only pressing it says it is wired. The editor injects its own input (see
  "Driving the editor" above), so "I could not exercise it" is not available as an
  excuse — click the button, drag the handle, then read the model back and check
  the history for the record it should have left.

### The distinction to always apply

Before shipping, ask: *am I fixing the root cause, or hiding the symptom?* If it's
the latter and it touches any fundamental listed above, **stop and ask for
explicit consent**, presenting the real cause and the proper fix as the default.

## C# Coding Style Guide

Normative for all newly written C#. Formatting, naming, member ordering and
control flow live in their own file:

@docs/csharp-style.md

## Tech debt

This is a **living ledger**. Balance two failure modes: don't silently accumulate
debt, and don't over-engineer ahead of the current stage.

- If you make a change that **reasonably increases tech debt at the current stage**
  (a deliberate shortcut consistent with staged delivery), you MUST record it here:
  **what** the shortcut is, **why** it's acceptable now, and **the proper solution**
  later.
- Prefer a small, honest debt entry over either a silent hack or premature
  architecture.
- When you pay debt down, remove or update its entry.
