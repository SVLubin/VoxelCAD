using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Render;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// The command that draws a picture rather than a viewport: a path traced render
/// of the scene being worked on, taken through the camera it renders with.
/// </summary>
/// <remarks>
/// Editor-only, like the camera commands and for the same reason — it wants a
/// device that runs compute, and a headless run has none.
/// <para>
/// It does not mutate. A render reads the document and touches nothing in it, so
/// it is free on the same terms <c>slice</c>, <c>bounds</c> and <c>frame</c> are:
/// looking is not editing, and making an agent take control of the editor to look
/// at it would be absurd. What it does take is <em>time</em>, which is why the
/// sample count is an argument and not a constant.
/// </para>
/// </remarks>
internal static class RenderCommands
{
    /// <summary>
    /// How many whole samples over the picture go into one command list before it
    /// is submitted. Small enough that a device is not handed an unbounded batch,
    /// large enough that the round trip is not what the render is spending its
    /// time on.
    /// </summary>
    private const int SamplesPerBatch = 4;

    /// <summary>
    /// What this command renders when it is asked for nothing in particular. Not
    /// <see cref="RenderSettings.Default"/> outright: a picture answered into a
    /// conversation is looked at there rather than hung on a wall, so it is
    /// smaller and shorter than what the card offers.
    /// </summary>
    /// <remarks>
    /// One place, because <c>render-settings</c> promises these and <c>render</c>
    /// has to keep the promise — reported from the renderer's own defaults while
    /// three of them were overridden here, the report named a width, a height and
    /// a sample count that no render would ever have used.
    /// </remarks>
    private static RenderSettings Defaults => RenderSettings.Default with
    {
        Width = 960,
        Height = 540,
        Samples = 128
    };

    public static IReadOnlyList<CommandDefinition> Create(RenderPresenter card)
    {
        var list = new List<CommandDefinition>
        {
            Query(
                "render",
                "Path traces the active scene through its camera and answers a PNG. "
                + "This is not the viewport: light bounces, the sun has a size and so a soft "
                + "shadow, glass refracts and an emissive material lights what is beside it. "
                + "Use 'frame' to see what the editor is showing — this is for a picture of "
                + "what was built. It takes real time: the sample count is what that time buys, "
                + "and a render is noisy until it has had enough of them.",
                (context, arguments) => Render(context, arguments),
                Optional("width", $"Image width in pixels, {Sizes}. Defaults to {N(Defaults.Width)}."),
                Optional("height", $"Image height in pixels, {Sizes}. Defaults to {N(Defaults.Height)}."),
                Optional(
                    "samples",
                    $"Paths traced per pixel, {Range(RenderLimits.MinimumSamples, RenderLimits.MaximumSamples)}. "
                        + $"Defaults to {N(Defaults.Samples)}. Noise falls as the square root of "
                        + "this, so four times the samples is half the noise."),
                Optional(
                    "bounces",
                    $"How far light is followed, "
                        + $"{Range(RenderLimits.MinimumBounces, RenderLimits.MaximumBounces)}. "
                        + $"Defaults to {N(Defaults.Bounces)}."),
                Optional(
                    "glare",
                    $"How much of the picture the lens scatters, {Range(0f, RenderLimits.MaximumGlare)}. "
                    + "A bright thing "
                    + "spreads into what is beside it, which is what makes an emissive "
                    + "material read as a light. Nought is a lens that does not scatter."),
                Optional(
                    "exposure",
                    $"Multiplies the light before the film curve, "
                        + $"{Range(RenderLimits.MinimumExposure, RenderLimits.MaximumExposure)}. "
                        + $"Defaults to {N(Defaults.Exposure)}."),
                Optional(
                    "sun-elevation",
                    $"Degrees above the horizon, "
                        + $"{Range(-RenderLimits.MaximumElevation, RenderLimits.MaximumElevation)}. "
                        + $"Defaults to {N(Defaults.SunElevationDegrees)}, which is where the "
                        + "editor's own key light stands."),
                Optional(
                    "sun-azimuth",
                    $"Degrees around the horizon. Defaults to {N(Defaults.SunAzimuthDegrees)}."),
                Optional(
                    "sun-size",
                    $"The sun's angular radius in degrees, "
                        + $"{Range(RenderLimits.MinimumSunRadius, RenderLimits.MaximumSunRadius)}. "
                        + $"Defaults to {N(Defaults.SunAngularRadiusDegrees)}. "
                    + "This is what decides how soft every shadow in the picture is."),
                Optional(
                    "sun-intensity",
                    $"How much light the sun delivers to a surface facing it, "
                        + $"{Range(0f, RenderLimits.MaximumIntensity)}. "
                        + $"Defaults to {N(Defaults.SunIntensity)}. "
                    + "Zero switches it off, which with a flat sky is the furnace an energy "
                    + "check needs."),
                Optional(
                    "sky-intensity",
                    $"Multiplies the sky, {Range(0f, RenderLimits.MaximumIntensity)}. "
                    + $"Defaults to {N(Defaults.SkyIntensity)}; zero switches it off."),
                Optional(
                    "sky",
                    "One colour, 'r,g,b', for the whole sky — zenith, horizon and below — "
                    + "instead of the gradient. What makes an environment uniform enough to "
                    + "measure against."),
                Optional(
                    "backdrop",
                    "environment, plain or transparent. What a camera ray that reaches nothing "
                    + "shows — the scene is lit by the sky whichever is chosen. Transparent "
                    + "answers a PNG with an alpha channel."),
                Optional("backdrop-color", "'r,g,b' for the plain backdrop."),
                Optional("ground", "Whether a floor catches the shadow. Defaults to true."),
                Optional(
                    "path",
                    "Also write the PNG here. A 'res://' or 'user://' path, or an absolute one."),
                Optional(
                    "sample",
                    "Rectangles of the picture to answer as numbers as well: "
                    + "'x,y,w,h', several separated by ';'. The mean radiance over each, in "
                    + "the tracer's own units — before exposure and before the film curve, "
                    + "which is what an energy check needs and what the image cannot give: "
                    + "read off the encoded picture an error comes back about a third "
                    + "smaller than it is.")),

            Query(
                "render-settings",
                "Reports the render defaults and what each dial is allowed to be, so a render "
                + "can be asked for without finding the bounds by being refused.",
                (_, _) => Describe(Defaults))
        };

        list.AddRange(CardCommands(card));

        return list;
    }

