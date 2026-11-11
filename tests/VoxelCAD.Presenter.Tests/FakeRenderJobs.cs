using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using VoxelCAD.Render;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// A renderer that starts without a graphics device, so the card's decisions can
/// be asked about at all. What it records is what the card asked for; what it
/// hands back is a job that does nothing but remember.
/// </summary>
internal sealed class FakeRenderJobs : IRenderJobs
{
    private readonly List<RenderSettings> _startedList = new();
    private readonly List<CameraView> _camerasList = new();

    /// <summary>Set to refuse, the way a machine with no compute device does.</summary>
    public string Refusal { get; set; } = string.Empty;

    public IReadOnlyList<RenderSettings> Started => _startedList;

    /// <summary>Where each render was taken from, in the order they started.</summary>
    public IReadOnlyList<CameraView> Cameras => _camerasList;

    public FakeRenderJob? Last { get; private set; }

    public int Starts => _startedList.Count;

    public bool TryStart(
        RenderScene scene,
        RenderSettings settings,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        out IRenderJob? job,
        out string error)
    {
        if (Refusal.Length > 0)
        {
            job = null;
            error = Refusal;

            return false;
        }

        _startedList.Add(settings);
        _camerasList.Add(camera);

        Last = new FakeRenderJob(settings.Samples);
        job = Last;
        error = string.Empty;

        return true;
    }
}

/// <summary>One render, as far as the card can tell.</summary>
internal sealed class FakeRenderJob : IRenderJob
{
    /// <summary>
    /// Started with the sample count it was asked for, because the real tracer is:
    /// a restart carries it in the settings, and only a dial that re-grades reaches
    /// <see cref="SetSamplesWanted"/> at all. A fake that learned it solely from
    /// that setter would report nought for every render that started again.
    /// </summary>
    public FakeRenderJob(int samplesWanted) => SamplesWanted = samplesWanted;

    public bool Disposed { get; private set; }

    public int Ticks { get; private set; }

    public float Exposure { get; private set; } = float.NaN;

    public int SamplesWanted { get; private set; }

    public RenderRegion Region { get; private set; }

    /// <summary>What a depth read answers, or nothing where the ray reached the sky.</summary>
    public float? Depth { get; set; }

    /// <summary>
    /// What the card reads to report itself. The sample count is composed rather
    /// than stored, because the real tracer answers <c>SamplesWanted</c> from the
    /// scheduler that <see cref="SetSamplesWanted"/> just moved — a fake that let
    /// the two disagree would let a test agree with itself.
    /// </summary>
    public RenderProgress Progress
    {
        get => _progress with { SamplesWanted = SamplesWanted };
        set => _progress = value;
    }

    private RenderProgress _progress = RenderProgress.None;

    public void Tick() => Ticks++;

    public bool TryTakeImage(out byte[] pixels, out int width, out int height) =>
        Peek(out pixels, out width, out height);

    public bool TryPeekImage(out byte[] pixels, out int width, out int height) =>
        Peek(out pixels, out width, out height);

    public void SetPriorityRegion(int x, int y, int width, int height) =>
        Region = new RenderRegion(x, y, width, height);

    public void SetExposure(float exposure) => Exposure = exposure;

    public void SetSamplesWanted(int samples) => SamplesWanted = samples;

    public bool TryReadDepth(int x, int y, out float distance)
    {
        distance = Depth ?? 0f;

        return Depth.HasValue;
    }

    public void Dispose() => Disposed = true;

    /// <summary>
    /// Never a picture, and that is a limit rather than a shortcut: what the card
    /// does with one is build a Godot <c>Image</c> and an <c>ImageTexture</c>, both
    /// of which need an engine — so a fake that answered with pixels would take the
    /// whole test host down rather than fail. "Nothing new since you last asked" is
    /// a state the card is written for, so the path is honest as far as it goes.
    /// </summary>
    private static bool Peek(out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = 0;
        height = 0;

        return false;
    }
}
