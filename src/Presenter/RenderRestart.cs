using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// Whether a dial that moved means tracing everything again, or only reading what
/// has already been traced differently.
/// </summary>
/// <remarks>
/// Two dials describe how the result is <em>read</em> rather than what is being
/// traced: exposure is applied when the picture is resolved, and the sample count
/// only says how long the same render may carry on for. Everything else — the
/// size, the sun, the floor — changes what the light does, and a picture that kept
/// its old samples through one of those would be two renders averaged together.
/// <para>
/// Stated as "everything except these two" rather than as a list of what restarts,
/// so a dial added tomorrow restarts until somebody decides it should not. A list
/// of exceptions rots as the record grows; this one cannot.
/// </para>
/// </remarks>
internal static class RenderRestart
{
    public static bool IsNeeded(RenderDials was, RenderDials wanted) =>
        Grading(was) != Grading(wanted);

    /// <summary>The dials with the two that only re-grade taken out of the comparison.</summary>
    private static RenderDials Grading(RenderDials dials) =>
        dials with { Exposure = 0f, Samples = 0 };
}