    private static CommandResult Render(CommandContext context, CommandArguments arguments)
    {
        // The camera before anything else: a render with nowhere to stand is a
        // refusal rather than a picture, and finding that out after compiling the
        // scene would only make it slower.
        if (!SceneCamera.TryRead(
                context.Scene,
                out CameraView camera,
                out CameraLens lens,
                out string refusal))
        {
            throw new CommandException(refusal);
        }

        RenderSettings settings = ReadSettings(arguments, lens);
        RenderScene scene = RenderFactory.CompileScene(context.Project, context.Scene);

        var clock = Stopwatch.StartNew();

        if (!RenderFactory.TryCreateTracer(
                scene,
                settings,
                camera,
                ReadFinishes(scene),
                out IPathTracer? tracer,
                out string error))
            throw new CommandException($"The render could not be started: {error}");

        using (tracer)
        {
            while (!tracer!.IsFinished)
            {
                tracer.RunSteps(tracer.StepsPerSample * SamplesPerBatch);

                // A one-shot render adapts too. It has no viewer to favour a part
                // of the picture, so this is the whole of what decides its order:
                // where it is still moving.
                tracer.MeasureNoise();
            }

            Image image = tracer.ReadImage();
            byte[] bytes = image.SavePngToBuffer();

            clock.Stop();

            var result = new Dictionary<string, object?>
            {
                ["mimeType"] = "image/png",
                ["width"] = image.GetWidth(),
                ["height"] = image.GetHeight(),
                ["samples"] = tracer.SamplesTaken,
                ["elements"] = scene.ElementCount,
                ["milliseconds"] = clock.ElapsedMilliseconds,
                ["bytes"] = bytes.Length
            };

            if (arguments.Has("path"))
                result["path"] = Save(image, arguments.String("path"));

            if (arguments.Has("sample"))
                result["sampled"] = Sampled(tracer, arguments.String("sample"));

            AddNotes(result, scene);

            result["image"] = Convert.ToBase64String(bytes);

            return CommandResult.Success(result);
        }
    }

