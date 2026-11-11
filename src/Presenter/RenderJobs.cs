using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Render;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// The real renderer, behind the seam the card is handed. It holds nothing and
/// decides nothing — it forwards, which is the whole point: everything worth
/// deciding is on the other side of it, where a test can reach it.
/// </summary>
internal sealed class RenderJobs : IRenderJobs
{
    public static IRenderJobs Default { get; } = new RenderJobs();

    public bool TryStart(
        RenderScene scene,
        RenderSettings settings,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        out IRenderJob? job,
        out string error) =>
        RenderFactory.TryStartRender(scene, settings, camera, finishes, out job, out error);
}
