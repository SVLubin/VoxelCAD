using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VoxelCAD.Export;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>The project's own lifecycle: a document is started, opened, saved.</summary>
internal static class ProjectCommands
{
    /// <summary>
    /// The room a fresh document gets, and the floor under the room a document
    /// opened from a voxel file gets.
    /// </summary>
    private static readonly Vector3I DefaultSceneSize = new(32, 32, 32);

    public static void AddTo(CommandCatalogBuilder builder, IImporter importer)
    {
        string readable = string.Join(
            ", ", importer.Formats.Select(format => $"{format.Extension} ({format.Label})"));

        string samples = string.Join(", ", ModelFactory.SampleNames);

        builder.Add("new", "Starts an empty project, or one of the samples the editor ships "
            + "with — the one a first launch opens. A sample arrives untitled and unmodified, so "
            + "saving it asks where and the shipped copy is never written over. In the editor, "
            + "an unsaved document being replaced keeps a recovery snapshot; 'autosave-status' "
            + "lists it.",
            (context, arguments) =>
            {
                if (arguments.Has("sample"))
                {
                    if (arguments.Has("size"))
                        throw new CommandException(
                            "'size' and 'sample' do not go together: a sample brings its own room.");

                    context.SetProject(OpenSample(arguments.String("sample")), null);

                    return CommandResult.Success(Describe(context));
                }

                Vector3I size = arguments.Has("size")
                    ? arguments.VectorI("size")
                    : DefaultSceneSize;

                context.SetProject(ModelFactory.CreateProject(size), null);

                return CommandResult.Success(Describe(context));
            },
            Optional("size",
                "Buildable region in cells, 'x,y,z'. Defaults to "
                + $"{DefaultSceneSize.X},{DefaultSceneSize.Y},{DefaultSceneSize.Z}."),
            Optional("sample", $"Start from a shipped sample instead of an empty project: {samples}."));

        builder.Add("open", "Opens a project from disk. A voxel file — MagicaVoxel .vox or "
            + "Qubicle .qb — is read into a new document instead, sized to what arrived and left "
            + "untitled, so saving it asks where rather than writing this editor's format over "
            + "somebody else's file. In the editor, an unsaved document being replaced keeps a "
            + "recovery snapshot; 'autosave-status' lists it.",
            (context, arguments) =>
            {
                var path = arguments.String("path");

                if (!importer.CanRead(path))
                {
                    context.SetProject(Load(path), path);

                    return CommandResult.Success(Describe(context));
                }

                if (!File.Exists(path))
                    throw new CommandException($"There is no file at '{path}'.");

                // Read, build and fill before anything is replaced, so a file that
                // turns out to be unreadable leaves the open document exactly as it
                // was rather than swapping it for an empty one. It is also the
                // cheaper order: a document nothing is bound to yet costs no view
                // work at all while a hundred and sixty thousand cells arrive.
                IImportPlan plan = ImportCommands.Plan(importer, path);
                IProject project = ModelFactory.CreateProject(RoomFor(plan.Extent));
                IScene scene = project.Scenes[0];
                ImportResult result = importer.Apply(plan, project, scene, scene.Id);

                // Untitled on purpose. The path names somebody else's file in
                // somebody else's format, and adopting it would put a .vcad in it
                // the first time the user pressed Save.
                context.SetProject(project, null);

                Dictionary<string, object?> described = Describe(context);

                // Under a key of its own rather than merged: both halves have
                // something to say about "size" — the room the document got, and
                // the box the model arrived in — and merging them would quietly
                // let one answer stand for the other.
                described["imported"] = ImportCommands.Describe(result);

                return CommandResult.Success(described);
            },
            Required("path", $"Path to a .vcad file, or a voxel file to read: {readable}."));

        builder.Add("save", "Writes the project to disk.",
            (context, arguments) =>
            {
                // Not String("path", fallback): the fallback would be evaluated
                // even when the argument is present, and throw.
                var path = arguments.Has("path")
                    ? arguments.String("path")
                    : context.Path ?? throw new CommandException("No path yet; pass 'path'.");

                context.Project.Save(path);
                context.MarkSaved(path);

                return CommandResult.Success(new Dictionary<string, object?> { ["path"] = path });
            },
            Optional("path", "Where to write. Defaults to where the project was opened from."));
    }

    /// <summary>
    /// Reads a project file, turning whatever a damaged one throws into a refusal
    /// phrased for whoever asked.
    /// </summary>
    /// <remarks>
    /// A broad catch, and this is where the style guide allows one: reading a file
    /// somebody else may have damaged is a genuine execution boundary, and a
    /// deserializer meeting nonsense throws whatever its own parser throws. Without
    /// it a corrupt <c>.vcad</c> escaped every transport with a stack trace —
    /// through the console, through an agent, and through the File menu, which now
    /// opens by the same route as the rest.
    /// </remarks>
    private static IProject Load(string path)
    {
        if (!File.Exists(path))
            throw new CommandException($"There is no file at '{path}'.");

        try
        {
            return ModelFactory.LoadProject(path);
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CommandException(
                $"'{path}' could not be read as a project: {exception.Message}");
        }
    }

    /// <summary>Opens a shipped sample, turning an unknown name into a refusal that lists them.</summary>
    private static IProject OpenSample(string name)
    {
        try
        {
            return ModelFactory.OpenSample(name);
        }
        catch (KeyNotFoundException exception)
        {
            throw new CommandException(exception.Message);
        }
    }

    /// <summary>
    /// The buildable region a freshly opened voxel file gets. Rounded up only in
    /// the sense that it is never smaller than what arrived, on any axis.
    /// </summary>
    private static Vector3I RoomFor(Vector3I extent) => new(
        Math.Max(extent.X, DefaultSceneSize.X),
        Math.Max(extent.Y, DefaultSceneSize.Y),
        Math.Max(extent.Z, DefaultSceneSize.Z));
}