    /// <summary>
    /// The mean radiance over each rectangle asked for, in the tracer's own units —
    /// before exposure, before the film curve. A picture is what a render is for
    /// and a number is what a check needs: read off the encoded image, an energy
    /// error comes back about a third smaller than it is.
    /// </summary>
    private static List<object?> Sampled(IPathTracer tracer, string text)
    {
        var answered = new List<object?>();

        foreach (RenderSampleRect rect in RenderSampleRect.ParseAll(text))
        {
            Vector3 radiance = tracer.ReadRadiance(rect.X, rect.Y, rect.Width, rect.Height);

            answered.Add(new Dictionary<string, object?>
            {
                ["rect"] = rect.ToString(),
                ["r"] = MathF.Round(radiance.X, 6),
                ["g"] = MathF.Round(radiance.Y, 6),
                ["b"] = MathF.Round(radiance.Z, 6),

                // The one number an energy check compares, so it is not left to be
                // recomputed by every caller with its own weights.
                ["luminance"] = MathF.Round(
                    0.2126f * radiance.X + 0.7152f * radiance.Y + 0.0722f * radiance.Z, 6)
            });
        }

        return answered;
    }

    /// <summary>
    /// What the render could not say about the document. A picture that quietly
    /// disagreed with the viewport would be worse than one that admits where it
    /// does.
    /// </summary>
    private static void AddNotes(Dictionary<string, object?> result, RenderScene scene)
    {
        var notesList = new List<string>();

        if (scene.IsEmpty)
            notesList.Add("Nothing visible to render: the picture is the sky and the floor.");

        if (scene.OversizedElements.Count > 0)
        {
            notesList.Add(
                "Left out as too sparse to hold densely: "
                + string.Join(", ", scene.OversizedElements)
                + ".");
        }

        if (notesList.Count > 0)
            result["notes"] = notesList;
    }

    private static string Save(Image image, string path)
    {
        string resolved = path.StartsWith("res://", StringComparison.Ordinal)
            || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : path;

        Error error = image.SavePng(resolved);

        if (error != Error.Ok)
            throw new CommandException($"The render could not be written to '{path}': {error}.");

        return resolved;
    }

    /// <summary>
    /// The height field for every facture the scene wears, drawn by the view — the
    /// same patterns the viewport lays over the same materials, so the render and
    /// the editor cannot come to wear two sets that look alike.
    /// </summary>
    private static Dictionary<string, FinishHeightField> ReadFinishes(RenderScene scene)
    {
        var fields = new Dictionary<string, FinishHeightField>();

        foreach (RenderFacture facture in scene.Factures)
            fields[facture.Name] = FinishHeights.Get(facture.Name, facture.Finish);

        return fields;
    }

    private static RenderSettings ReadSettings(CommandArguments arguments, CameraLens lens)
    {
        // Defaults, not RenderSettings.Default: this command's picture is looked at
        // in a conversation rather than hung on a wall, so it is smaller and
        // shorter — and 'render-settings' promises exactly these, so they are read
        // from the one place rather than restated here. Restated, they agreed
        // until somebody changed one.
        RenderSettings defaults = Defaults;

        RenderSettings settings = (defaults with
        {
            Width = arguments.Int("width", defaults.Width),
            Height = arguments.Int("height", defaults.Height),
            Samples = arguments.Int("samples", defaults.Samples),
            Bounces = arguments.Int("bounces", defaults.Bounces),
            Exposure = arguments.Float("exposure", defaults.Exposure),
            SunElevationDegrees = arguments.Float("sun-elevation", defaults.SunElevationDegrees),
            SunAzimuthDegrees = arguments.Float("sun-azimuth", defaults.SunAzimuthDegrees),
            SunAngularRadiusDegrees = arguments.Float("sun-size", defaults.SunAngularRadiusDegrees),
            SunIntensity = arguments.Float("sun-intensity", defaults.SunIntensity),
            SkyIntensity = arguments.Float("sky-intensity", defaults.SkyIntensity),
            Glare = arguments.Float("glare", defaults.Glare),

            // The lens is the camera's and takes no argument here. A picture taken
            // through the same camera has to be the same picture, and an override
            // on the command would be a second place the shot was decided.
            Aperture = lens.Aperture,
            FocusDistance = lens.FocusDistance,
            Projection = lens.Projection,
            OrthographicHeight = lens.OrthographicHeight,
            Backdrop = arguments.Enum("backdrop", defaults.Backdrop),
            BackdropColor = arguments.Has("backdrop-color")
                ? arguments.Color("backdrop-color")
                : defaults.BackdropColor,
            GroundEnabled = arguments.Bool("ground", defaults.GroundEnabled)
        }).Clamped();

        if (!arguments.Has("sky"))
            return settings;

        // One colour for all three stops: a gradient is a lovely backdrop and a
        // useless reference, because no two directions agree about what the
        // answer should have been.
        Color flat = arguments.Color("sky");

        return settings with { SkyZenith = flat, SkyHorizon = flat, SkyGround = flat };
    }

