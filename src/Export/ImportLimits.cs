namespace VoxelCAD.Export;

/// <summary>
/// What an import will not do, however politely the file asks.
/// </summary>
internal static class ImportLimits
{
    /// <summary>
    /// The most cells one import will create, counted across everything in the
    /// file rather than per model.
    /// </summary>
    /// <remarks>
    /// It has to be a total, because a per-model bound is no bound at all: a file
    /// may hold any number of models, and a run-length stream states a filled
    /// million in twelve bytes — so a kilobyte of Qubicle could ask for more cells
    /// than the machine has memory, and be given them.
    /// <para>
    /// <b>Measured rather than chosen.</b> An import costs about 320 bytes a cell
    /// once the intermediate list and the document's own map are both standing:
    /// 0.14 GB of process for a model of 20 cells and 1.33 GB for one of 3,958,985,
    /// which is the largest real model in the reference corpus. So this ceiling is
    /// some five gigabytes — survivable, and four times anything anybody has.
    /// </para>
    /// <para>
    /// The number itself is 256 cubed: the largest volume either format calls a
    /// single model, which is the one bound in the neighbourhood that is not
    /// arbitrary. Raising it is a decision about how much memory a file may be
    /// allowed to ask for, and the figures above are what it would be traded
    /// against.
    /// </para>
    /// </remarks>
    public const long MaximumCells = 256L * 256L * 256L;

    /// <summary>The refusal, phrased the same way by every reader.</summary>
    public static string TooManyCells(string extension, long cells) =>
        $"This {extension} file cannot be read: it asks for {cells:N0} cells or more, over the "
        + $"{MaximumCells:N0} an import will create. A file that large is a machine's worth of "
        + "memory rather than a model.";
}
