using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// The commands an agent aims and reads the camera with. Editor-only, like the
/// control commands and for the same reason: they reach a live viewport, which a
/// headless run does not have. They set camera state directly — not by replaying
/// the mouse gestures — through the one <see cref="ISceneView"/> setter a handover
/// also saves and restores through.
/// </summary>
/// <remarks>
/// Aiming moves the user's own camera, so <c>focus</c>, <c>orbit</c> and
/// <c>zoom</c> mutate: they are gated on control like any edit, because turning
/// the view out from under a working user is as rude as editing over them.
/// Reading is not — <c>camera</c> and <c>frame</c> are looking, and looking is
/// free, the same bargain <c>slice</c> and <c>bounds</c> make.
/// </remarks>
internal static class CameraCommands
{
    public static IReadOnlyList<CommandDefinition> Create(ISceneView view)
    {
        return new List<CommandDefinition>
        {
            Mutating("focus", "Aims the camera at a point, an element, or the whole scene.",
                (context, arguments) =>
                {
                    if (arguments.Has("element"))
                    {
                        view.FocusOn(context.ResolveElement(arguments.String("element")));
                    }
                    else if (arguments.Has("at"))
                    {
                        CameraPose pose = view.GetCameraPose();
                        view.SetCameraPose(pose with { Pivot = arguments.Vector("at") });
                    }
                    else
                    {
                        // Nothing named: frame the buildable region, the same "show
                        // me everything" the focus key falls back to.
                        view.FocusOn(context.Scene.Id);
                    }

                    return PoseResult(view);
                },
                Optional("element", "Element to frame, by id or name."),
                Optional("at", "Focus point to turn around, 'x,y,z'. Ignored if 'element' is given.")),

            Mutating("orbit", "Turns the camera to absolute yaw and pitch, in degrees.",
                (_, arguments) =>
                {
                    CameraPose pose = view.GetCameraPose();

                    view.SetCameraPose(pose with
                    {
                        YawDegrees = arguments.Has("yaw") ? arguments.Float("yaw") : pose.YawDegrees,
                        PitchDegrees = arguments.Has("pitch") ? arguments.Float("pitch") : pose.PitchDegrees
                    });

                    return PoseResult(view);
                },
                Optional("yaw", "Turntable yaw in degrees. Kept as-is if omitted."),
                // Measured, because the sentence an agent aims by has to be right:
                // the camera sits at -sin(pitch) above the focus point, the default
                // pose is -30° with the camera above it, and a run at ±70° shows the
                // model's lit top from the negative side and its shadowed underside
                // from the positive one. The description used to say the opposite.
                Optional("pitch", "Turntable pitch in degrees; negative looks down from above, "
                    + "positive looks up from below. Kept as-is if omitted.")),

            Mutating("view", "Looks at the scene from a named direction — the same 26 standard "
                + "views the navigation cube offers. Instant, not animated. The cube is a widget "
                + "for the user's mouse: its zones are not separate controls and it is not an "
                + "aiming channel, so use this or 'orbit' rather than clicking it.",
                (_, arguments) =>
                {
                    var name = arguments.String("direction");

                    if (!ViewCubeDirections.TryParse(name, out Vector3I zone))
                    {
                        return CommandResult.Failure(
                            $"No view is called '{name}'. Accepted: "
                            + string.Join(", ", ViewCubeDirections.GetNames()) + ".");
                    }

                    // The very function the widget turns a press into a pose with,
                    // so "front" cannot come to mean one thing to the mouse and
                    // another to an agent.
                    view.SetCameraPose(ViewCubeGeometry.GetPose(view.GetCameraPose(), zone));

                    return PoseResult(view);
                },
                Required("direction", "A standard view: 'front', 'top', 'top-front-right'. Any "
                    + "order of the parts is accepted; the focus point and the distance are left "
                    + "alone.")),

            Mutating("zoom", "Sets the camera's distance from the focus point.",
                (_, arguments) =>
                {
                    view.SetCameraPose(view.GetCameraPose() with { Distance = arguments.Float("distance") });

                    return PoseResult(view);
                },
                Required("distance", "Distance in world units. Clamped to the camera's range.")),

            Query("camera", "Reports where the camera is aimed.",
                (_, _) => PoseResult(view)),

            Query("frame", "Captures the scene as an image, with none of the editor chrome in it. "
                + "Use 'window' for a picture of the interface instead.",
                (_, arguments) =>
                {
                    FrameCaptureOptions options =
                        FrameCaptureArguments.Read(arguments, includeChrome: false);

                    return FrameCaptureArguments.Describe(view.CaptureFrame(options), options.Format);
                },
                FrameCaptureArguments.CreateParameters()),

            Query("material-preview",
                "Renders one cube wearing a material and answers with the image. The "
                + "document and the scene are untouched — this is a viewport of its own. "
                + "A material's whole point is how it looks, and neither 'slice' nor "
                + "'frame' can answer that: a slice has no notion of a facture, and the "
                + "scene shows whatever has been built rather than the material alone. "
                + "'yaw' and 'pitch' turn the block, which is how a directional facture "
                + "is read — a brushed streak is a mark from the front and a sheen from "
                + "the side, and one angle cannot show both.",
                (context, arguments) =>
                {
                    Guid id = context.ResolveMaterial(arguments.String("material"));
                    context.Project.TryGetMaterial(id, out IMaterial material);

                    var format = arguments.Enum("format", FrameFormat.Png);

                    MaterialPreviewTurn turn = MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(
                        arguments.Float("yaw", MaterialPreviewTurn.Default.YawDegrees),
                        arguments.Float("pitch", MaterialPreviewTurn.Default.PitchDegrees)));

                    FrameCapture capture = view.CapturePreview(
                        material.Name,
                        material.Color,
                        material.Appearance,
                        turn,
                        Math.Clamp(arguments.Int("size", 192), 64, 512),
                        format,
                        Math.Clamp(arguments.Float("quality", 0.8f), 0.1f, 1f));

                    // The turn as it was actually used: the pitch is clamped short of
                    // the poles and the yaw wraps, so what was asked for is not always
                    // what was photographed — the same reason every aim command answers
                    // with where the camera ended up.
                    return FrameCaptureArguments.Describe(capture, format,
                        new Dictionary<string, object?>
                        {
                            ["yaw"] = Round(turn.YawDegrees),
                            ["pitch"] = Round(turn.PitchDegrees)
                        });
                },
                Required("material", "Material by id or name."),
                Optional("yaw", "Degrees around the upright axis; wraps. Defaults to 35."),
                Optional("pitch", "Degrees above the horizon, -85 to 85, negative looking down "
                    + "on the block. Defaults to -25."),
                Optional("size", "Side of the square image in pixels, 64 to 512. Defaults to 192."),
                Optional("format", "png, jpeg or webp. Defaults to png."),
                Optional("quality", "For jpeg and webp, 0.1 to 1. Defaults to 0.8."))
        };
    }

    /// <summary>
    /// Every aim command answers with where the camera actually ended up, which is
    /// not always where it was asked to go: the clamps bring an out-of-range angle,
    /// distance or focus point into range, and an agent aiming iteratively has to
    /// see that.
    /// </summary>
    private static CommandResult PoseResult(ISceneView view)
    {
        CameraPose pose = view.GetCameraPose();

        return CommandResult.Success(new Dictionary<string, object?>
        {
            ["pivot"] = new[] { Round(pose.Pivot.X), Round(pose.Pivot.Y), Round(pose.Pivot.Z) },
            ["yaw"] = Round(pose.YawDegrees),
            ["pitch"] = Round(pose.PitchDegrees),
            ["distance"] = Round(pose.Distance)
        });
    }

    private static double Round(float value) => Math.Round(value, 3);

    private static CommandParameter Required(string name, string summary) => new(name, summary, true);

    private static CommandParameter Optional(string name, string summary) => new(name, summary, false);

    private static CommandDefinition Mutating(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters), handler);

    private static CommandDefinition Query(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters, mutates: false), handler);
}