    private static CommandResult Describe(RenderSettings defaults) =>
        CommandResult.Success(new Dictionary<string, object?>
        {
            ["defaults"] = new Dictionary<string, object?>
            {
                ["width"] = defaults.Width,
                ["height"] = defaults.Height,
                ["samples"] = defaults.Samples,
                ["bounces"] = defaults.Bounces,
                ["exposure"] = defaults.Exposure,
                ["sunElevation"] = defaults.SunElevationDegrees,
                ["sunAzimuth"] = defaults.SunAzimuthDegrees,
                ["sunSize"] = defaults.SunAngularRadiusDegrees,
                ["sunIntensity"] = defaults.SunIntensity,
                ["skyIntensity"] = defaults.SkyIntensity,
                ["glare"] = defaults.Glare,
                ["ground"] = defaults.GroundEnabled,
                ["backdrop"] = defaults.Backdrop.ToString()
            },

            // Every dial that has a bound, not the four that happened to be
            // written down first: the whole point of this command is that a
            // render can be asked for without finding the edges by being refused.
            ["limits"] = new Dictionary<string, object?>
            {
                ["size"] = new[] { RenderLimits.MinimumSize, RenderLimits.MaximumSize },
                ["samples"] = new[] { RenderLimits.MinimumSamples, RenderLimits.MaximumSamples },
                ["bounces"] = new[] { RenderLimits.MinimumBounces, RenderLimits.MaximumBounces },
                ["exposure"] = new[] { RenderLimits.MinimumExposure, RenderLimits.MaximumExposure },
                ["sunElevation"] =
                    new[] { -RenderLimits.MaximumElevation, RenderLimits.MaximumElevation },
                ["sunSize"] =
                    new[] { RenderLimits.MinimumSunRadius, RenderLimits.MaximumSunRadius },
                ["sunIntensity"] = new[] { 0f, RenderLimits.MaximumIntensity },
                ["skyIntensity"] = new[] { 0f, RenderLimits.MaximumIntensity },
                ["glare"] = new[] { 0f, RenderLimits.MaximumGlare }
            },

            // The lens's bounds are reported here too, even though no argument of
            // this command takes one: a reader asking what a render may be is
            // asking about the whole picture, and being sent to a second report for
            // the blur would be the bound-by-refusal this command exists to avoid.
            ["cameraLimits"] = new Dictionary<string, object?>
            {
                ["fov"] = new[] { CameraLimits.MinimumFov, CameraLimits.MaximumFov },
                ["blur"] = new[] { 0f, CameraLimits.MaximumAperture },
                ["focus"] = new[] { 0f, CameraLimits.MaximumFocusDistance },
                ["orthoHeight"] = new[] { 0f, CameraLimits.MaximumOrthographicHeight }
            },
            ["note"] = "The sun's default aim is the editor's own key light, so a render is lit "
                + "from where the viewport is lit from. These are this command's defaults; the "
                + "render form has its own, which 'preferences' reports. The lens — the "
                + "projection, the blur and the focus — is not a render setting at all: it "
                + "belongs to the camera the picture is taken through, and 'set-camera' moves it."
        });

