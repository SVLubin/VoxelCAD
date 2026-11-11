using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Render;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Where a render comes from, as something the card can be handed rather than
/// something it calls.
/// </summary>
/// <remarks>
/// <para>
/// One line of indirection, and it is the line that separates the render card's
/// <em>decisions</em> from the one call in it that needs a graphics device.
/// Starting a tracer reaches <c>RenderingServer</c>, and a test host without an
/// engine does not merely fail there — this file's ledger already records a
/// static Godot call taking a whole run down and printing the passing count
/// underneath it. So every path through the card that ends in a render was
/// untestable, and that is nearly every path: whether a moved dial re-grades or
/// starts again, whether an edit to the document marks the picture stale, whether
/// the scene subscription is dropped again. All of them are gated behind there
/// being a job, and only a device could make one.
/// </para>
/// <para>
/// It is the standing fix this repository already applies at every panel seam,
/// one layer further in: keep the decisions out of the part that needs an engine.
/// <see cref="RenderJobs.Default"/> is the real one and is what the shell hands
/// over; nothing else about the card changes.
/// </para>
/// </remarks>
internal interface IRenderJobs
{
    /// <summary>
    /// Starts a render, or says why it could not. A machine without a device that
    /// runs compute is an answer rather than a fault, which is why this reports
    /// instead of throwing.
    /// </summary>
    bool TryStart(
        RenderScene scene,
        RenderSettings settings,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        out IRenderJob? job,
        out string error);
}
