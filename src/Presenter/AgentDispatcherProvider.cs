using System;
using System.Collections.Generic;
using VoxelCAD.Commands;

namespace VoxelCAD.Presenter;

/// <inheritdoc cref="IAgentDispatcherProvider"/>
/// <remarks>
/// One dispatcher per client, cached, because the control commands are closed over
/// the client they speak for — an agent cannot name another one, so it cannot end
/// its session or write on its plaque. Everything under them is shared: the same
/// context, the same document, the same history. A dispatcher here is a view, not
/// state.
/// <para>
/// That is what makes the cache cheap to be wrong about. A client id arrives from a
/// header, so a rude client could mint an unbounded number of them; past
/// <see cref="MaximumCachedClients"/> the cache is emptied rather than grown, and
/// the only cost of a wrongly dropped entry is rebuilding a catalogue.
/// </para>
/// </remarks>
internal sealed class AgentDispatcherProvider : IAgentDispatcherProvider
{
    private const int MaximumCachedClients = 16;

    private readonly ControlArbiter _arbiter;
    private readonly ControlPresenter _control;
    private readonly EditorSession _session;
    private readonly Func<IReadOnlyList<CommandDefinition>, ICommandDispatcher> _buildInner;

    private readonly Dictionary<string, ICommandDispatcher> _byClientDictionary =
        new(StringComparer.Ordinal);

    /// <param name="buildInner">
    /// Builds the document surface plus the host commands, given the control
    /// commands to add to them. The shell owns what goes in there; this class owns
    /// only that there is one per client.
    /// </param>
    public AgentDispatcherProvider(
        ControlArbiter arbiter,
        ControlPresenter control,
        EditorSession session,
        Func<IReadOnlyList<CommandDefinition>, ICommandDispatcher> buildInner)
    {
        _arbiter = arbiter;
        _control = control;
        _session = session;
        _buildInner = buildInner;
    }

    public ICommandDispatcher ForClient(string clientId)
    {
        if (_byClientDictionary.TryGetValue(clientId, out ICommandDispatcher? cached))
            return cached;

        if (_byClientDictionary.Count >= MaximumCachedClients)
            _byClientDictionary.Clear();

        ICommandDispatcher dispatcher = new ControlGatedDispatcher(
            _buildInner(ControlCommands.Create(_control, clientId)),
            _arbiter,
            _session,
            clientId);

        _byClientDictionary[clientId] = dispatcher;

        return dispatcher;
    }
}
