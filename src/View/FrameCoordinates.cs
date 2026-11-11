using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The arithmetic tying a captured image back to the coordinate space input is
/// aimed in. Pure, and out of the <see cref="SceneView"/> node for the usual
/// reason: a node cannot be built without the engine, and this is the part that
/// is worth a test.
/// </summary>
/// <remarks>
/// Three spaces meet here. The captured image is in window pixels. Input is
/// aimed in window pixels too — that is what <c>Input.ParseInputEvent</c>
/// expects — so the uncropped, unscaled frame is one to one with it. A crop
/// moves the origin and a downscale changes the ratio, and both have to be
/// reported or an agent reading a pixel off the image would aim at the wrong
/// place. (The third space, the canvas units a <see cref="Control"/>'s rect is
/// measured in, is <see cref="UiCoordinates"/>' business.)
/// </remarks>
internal static class FrameCoordinates
{
    /// <summary>
    /// Where the finished image's top-left corner sits in input coordinates, and
    /// how many image pixels one input unit became. An agent converts a pixel
    /// with <c>input = origin + pixel / scale</c>.
    /// </summary>
    public static (Vector2 Origin, Vector2 Scale) Resolve(
        Vector2I fullPixels,
        Vector2I windowInput,
        Rect2I crop,
        Vector2I finalPixels)
    {
        Vector2 pixelsPerInput = Divide(fullPixels, windowInput);
        Vector2 cropInInput = Divide(crop.Size, pixelsPerInput);

        return (Divide(crop.Position, pixelsPerInput), Divide(finalPixels, cropInInput));
    }

    /// <summary>
    /// Componentwise, with a floor under the divisor: a degenerate frame is a
    /// window nobody can click in, not an exception for the capture path to
    /// raise.
    /// </summary>
    private static Vector2 Divide(Vector2 value, Vector2 divisor) =>
        new(value.X / Mathf.Max(divisor.X, 1e-4f), value.Y / Mathf.Max(divisor.Y, 1e-4f));

    private static Vector2 Divide(Vector2I value, Vector2I divisor) =>
        Divide(new Vector2(value.X, value.Y), new Vector2(divisor.X, divisor.Y));

    private static Vector2 Divide(Vector2I value, Vector2 divisor) =>
        Divide(new Vector2(value.X, value.Y), divisor);
}
