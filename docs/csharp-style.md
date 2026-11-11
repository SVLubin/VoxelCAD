# C# Coding Style Guide

This section is normative. Follow it for all newly written C# code unless a task
explicitly requires a different format. The keywords MUST, SHOULD, and MAY
indicate requirement strength.

> **API note.** Some examples below use Unity API names, matching the user's
> background. The *formatting* rules are what's normative; in this project use
> the Godot equivalent — `_Process`/`_PhysicsProcess` for `Update`/`LateUpdate`,
> `GD.PushError`/`GD.PrintErr` for `Debug.LogError`, `[Export]` for
> `[SerializeField]`, `[Signal]` delegates for C# `event` on `Node` types,
> `Vector3I` for `Vector2Int`. Godot `Node` classes MUST be `partial`, and stay
> `public` where Godot instantiates or serializes them.

## General Principles

- Optimize code for readability, predictable structure, and safe modification.
- Prefer explicit domain meaning over cleverness or brevity.
- Keep changes focused. Do not reformat unrelated files.
- Prefer small cohesive types and methods over large multipurpose classes.
- Preserve public API names, serialized field names, persisted enum values, and
  externally consumed contracts unless migration is part of the task.
- Do not imitate isolated formatting anomalies. Apply this guide consistently.

## Files And Namespaces

- Use one primary type per `.cs` file.
- Give the file the same name as its primary type.
- Small private nested types and tightly coupled enums or delegates MAY share the
  primary type's file.
- Use file-scoped namespaces:

```csharp
namespace Example.Gameplay.Units;

internal sealed class UnitController
{
}
```

- Do not use block-scoped namespaces. One type per file makes their only real
  advantage — several namespaces in one file — irrelevant, and they cost an
  indentation level on every line of the codebase.
- Namespace segments use `PascalCase`.
- Namespace structure SHOULD reflect the project's folder and domain structure.
- Place all `using` directives before the namespace.
- Order `using` directives as follows:
  1. `System` namespaces.
  2. Third-party/framework namespaces.
  3. Project namespaces.
  4. Engine namespaces (`Godot`), when applicable.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Document;
using VoxelCAD.Editor.UI;
using Godot;
```

- Remove unused imports.
- Put editor-only imports inside the matching preprocessor guard.
- Preprocessor directives MUST begin at column zero.
- Do not use `#region` to organize ordinary code.

## Naming

### Types And Members

Use `PascalCase` for:

- Namespaces.
- Classes, structs, records, interfaces, delegates, and enums.
- Methods and local functions.
- Properties and events.
- Enum members.
- Constants declared at type level.
- `static readonly` fields.

Use `camelCase` for:

- Method parameters.
- Local variables.
- Lambda parameters.
- Method-local constants.

Use `_camelCase` for:

- All private instance fields.
- Serialized private fields.
- Bound or injected private fields.
- Private `readonly` fields.

```csharp
private const int MaximumAttempts = 3;
private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

private readonly List<UnitState> _unitsList = new();
private int _currentPlayerId;

public bool IsFinished { get; private set; }
```

### Interfaces And Role Suffixes

- Interface names MUST start with `I`: `IUnitState`, `IMapView`.
- Abstract reusable base classes SHOULD end with `Base`.
- Extension-method containers MUST end with `Extensions`.
- Use consistent architectural suffixes: `State`, `Snapshot`, `Event`,
  `Declaration`, `Command`, `Presenter`, `View`, `Controller`, `Service`,
  `Provider`, `Factory`, `Config`, `Container`, `Pool`, `Helper`, `Extensions`.

```csharp
UnitState
UnitSnapshot
UnitMovedEvent
UnitMoveDeclaration
UnitMoveCommand
UnitPresenter
UnitView
UnitSelectionController
UnitFactory
```

### Methods

Method names MUST describe an action. Use these prefixes consistently:

- `Get` — retrieve a required value; absence may throw.
- `TryGet` — attempt retrieval and return `bool` with an `out` parameter.
- `Has` — test possession or membership.
- `Is` — test current state.
- `Can` — test permission or capability.
- `Should` — make a policy or lifecycle decision.
- `Calculate` — derive a value without committing state changes.
- `Create` — construct a new value or object.
- `Spawn` — acquire and activate a runtime or pooled object.
- `Apply` — apply a prepared state transition.
- `Set` — assign a specific value.
- `Change` — perform a domain transition.
- `Reset` or `Clear` — return owned state to its initial condition.
- `Despawn` — release a runtime or pooled object.

