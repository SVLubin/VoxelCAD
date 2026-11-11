using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>
/// References: somebody else's picture or 3D model, standing in the scene to be
/// traced over or built around.
/// </summary>
/// <remarks>
/// A reference is an element, so moving, hiding, renaming, grouping and deleting
/// one is <c>set-transform</c>, <c>set-visible</c>, <c>rename-element</c>,
/// <c>reparent-element</c> and <c>delete-element</c>. What is here is what only a
/// reference has: which file it stands for, how big the picture is in cells, and
/// how it is drawn.
/// </remarks>
internal static class ReferenceCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("import-reference",
            "Puts somebody else's file in the scene as a reference: a picture to trace over, "
            + "or a 3D model to build around. Placed and never edited — this editor changes "
            + "nothing about the file, only where it stands and how it is drawn. The file is "
            + "linked rather than copied, so editing it elsewhere shows up here. A picture "
            + "arrives at one of its pixels to one voxel, which is what tracing pixel art "
            + "wants; 'set-reference' resizes it.",
            (context, arguments) =>
            {
                Guid parentId = arguments.Has("parent")
                    ? context.ResolveElement(arguments.String("parent"))
                    : context.Scene.Id;

                string path = Resolve(arguments.String("path"));
                Guid id = context.Scene.CreateReference(
                    parentId,
                    arguments.String("name", DefaultName(path)),
                    path);

                context.Scene.TryGetElement(id, out IHierarchyElement element);

                return CommandResult.Success(DescribeElement(element, deep: false));
            },
            Required("path",
                "The file to stand in the scene. Pictures: "
                + $"{string.Join(", ", ReferenceFormats.ImageExtensions)}. Models: "
                + $"{string.Join(", ", ReferenceFormats.ModelExtensions)}. A relative path is "
                + "resolved against the working directory; what the project file stores is "
                + "relative to itself whenever the asset sits beside it."),
            Optional("name",
                "Name of the new element. Defaults to the file's own name without its "
                + "extension."),
            Optional("parent", "Parent element, by id or name. Defaults to the scene root."));

        builder.Add("set-reference",
            "Changes a reference: which file it stands for, how big the picture is, and how "
            + "it is drawn. Named rather than whole — what you do not name is left where it "
            + "was. Nothing here moves it: where it stands is its element's transform, so "
            + "'set-transform' is what moves and turns it, and for a 3D model the Scale "
            + "channel is what resizes it.",
            (context, arguments) =>
            {
                Guid id = context.ResolveElement(arguments.String("reference"));

                context.Scene.TryGetElement(id, out IHierarchyElement element);

                if (element.Component is IReferenceImage image)
                    SetImage(context, id, element, image.Settings, arguments);
                else if (element.Component is IReferenceModel model)
                    SetModel(context, id, element, model.Settings, arguments);
                else
                    throw NotAReference(element);

                context.Scene.TryGetElement(id, out element);

                return CommandResult.Success(DescribeElement(element, deep: false));
            },
            Required("reference", "Reference element, by id or name."),
            Optional("path",
                "Another file for this reference to stand for. Its kind must match what the "
                + "element already is — a picture cannot become a model, since a component is "
                + "fixed for an element's lifetime; import the other one instead. A picture "
                + "re-pointed with nothing said about its size keeps its width and follows the "
                + "new picture's proportions, so it never comes back stretched."),
            Optional("opacity",
                $"How solid it is, {Bound(ReferenceLimits.MinimumOpacity, ReferenceLimits.MaximumOpacity)}. "
                + "Nought keeps it in the scene and stops drawing it, which is a different "
                + "thing from hiding the element."),
            Optional("lit",
                "true or false. Whether the scene's lights fall on it. Off by default: a "
                + "photograph already carries its own light, and a drawing reads worse under "
                + "a key light."),
            Optional("size",
                "Pictures only. 'width,height' in cells, or one number for the width with the "
                + "height following the picture's own proportions."),
            Optional("pixels-per-cell",
                "Pictures only. How many of the picture's own pixels fall on one cell — 1 "
                + "puts one pixel on one voxel, 2 makes it half the size. Sets 'size' from the "
                + "picture's resolution, so it needs a file that could be measured."),
            Optional("filtered",
                "Pictures only. true or false. Smoothed between pixels. Off is what pixel art "
                + "needs and is why it is the default."),
            Optional("wireframe",
                "Models only. true or false. Drawn as its own edges instead of its surfaces, "
                + "which is how a silhouette is traced without the reference covering what is "
                + "being built inside it."));

        builder.AddQuery("references",
            "Every reference in the scene: the file it stands for, whether that file is "
            + "actually there, how big it is and how it is drawn. Read this when a reference "
            + "looks blank — a path nobody can open reports found: false, and everything else "
            + "in the report describes a picture that is not on screen.",
            (context, _) =>
            {
                var referencesList = new List<object?>();
                var missing = 0;

                foreach (IHierarchyElement element in Walk(context.Scene.Children))
                {
                    if (!ReferenceFormats.IsReference(element.ComponentKind))
                        continue;

                    Dictionary<string, object?> description = DescribeElement(element, deep: false);

                    referencesList.Add(description);

                    if (description["reference"] is Dictionary<string, object?> reference
                        && reference["found"] is false)
                    {
                        missing++;
                    }
                }

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["references"] = referencesList,
                    ["missing"] = missing,
                    ["note"] = referencesList.Count == 0
                        ? "This scene has no reference. 'import-reference path=...' puts a "
                            + "picture or a 3D model in it to trace over."
                        : "A reference is an element: 'set-transform' moves it, "
                            + "'set-visible' hides it and 'set-reference' changes the file it "
                            + "stands for and how it is drawn."
                });
            });
    }

    private static void SetImage(
        CommandContext context,
        Guid id,
        IHierarchyElement element,
        ReferenceImageSettings settings,
        CommandArguments arguments)
    {
        Refuse(arguments, element, "wireframe", "models");

        var wanted = settings with
        {
            Opacity = arguments.Float("opacity", settings.Opacity),
            LitByScene = arguments.Bool("lit", settings.LitByScene),
            Filtered = arguments.Bool("filtered", settings.Filtered)
        };

        if (arguments.Has("path"))
            wanted = wanted with { Path = ChangedPath(arguments, element) };

        // What the model will make of the path, asked before the write so a size
        // can be computed against it. Measuring here rather than reading the
        // current settings is what makes "re-point and resize" one command rather
        // than two.
        Vector2I pixels = wanted.Path == settings.Path
            || !ImageHeader.TryRead(wanted.Path, out Vector2I measured)
                ? settings.PixelSize
                : measured;

        wanted = wanted with { PixelSize = pixels };

        if (arguments.Has("size"))
            wanted = wanted with { SizeInCells = ReadSize(arguments, pixels, wanted.SizeInCells) };
        else if (arguments.Has("pixels-per-cell"))
            wanted = wanted with { SizeInCells = ReadPixelsPerCell(arguments, pixels, element) };
        else if (pixels != settings.PixelSize)
            wanted = wanted with { SizeInCells = Refitted(wanted.SizeInCells, pixels) };

        context.Scene.SetReferenceImage(id, wanted);
    }

    private static void SetModel(
        CommandContext context,
        Guid id,
        IHierarchyElement element,
        ReferenceModelSettings settings,
        CommandArguments arguments)
    {
        Refuse(arguments, element, "size", "pictures");
        Refuse(arguments, element, "pixels-per-cell", "pictures");
        Refuse(arguments, element, "filtered", "pictures");

        var wanted = settings with
        {
            Opacity = arguments.Float("opacity", settings.Opacity),
            LitByScene = arguments.Bool("lit", settings.LitByScene),
            Wireframe = arguments.Bool("wireframe", settings.Wireframe)
        };

        if (arguments.Has("path"))
            wanted = wanted with { Path = ChangedPath(arguments, element) };

        context.Scene.SetReferenceModel(id, wanted);
    }

    /// <summary>
    /// The new path, refused where it would change what kind of thing the element
    /// is. A component is fixed for an element's lifetime, so this cannot be
    /// silently obeyed — and a picture that quietly became a model would take its
    /// size, its filtering and everything it was placed for with it.
    /// </summary>
    private static string ChangedPath(CommandArguments arguments, IHierarchyElement element)
    {
        string path = Resolve(arguments.String("path"));

        if (!ReferenceFormats.TryGetKind(path, out ComponentKind kind))
        {
            throw new CommandException(
                $"Nothing here can stand for '{path}'. Pictures: "
                + $"{string.Join(", ", ReferenceFormats.ImageExtensions)}. Models: "
                + $"{string.Join(", ", ReferenceFormats.ModelExtensions)}.");
        }

        if (kind != element.ComponentKind)
        {
            throw new CommandException(
                $"'{element.Name}' is a {element.ComponentKind} and '{Path.GetFileName(path)}' "
                + $"would make it a {kind}. Import the other file as its own reference.");
        }

        return path;
    }

    /// <summary>
    /// A picture re-pointed at another file with nothing said about its size keeps
    /// the width it was placed at and follows the new picture's proportions.
    /// </summary>
    /// <remarks>
    /// The alternative — leave the size exactly alone, which is what "named rather
    /// than whole" would say — draws the new picture stretched, and a reference
    /// that lies about the shape of the thing it stands for is worse than one that
    /// moved. Where the two pictures have the same proportions, and where the
    /// picture is merely being re-pointed at itself after a move, nothing changes
    /// at all.
    /// </remarks>
    private static Vector2 Refitted(Vector2 size, Vector2I pixels) =>
        pixels.X > 0 && pixels.Y > 0
            ? new Vector2(size.X, size.X * pixels.Y / pixels.X)
            : size;

    /// <summary>
    /// One number is a width with the height following the picture's own
    /// proportions, which is what "fit this drawing into thirty-two cells" means.
    /// Without a measurable picture there is no aspect to follow, so the height it
    /// already had is kept.
    /// </summary>
    private static Vector2 ReadSize(
        CommandArguments arguments,
        Vector2I pixels,
        Vector2 current)
    {
        string text = arguments.String("size");
        string[] parts = text.Split(',');

        if (parts.Length == 2)
            return new Vector2(Number(parts[0], "size"), Number(parts[1], "size"));

        if (parts.Length != 1)
        {
            throw new CommandException(
                $"'size' takes 'width,height' in cells or one number for the width; got '{text}'.");
        }

        float width = Number(parts[0], "size");

        return pixels.X > 0 && pixels.Y > 0
            ? new Vector2(width, width * pixels.Y / pixels.X)
            : new Vector2(width, current.Y);
    }

    private static Vector2 ReadPixelsPerCell(
        CommandArguments arguments,
        Vector2I pixels,
        IHierarchyElement element)
    {
        float perCell = arguments.Float("pixels-per-cell");

        if (perCell <= 0f)
            throw new CommandException($"'pixels-per-cell' must be above nought; got {perCell}.");

        if (pixels.X <= 0 || pixels.Y <= 0)
        {
            throw new CommandException(
                $"'{element.Name}' has no picture to measure, so pixels per cell means "
                + "nothing. Point it at a file that is there, or set 'size' in cells.");
        }

        return new Vector2(pixels.X / perCell, pixels.Y / perCell);
    }

    private static float Number(string text, string name)
    {
        if (!float.TryParse(
            text.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float value))
        {
            throw new CommandException($"'{name}' expected a number, got '{text.Trim()}'.");
        }

        return value;
    }

    /// <summary>
    /// An argument that means nothing for this kind of reference is refused by
    /// name. Ignoring it would be the dispatcher's own "an argument nobody knows
    /// does nothing and says nothing" bug, one level in: the caller meant something
    /// by it.
    /// </summary>
    private static void Refuse(
        CommandArguments arguments,
        IHierarchyElement element,
        string name,
        string owner)
    {
        if (arguments.Has(name))
        {
            throw new CommandException(
                $"'{name}' is for {owner}, and '{element.Name}' is a {element.ComponentKind}.");
        }
    }

    private static CommandException NotAReference(IHierarchyElement element) =>
        new($"'{element.Name}' is a {element.ComponentKind} element, not a reference. "
            + "'import-reference' is what puts one in the scene.");

    /// <summary>
    /// Absolute, because that is the form every consumer can use without knowing
    /// where the document lives — and the working directory is a fact about the
    /// caller, which is exactly the sort of thing the model must not depend on.
    /// </summary>
    private static string Resolve(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            throw new CommandException($"'{path}' is not a path this system can name.");
        }
    }

    private static string DefaultName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);

        return string.IsNullOrWhiteSpace(name) ? "Reference" : name;
    }
}