    /// <summary>
    /// The card, as four commands. A render an agent asks for over the wire is one
    /// picture answered into a conversation; the card is the other way of having
    /// one — it keeps going, it can be looked into, and what it is looking at
    /// decides where the samples go. Both belong on the surface for the reason
    /// everything does: an agent sees what the user sees.
    /// </summary>
    private static IEnumerable<CommandDefinition> CardCommands(RenderPresenter card)
    {
        yield return Mutating(
            "render-open",
            "Raises the render card over the editor and starts a render at the settings it "
            + "was last left at. Unlike 'render' this one keeps going and can be looked "
            + "into — use 'render-status' to watch it and 'render-view' to aim it. For one "
            + "picture answered straight back, use 'render'. A scene with no camera gets "
            + "one, placed where the viewport is looking — a picture is taken through a "
            + "camera, and that is the camera you would have had to make.",
            (_, _) =>
            {
                card.Open();

                return Report(card);
            });

        yield return Mutating(
            "render-close",
            "Puts the render card away and stops the render it was running.",
            (_, _) =>
            {
                card.Close();

                return Report(card);
            });

        yield return Mutating(
            "render-again",
            "Takes the picture again at the card's current dials. A render is compiled "
            + "from the document as it stood when it started, so this is the only way to "
            + "get a picture of a scene that has moved since — 'render-status' says so as "
            + "'stale'. Unlike closing and reopening the card it keeps the viewer where it "
            + "is, so a corner being watched goes on being watched.",
            (_, _) =>
            {
                if (!card.IsOpen)
                    throw new CommandException("There is no render card open. Use 'render-open'.");

                card.RenderAgain();

                return Report(card);
            });

        yield return Mutating(
            "render-set",
            "Turns the open card's dials, by the same names 'render' takes them. Most of "
            + "them describe what is being traced and so start the render again; exposure "
            + "and samples do not — exposure is applied when the picture is read, and asking "
            + "for more samples is asking the same render to carry on. What comes back is "
            + "what the card actually holds, which is not always what was asked for: a "
            + "field has a step, and a value off it is snapped to the nearest one on it.",
            (_, arguments) =>
            {
                if (!card.IsOpen)
                    throw new CommandException("There is no render card open. Use 'render-open'.");

                card.ApplyDials(ReadDials(card.Dials, arguments));

                return Report(card);
            },
            Optional("width", $"Image width in pixels, {Sizes}."),
            Optional("height", $"Image height in pixels, {Sizes}."),
            Optional(
                "samples",
                $"Paths traced per pixel, {Range(RenderLimits.MinimumSamples, RenderLimits.MaximumSamples)}. "
                    + "Raising it lets the render carry on rather than starting it again; "
                    + "lowering it below what has already been traced simply finishes it."),
            Optional(
                "exposure",
                $"Multiplies the light before the film curve, "
                    + $"{Range(RenderLimits.MinimumExposure, RenderLimits.MaximumExposure)}. "
                    + "Re-grades what has been traced rather than starting again."),
            Optional(
                "sun-elevation",
                $"Degrees above the horizon, "
                    + $"{Range(-RenderLimits.MaximumElevation, RenderLimits.MaximumElevation)}."),
            Optional(
                "sun-azimuth",
                $"Degrees around the horizon, "
                    + $"{Range(-RenderLimits.MaximumAzimuth, RenderLimits.MaximumAzimuth)}."),
            Optional(
                "sun-size",
                $"The sun's angular radius in degrees, "
                    + $"{Range(RenderLimits.MinimumSunRadius, RenderLimits.MaximumSunRadius)}. "
                    + "What decides how soft every shadow is."),
            Optional(
                "glare",
                $"How much of the picture the lens scatters, {Range(0f, RenderLimits.MaximumGlare)}."),
            Optional("backdrop", "environment, plain or transparent."),
            Optional("ground", "Whether a floor catches the shadow."));

        yield return Mutating(
            "render-view",
            "Aims what the card is showing. Not only a display change: what is on screen is "
            + "what the renderer traces first, so a corner brought up converges several "
            + "times sooner than the rest of the picture — which still finishes, just later.",
            (_, arguments) =>
            {
                if (!card.IsOpen)
                    throw new CommandException("There is no render card open. Use 'render-open'.");

                string wanted = arguments.Has("show") ? arguments.String("show") : "region";

                switch (wanted.ToLower())
                {
                    case "fit":
                        card.FitImage();

                        break;

                    case "actual":
                        card.ShowActualSize();

                        break;

                    case "region":
                        card.ShowRegion(ReadRegion(arguments));

                        break;

                    default:
                        throw new CommandException(
                            $"'show' is 'fit', 'actual' or 'region', got '{wanted}'.");
                }

                return Report(card);
            },
            Optional(
                "show",
                "'fit' for the whole picture, 'actual' for one screen pixel per render pixel, "
                + "or 'region' for the rectangle given below. Defaults to 'region'."),
            Optional("x", "Left edge of the region, in the render's own pixels. Defaults to 0."),
            Optional("y", "Top edge of the region. Defaults to 0."),
            Optional("width", "Width of the region."),
            Optional("height", "Height of the region."));

        yield return Mutating(
            "render-focus",
            "Focuses the open card's lens on whatever the picture shows at a point, given in "
            + "the render's own pixels. The distance is the one the render already measured "
            + "for that pixel, so what is focused on is exactly what is drawn there. It writes "
            + "the camera's own focus distance, so it is an undoable edit to the document, and "
            + "it restarts the render — focus decides what the light does. Only useful with "
            + "the camera's 'blur' above nought: a pinhole has everything sharp.",
            (_, arguments) =>
            {
                if (!card.IsOpen)
                    throw new CommandException("There is no render card open. Use 'render-open'.");

                int x = arguments.Int("x");
                int y = arguments.Int("y");

                if (!card.FocusAt(x, y))
                {
                    throw new CommandException(
                        $"The picture shows nothing at {x},{y} — the camera ray reached the sky "
                        + "there, and there is no distance to focus on.");
                }

                return Report(card);
            },
            Required("x", "Across the picture, in its own pixels."),
            Required("y", "Down the picture, in its own pixels."));

        yield return Query(
            "render-image",
            "Answers the open card's picture as it stands — a PNG, the same one on screen. "
            + "A render is the same render however long it is left to run, so asking early "
            + "gives a noisier picture of it rather than a different one. This is the only "
            + "way to get a card's render out: its Save button opens the platform's own file "
            + "picker, which is not reachable from here.",
            (_, arguments) =>
            {
                if (!card.IsOpen)
                    throw new CommandException("There is no render card open. Use 'render-open'.");

                Image? image = card.ReadPicture()
                    ?? throw new CommandException(
                        "The render has not produced a picture yet. Try again in a moment.");

                byte[] bytes = image.SavePngToBuffer();
                RenderProgress progress = card.Progress;

                var result = new Dictionary<string, object?>
                {
                    ["mimeType"] = "image/png",
                    ["width"] = image.GetWidth(),
                    ["height"] = image.GetHeight(),
                    ["samples"] = progress.SamplesTaken,
                    ["samplesInView"] = progress.SamplesPeak,
                    ["finished"] = progress.IsFinished,
                    ["bytes"] = bytes.Length
                };

                if (arguments.Has("path"))
                    result["path"] = Save(image, arguments.String("path"));

                result["image"] = Convert.ToBase64String(bytes);

                return CommandResult.Success(result);
            },
            Optional(
                "path",
                "Writes the PNG here as well as answering it. Anything the editor can write "
                + "to; a bare name lands beside the project."));

        yield return Query(
            "render-status",
            "How the open render card is going: what it is a picture of, how far along it is, "
            + "how much longer it is likely to take, and which part of it is being served "
            + "first.",
            (_, _) => Report(card));
    }

