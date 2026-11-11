using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// Where a scene's picture is taken from, and through what: the active camera's
/// world transform and its lens, or the reason there is not one.
/// </summary>
/// <remarks>
/// One answer, because two ask it — the render card and the one-shot
/// <c>render</c> — and a picture that came out differently depending on which of
/// them asked would be the drift this codebase keeps a shared function to prevent.
/// Pure, so a test can reach every branch of it without a device.
/// </remarks>
internal static class SceneCamera
{
    /// <summary>
    /// What the surfaces say when the scene has nothing to take a picture through.
    /// Named rather than composed at each site: the card and the command both say
    /// it, and what differs is only how much of the sentence each can supply.
    /// </summary>
    public const string NoneReport =
        "This scene has no camera to render through. Add one — a camera is an element, "
        + "so 'create-element kind=Camera' places one and 'set-transform' aims it — or open "
        + "the render card, which places one where the viewport is looking.";

    /// <summary>
    /// The active camera as the tracer wants it, or false with a sentence saying
    /// why not.
    /// </summary>
    /// <remarks>
    /// The basis is the camera's <b>rotation</b>. A camera has no size of its own,
    /// so it ignores scale — and a scaled basis handed to the tracer would skew
    /// every ray it cast, which is a picture wrong in a way nobody could name from
    /// looking at it. A basis with no determinant is refused rather than guessed
    /// at, the rule <c>SceneView.TryPick</c> already keeps: it is the place where
    /// an infinity would otherwise become a direction.
    /// </remarks>
    public static bool TryRead(
        IScene scene,
        out CameraView view,
        out CameraLens lens,
        out string refusal)
    {
        view = default;

        if (!TryReadLens(scene, out Guid id, out lens))
        {
            refusal = NoneReport;

            return false;
        }

        scene.TryGetElement(id, out IHierarchyElement element);

        Transform3D world = element.WorldTransform;

        if (Mathf.IsZeroApprox(world.Basis.Determinant()))
        {
            refusal =
                $"The camera '{element.Name}' has a zero scale on one of its axes, so which "
                + "way it points cannot be worked out. Give it a scale and render again.";

            return false;
        }

        view = new CameraView(
            new Transform3D(new Basis(world.Basis.GetRotationQuaternion()), world.Origin),
            lens.FovDegrees);
        refusal = string.Empty;

        return true;
    }

    /// <summary>
    /// The active camera's id and lens, without asking where it stands. What
    /// focusing needs: it writes the lens back and never reads the transform.
    /// </summary>
    public static bool TryReadLens(IScene scene, out Guid id, out CameraLens lens)
    {
        lens = CameraLens.Default;
        id = scene.ActiveCameraId;

        if (id == Guid.Empty
            || !scene.TryGetElement(id, out IHierarchyElement element)
            || element.Component is not ICamera camera)
        {
            id = Guid.Empty;

            return false;
        }

        lens = camera.Lens;

        return true;
    }
}