Methods returning `Task`, `ValueTask`, or another awaitable MUST end with `Async`.

```csharp
public bool TryGetUnit(int unitId, out IUnitState unitState);
public float CalculateDamage(IUnitState attacker, IUnitState defender);
public async Task LoadBattleAsync(CancellationToken cancellationToken);
```

### Booleans

Boolean names MUST read naturally inside a condition.

Good: `IsFinished`, `HasSelection`, `CanAttack`, `ShouldSkip`, `Visible`, `ApplyRecoil`.

Avoid: `FinishedFlag`, `AttackCheck`, `SelectionValue`, `DoRecoil`.

### Collections

Private mutable collection fields SHOULD include their concrete collection kind:

```csharp
private readonly List<UnitState> _unitsList = new();
private readonly Dictionary<int, UnitState> _unitsByIdDictionary = new();
private readonly HashSet<int> _visibleUnitIdsHashSet = new();
private readonly Queue<ICommand> _commandsQueue = new();
private readonly Stack<StateSnapshot> _snapshotsStack = new();
```

Arrays and read-only collection properties use plural nouns:

```csharp
private UnitView[] _unitViews;

public IReadOnlyList<UnitState> Units => _unitsList;
```

### Acronyms

- Use `Id`, not `ID`, inside ordinary identifiers.
- Preserve universally recognized domain acronyms such as `UI`, `API`, `URL`, or
  project-specific proper names.
- Avoid invented abbreviations.

Good: `playerId`, `targetPosition`, `configuration`, `UIController`, `apiUrl`.

Avoid: `playerID`, `tgtPos`, `cfg`.

## Accessibility And Type Design

- Always use the narrowest practical accessibility.
- Concrete implementation types SHOULD be `internal` unless another assembly or
  engine serialization requires public access.
- Expose interfaces and immutable contracts across architectural boundaries.
- Mark leaf classes `sealed` when inheritance is not intended.
- Mark immutable structs `readonly`.
- Mark fields `readonly` when they are assigned only during declaration or construction.
- Prefer properties over public mutable fields.
- Prefer get-only properties or restricted setters:

```csharp
public int Id { get; }

public bool IsInitialized { get; private set; }

internal int CurrentHealth { get; set; }
```

- Use explicit interface implementation when a member should only be available
  through a specific contract:

```csharp
int IUnitState.CurrentHealth
{
	get => _currentHealth;
	set => _currentHealth = value;
}
```

- Use `const` only for compile-time values.
- Type-level constants use `PascalCase`. Local constants use `camelCase`.

```csharp
private const float MinimumOpacity = 0.25f;

private void ApplyOpacity(float opacity)
{
	const float epsilon = 0.0001f;
}
```

## Member Ordering

Use the following default order in a new type:

1. Constants.
2. `static readonly` fields.
3. Serialized or injected fields.
4. Other `readonly` fields.
5. Mutable fields.
6. Events.
7. Public properties.
8. Internal and protected properties.
9. Constructors.
10. Framework or engine lifecycle methods.
11. Public methods.
12. Internal methods.
13. Protected methods and overrides.
14. Private methods.
15. Private nested types.

Keep overloads adjacent. Group related members with one blank line. Do not insert
blank lines between every simple field or assignment.

## Indentation And Whitespace

- Use four spaces per indentation level.
- Never use tabs.
- Use LF line endings.
- Remove trailing whitespace.
- Use exactly one blank line between methods.
- Use one blank line between logical member groups.
- Do not use multiple consecutive blank lines.
- Put one statement per line.
- Put one variable declaration per line.

Operators have spaces around them; commas are followed by a space; parentheses
have no inner padding:

```csharp
var damage = attack * multiplier - defense;
CreateUnit(unitId, position, ownerId);
CalculateDamage(attacker, defender);
```

Control-flow keywords have a space before `(`; method calls do not:

```csharp
if (condition)
while (condition)
for (var i = 0; i < count; i++)

UpdateState();
```

## Braces

Use Allman braces for types, methods, property accessors with bodies,
lambdas with bodies, and multi-statement control flow.

```csharp
public void ApplyDamage(int amount)
{
	if (amount < 0)
	{
		Debug.LogError($"Invalid damage amount: {amount}");
		return;
	}

	_currentHealth -= amount;
}
```

