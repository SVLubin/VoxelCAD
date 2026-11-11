using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;

namespace VoxelCAD.Commands;

/// <summary>
/// The answers several command domains shape the same way: what the scene and an
/// element look like as a report, and how a voxel model is resolved from an
/// argument. One copy, or two domains drift apart about the same element.
/// </summary>
internal static class CommandCatalogHelper
{
    /// <summary>
    /// The six faces as the surface names them, indexed the way the face
    /// machinery indexes them — '+x' is face 0 and so on — so a command that
    /// names a face and one that reports one cannot come to spell it differently.
    /// </summary>
    public static readonly IReadOnlyList<string> FaceNames =
        new[] { "+x", "-x", "+y", "-y", "+z", "-z" };

    /// <summary>
    /// The element a command was pointed at, refused by name when it holds no
    /// voxels — a group and a model look alike from the outside, and "no voxels
    /// here" is a more useful answer than an empty grid.
    /// </summary>
    public static IVoxelModel GetVoxelModel(
        CommandContext context,
        CommandArguments arguments,
        out IHierarchyElement element)
    {
        var text = arguments.String("element");
        context.Scene.TryGetElement(context.ResolveElement(text), out element);

        return element.Component as IVoxelModel
            ?? throw new CommandException(
                $"'{element.Name}' is a {element.ComponentKind} element, not a voxel model.");
    }

    /// <summary>
    /// Reported in the unit 'set-symmetry' accepts: offsets from the pivot, 0
    /// lying exactly on it. A read after a pivot move shows new offsets over the
    /// same unmoved planes, which is the honest description of what happened.
    /// </summary>
    public static Dictionary<string, object?> DescribeSymmetry(
        SymmetryValues symmetry,
        Vector3 pivot)
    {
        SymmetryValues offsets = SymmetryUnits.ToOffsets(symmetry, pivot);

        return new Dictionary<string, object?>
        {
            ["x"] = DescribeSymmetryPlane(offsets.X),
            ["y"] = DescribeSymmetryPlane(offsets.Y),
            ["z"] = DescribeSymmetryPlane(offsets.Z)
        };
    }

    public static Dictionary<string, object?> Describe(CommandContext context) =>
        new()
        {
            ["scene"] = context.Scene.Id.ToString(),
            ["name"] = context.Scene.Name,
            ["size"] = new[] { context.Scene.Size.X, context.Scene.Size.Y, context.Scene.Size.Z },
            ["elements"] = context.Scene.Children
                .Select(child => (object?)DescribeElement(child, deep: true))
                .ToList()
        };

    public static Dictionary<string, object?> DescribeElement(IHierarchyElement element, bool deep)
    {
        var description = new Dictionary<string, object?>
        {
            ["id"] = element.Id.ToString(),
            ["name"] = element.Name,
            ["kind"] = element.ComponentKind.ToString(),
            ["position"] = ToArray(element.Transform.Position),
            ["rotation"] = ToArray(element.Transform.RotationDegrees),
            ["scale"] = ToArray(element.Transform.Scale),
            ["modes"] = new[]
            {
                element.TransformModes.Position.ToString(),
                element.TransformModes.Rotation.ToString(),
                element.TransformModes.Scale.ToString()
            },
            // The element's own flag, always reported rather than left silent in
            // its default state: "why is this not in my export" is exactly the
            // question a reader arrives with, and an absent key answers it only
            // for somebody who already knows the default.
            ["visible"] = element.Visible,
            // Not derivable from the three values above: it is the *world*
            // transform, so an ancestor's tilt decides it too. False means the
            // element's cells no longer sit on the scene's own grid, which is what
            // stops the region's walls being a surface the brush can place against.
            ["gridAligned"] = GridAlignment.IsAligned(element.WorldTransform),
            // The point 'position' above names, as an offset from the middle of
            // what the element holds — the unit 'set-pivot' accepts. 0,0,0 reads
            // "the grip is at the model's centre" without a coordinate in sight.
            ["pivot"] = ToArray(PivotUnits.ToOffset(element.PivotPoint, element))
        };

        // What a camera sees through. Always, unlike symmetry's silence in its
        // default state: a lens is the whole of what a camera is, so a reader that
        // was told the kind and nothing else would have to ask a second command
        // what the shot actually is.
        if (element.Component is ICamera camera)
            description["lens"] = DescribeLens(camera.Lens);

        // What a reference stands for. Always, on the lens's own terms: the file
        // is the whole of what a reference is, so a reader told the kind and
        // nothing else would have to ask a second command what it is looking at.
        if (element.Component is IReferenceImage image)
            description["reference"] = DescribeReference(image.Settings);

        if (element.Component is IReferenceModel reference)
            description["reference"] = DescribeReference(reference.Settings);

        // What an instance is: a reference. Its children below are derived from
        // that prefab and cannot be edited — 'edit-prefab' is the way in.
        if (element.Component is IPrefabInstance instance)
            description["source"] = instance.SourceId.ToString();

        // Present exactly on the elements every edit refuses, naming the instance
        // they belong to, so a reader learns why before it is told no.
        if (element.PrefabInstanceId is { } ownerId)
            description["prefabContent"] = ownerId.ToString();

        if (element.Component is IVoxelModel model)
        {
            description["voxels"] = model.Voxels.Count;

            // Silent while every slot is in its default state, so the common
            // case does not carry three "off" entries through every tree read.
            if (model.Symmetry != default)
                description["symmetry"] = DescribeSymmetry(model.Symmetry, element.PivotPoint);
        }

        if (deep)
        {
            description["children"] = element.Children
                .Select(child => (object?)DescribeElement(child, deep: true))
                .ToList();
        }

        return description;
    }

