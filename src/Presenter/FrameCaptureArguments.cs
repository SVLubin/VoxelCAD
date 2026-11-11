using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;

namespace VoxelCAD.Presenter;

/// <summary>
/// The dials shared by the two capture commands, in one place. <c>frame</c> and
/// <c>window</c> answer different questions — what the model looks like, what the
/// interface looks like — but they crop, scale and encode identically, and a
/// second copy of that is a thing that drifts.
/// </summary>
internal static class FrameCaptureArguments
{
    /// <summary>The four arguments both commands take, in the order both list them.</summary>
    public static CommandParameter[] CreateParameters() => new[]
    {
        new CommandParameter("scale", "Fraction of the full size, 0.05 to 1. Defaults to 1.", false),
        new CommandParameter(
            "format",
            "png, jpeg or webp. Defaults to png. jpeg and webp are far smaller.",
            false),
        new CommandParameter(
            "quality",
            "For jpeg and webp, 0.1 to 1. Defaults to 0.8. Ignored for png.",
            false),
        new CommandParameter(
            "region",
            "Crop before scaling, 'x,y,w,h' in image pixels; clamped to the frame. The "
            + "reported origin and scale say how the result maps back to input coordinates.",
            false)
    };

    public static FrameCaptureOptions Read(CommandArguments arguments, bool includeChrome) =>
        new(
            Clamp(arguments.Has("scale") ? arguments.Float("scale") : 1f, 0.05f, 1f),
            arguments.Enum("format", FrameFormat.Png),
            Clamp(arguments.Has("quality") ? arguments.Float("quality") : 0.8f, 0.1f, 1f),
            arguments.Has("region") ? ParseRegion(arguments.String("region")) : null,
            includeChrome);

    /// <summary>
    /// The image, plus the mapping back to the space input is aimed in. The
    /// transport turns the base64 into an image block and leaves the rest as
    /// structured content, so the numbers arrive without a second copy of the
    /// picture.
    /// </summary>
    /// <param name="extra">
    /// What this particular picture has to say for itself beyond its pixels — the
    /// preview's turn, which the clamps may have moved from what was asked for.
    /// </param>
    public static CommandResult Describe(
        FrameCapture capture,
        FrameFormat format,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var described = new Dictionary<string, object?>
        {
            ["mimeType"] = MimeType(format),
            ["width"] = capture.Width,
            ["height"] = capture.Height,
            ["bytes"] = capture.Bytes.Length,
            ["windowSize"] = new[] { capture.WindowSize.X, capture.WindowSize.Y },
            ["origin"] = new[] { Round(capture.Origin.X), Round(capture.Origin.Y) },
            ["scale"] = new[] { Round(capture.Scale.X), Round(capture.Scale.Y) },
            ["image"] = Convert.ToBase64String(capture.Bytes)
        };

        if (extra is not null)
        {
            foreach (KeyValuePair<string, object?> pair in extra)
                described[pair.Key] = pair.Value;
        }

        return CommandResult.Success(described);
    }

    private static double Round(float value) => Math.Round(value, 4);

    private static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

    /// <summary>
    /// Parses 'x,y,w,h' into a rect. Malformed input is refused here, where it is a
    /// typo to correct; an out-of-bounds but well-formed rect is not refused — the
    /// view clamps it, since only the view knows the frame's real size.
    /// </summary>
    private static Rect2I ParseRegion(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 4
            || !int.TryParse(parts[0], out int x)
            || !int.TryParse(parts[1], out int y)
            || !int.TryParse(parts[2], out int width)
            || !int.TryParse(parts[3], out int height))
        {
            throw new CommandException($"Argument 'region' must be 'x,y,w,h', got '{text}'.");
        }

        return new Rect2I(x, y, width, height);
    }

    private static string MimeType(FrameFormat format) => format switch
    {
        FrameFormat.Jpeg => "image/jpeg",
        FrameFormat.Webp => "image/webp",
        _ => "image/png"
    };
}
