namespace VoxelCAD.CommonTypes;

/// <summary>
/// Who a history record is attributed to. <see cref="Name"/> identifies the agent
/// — its model or client name — and is empty for the user, whose label the
/// presenter supplies so wording stays in the UI layer.
/// </summary>
/// <remarks>
/// The default value is the user, which is also correct for every record written
/// before this existed.
/// </remarks>
public readonly record struct HistoryActor(ActorKind Kind, string Name)
{
    /// <summary>
    /// Which grant of control this record belongs to, counted from one; zero for
    /// the user, who has no sessions. Records carry it so the history panel can
    /// tell one connection's work from the next one's — a name cannot, because
    /// the same agent reconnecting would look like the same run of edits.
    /// </summary>
    /// <remarks>
    /// Stamped like the rest of the actor, by whoever owns the handover, and
    /// never interpreted by the model: to the history it is one more number
    /// riding along with a record.
    /// </remarks>
    public int Session { get; init; }
}
