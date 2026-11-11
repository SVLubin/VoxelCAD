using System;
using System.Collections.Generic;
using VoxelCAD.Commands;

namespace VoxelCAD.Presenter;

/// <summary>
/// The one switch that says whether references are drawn at all. Editor-only, like
/// the camera and control commands and for the same reason: it reaches a live
/// viewport, which a headless run does not have.
/// </summary>
/// <remarks>
/// <para>
/// Everything else about a reference — its file, its size, how it is drawn, and
/// whether that one element is hidden — is on the ordinary surface, because all of
/// it is the document's. This is not: it is session state, for looking at the model
/// clean for a moment, so it leaves no history record and no mark in the file.
/// </para>
/// <para>
/// It is on the surface at all because it decides what a <c>frame</c> or a
/// <c>window</c> capture contains, and an agent reading a picture has to be able to
/// find out why the drawing it placed is not in it. Reading it is free; turning it
/// mutates, on <c>focus</c>'s own terms — changing what the user is looking at is
/// gated on holding the editor.
/// </para>
/// </remarks>
internal static class ReferenceSwitchCommands
{
    public static IReadOnlyList<CommandDefinition> Create(EditorSession session)
    {
        return new List<CommandDefinition>
        {
            Query("references-visible",
                "Whether references are drawn at all. Read this before judging a capture: a "
                + "reference missing from a picture is either hidden on its own row, pointing "
                + "at a file that is not there, or switched off here for the whole scene.",
                (_, _) => Report(session)),

            Mutating("set-references-visible",
                "Draws or stops drawing every reference in the scene at once — for looking at "
                + "the model without what it was traced from. It is not the document: nothing "
                + "is written to the file, no history record is left, and no element's own "
                + "hidden flag is touched, so putting it back shows exactly what was shown "
                + "before. To hide one reference and keep the rest, use 'set-visible' on it.",
                (_, arguments) =>
                {
                    session.ReferencesVisible = arguments.Bool("visible", true);

                    return Report(session);
                },
                Required("visible", "true or false."))
        };
    }

    private static CommandResult Report(EditorSession session) =>
        CommandResult.Success(new Dictionary<string, object?>
        {
            ["visible"] = session.ReferencesVisible
        });

    private static CommandParameter Required(string name, string summary) => new(name, summary, true);

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
