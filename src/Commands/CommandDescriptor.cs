using System.Collections.Generic;

namespace VoxelCAD.Commands;

/// <summary>What a command is called, what it does, and what it takes.</summary>
public sealed class CommandDescriptor
{
    public CommandDescriptor(
        string name,
        string summary,
        IReadOnlyList<CommandParameter> parameters,
        bool mutates = true,
        bool drives = false)
    {
        Name = name;
        Summary = summary;
        Parameters = parameters;
        Mutates = mutates;
        Drives = drives;
    }

    public string Name { get; }

    public string Summary { get; }

    public IReadOnlyList<CommandParameter> Parameters { get; }

    /// <summary>
    /// Whether the command changes the document. A read-only command runs whoever
    /// holds control; a mutating one is gated on it inside the editor, so an agent
    /// cannot edit while the user does.
    /// </summary>
    public bool Mutates { get; }

    /// <summary>
    /// Whether the command is the <i>hand</i> rather than an action of its own —
    /// the injected pointer and keys, which drive the interface and do not edit
    /// anything themselves.
    /// </summary>
    /// <remarks>
    /// The distinction is not about permission, where these behave like any other
    /// edit; it is about the canon's "anything that starts a new action ends the
    /// one in flight first". A synthesized keystroke starts nothing — it belongs to
    /// whatever gesture is under way, and ending that gesture on each one would cut
    /// a driven colour drag into a record per frame, which is the opposite of what
    /// a gesture exists for. Measured: a click, a select-all and a typed value
    /// arrived as three records before this flag existed.
    /// </remarks>
    public bool Drives { get; }
}
