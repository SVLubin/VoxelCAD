using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelCAD.Export;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>
/// Somebody else's voxel file read into this document — the way in from
/// MagicaVoxel and Qubicle.
/// </summary>
/// <remarks>
/// An import is an ordinary edit: it creates elements, cells and materials as one
/// undoable record, so <c>undo</c> takes the whole of it back. What it does not do
/// is open a document — <c>open</c> on one of these extensions does that, and says
/// so there.
/// </remarks>
internal static class ImportCommands
{
    public static void AddTo(CommandCatalogBuilder builder, IImporter importer)
    {
        string extensions = string.Join(
            ", ", importer.Formats.Select(format => $"{format.Extension} ({format.Label})"));

        builder.Add("import",
            "Reads somebody else's voxel file into the active scene: MagicaVoxel .vox or "
            + "Qubicle .qb. What arrives is ordinary elements with ordinary cells — the file is "
            + "read, not linked, so editing it elsewhere changes nothing here. One history "
            + "record, so 'undo' takes the whole import back, materials and all. Only the "
            + "palette entries the cells actually wear become materials. Use 'open' instead to "
            + "start a new document from one of these files.",
            (context, arguments) =>
            {
                var path = arguments.String("path");

                if (!importer.CanRead(path))
                    throw Unreadable(path, extensions);

                Guid parentId = arguments.Has("parent")
                    ? context.ResolveElement(arguments.String("parent"))
                    : context.Scene.Id;

                ImportResult result = importer.Apply(
                    Plan(importer, path), context.Project, context.Scene, parentId);

                return CommandResult.Success(Describe(result));
            },
            Required("path",
                $"The file to read. Formats: {extensions}. A relative path is resolved against "
                + "the working directory."),
            Optional("parent",
                "Element to put the import under, by id or name. Defaults to the scene root."));

        builder.AddQuery("import-formats", "Lists the file formats 'import' and 'open' can read.",
            (_, _) => CommandResult.Success(importer.Formats
                .Select(format => (object?)new Dictionary<string, object?>
                {
                    ["extension"] = format.Extension,
                    ["label"] = format.Label
                })
                .ToList()));
    }

    /// <summary>
    /// Reads a file, turning the reader's own refusals into ones phrased for
    /// whoever asked.
    /// </summary>
    /// <remarks>
    /// Shared with <c>open</c>, which reads a voxel file into a new document, so
    /// the two cannot come to disagree about what a damaged file does.
    /// </remarks>
    public static IImportPlan Plan(IImporter importer, string path)
    {
        if (!File.Exists(path))
            throw new CommandException($"There is no file at '{path}'.");

        try
        {
            return importer.Read(path);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // The reader refuses before anything has been created, so there is
            // nothing to undo and nothing half-imported — only a message to pass
            // on in the caller's own terms.
            throw new CommandException(exception.Message);
        }
    }

    public static CommandException Unreadable(string path, string extensions)
    {
        string extension = Path.GetExtension(path);
        var named = string.IsNullOrEmpty(extension)
            ? $"'{path}' has no extension"
            : $"'{extension}' is not a format this editor reads";

        return new CommandException($"{named}. Formats: {extensions}.");
    }

    public static Dictionary<string, object?> Describe(ImportResult result) =>
        new()
        {
            ["element"] = result.RootName,
            ["id"] = result.RootId.ToString(),
            ["format"] = result.Format,
            ["elements"] = result.Elements,
            ["cells"] = result.Cells,
            ["materials"] = result.Materials,
            ["size"] = ToArray(result.Extent),
            ["notes"] = result.Notes.ToList()
        };
}
