using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// The commands that read the editor's interface rather than its document: a
/// picture of the window, and the control tree behind it. Editor-only, like the
/// camera and control commands and for the same reason — a headless run has no
/// window to look at.
/// </summary>
/// <remarks>
/// Both are <c>mutates: false</c>. Looking at the interface is looking, on
/// exactly the terms <c>slice</c> and <c>frame</c> already have: an agent may do
/// it before it holds control, because making it wait to see the screen would be
/// absurd.
/// <para>
/// The division of labour between them is the point. <c>window</c> answers "does
/// this look right"; <c>ui</c> answers "what is there and where". Aiming input is
/// <c>ui</c>'s job — a reference from it survives a relayout, a pixel read off a
/// screenshot does not.
/// </para>
/// </remarks>
internal static class InterfaceCommands
{
    public static IReadOnlyList<CommandDefinition> Create(ISceneView scene, IUiDriver ui)
    {
        return new List<CommandDefinition>
        {
            Query("window", "Captures the whole editor window, chrome included — docks, dimmer, "
                + "plaque. For looking, not for measuring: aim input with 'ui' references, not "
                + "with pixels read off this picture. Use 'frame' for the scene alone.",
                (_, arguments) =>
                {
                    FrameCaptureOptions options =
                        FrameCaptureArguments.Read(arguments, includeChrome: true);

                    return FrameCaptureArguments.Describe(scene.CaptureFrame(options), options.Format);
                },
                FrameCaptureArguments.CreateParameters()),

            Query("ui", "Lists the editor's controls — what they are, what they say, and where "
                + "they are in the coordinates input is aimed in. The reference each one carries "
                + "is what 'input-click' and 'input-drag' take as their target.",
                (_, arguments) =>
                {
                    var options = new UiSnapshotOptions(
                        arguments.String("root", string.Empty),
                        arguments.Has("depth") ? arguments.Int("depth") : int.MaxValue,
                        arguments.Bool("invisible", false),
                        arguments.String("contains", string.Empty));

                    UiSnapshot snapshot = ui.ReadControls(options);

                    if (snapshot.TotalInScope > snapshot.Limit)
                    {
                        throw new CommandException(
                            $"That reading is {snapshot.TotalInScope} controls, over the "
                            + $"{snapshot.Limit} an answer carries. Pass 'root' to start from one "
                            + "panel, or 'depth' to stop short of the leaves.");
                    }

                    return Describe(snapshot, ui.GetWindowSize());
                },
                Optional("root", "A reference to start from, from an earlier reading. Defaults to "
                    + "the whole shell."),
                Optional("depth", "How many levels below 'root' to report. Unbounded by default."),
                Optional("invisible", "Report controls that are not on screen too. Off by default — "
                    + "an invisible control cannot be clicked."),
                Optional("contains", "Only the controls whose reference, words or type hold this, "
                    + "ignoring case. The whole shell is a few hundred controls and most of them "
                    + "are containers; this is how to find the one you mean."))
        };
    }

    /// <summary>
    /// In draw order, which is the order the walk found them in: a control listed
    /// later is drawn over one listed earlier. An agent reading which of two
    /// overlapping controls a point belongs to reads the later one.
    /// </summary>
    private static CommandResult Describe(UiSnapshot snapshot, Vector2I windowSize)
    {
        var controlsList = new List<object?>(snapshot.Controls.Count);

        foreach (UiControl control in snapshot.Controls)
        {
            var fields = new Dictionary<string, object?>
            {
                ["ref"] = control.Reference,
                ["type"] = control.Type,
                ["rect"] = new[]
                {
                    Round(control.Rect.Position.X),
                    Round(control.Rect.Position.Y),
                    Round(control.Rect.Size.X),
                    Round(control.Rect.Size.Y)
                },
                ["visible"] = control.Visible,
                ["disabled"] = control.Disabled,
                ["focused"] = control.Focused,
                ["takesMouse"] = control.TakesMouse
            };

            // Only when there is something to read: most of the tree is
            // containers, and an empty string per row is a third of the answer.
            if (control.Text.Length > 0)
                fields["text"] = control.Text;

            // Only when it is not the main window, where everything usually is.
            // Non-zero says "drawn over the rest", which is the whole of what a
            // popup or a dialog changes about aiming.
            if (control.Layer > 0)
                fields["layer"] = control.Layer;

            controlsList.Add(fields);
        }

        return CommandResult.Success(new Dictionary<string, object?>
        {
            ["windowSize"] = new[] { windowSize.X, windowSize.Y },
            ["count"] = controlsList.Count,
            ["controls"] = controlsList
        });
    }

    private static double Round(float value) => Math.Round(value, 1);

    private static CommandParameter Optional(string name, string summary) => new(name, summary, false);

    private static CommandDefinition Query(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters, mutates: false), handler);
}
