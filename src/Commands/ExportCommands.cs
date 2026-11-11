using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelCAD.Export;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;

namespace VoxelCAD.Commands;

/// <summary>External artifacts: a scene baked to a file, and the formats on offer.</summary>
internal static class ExportCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        // One exporter for both commands: it holds only the format registry, so
        // sharing it across every 'export' is cheaper than rebuilding it each call.
        IExporter exporter = ExportFactory.CreateExporter();

        // Reading, not writing: it produces an external artifact and leaves the
        // document untouched, the same status as 'capture' writing a PNG.
        builder.AddQuery("export", "Writes a scene to an external file; the extension picks the format.",
            (context, arguments) =>
            {
                var path = arguments.String("path");
                string extension = Path.GetExtension(path);

                if (string.IsNullOrEmpty(extension))
                    throw new CommandException($"'{path}' has no extension to pick a format from.");

                IScene scene = context.Scene;

                if (arguments.Has("scene"))
                    context.Project.TryGetScene(context.ResolveScene(arguments.String("scene")), out scene);

                CullingPolicy? culling = arguments.Has("culling")
                    ? arguments.Enum<CullingPolicy>("culling")
                    : null;
                ExportEncoding? encoding = arguments.Has("encoding")
                    ? arguments.Enum<ExportEncoding>("encoding")
                    : null;

                var options = new ExportOptions(
                    arguments.Has("scale") ? arguments.Float("scale") : 1f,
                    culling,
                    encoding);

                // The exporter buffers the whole file set and writes it only on
                // success, so a refused format or scene leaves nothing behind.
                IReadOnlyList<ExportArtifact> artifacts =
                    exporter.Export(context.Project, scene, path, options);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["scene"] = scene.Name,
                    ["format"] = extension,
                    ["files"] = artifacts
                        .Select(artifact => (object?)new Dictionary<string, object?>
                        {
                            ["path"] = artifact.Path,
                            ["bytes"] = artifact.Bytes
                        })
                        .ToList()
                });
            },
            Required("path", "Where to write; the extension picks the format (see 'export-formats')."),
            Optional("scene", "Scene to export, by id or name. Defaults to the active scene."),
            Optional("scale", "Output units per voxel. Defaults to 1. STL treats the unit as a millimetre."),
            Optional("culling", "Visual or Solid, forcing the mesh policy. Defaults to the format's own."),
            Optional("encoding", "ascii or binary, where a format offers both."));

        builder.AddQuery("export-formats", "Lists the file formats 'export' can write.",
            (_, _) => CommandResult.Success(exporter.Formats
                .Select(format => (object?)new Dictionary<string, object?>
                {
                    ["extension"] = format.Extension,
                    ["label"] = format.Label,
                    ["facet"] = format.Facet.ToString()
                })
                .ToList()));
    }
}