    /// <summary>
    /// A camera's lens, in the units <c>set-camera</c> accepts. One copy, so the
    /// element reports and the camera reports cannot come to describe the same
    /// lens differently.
    /// </summary>
    public static Dictionary<string, object?> DescribeLens(CameraLens lens) =>
        new()
        {
            ["projection"] = lens.Projection.ToString(),
            ["fov"] = System.Math.Round(lens.FovDegrees, 3),
            ["orthoHeight"] = System.Math.Round(lens.OrthographicHeight, 3),
            ["blur"] = System.Math.Round(lens.Aperture, 3),
            ["focus"] = System.Math.Round(lens.FocusDistance, 3)
        };

    /// <summary>
    /// A reference plane, in the units <c>set-reference</c> accepts. One copy, so
    /// the element report and the references report cannot come to describe the
    /// same picture differently.
    /// </summary>
    /// <remarks>
    /// <c>found</c> costs a look at the disk per reference per read. It is worth it
    /// and it is the first thing a reader needs: everything else here describes a
    /// picture that may not be there, and a size beside a path nobody can open is
    /// how "the reference is blank" becomes an hour of looking in the wrong place.
    /// </remarks>
    public static Dictionary<string, object?> DescribeReference(ReferenceImageSettings settings) =>
        new()
        {
            ["path"] = settings.Path,
            ["found"] = ReferenceFile.Exists(settings.Path),
            ["sizeCells"] = new[] { settings.SizeInCells.X, settings.SizeInCells.Y },
            // Nought where nothing has ever measured the file, which is the same
            // answer as "it was not there when it was named".
            ["pixels"] = new[] { settings.PixelSize.X, settings.PixelSize.Y },
            ["pixelsPerCell"] = new[] { settings.PixelsPerCell.X, settings.PixelsPerCell.Y },
            ["opacity"] = settings.Opacity,
            ["filtered"] = settings.Filtered,
            ["lit"] = settings.LitByScene
        };

    /// <inheritdoc cref="DescribeReference(ReferenceImageSettings)"/>
    public static Dictionary<string, object?> DescribeReference(ReferenceModelSettings settings) =>
        new()
        {
            ["path"] = settings.Path,
            ["found"] = ReferenceFile.Exists(settings.Path),
            ["opacity"] = settings.Opacity,
            ["lit"] = settings.LitByScene,
            ["wireframe"] = settings.Wireframe
        };

    /// <summary>
    /// Every element under these, depth first and in tree order — the order a
    /// reader sees them in the project tree, so a listing and the panel agree about
    /// which one is first.
    /// </summary>
    public static IEnumerable<IHierarchyElement> Walk(IReadOnlyList<IHierarchyElement> children)
    {
        foreach (IHierarchyElement child in children)
        {
            yield return child;

            foreach (IHierarchyElement nested in Walk(child.Children))
                yield return nested;
        }
    }

    /// <summary>
    /// A range as a parameter's description says it. Shared so that two commands
    /// quoting the same kind of bound cannot come to phrase it differently.
    /// </summary>
    public static string Bound(float least, float most) => $"{least:0.###} to {most:0.###}";

    public static float[] ToArray(Vector3 value) => new[] { value.X, value.Y, value.Z };

    public static int[] ToArray(Vector3I value) => new[] { value.X, value.Y, value.Z };

    /// <summary>Parses "'+x,-y'" or "'all'" into face indices; refusals name the vocabulary.</summary>
    public static IReadOnlyList<int> ParseFaceNames(string text)
    {
        if (string.Equals(text.Trim(), "all", System.StringComparison.OrdinalIgnoreCase))
            return new[] { 0, 1, 2, 3, 4, 5 };

        var facesList = new List<int>();

        foreach (var part in text.Split(',', System.StringSplitOptions.TrimEntries))
        {
            var index = -1;

            for (var face = 0; face < FaceNames.Count; face++)
            {
                if (string.Equals(part, FaceNames[face], System.StringComparison.OrdinalIgnoreCase))
                {
                    index = face;
                    break;
                }
            }

            if (index < 0)
            {
                throw new CommandException(
                    $"'{part}' is not a face. Faces are +x, -x, +y, -y, +z, -z, or 'all'.");
            }

            facesList.Add(index);
        }

        if (facesList.Count == 0)
            throw new CommandException("Argument 'faces' names no face.");

        return facesList;
    }

    private static Dictionary<string, object?> DescribeSymmetryPlane(SymmetryPlane plane) =>
        new()
        {
            ["enabled"] = plane.Enabled,
            ["plane"] = plane.PlaneDoubled / 2.0
        };
}