Omit braces for one simple statement:

```csharp
if (_currentHealth <= 0)
	Despawn();

foreach (var unit in units)
	unit.ResetState();
```

Use braces when:

- The body has multiple statements.
- The body contains nested control flow.
- A comment belongs specifically to the branch.
- Omitting braces would make the structure ambiguous.

Empty virtual hooks and empty constructors remain on one line:

```csharp
protected virtual void OnReset() { }
```

Place `else`, `catch`, and `finally` on a new line:

```csharp
if (condition)
{
	Apply();
}
else
{
	Reset();
}
```

## Line Length And Wrapping

- Target approximately 100 characters per line.
- Lines up to approximately 120 characters are acceptable when wrapping would
  reduce readability.
- Wrap dense conditions, signatures, calls, interpolations, and generic types earlier.
- Do not split a short and readable expression only to satisfy a strict number.

### Method Declarations

Keep short declarations on one line:

```csharp
public bool HasUnit(int unitId);
```

Put every parameter on a separate line when wrapping:

```csharp
public UnitState CreateUnit(
	int unitId,
	Vector2Int position,
	int ownerPlayerId,
	UnitConfig configuration)
{
}
```

Indent continuation lines by one additional indentation level. Do not align
parameters using large runs of spaces.

### Constructor Initializers

Put a non-trivial constructor initializer on the next line:

```csharp
public UnitMoveCommand(
	BattleContext context,
	UnitMovedEvent targetEvent)
	: base(context, targetEvent)
{
}
```

A short expression-bodied constructor may remain compact:

```csharp
public UnitId(int value) =>
	Value = value;
```

### Method Calls

Keep short calls on one line:

```csharp
unit.SetPosition(position);
```

For wrapped calls, place arguments on separate lines:

```csharp
modelState.MoveUnit(
	unitState,
	declaration.MovePath,
	declaration.MoveType,
	pushLastAction: true);
```

Nested construction should expose its structure:

```csharp
commandsList.Add(
	new SequenceCommand(
		waitForReleasePointCommand,
		consequenceCommand));
```

### Fluent Chains And LINQ

Keep the receiver on the first line and begin continuation lines with `.`:

```csharp
var visibleUnits = units
	.Where(unit => unit.Visible)
	.OrderBy(unit => unit.Position.x)
	.ThenBy(unit => unit.Position.y)
	.ToArray();
```

### Conditions

Break long boolean expressions before the operator:

```csharp
if (unitState.IsValid
	&& unitState.HasOwner
	&& unitState.CurrentHealth > 0)
	ProcessUnit(unitState);
```

For mixed operators, add parentheses when precedence is not immediately obvious:

```csharp
if ((isPlayerTurn || forceExecution)
	&& command.CanExecute)
	command.Execute();
```

Order conditions from fundamental or inexpensive checks to more specific or
expensive checks.

### Ternary Expressions

Use a ternary only when it remains easy to scan:

```csharp
var sound = targetSurvived
	? Sound.Attack
	: Sound.Kill;
```

Do not use deeply nested ternaries. Prefer `if` or `switch`.

### Long Strings

Split long interpolated strings into adjacent parts:

```csharp
Debug.LogError(
	$"Cannot move unit {unitState.Id} from {startPosition} to {endPosition}. " +
	$"The destination is occupied by {targetUnitState.Id}.");
```

## Properties And Expression Bodies

Use expression-bodied properties for direct retrieval or calculation:

```csharp
public bool HasSelection => _selectedUnitsList.Count > 0;

public int CurrentPlayerId => _gameState.CurrentPlayer.Id;
```

Use a full property when an accessor performs validation, mutation, logging, or
multiple steps:

```csharp
public Sprite Sprite
{
	get => _spriteRenderer.sprite;
	set
	{
		if (_spriteRenderer.sprite == value)
			return;

		_spriteRenderer.sprite = value;
		UpdateVisibility();
	}
}
```

Expression-bodied methods are appropriate for a single obvious operation:

```csharp
public bool HasUnit(int unitId) =>
	_unitsByIdDictionary.ContainsKey(unitId);
```

Use a block when a method has multiple conceptual steps.

## Type Inference And Object Creation

Use `var` when the type is clear from the right-hand side:

```csharp
var unitState = new UnitState(unitId);
var currentPlayer = gameState.GetCurrentPlayer();
```

