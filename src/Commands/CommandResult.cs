using System.Threading.Tasks;

namespace VoxelCAD.Commands;

/// <summary>
/// Outcome of one command. <see cref="Value"/> holds plain dictionaries, lists
/// and primitives, so a transport can render it as JSON without knowing what any
/// particular command returns.
/// </summary>
/// <param name="Pending">
/// A later, better answer to the same call, for the one kind of command that has
/// to wait on something outside itself — an agent asking to be told when the
/// editor is free. The immediate <see cref="Value"/> is always a complete answer
/// on its own ("still busy, you are queued"), so a transport that cannot wait
/// simply ignores this and stays correct; one that can — the editor's MCP
/// endpoint — answers with what this resolves to instead.
/// </param>
/// <remarks>
/// A pending answer is deliberately not a general async command shape. The
/// dispatcher stays synchronous, because everything else it does is: a command
/// touches the model on the main thread and is finished. Only the handover has a
/// counterparty to wait for.
/// </remarks>
public readonly record struct CommandResult(
    bool Ok,
    string? Error,
    object? Value,
    Task<CommandResult>? Pending = null)
{
    public static CommandResult Success(object? value = null) => new(true, null, value);

    public static CommandResult Failure(string error) => new(false, error, null);

    /// <summary>
    /// An answer now and a better one later: <paramref name="value"/> describes the
    /// wait itself, <paramref name="pending"/> completes with how it ended.
    /// </summary>
    public static CommandResult Waiting(object? value, Task<CommandResult> pending) =>
        new(true, null, value, pending);
}
