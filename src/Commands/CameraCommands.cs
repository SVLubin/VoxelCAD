using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>
/// Cameras: the lens each one sees through, where it is pointed, and which of
/// them the scene's picture is taken with.
/// </summary>
/// <remarks>
/// Creating one is <c>create-element kind=Camera</c> and moving one is
/// <c>set-transform</c> — a camera is an element, and giving it a create and a
/// move of its own would be a second vocabulary for what the tree already does.
/// What is here is what only a camera has.
/// </remarks>
internal static class CameraCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("set-camera",
            "Sets a camera's lens: how the drawing converges, how wide it is, and what the "
            + "lens does with what is not in focus. Named rather than whole — what you do not "
            + "name is left where it was. Nothing here moves the camera: where it stands and "
            + "which way it points is its element's transform, so 'set-transform' and "
            + "'aim-camera' are what move it.",
            (context, arguments) =>
            {
                Guid id = context.ResolveElement(arguments.String("camera"));
                CameraLens lens = GetLens(context, id, out IHierarchyElement element);

                context.Scene.SetCameraLens(id, lens with
                {
                    Projection = arguments.Enum("projection", lens.Projection),
                    FovDegrees = arguments.Float("fov", lens.FovDegrees),
                    OrthographicHeight =
                        arguments.Float("ortho-height", lens.OrthographicHeight),
                    Aperture = arguments.Float("blur", lens.Aperture),
                    FocusDistance = arguments.Float("focus", lens.FocusDistance)
                });

                return CommandResult.Success(Describe(context, element));
            },
            Required("camera", "Camera element, by id or name."),
            Optional("projection",
                "perspective or orthographic. Orthographic draws without convergence, so "
                + "parallel edges stay parallel and a cube seen from its corner is isometric."),
            Optional("fov",
                $"Vertical field of view in degrees, "
                + $"{Bound(CameraLimits.MinimumFov, CameraLimits.MaximumFov)}. What a "
                + "converging lens takes in; a parallel one ignores it and keeps it."),
            Optional("ortho-height",
                $"How tall the parallel window is, in cells, "
                + $"{Bound(0f, CameraLimits.MaximumOrthographicHeight)}. Nought keeps the "
                + "framing the converging lens had, so changing the projection changes the "
                + "drawing rather than what is in it."),
            Optional("blur",
                $"The lens's radius in cells, {Bound(0f, CameraLimits.MaximumAperture)}. "
                + "Nought is a pinhole and everything is sharp; above it what is not at the "
                + "focus distance spreads."),
            Optional("focus",
                $"How far the lens is focused, in cells, "
                + $"{Bound(0f, CameraLimits.MaximumFocusDistance)}. Nought is whatever the "
                + "camera is pointed at. 'render-focus' sets this by pointing at a picture "
                + "instead, which is usually what is wanted."));

        builder.Add("aim-camera",
            "Turns a camera to look at a point, in the scene's own coordinates. One record, "
            + "on the rotation channel — the arithmetic a caller would otherwise have to do "
            + "to write Euler degrees that point somewhere. Use 'set-transform' with the "
            + "Position channel to move it there first.",
            (context, arguments) =>
            {
                Guid id = context.ResolveElement(arguments.String("camera"));

                GetLens(context, id, out IHierarchyElement element);

                Vector3 target = arguments.Vector("at");
                Vector3 eye = element.WorldTransform.Origin;

                if (eye.IsEqualApprox(target))
                {
                    throw new CommandException(
                        $"'{element.Name}' already stands at {Say(target)}, so there is no "
                        + "direction to look in. Move it first, or aim it somewhere else.");
                }

                context.Scene.SetTransform(
                    id,
                    TransformChannel.Rotation,
                    LookAtDegrees(eye, target, element));

                return CommandResult.Success(Describe(context, element));
            },
            Required("camera", "Camera element, by id or name."),
            Required("at", "'x,y,z' in the scene's coordinates — the point to look at."));

        builder.Add("set-active-camera",
            "Chooses which camera this scene's picture is taken through. Only one renders at "
            + "a time; the rest stand where they are and are drawn in the viewport so they "
            + "can be aimed. A scene with cameras and no choice uses the first of them.",
            (context, arguments) =>
            {
                Guid id = context.ResolveElement(arguments.String("camera"));

                GetLens(context, id, out IHierarchyElement element);
                context.Scene.SetActiveCamera(id);

                return CommandResult.Success(Describe(context, element));
            },
            Required("camera", "Camera element, by id or name."));

        builder.AddQuery("cameras",
            "Every camera in the scene: where it stands, which way it points, the lens it "
            + "sees through, and which of them renders. What to read before asking for a "
            + "picture — a render is taken through one of these, and nothing else decides "
            + "the shot.",
            (context, _) =>
            {
                var camerasList = new List<object?>();

                foreach (IHierarchyElement element in Walk(context.Scene.Children))
                {
                    if (element.Component is ICamera)
                        camerasList.Add(Describe(context, element));
                }

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["cameras"] = camerasList,
                    ["active"] = context.Scene.ActiveCameraId == Guid.Empty
                        ? null
                        : context.Scene.ActiveCameraId.ToString(),
                    ["note"] = camerasList.Count == 0
                        ? "This scene has no camera. 'create-element kind=Camera' places one, "
                            + "'aim-camera' points it and 'set-camera' sets its lens."
                        : "A camera is an element: 'set-transform' moves it, 'aim-camera' "
                            + "turns it, and 'set-camera' changes what it sees through."
                });
            });
    }

    /// <summary>
    /// One camera, as every camera command answers it: the element's own facts,
    /// plus where it actually stands in the scene and what it renders with.
    /// </summary>
    /// <remarks>
    /// <c>position</c> on the element description is <em>local</em>, like every
    /// element's; a camera is usually read to find out where in the room it is, so
    /// the world point and the direction it faces are reported beside it rather
    /// than left to be composed.
    /// </remarks>
    public static Dictionary<string, object?> Describe(
        CommandContext context,
        IHierarchyElement element)
    {
        Dictionary<string, object?> description = DescribeElement(element, deep: false);
        Transform3D world = element.WorldTransform;

        description["renders"] = context.Scene.ActiveCameraId == element.Id;
        description["standsAt"] = ToArray(world.Origin);

        // A camera looks down its own -Z, Godot's convention and the one the
        // tracer's own arithmetic restates.
        description["looksAlong"] = ToArray(-world.Basis.Z.Normalized());

        // The lens is already on there, from the shared element description: a
        // camera read and an element read must not describe the same lens
        // differently.
        return description;
    }

    /// <summary>
    /// The element a camera command was pointed at, refused by name when it is not
    /// one — a camera and a group look alike from the outside, and "that is a
    /// group" is a more useful answer than a lens nobody can see.
    /// </summary>
    private static CameraLens GetLens(
        CommandContext context,
        Guid id,
        out IHierarchyElement element)
    {
        context.Scene.TryGetElement(id, out element);

        return element.Component is ICamera camera
            ? camera.Lens
            : throw new CommandException(
                $"'{element.Name}' is a {element.ComponentKind} element, not a camera.");
    }

    /// <summary>
    /// The rotation, in the element's <b>parent</b> frame, that points its -Z at a
    /// world point. Composed through the parent's inverse rather than assigned
    /// outright, because a camera under a group is turned by the number its own
    /// channel holds and that number is local — aiming one without this would look
    /// right at the scene's top level and be wrong everywhere else.
    /// </summary>
    private static Vector3 LookAtDegrees(Vector3 eye, Vector3 target, IHierarchyElement element)
    {
        // Up is the world's, so a camera aimed by this stands level. Straight up or
        // down leaves that undefined, and Godot's LookingAt answers it by refusing
        // — so the fallback names an up that cannot be parallel to the aim.
        Vector3 aim = (target - eye).Normalized();
        Vector3 up = Mathf.Abs(aim.Dot(Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;

        // Godot's own convention: LookingAt is handed the direction the forward
        // axis should take, and forward is -Z — the very axis the tracer casts
        // along. Handed the negated aim it points the camera away, which is a
        // picture of the wrong half of the room and looks like nothing at all.
        var world = new Transform3D(Basis.LookingAt(aim, up), eye);
        Transform3D parent = element.Parent is IHierarchyElement owner
            ? owner.WorldTransform
            : Transform3D.Identity;

        return TransformHelper.ToValues(parent.AffineInverse() * world).RotationDegrees;
    }

    private static string Say(Vector3 point) =>
        $"{point.X:0.###},{point.Y:0.###},{point.Z:0.###}";

}