Use an explicit type when it communicates an important boundary or the result
type is not obvious:

```csharp
IUnitState unitState = new UnitState(unitId);
float duration = tween.Duration();
Vector2Int position = map.GetPosition(unitId);
```

Use target-typed `new()` when the declared type is visible:

```csharp
private readonly List<UnitState> _unitsList = new();
private readonly Dictionary<int, UnitState> _unitsByIdDictionary = new();
```

Use named constructor arguments when they document a non-obvious value:

```csharp
private readonly List<UnitState> _bufferList = new(capacity: 256);
```

## Control Flow

- Prefer guard clauses over deep nesting.
- Return early when the operation is no longer applicable.
- Avoid more than two or three levels of nested control flow.
- Extract a named method when a condition or branch represents a meaningful
  domain operation.

Preferred:

```csharp
public void Process(UnitState unitState)
{
	if (unitState == null)
		throw new ArgumentNullException(nameof(unitState));

	if (!unitState.IsValid)
		return;

	if (!CanProcess(unitState))
		return;

	ApplyProcessing(unitState);
}
```

Avoid:

```csharp
public void Process(UnitState unitState)
{
	if (unitState != null)
	{
		if (unitState.IsValid)
		{
			if (CanProcess(unitState))
			{
				ApplyProcessing(unitState);
			}
		}
	}
}
```

Use switch expressions for value mapping:

```csharp
private static Color GetTeamColor(Team team) =>
	team switch
	{
		Team.Blue => Color.blue,
		Team.Red => Color.red,
		Team.Neutral => Color.gray,
		_ => throw new ArgumentOutOfRangeException(nameof(team), team, null)
	};
```

Use statement switches for multi-step behavior:

```csharp
switch (command.State)
{
	case CommandState.Pending:
		command.Start();
		break;

	case CommandState.Running:
		command.Update();
		break;

	case CommandState.Finished:
	{
		var result = command.GetResult();
		PublishResult(result);
		break;
	}

	default:
		throw new ArgumentOutOfRangeException();
}
```

## Null Handling

- Validate required dependencies and arguments early.
- Use nullable annotations when enabled by the project.
- Do not use the null-forgiving operator merely to silence a warning.
- Use `!` only when a previously established invariant proves non-null.
- Use `?.` for optional callbacks and event invocation:

```csharp
Finished?.Invoke();
```

- Use `??` for clear fallback values:

```csharp
var units = providedUnits ?? Array.Empty<UnitState>();
```

For ordinary CLR references, use `is null` and `is not null` when helpful:

```csharp
if (result is null)
	return;
```

For engine objects, remember that a `GodotObject` reference can outlive the
native instance — check `GodotObject.IsInstanceValid(node)` rather than assuming
a non-null reference is usable.

## Collections And Allocation

- Use `Array.Empty<T>()` instead of allocating an empty array.
- Do not expose a mutable owned collection directly.
- Return `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, or `IEnumerable<T>` as appropriate.
- Use `TryGetValue` instead of separate dictionary lookup and indexing.
- Use `foreach` for ordinary readable iteration.
- Use `for` for indexed traversal or allocation-sensitive hot paths.
- LINQ is allowed when it improves clarity outside hot paths.
- Avoid LINQ, closures, iterator allocations, and temporary arrays in:
  - `_Process` and `_PhysicsProcess`.
  - Rendering, remeshing, and visibility loops.
  - Picking and raycasting.
  - Pooling operations.
  - Animation and tween update callbacks.
  - Frequently executed input or gameplay calculations.

## Events And Subscriptions

- Events use `PascalCase` and describe something that occurred:

```csharp
public event Action SelectionChanged;
public event Action<UnitState> UnitSpawned;
```

- Raise nullable events using `?.Invoke`.
- Name handlers with the `On` prefix:

```csharp
private void OnUnitSpawned(UnitState unitState)
{
}
```

- Every subscription that has a limited lifetime MUST have a matching unsubscription.
- Prefer named methods over anonymous lambdas when unsubscription is required.

```csharp
public override void _EnterTree()
{
	_unitService.UnitSpawned += OnUnitSpawned;
}

