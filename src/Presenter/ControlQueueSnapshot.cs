namespace VoxelCAD.Presenter;

/// <summary>
/// The line of agents waiting for the editor, as one reader sees it: how many are
/// in it, who is next, and where the reader itself stands.
/// </summary>
/// <param name="Count">How many agents are waiting; zero when nobody is.</param>
/// <param name="NextAgent">
/// What the first in line calls itself. Empty when the line is empty — and empty
/// when that agent never introduced itself, which is why <paramref name="Count"/>
/// is what says whether anybody is waiting.
/// </param>
/// <param name="Position">
/// The reader's own place, counting from one; zero when it is not in the line.
/// One-based because it is a place in a queue as a person would say it.
/// </param>
internal readonly record struct ControlQueueSnapshot(int Count, string NextAgent, int Position);