    /// <summary>
    /// What the card is and what it is doing. The samples are reported twice on
    /// purpose: the whole picture is worth what its least converged part is worth,
    /// and the part on screen may be a long way ahead of that.
    /// </summary>
    private static CommandResult Report(RenderPresenter card)
    {
        var result = new Dictionary<string, object?> { ["open"] = card.IsOpen };

        if (!card.IsOpen)
            return CommandResult.Success(result);

        RenderProgress progress = card.Progress;
        RenderDials dials = card.Dials;
        RenderRegion region = card.Region;

        result["width"] = dials.Width;
        result["height"] = dials.Height;
        result["samples"] = progress.SamplesTaken;
        result["samplesInView"] = progress.SamplesPeak;
        result["samplesWanted"] = progress.SamplesWanted;
        result["progress"] = Math.Round(progress.Fraction, 4);
        result["seconds"] = Math.Round(progress.Seconds, 2);
        result["samplesPerSecond"] = Math.Round(progress.SamplesPerSecond, 2);
        result["finished"] = progress.IsFinished;

        // What says whether more samples would help, where the count alone cannot:
        // a flat sky is settled at four and a caustic is not at four thousand.
        if (progress.Noise > 0f)
            result["noise"] = Math.Round(progress.Noise, 4);

        if (progress.RemainingSeconds >= 0d)
            result["remainingSeconds"] = Math.Round(progress.RemainingSeconds, 1);

        result["view"] = region.IsEmpty
            ? "whole"
            : $"{region.X},{region.Y} {region.Width}x{region.Height}";

        // A render is a snapshot, so a reader deciding whether to trust it needs
        // to know the document has moved under it.
        result["stale"] = card.IsStale;

        if (!string.IsNullOrEmpty(card.Message))
            result["notes"] = card.Message;

        return CommandResult.Success(result);
    }

