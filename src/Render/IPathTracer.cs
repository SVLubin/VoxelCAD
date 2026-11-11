using System;
using Godot;

namespace VoxelCAD.Render;

/// <summary>
/// A render in progress: samples accumulate a tile at a time and the picture can
/// be read at any point along the way.
/// </summary>
/// <remarks>
/// Progressive by construction, which is what lets one caller run it to the end
/// in a loop and another spend a slice of each frame on it without the two being
/// different renderers. Stopping early gives a noisier picture of the same
/// render, never a different one.
/// <para>
/// The picture is traced in tiles rather than whole passes, so that a caller
/// looking at part of it can say so and have that part converge first. What that
/// changes is the <em>order</em>: every tile is asked for the same number of
/// samples in the end, so a finished render is the same picture whatever was
/// being looked at while it ran.
/// </para>
/// </remarks>
public interface IPathTracer : IDisposable
{
    /// <summary>
    /// Samples per pixel the least converged part of the picture has had — what
    /// the whole render is worth, rather than what its best corner is worth.
    /// </summary>
    int SamplesTaken { get; }

    /// <summary>Samples per pixel the best served part has had.</summary>
    int SamplesPeak { get; }

    /// <summary>Samples per pixel the settings asked for.</summary>
    int SamplesWanted { get; }

    /// <summary>
    /// How many steps make one sample over the whole picture. A caller that wants
    /// to think in samples rather than in tiles multiplies by this.
    /// </summary>
    int StepsPerSample { get; }

    /// <summary>How much of the whole render is done, from nought to one.</summary>
    float Progress { get; }

    /// <summary>
    /// How far from settled the worst part of the picture is, as a relative error;
    /// nought before anything has been measured.
    /// </summary>
    float Noise { get; }

    /// <summary>
    /// Asks for more, or fewer, without starting again. What has accumulated is
    /// not thrown away: a render is the same render however long it is left to
    /// run, so raising this simply lets it carry on.
    /// </summary>
    void SetSamplesWanted(int samples);

    /// <summary>
    /// Re-grades what has accumulated. Exposure is applied when the picture is
    /// read rather than while it is traced, so this costs one pass over the buffer
    /// and never a restart — which is what lets it be turned while looking at the
    /// result.
    /// </summary>
    void SetExposure(float exposure);

    /// <summary>
    /// Which part of the picture to serve first, in image pixels. An empty
    /// rectangle means all of it equally. Nothing already traced is thrown away
    /// and nothing is starved — every tile still finishes.
    /// </summary>
    void SetPriorityRegion(int x, int y, int width, int height);

    bool IsFinished { get; }

    /// <summary>
    /// Traces up to this many more tiles, one sample each, and answers how many it
    /// managed. Which tiles is decided by where the caller said it was looking.
    /// </summary>
    int RunSteps(int steps);

    /// <summary>
    /// The same, without waiting for the device to finish — the caller waits when
    /// it next has nothing better to do, which is what keeps a render from costing
    /// the frame that started it.
    /// </summary>
    int SubmitSteps(int steps);

    /// <summary>Waits for whatever was submitted; does nothing if nothing was.</summary>
    void Wait();

    /// <summary>
    /// Measures how far from settled each part of the picture still is, and lets
    /// that decide — with where the viewer is looking — what the next samples are
    /// spent on. It changes the order and not the total.
    /// </summary>
    void MeasureNoise();

    /// <summary>
    /// How far the camera ray at this pixel reached, or false where it reached
    /// nothing. What focusing the lens on something in the picture is done with.
    /// </summary>
    bool TryReadDepth(int x, int y, out float distance);

    /// <summary>
    /// The mean radiance over a rectangle of the picture, in the units the tracer
    /// works in — before exposure, before the film curve, before glare. A picture
    /// is what a render is for; this is what a check needs, since read off the
    /// encoded image an energy error comes back about a third smaller than it is.
    /// </summary>
    Vector3 ReadRadiance(int x, int y, int width, int height);

    /// <summary>What has been accumulated, exposed, tone mapped and encoded.</summary>
    Image ReadImage();

    /// <summary>
    /// The same picture as bytes, four to a pixel. What a render running on its own
    /// thread publishes: a <see cref="Image"/> is an engine object, and the thread
    /// that reads the device is not the one that will hand it to a texture.
    /// </summary>
    byte[] ReadPixels(out int width, out int height);
}
