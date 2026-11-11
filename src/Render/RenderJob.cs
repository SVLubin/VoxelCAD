using System;
using System.Diagnostics;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// A render in progress, driven a frame at a time: it submits a batch, lets the
/// frame draw while the card works on it, and picks the answer up next time round.
/// </summary>
/// <remarks>
/// It was a thread first, and that is worth writing down because it looked as
/// though it worked. A <em>local</em> rendering device is documented as being for
/// work on a separate thread, and it is not: every call from a thread of our own
/// was refused with "can only be called from the render thread", while the
/// scheduler went on counting samples nothing had traced and the card showed a
/// progress bar filling and an estimate counting down for a render doing no work
/// at all. Measured, in the running editor, from the engine's own log.
/// <para>
/// What is left buys most of what the thread was for, and honestly. The cost was
/// never the submission — it was the <c>Sync</c> straight after it, where the
/// processor stood and watched the card. Submitting at the end of one frame and
/// waiting at the start of the next means the frame's own drawing happens
/// alongside the tracing, and the wait is usually already satisfied when it comes.
/// </para>
/// <para>
/// The batch is sized by what it costs rather than counted, so a dial moved or a
/// card closed is answered within about a frame whatever the machine is.
/// </para>
/// </remarks>
internal sealed class RenderJob : IRenderJob
{
    /// <summary>
    /// How long one batch should take on the device. About two thirds of a
    /// sixty-hertz frame: long enough that submitting is not what the render
    /// spends its time on, short enough that the wait at the head of the next
    /// frame is over before it starts.
    /// </summary>
    private const double BatchSeconds = 0.011;

    /// <summary>How often the picture is read off the device and shown.</summary>
    private const double RefreshSeconds = 0.1;

    /// <summary>
    /// How often the picture is measured for where it is still noisy. Slower than
    /// the refresh because the answer moves slowly — noise falls as the square root
    /// of the samples — and the measurement walks every pixel.
    /// </summary>
    private const double MeasureSeconds = 1.0;

    /// <summary>
    /// How much of the rate reading is the newest batch. What is left is estimated
    /// from how fast it is going now rather than from the whole run, or the first
    /// slow seconds of a render would still be dragging the estimate down at the
    /// end of it.
    /// </summary>
    private const double RateSmoothing = 0.15;

    private readonly IPathTracer _tracer;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _batch = new();

    private byte[]? _pixels;
    private int _width;
    private int _height;
    private bool _fresh;

    private double _sinceRefresh;
    private double _sinceMeasure;
    private double _stepsPerSecond;
    private int _batchSteps;
    private int _submitted;
    private bool _publishedFinal;

    public RenderJob(IPathTracer tracer)
    {
        _tracer = tracer;

        // A whole sample over the picture is the wrong first guess on a large one
        // and the right one on a small: an eighth of it is a batch that measures
        // itself within a frame or two either way.
        _batchSteps = Math.Max(1, tracer.StepsPerSample / 8);

        _clock.Start();

        // A job always has a picture. The first batch runs here rather than on the
        // first frame, so a caller that opens a card and asks what is on it in the
        // same breath is answered instead of being told to come back — and the card
        // itself shows something on the frame it appears rather than a blank.
        _batch.Restart();
        _submitted = _tracer.SubmitSteps(_batchSteps);

        _tracer.Wait();
        Publish();
    }

    public RenderProgress Progress => new(
        _tracer.SamplesTaken,
        _tracer.SamplesPeak,
        _tracer.SamplesWanted,
        _tracer.Progress,
        _clock.Elapsed.TotalSeconds,
        _stepsPerSecond / Math.Max(_tracer.StepsPerSample, 1),
        _tracer.Noise,
        _tracer.IsFinished);

    /// <summary>
    /// One frame's worth. Waits for what was submitted last time, measures it,
    /// publishes a picture if one is due, and submits the next batch.
    /// </summary>
    public void Tick()
    {
        if (_tracer.IsFinished)
        {
            if (_publishedFinal)
                return;

            _clock.Stop();
            Publish();

            _publishedFinal = true;

            return;
        }

        _publishedFinal = false;

        if (!_clock.IsRunning)
            _clock.Start();

        // The batch submitted last frame. Waiting for it here rather than where it
        // was submitted is the whole trick: the card has had a frame to work on it.
        _tracer.Wait();

        double seconds = _batch.Elapsed.TotalSeconds;

        if (_submitted > 0 && seconds > 0d)
            Measure(_submitted, seconds);

        _sinceMeasure += seconds;

        if (_sinceMeasure >= MeasureSeconds)
        {
            _sinceMeasure = 0d;

            _tracer.MeasureNoise();
        }

        _sinceRefresh += seconds;

        if (_sinceRefresh >= RefreshSeconds)
        {
            _sinceRefresh = 0d;

            Publish();
        }

        _batch.Restart();
        _submitted = _tracer.SubmitSteps(_batchSteps);
    }

    public bool TryTakeImage(out byte[] pixels, out int width, out int height)
    {
        if (!_fresh || _pixels is null)
        {
            pixels = Array.Empty<byte>();
            width = 0;
            height = 0;

            return false;
        }

        _fresh = false;
        pixels = _pixels;
        width = _width;
        height = _height;

        return true;
    }

    public bool TryPeekImage(out byte[] pixels, out int width, out int height)
    {
        pixels = _pixels ?? Array.Empty<byte>();
        width = _width;
        height = _height;

        return _pixels is not null;
    }

    public void SetPriorityRegion(int x, int y, int width, int height) =>
        _tracer.SetPriorityRegion(x, y, width, height);

    public void SetExposure(float exposure)
    {
        _tracer.SetExposure(exposure);

        // Exposure is applied when the picture is read, so a finished render has
        // to be read again for a turn of it to be seen at all.
        Publish();
    }

    public void SetSamplesWanted(int samples) => _tracer.SetSamplesWanted(samples);

    public bool TryReadDepth(int x, int y, out float distance) =>
        _tracer.TryReadDepth(x, y, out distance);

    public void Dispose() => _tracer.Dispose();

    /// <summary>
    /// What the last batch cost decides how big the next one is. Kept inside a
    /// factor of two either way, so one slow batch does not make the next a stall.
    /// </summary>
    private void Measure(int steps, double seconds)
    {
        double rate = steps / seconds;

        _stepsPerSecond = _stepsPerSecond > 0d
            ? _stepsPerSecond * (1d - RateSmoothing) + rate * RateSmoothing
            : rate;

        _batchSteps = Math.Clamp(
            (int)(rate * BatchSeconds),
            Math.Max(1, _batchSteps / 2),
            Math.Max(1, _batchSteps * 2));
    }

    private void Publish()
    {
        _pixels = _tracer.ReadPixels(out _width, out _height);
        _fresh = true;
    }
}