    /// <summary>
    /// The dials with whatever was named replaced, and everything else left where
    /// the card had it. Named rather than whole, so an agent may turn one dial
    /// without having to restate the twelve it is not touching — and so the same
    /// call does not silently reset what a user set by hand a moment ago.
    /// </summary>
    /// <remarks>
    /// Nothing is clamped here. The fields do it, on bounds they take from
    /// <c>RenderLimits</c>, and the answer is read back off them — so what an agent
    /// is told is what the card is holding rather than what it asked for.
    /// </remarks>
    private static RenderDials ReadDials(RenderDials dials, CommandArguments arguments) =>
        dials with
        {
            Width = arguments.Int("width", dials.Width),
            Height = arguments.Int("height", dials.Height),
            Samples = arguments.Int("samples", dials.Samples),
            Exposure = arguments.Float("exposure", dials.Exposure),
            SunElevationDegrees =
                arguments.Float("sun-elevation", dials.SunElevationDegrees),
            SunAzimuthDegrees = arguments.Float("sun-azimuth", dials.SunAzimuthDegrees),
            SunSizeDegrees = arguments.Float("sun-size", dials.SunSizeDegrees),
            Glare = arguments.Float("glare", dials.Glare),
            Backdrop = arguments.Enum("backdrop", dials.Backdrop),
            Ground = arguments.Bool("ground", dials.Ground)
        };

    private static RenderRegion ReadRegion(CommandArguments arguments)
    {
        if (!arguments.Has("width") || !arguments.Has("height"))
        {
            throw new CommandException(
                "A region needs 'width' and 'height', and may take 'x' and 'y'.");
        }

        return new RenderRegion(
            arguments.Int("x", 0),
            arguments.Int("y", 0),
            arguments.Int("width"),
            arguments.Int("height"));
    }

    /// <summary>
    /// A number written the way a machine reads one, whatever this machine's
    /// locale is.
    /// </summary>
    /// <remarks>
    /// These descriptions are read by an agent, and this project has already been
    /// bitten once by a decimal comma reaching text that had to be parsed — the
    /// shader's own constants, which then failed to compile with a message naming
    /// neither the number nor the locale. It returns a <c>string</c> rather than
    /// taking a <see cref="FormattableString"/>, because a description is built by
    /// concatenation and a concatenation is a string long before anything could
    /// have formatted it invariantly.
    /// </remarks>
    private static string N(double value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The bounds of a dial, said the way every description says them.</summary>
    private static string Range(double least, double most) =>
        N(least) + " to " + N(most);

    /// <summary>The size bounds, which two descriptions share.</summary>
    private static string Sizes =>
        Range(RenderLimits.MinimumSize, RenderLimits.MaximumSize);

    private static CommandParameter Optional(string name, string summary) =>
        new(name, summary, false);

    private static CommandParameter Required(string name, string summary) =>
        new(name, summary, true);

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
