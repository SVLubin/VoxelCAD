using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Render;

/// <summary>
/// The way in. A document is compiled into a scene the tracer can march, and a
/// tracer is started on it; nothing else in this layer is public.
/// </summary>
/// <remarks>
/// The two steps are separate because they cost differently and fail differently:
/// compiling walks the document and can say what it had to leave out, while
/// starting a tracer touches a device and can find there is not one.
/// </remarks>
public static class RenderFactory
{
    /// <summary>
    /// Walks a scene into what a render needs: every visible voxel model as a grid
    /// in its own cells, and the materials those grids wear.
    /// </summary>
    public static RenderScene CompileScene(IProject project, IScene scene) =>
        RenderSceneBuilder.Build(project, scene);

    /// <summary>
    /// Starts a render, or says why it could not be started. A machine without a
    /// device that runs compute is an answer rather than a fault, so it is
    /// reported and not thrown.
    /// </summary>
    /// <param name="finishes">
    /// A height field per facture the scene reports in <see cref="RenderScene.Factures"/>.
    /// They are drawn by the view, which draws the same patterns for the viewport,
    /// so a render and the editor wear one set of factures rather than two that
    /// look alike. A finish left out simply renders flat.
    /// </param>
    /// <remarks>
    /// Answers something to watch the render with, ticked a frame at a time by
    /// whoever owns the clock — it is deliberately not a thread, a rendering
    /// device being drivable only from the thread the engine renders on. What it
    /// does instead is submit a batch and pick it up next frame.
    /// <para>
    /// The synchronous <see cref="TryCreateTracer"/> beside it is what a command
    /// uses — one call that renders to the end and answers a picture has no
    /// interface to keep answering.
    /// </para>
    /// </remarks>
    public static bool TryStartRender(
        RenderScene scene,
        RenderSettings settings,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        out IRenderJob? job,
        out string error)
    {
        job = null;

        if (!TryCreateTracer(scene, settings, camera, finishes, out IPathTracer? tracer, out error))
            return false;

        job = new RenderJob(tracer!);

        return true;
    }

    public static bool TryCreateTracer(
        RenderScene scene,
        RenderSettings settings,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        out IPathTracer? tracer,
        out string error)
    {
        bool ok = PathTracer.TryCreate(scene, settings, camera, finishes, out PathTracer? created, out error);

        tracer = created;

        return ok;
    }
}
