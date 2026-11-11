using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Where a picture sits inside the panel showing it: how far it is scaled and
/// where its top left corner has been dragged to.
/// </summary>
/// <remarks>
/// Pure, and out of the node for the reason every decision in this codebase is —
/// a rule inside a <see cref="Control"/> is a rule no test can reach. The one
/// worth holding is the zoom's: the point of the image under the pointer must not
/// move while the wheel turns, or zooming walks the picture out from under the
/// thing being looked at.
/// <para>
/// It also answers <see cref="VisibleRegion"/>, which is not a display question
/// at all: it is what the renderer is told to trace first, so a viewer zoomed
/// into a corner converges that corner rather than the whole frame.
/// </para>
/// </remarks>
internal readonly record struct ImageView(Vector2 Offset, float Zoom)
{
    /// <summary>
    /// Below one screen pixel per twenty of the image nothing is legible, and a
    /// picture cannot be zoomed out past fitting anyway — <see cref="Fit"/> is
    /// allowed under this, which is what lets a large render still be fitted.
    /// </summary>
    public const float MinimumZoom = 0.05f;

    /// <summary>
    /// Thirty-two screen pixels to one of the picture. A render is pixels and
    /// somebody judging noise wants to see them, which is also why past one to one
    /// they are drawn without smoothing.
    /// </summary>
    public const float MaximumZoom = 32f;

    /// <summary>Nothing yet: no picture, so nowhere to put one.</summary>
    public static ImageView None => new(Vector2.Zero, 1f);

    /// <summary>
    /// As large as it goes while still whole, centred. What a card opens at, and
    /// what the Fit button gives back.
    /// </summary>
    public static ImageView Fit(Vector2 image, Vector2 canvas)
    {
        if (image.X <= 0f || image.Y <= 0f || canvas.X <= 0f || canvas.Y <= 0f)
            return None;

        float zoom = Math.Min(canvas.X / image.X, canvas.Y / image.Y);

        return new ImageView(Vector2.Zero, zoom).Clamped(image, canvas);
    }

    /// <summary>
    /// One screen pixel to one of the picture, holding whatever is in the middle
    /// of the panel. Held rather than centred on the picture, because pressing 1:1
    /// while looking at a corner should show that corner at full size rather than
    /// jump somewhere else.
    /// </summary>
    public ImageView AtActualSize(Vector2 image, Vector2 canvas) =>
        ZoomedAt(canvas * 0.5f, 1f, image, canvas);

    /// <summary>
    /// As close in as a region of the picture can be brought while still showing
    /// all of it, centred on it. What "show me this part" means, and what the
    /// renderer is then told to trace first.
    /// </summary>
    public static ImageView Framing(RenderRegion region, Vector2 image, Vector2 canvas)
    {
        if (region.IsEmpty || image.X <= 0f || image.Y <= 0f || canvas.X <= 0f || canvas.Y <= 0f)
            return Fit(image, canvas);

        float zoom = Math.Min(canvas.X / region.Width, canvas.Y / region.Height);
        var middle = new Vector2(region.X + region.Width * 0.5f, region.Y + region.Height * 0.5f);

        return new ImageView(canvas * 0.5f - middle * zoom, zoom).Clamped(image, canvas);
    }

    /// <summary>Where the picture lands, in the panel's own coordinates.</summary>
    public Rect2 Placement(Vector2 image) => new(Offset, image * Zoom);

    /// <summary>
    /// Scaled about a point, so that whatever was under it stays under it. The
    /// whole reason the offset is stored rather than a centre: a centre would have
    /// to be re-solved for this every time, and getting it wrong is the drift
    /// everybody has felt in a viewer that zooms about the middle instead.
    /// </summary>
    public ImageView ZoomedAt(Vector2 point, float zoom, Vector2 image, Vector2 canvas)
    {
        float wanted = Math.Clamp(zoom, Floor(image, canvas), MaximumZoom);

        if (Zoom <= 0f)
            return new ImageView(Vector2.Zero, wanted).Clamped(image, canvas);

        Vector2 held = (point - Offset) / Zoom;

        return new ImageView(point - held * wanted, wanted).Clamped(image, canvas);
    }

    /// <summary>Scaled by a factor about a point — what a wheel notch means.</summary>
    public ImageView ScaledAt(Vector2 point, float factor, Vector2 image, Vector2 canvas) =>
        ZoomedAt(point, Zoom * factor, image, canvas);

    public ImageView Panned(Vector2 delta, Vector2 image, Vector2 canvas) =>
        new ImageView(Offset + delta, Zoom).Clamped(image, canvas);

    /// <summary>
    /// Which part of the picture is actually on screen, in its own pixels. Empty
    /// when all of it is, because "all of it" is what the renderer is told when
    /// nobody is looking anywhere in particular — and an empty region is how that
    /// is said.
    /// </summary>
    public RenderRegion VisibleRegion(Vector2 image, Vector2 canvas)
    {
        if (Zoom <= 0f || image.X <= 0f || image.Y <= 0f)
            return default;

        int left = (int)Math.Floor(Math.Clamp(-Offset.X / Zoom, 0f, image.X));
        int top = (int)Math.Floor(Math.Clamp(-Offset.Y / Zoom, 0f, image.Y));
        int right = (int)Math.Ceiling(Math.Clamp((canvas.X - Offset.X) / Zoom, 0f, image.X));
        int bottom = (int)Math.Ceiling(Math.Clamp((canvas.Y - Offset.Y) / Zoom, 0f, image.Y));

        if (left <= 0 && top <= 0 && right >= (int)image.X && bottom >= (int)image.Y)
            return default;

        return new RenderRegion(left, top, Math.Max(right - left, 0), Math.Max(bottom - top, 0));
    }

    /// <summary>
    /// Kept where a picture can be seen: smaller than the panel it is centred,
    /// larger than the panel it may not be dragged clear of it. The rule every
    /// photo viewer has, and the one thing that makes panning feel like moving a
    /// print about rather than steering a camera.
    /// </summary>
    public ImageView Clamped(Vector2 image, Vector2 canvas)
    {
        Vector2 size = image * Zoom;

        return new ImageView(
            new Vector2(Hold(Offset.X, size.X, canvas.X), Hold(Offset.Y, size.Y, canvas.Y)),
            Zoom);
    }

    /// <summary>
    /// How far out this picture may be zoomed in this panel: never past fitting,
    /// however large the picture is.
    /// </summary>
    private static float Floor(Vector2 image, Vector2 canvas)
    {
        if (image.X <= 0f || image.Y <= 0f || canvas.X <= 0f || canvas.Y <= 0f)
            return MinimumZoom;

        return Math.Min(MinimumZoom, Math.Min(canvas.X / image.X, canvas.Y / image.Y));
    }

    private static float Hold(float offset, float size, float canvas)
    {
        if (size <= canvas)
            return (canvas - size) * 0.5f;

        return Math.Clamp(offset, canvas - size, 0f);
    }
}
