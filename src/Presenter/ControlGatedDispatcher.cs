using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;

namespace VoxelCAD.Presenter;

/// <summary>
/// Wraps the dispatcher one agent drives, and lets a mutating command through only
/// while <b>that</b> agent holds control. Read commands and the control commands
/// themselves always pass — an agent must be able to look, and to ask for
/// control, before it has any.
/// </summary>
/// <remarks>
/// This is the enforcement behind "the user and an agent never edit at once", and
/// behind "and no two agents either". The user's own edits do not pass through
/// here — they go straight to the model through the panel presenters — so only
/// agents are gated.
/// <para>
/// The gate asks whether the editor is held <b>by this client</b>, not merely
/// whether it is held. Checking the status alone was the hole under the whole
/// arrangement: with one agent working, a second one's edits passed the moment the
/// first was granted control, and the model was edited under its hands.
/// </para>
/// <para>
/// It is also the one place that sees every command from one agent, so it is where
/// liveness is stamped: each command, refused or not, tells the arbiter that agent
/// is still there. The signal is silence, not work — a command that takes minutes
/// is one command, and stamping it once at the start is enough.
/// </para>
/// <para>
/// And it is where the canon's <b>anything that starts a new action ends the one in
/// flight first</b> reaches the agent surface. The shell says that for its own
/// actions; the dispatcher is the GUI's peer rather than something under it, so an
/// edit arriving here has to say it too. Without it an agent that had driven the
/// interface — clicked into a colour field, say — left that gesture open, and the
/// next command was recorded <em>before</em> the earlier, still-uncommitted edit:
/// an 'undo' then reversed the record under it and took the user's edit with it,
/// leaving no trace in the history of either. Measured on the running editor.
/// </para>
/// </remarks>
internal sealed class ControlGatedDispatcher : ICommandDispatcher
{
    private readonly ICommandDispatcher _inner;
    private readonly ControlArbiter _arbiter;
    private readonly EditorSession _session;
    private readonly string _clientId;

    public ControlGatedDispatcher(
        ICommandDispatcher inner,
        ControlArbiter arbiter,
        EditorSession session,
        string clientId)
    {
        _inner = inner;
        _arbiter = arbiter;
        _session = session;
        _clientId = clientId;
    }

    public IReadOnlyList<CommandDescriptor> Commands => _inner.Commands;

    public CommandResult Execute(string name, IReadOnlyDictionary<string, string> arguments)
    {
        // Before the gate, so a refused edit still counts as proof of life: an
        // agent trying to work is an agent that is still there. It counts for a
        // waiting agent too, which is what keeps its reservation alive.
        _arbiter.NoteAgentCommand(_clientId);

        CommandDescriptor? descriptor = _inner.Commands.FirstOrDefault(command => command.Name == name);

        if (descriptor is { Mutates: true })
        {
            if (!_arbiter.IsHeldBy(_clientId))
                return CommandResult.Failure(GetRefusal());

            // A gesture the interface left open goes on collecting, so this edit
            // would be filed inside it — or, worse, published after it. Only for a
            // mutating command: looking must not disturb a gesture, and a read is
            // exactly what an agent does while a user is mid-drag. And not for the
            // injected input, which is the hand rather than an action — ending the
            // gesture on each keystroke would cut a driven drag into a record a
            // frame, which is what a gesture exists to prevent.
            if (!descriptor.Drives)
                _session.EndGesture();
        }

        return _inner.Execute(name, arguments);
    }

    /// <summary>
    /// Why the edit was refused, in the terms that tell the agent what to do next.
    /// Being second in line is a different situation from the user simply having the
    /// editor, and an agent that cannot tell them apart either waits when it should
    /// ask or asks when it should wait.
    /// </summary>
    private string GetRefusal()
    {
        if (_arbiter.IsClaimedByAnother(_clientId))
        {
            string name = _arbiter.AgentName.Length > 0 ? $"'{_arbiter.AgentName}'" : "Another agent";

            return $"{name} holds the editor, so nothing you send can edit the document. Call "
                + "'take-control' with 'wait=<seconds>' to reserve your turn, or 'control-status' "
                + "to see who has it.";
        }

        return "The editor is under the user's control. Call 'take-control' and wait for it "
            + "to be granted before editing.";
    }
}
