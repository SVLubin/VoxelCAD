using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// A render in progress, driven a frame at a time. What a caller does with one is
/// ask how it is going, take whatever picture is ready, and turn the few dials
/// that do not mean starting again.
/// </summary>
/// <remarks>
/// Driven a frame at a time by whoever owns the clock. It is deliberately not a
/// thread: a rendering device may only be driven from the thread the engine
/// renders on, which was measured rather than assumed. What it does instead is
/// submit a batch and pick it up next frame, so the frame's own drawing happens
/// while the card is tracing.
/// </remarks>
public interface IRenderJob : IDisposable
{
    /// <summary>How it is going, as one consistent reading.</summary>
    RenderProgress Progress { get; }

    /// <summary>
    /// One frame's worth of work. Everything this job does happens here, so a
    /// caller that stops ticking stops the render without having to say so.
    /// </summary>
    void Tick();

    /// <summary>
    /// The picture as it last stood, or false when nothing new has arrived since
    /// the last time it was asked. Polled rather than raised, because the caller
    /// is a frame loop and one is ticking this anyway.
    /// </summary>
    bool TryTakeImage(out byte[] pixels, out int width, out int height);

    /// <summary>
    /// The picture as it last stood, whether or not it has been taken already.
    /// What somebody looking at the card asked to keep is what they can see.
    /// </summary>
    bool TryPeekImage(out byte[] pixels, out int width, out int height);

    /// <summary>
    /// Which part of the picture to serve first, in image pixels. An empty
    /// rectangle means all of it equally.
    /// </summary>
    void SetPriorityRegion(int x, int y, int width, int height);

    void SetExposure(float exposure);

    /// <summary>
    /// How far away what is at this pixel of the picture is, or false where there
    /// is nothing there. Focusing the lens by pointing at something rather than by
    /// typing a distance.
    /// </summary>
    bool TryReadDepth(int x, int y, out float distance);

    void SetSamplesWanted(int samples);
}
