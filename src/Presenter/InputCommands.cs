using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Synthesized input: the editor driving its own interface. A debug and self-test
/// instrument, and the one thing no semantic command can stand in for — a
/// transform handle can only be shown to work by being dragged.
/// </summary>
/// <remarks>
/// <b>Not a way to work.</b> Every description here names the command to use
/// instead, because an agent that clicks where it could have called
/// <c>set-voxels</c> or <c>set-transform</c> is an agent quietly making the
/// semantic surface redundant — and that surface is what the whole project is
/// built around.
/// <para>
/// Off unless the preference says otherwise, and registered anyway. A tool that
/// vanishes from the list is worse than one that explains itself: MCP clients
/// cache what they were told at connection, so a tool appearing and disappearing
/// with a setting would leave an agent calling something that is no longer there,
/// with nothing to read about why.
/// </para>
/// <para>
/// They all mutate, so <see cref="ControlGatedDispatcher"/> gates them on control
/// like any edit — which is right twice over: injected input changes the document,
/// and the user's interface is suspended precisely while an agent may inject into
/// it.
/// </para>
/// </remarks>
internal static class InputCommands
{
    private const string Instrument =
        "Debug instrument: this drives the interface, it does not build models.";

    public static IReadOnlyList<CommandDefinition> Create(
        IUiDriver ui,
        IEditorPreferences preferences)
    {
        return new List<CommandDefinition>
        {
            Mutating("input-move", "Moves the pointer. " + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Aim aim = Resolve(ui, arguments);

                    ui.Move(aim.Point);

                    return CommandResult.Success(Landed(aim));
                }),
                AimParameters()),

