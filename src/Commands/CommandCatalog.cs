using System.Collections.Generic;

namespace VoxelCAD.Commands;

/// <summary>
/// Every command there is, one domain at a time. Read commands sit beside write
/// commands on purpose: an agent that acts like a user also has to see like one.
/// </summary>
/// <remarks>
/// The order is the order agents see the tools in, so a domain's commands stay
/// together and the domains keep their long-standing sequence. A new feature's
/// commands go into the domain they belong to — or a new domain class beside
/// these, when they are genuinely a new subject.
/// </remarks>
internal static class CommandCatalog
{
    public static IReadOnlyList<CommandDefinition> Build()
    {
        var builder = new CommandCatalogBuilder();

        // One importer for both domains: 'open' reads a voxel file into a new
        // document and 'import' reads one into the open document, and a second
        // registry could come to know a different set of formats.
        Export.IImporter importer = Export.ExportFactory.CreateImporter();

        ProjectCommands.AddTo(builder, importer);
        SceneCommands.AddTo(builder);
        ElementCommands.AddTo(builder);
        PrefabCommands.AddTo(builder);
        TransformCommands.AddTo(builder);
        CameraCommands.AddTo(builder);
        ReferenceCommands.AddTo(builder);
        VoxelCommands.AddTo(builder);
        MaterialCommands.AddTo(builder);
        HistoryCommands.AddTo(builder);
        QueryCommands.AddTo(builder);
        ExportCommands.AddTo(builder);
        ImportCommands.AddTo(builder, importer);

        return builder.Definitions;
    }
}