public override void _ExitTree()
{
	_unitService.UnitSpawned -= OnUnitSpawned;
}
```

## Exceptions And Logging

Use:

- `ArgumentNullException` for required null arguments.
- `ArgumentException` for invalid argument combinations.
- `ArgumentOutOfRangeException` for unsupported values.
- `InvalidOperationException` for impossible state or lifecycle transitions.
- `KeyNotFoundException` only when required lookup semantics justify it.

Use `Try...` methods for expected absence. Do not use exceptions as ordinary branching.

```csharp
public UnitState GetUnit(int unitId)
{
	if (!_unitsByIdDictionary.TryGetValue(unitId, out var unitState))
		throw new KeyNotFoundException($"Unit {unitId} is not registered.");

	return unitState;
}

public bool TryGetUnit(int unitId, out UnitState unitState) =>
	_unitsByIdDictionary.TryGetValue(unitId, out unitState);
```

Log errors when the application can continue and runtime context is useful:

```csharp
GD.PushError(
	$"[{nameof(VoxelWorld)}] Cannot place a voxel at {cell}. " +
	$"Expected a cell inside 0..{BoxSize}.");
```

Do not catch broad exceptions inside ordinary implementation methods. Catch them
only at a genuine execution boundary, then log the full exception.

## Comments And Documentation

- Comments explain intent, invariants, ordering, compatibility, or non-obvious tradeoffs.
- Do not narrate what the code already states.
- Keep comments current when changing nearby behavior.
- Remove obsolete commented-out implementations.
- Use these markers for new comments:

```csharp
// TODO: Describe required future work.
// NOTE: Explain a non-obvious invariant or constraint.
```

- Public APIs SHOULD receive XML documentation when their contract, side effects,
  ownership, lifetime, or failure behavior is not obvious.
- Do not add empty boilerplate XML comments that merely repeat a member name.

## Canonical Example

```csharp
using System;
using System.Collections.Generic;

namespace Example.Gameplay.Units;

internal sealed class UnitSelectionService
{
    private const int MaximumSelectedUnits = 12;

    private readonly List<UnitState> _selectedUnitsList = new();
    private readonly HashSet<int> _selectedUnitIdsHashSet = new();

    public event Action<IReadOnlyList<UnitState>> SelectionChanged;

    public bool HasSelection => _selectedUnitsList.Count > 0;

    public IReadOnlyList<UnitState> SelectedUnits => _selectedUnitsList;

    public bool TrySelect(UnitState unitState)
    {
        if (unitState == null)
            throw new ArgumentNullException(nameof(unitState));

        if (!CanSelect(unitState))
            return false;

        _selectedUnitsList.Add(unitState);
        _selectedUnitIdsHashSet.Add(unitState.Id);

        SelectionChanged?.Invoke(_selectedUnitsList);

        return true;
    }

    public bool Deselect(int unitId)
    {
        if (!_selectedUnitIdsHashSet.Remove(unitId))
            return false;

        var unitIndex = _selectedUnitsList.FindIndex(unit => unit.Id == unitId);

        if (unitIndex < 0)
            throw new InvalidOperationException(
                $"Unit {unitId} exists in the selected ID set but not in the selected unit list.");

        _selectedUnitsList.RemoveAt(unitIndex);
        SelectionChanged?.Invoke(_selectedUnitsList);

        return true;
    }

    public void Clear()
    {
        if (!HasSelection)
            return;

        _selectedUnitsList.Clear();
        _selectedUnitIdsHashSet.Clear();

        SelectionChanged?.Invoke(_selectedUnitsList);
    }

    private bool CanSelect(UnitState unitState) =>
        unitState.IsSelectable
        && !_selectedUnitIdsHashSet.Contains(unitState.Id)
        && _selectedUnitsList.Count < MaximumSelectedUnits;
}
```

## Completion Checklist

Before completing a C# change, verify:

- Names follow the defined casing and role conventions.
- The primary type and filename match.
- Access modifiers are as narrow as practical.
- Mutable collections are not exposed directly.
- Private fields use `_camelCase`.
- Type-level constants use `PascalCase`.
- The file uses four spaces and Allman braces.
- There is no trailing whitespace or unrelated reformatting.
- Long signatures, calls, conditions, and chains are wrapped consistently.
- Guard clauses are used instead of unnecessary nesting.
- Required failures throw specific exceptions.
- Expected absence uses `Try...` APIs.
- Async methods end with `Async`.
- Event subscriptions have matching unsubscriptions.
- Hot paths avoid unnecessary allocation.
- Serialized names and enum values remain compatible.
- Relevant tests and compilation checks pass.