            Mutating("input-click",
                "Clicks a control. To place voxels use 'set-voxels' or 'fill-box'; this is for "
                + "exercising the interface itself. " + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Aim aim = Resolve(ui, arguments);
                    HoldAction action = arguments.Enum("action", HoldAction.Tap);
                    int clicks = arguments.Has("clicks") ? arguments.Int("clicks") : 1;

                    // A held press is one press, and a double click is a pair the
                    // engine has to be told about. Asking for both is asking for
                    // something with no meaning, so it is refused rather than
                    // resolved by a rule nobody could guess.
                    if (action != HoldAction.Tap && clicks != 1)
                    {
                        throw new CommandException(
                            $"A '{action.ToString().ToLowerInvariant()}' is a single press, so "
                            + "'clicks' does not apply to it.");
                    }

                    ui.Click(
                        aim.Point,
                        ReadButton(arguments),
                        ReadModifiers(arguments),
                        clicks,
                        action);

                    Dictionary<string, object?> landed = Landed(aim);
                    landed["action"] = action.ToString();

                    return CommandResult.Success(landed);
                }),
                AimParameters(
                    Optional("button", "left, right or middle. Defaults to left."),
                    Optional("modifiers", "Held for the click: shift, alt, ctrl, meta — comma "
                        + "separated. Genuinely held, not just flagged."),
                    Optional("clicks", "1, 2 or 3. Defaults to 1."),
                    Optional("action", "tap, press or release. Defaults to tap. A press leaves "
                        + "the button down so a gesture stays in flight — move with 'input-move', "
                        + "read what the editor shows, then release. That is the only way to see "
                        + "what a drag shows while a handle is held."))),

            Mutating("input-drag",
                "Drags from one point to another, a step per frame. To move an element use "
                + "'set-transform'; this is for exercising the handles that do. " + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Aim from = Resolve(ui, arguments);
                    Vector2 to = ReadPoint(arguments, "to");

                    ui.Drag(
                        from.Point,
                        to,
                        arguments.Has("steps") ? arguments.Int("steps") : 12,
                        ReadButton(arguments),
                        ReadModifiers(arguments));

                    Dictionary<string, object?> landed = Landed(from);
                    landed["to"] = new[] { Round(to.X), Round(to.Y) };

                    return CommandResult.Success(landed);
                }),
                AimParameters(
                    Required("to", "Where the drag ends, 'x,y' in input coordinates."),
                    Optional("steps", "Motions between the ends, one per frame. 1 to 120, 12 by "
                        + "default. Fewer is a coarser path, not a faster drag."),
                    Optional("button", "left, right or middle. Defaults to left."),
                    Optional("modifiers", "Held for the drag: shift, alt, ctrl, meta."))),

            Mutating("input-key",
                "Presses a key. Tool and camera keys go through the editor's own bindings, so "
                + "this is how a tool is switched from here. " + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Key key = ReadKey(arguments);
                    HoldAction action = arguments.Enum("action", HoldAction.Tap);

                    ui.Key(key, action, ReadModifiers(arguments));

                    return CommandResult.Success(new Dictionary<string, object?>
                    {
                        ["key"] = key.ToString(),
                        ["action"] = action.ToString()
                    });
                }),
                Required("key", "A Godot key name: W, Escape, Left, F, Z. Case-insensitive."),
                Optional("action", "tap, press or release. Defaults to tap. Press and release "
                    + "exist for keys the editor reads as held — the camera arrows."),
                Optional("modifiers", "Held with it: shift, alt, ctrl, meta.")),

            Mutating("input-text",
                "Types a string into whatever holds the keyboard. To rename something use "
                + "'rename-element' or 'rename-material'. " + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    var text = arguments.String("text");

                    if (text.Length > InputTextLimit)
                    {
                        throw new CommandException(
                            $"That is {text.Length} characters, over the {InputTextLimit} this "
                            + "types in one go.");
                    }

                    ui.TypeText(text);

                    return CommandResult.Success(new Dictionary<string, object?>
                    {
                        ["typed"] = text.Length
                    });
                }),
                Required("text", "What to type. Up to 256 characters.")),

            Mutating("input-wheel",
                "Turns the wheel over a point. To aim the camera use 'zoom' or 'orbit'; this is "
                + "for exercising what a wheel is wired to. " + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Aim aim = Resolve(ui, arguments);
                    int notches = arguments.Int("notches");

                    ui.Wheel(aim.Point, notches, ReadModifiers(arguments));

                    Dictionary<string, object?> landed = Landed(aim);
                    landed["notches"] = notches;

                    return CommandResult.Success(landed);
                }),
                AimParameters(
                    Required("notches", "Positive scrolls up, negative down. -32 to 32."),
                    Optional("modifiers", "Held for it: shift, alt, ctrl, meta."))),

            Mutating("input-pan",
                "A trackpad's two-finger scroll over a point, carrying a precise delta rather "
                + "than a wheel notch. A wheel and a trackpad are different events, and code "
                + "wired to one is not exercised by the other — which is what this exists for. "
                + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Aim aim = Resolve(ui, arguments);

                    var delta = new Vector2(
                        arguments.Float("dx", 0f),
                        arguments.Float("dy", 0f));

                    int steps = arguments.Int("steps", 8);

                    ui.Pan(aim.Point, delta, steps, ReadModifiers(arguments));

                    Dictionary<string, object?> landed = Landed(aim);
                    landed["delta"] = $"{delta.X},{delta.Y}";

                    return CommandResult.Success(landed);
                }),
                AimParameters(
                    Optional("dx", "Across, in the gesture's own units — about one per notch."),
                    Optional("dy", "Down, in the same units."),
                    Optional("steps", "How many frames to spread it over. 2 to 64, defaults to 8."),
                    Optional("modifiers", "Held for it: shift, alt, ctrl, meta."))),

            Mutating("input-pinch",
                "A trackpad's pinch over a point, as the factor it multiplies by over the "
                + "whole gesture — 2 spreads the fingers to twice, 0.5 closes them to half. "
                + Instrument,
                (_, arguments) => Guarded(preferences, () =>
                {
                    Aim aim = Resolve(ui, arguments);
                    float factor = arguments.Float("factor");
                    int steps = arguments.Int("steps", 8);

                    ui.Magnify(aim.Point, factor, steps, ReadModifiers(arguments));

                    Dictionary<string, object?> landed = Landed(aim);
                    landed["factor"] = factor;

                    return CommandResult.Success(landed);
                }),
                AimParameters(
                    Required("factor", "What the whole gesture multiplies by. 0.05 to 20."),
                    Optional("steps", "How many frames to spread it over. 2 to 64, defaults to 8."),
                    Optional("modifiers", "Held for it: shift, alt, ctrl, meta.")))
        };
    }

    private const int InputTextLimit = 256;

    /// <summary>
    /// The instrument is off unless it has been switched on, and the refusal says
    /// where the switch is. Registered either way, so an agent that calls one
    /// learns why rather than finding nothing there.
    /// </summary>
    private static CommandResult Guarded(IEditorPreferences preferences, Func<CommandResult> run)
    {
        if (!preferences.InputInjectionEnabled)
        {
            return CommandResult.Failure(
                "Driving the interface is switched off. Turn on \"Let an agent drive the "
                + "interface\" under File → Preferences. It is a debug instrument — to build "
                + "a model use 'set-voxels', 'fill-box' and 'set-transform' instead.");
        }

        return run();
    }

    /// <summary>
    /// Where a gesture will land, and what it resolved through. A target is the
    /// documented way; coordinates exist for what has no control behind it, such as
    /// a point in the 3D scene or a transform handle.
    /// </summary>
    private readonly record struct Aim(Vector2 Point, UiControl Control, bool ByTarget);

    private static Aim Resolve(IUiDriver ui, CommandArguments arguments)
    {
        if (arguments.Has("target"))
        {
            Vector2 offset = arguments.Has("offset")
                ? ReadPoint(arguments, "offset")
                : new Vector2(0.5f, 0.5f);

            InputTargetResolution resolution = ui.Resolve(arguments.String("target"), offset);

            if (!resolution.Ok)
                throw new CommandException(resolution.Refusal);

            return new Aim(resolution.Point, resolution.Control, true);
        }

        if (!arguments.Has("at"))
        {
            throw new CommandException(
                "Pass 'target' — a reference from 'ui' — or 'at' as 'x,y' in input coordinates. "
                + "A target is the one to prefer: it survives a relayout and refuses when the "
                + "control is hidden, disabled or covered.");
        }

        return new Aim(ReadPoint(arguments, "at"), default, false);
    }

    /// <summary>
    /// Where it actually went, always — so an interaction that did nothing can be
    /// diagnosed off the transcript instead of off a screenshot taken afterwards.
    /// </summary>
    private static Dictionary<string, object?> Landed(Aim aim)
    {
        var landed = new Dictionary<string, object?>
        {
            ["at"] = new[] { Round(aim.Point.X), Round(aim.Point.Y) }
        };

        if (!aim.ByTarget)
            return landed;

        landed["target"] = aim.Control.Reference;
        landed["type"] = aim.Control.Type;

        if (aim.Control.Text.Length > 0)
            landed["text"] = aim.Control.Text;

        return landed;
    }

    /// <summary>
    /// Invariant, like every other number that crosses this surface: a caller
    /// sends "0.5" whatever the editor's locale thinks a decimal point is, and a
    /// culture where '.' groups digits would silently read it as five.
    /// </summary>
    private static Vector2 ReadPoint(CommandArguments arguments, string name)
    {
        var text = arguments.String(name);
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 2
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        {
            throw new CommandException($"Argument '{name}' must be 'x,y', got '{text}'.");
        }

        return new Vector2(x, y);
    }

    private static MouseButton ReadButton(CommandArguments arguments) =>
        arguments.String("button", "left").ToLowerInvariant() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            var other => throw new CommandException(
                $"Argument 'button' must be left, right or middle, got '{other}'.")
        };

    private static Key ReadKey(CommandArguments arguments)
    {
        var text = arguments.String("key");

        if (Enum.TryParse(text, ignoreCase: true, out Key key))
            return key;

        throw new CommandException(
            $"Argument 'key' must be a key name such as W, Escape or Left, got '{text}'.");
    }

    /// <summary>
    /// A comma-separated list rather than four flags, because they are held
    /// together or not at all and reading "shift,alt" is reading the gesture.
    /// </summary>
    private static InputModifiers ReadModifiers(CommandArguments arguments)
    {
        if (!arguments.Has("modifiers"))
            return InputModifiers.None;

        var text = arguments.String("modifiers");
        var modifiers = InputModifiers.None;

        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            modifiers |= part.Trim().ToLowerInvariant() switch
            {
                "shift" => InputModifiers.Shift,
                "alt" or "option" => InputModifiers.Alt,
                "ctrl" or "control" => InputModifiers.Ctrl,
                "meta" or "cmd" or "command" => InputModifiers.Meta,
                var other => throw new CommandException(
                    $"Argument 'modifiers' must be shift, alt, ctrl or meta, got '{other}'.")
            };
        }

        return modifiers;
    }

    private static double Round(float value) => Math.Round(value, 1);

    /// <summary>The two ways to aim, plus whatever else a command takes.</summary>
    private static CommandParameter[] AimParameters(params CommandParameter[] rest)
    {
        var parametersList = new List<CommandParameter>
        {
            Optional("target", "A control reference from 'ui'. The way to aim: it refuses when "
                + "the control is hidden, disabled, zero-sized or covered by something else."),
            Optional("offset", "Where inside the target's rect, 'x,y' from 0,0 to 1,1. Its centre "
                + "by default. How to reach a row of a list, which is not a control of its own."),
            Optional("at", "An explicit point, 'x,y' in the coordinates 'ui' and 'window' report. "
                + "For what has no control behind it — a point in the 3D scene, a gizmo handle.")
        };

        parametersList.AddRange(rest);

        return parametersList.ToArray();
    }

    private static CommandParameter Required(string name, string summary) => new(name, summary, true);

    private static CommandParameter Optional(string name, string summary) => new(name, summary, false);

    /// <summary>
    /// Gated like an edit, because injecting input is something an agent does while
    /// holding the editor — but marked as driving rather than acting, so a gesture
    /// the interface has under way survives the keystrokes that are making it.
    /// </summary>
    private static CommandDefinition Mutating(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters, drives: true), handler);
}
