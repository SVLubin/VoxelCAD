using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The panel a render is shown in: the picture, scaled and dragged about, on a
/// ground that says where its edges are.
/// </summary>
/// <remarks>
/// A <see cref="Control"/> that draws rather than a <see cref="TextureRect"/>,
/// for three things a rect cannot do: it scales about the pointer, it says which
/// part of the picture is on screen so the renderer can trace that part first,
/// and it draws the transparency chequer under a picture that has an alpha
/// channel.
/// <para>
/// The gestures are the engine's <c>_GuiInput</c> rather than the chrome's polled
/// drag. The chrome polls because hover follows the machine's cursor whatever an
/// agent is doing; a press captured by a control does not — Godot goes on feeding
/// motion to whatever the press grabbed — so here the ordinary route is both
/// correct and the one an injected drag arrives on.
/// </para>
/// </remarks>
internal sealed partial class RenderCanvas : Control
{
    /// <summary>A wheel notch. A sixth of a doubling, which is about four notches to twice the size.</summary>
    private const float WheelStep = 1.2f;

    /// <summary>
    /// What one unit of a pan gesture is worth in wheel notches. The viewport's
    /// own number, so a two-finger scroll over the preview and over the scene move
    /// at the same rate.
    /// </summary>
    private const float PanGestureNotches = 0.15f;

    /// <summary>
    /// How long after a pan gesture a wheel notch is ignored. Godot's macOS
    /// backend sends <em>both</em> for one physical two-finger scroll, so without
    /// this a trackpad would zoom twice as far as it asked for.
    /// </summary>
    private const float PanGestureGrace = 0.25f;

    /// <summary>The chequer's square, in screen pixels — the size everything else uses for one.</summary>
    private const int CheckerSquare = 8;

    private Texture2D? _texture;
    private ImageView _view = ImageView.None;
    private Vector2 _imageSize;

    private bool _dragging;
    private Vector2 _grabbed;

    /// <summary>
    /// Whether the picture has been moved from fitting. What the Fit button is
    /// greyed by, and — more to the point — what says whether the renderer is
    /// being asked to favour part of the frame.
    /// </summary>
    private bool _aimed;

    /// <summary>
    /// Whether the next click names a point rather than starting a pan. Armed by a
    /// button rather than by a modifier: a modifier nobody is told about is a
    /// feature nobody finds, and a button is something an agent can press.
    /// </summary>
    private bool _picking;

    /// <summary>Seconds since the last pan gesture, or more than the grace.</summary>
    private float _sincePan = 1f;

    /// <summary>
    /// A region asked for before the panel had a size to show it in. A card opened
    /// and aimed in the same breath is the ordinary way an agent uses one, and a
    /// zero-sized panel can frame nothing — so the request is kept and answered on
    /// the frame the layout lands.
    /// </summary>
    private RenderRegion? _pending;

    public RenderCanvas()
    {
        Name = "RenderCanvas";
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        MouseDefaultCursorShape = CursorShape.Drag;

        SetProcess(false);
    }

    /// <summary>The view moved: the zoom readout follows, and so does what is traced first.</summary>
    public event Action? ViewChanged;

    /// <summary>
    /// A point of the picture was pointed at, in its own pixels. Raised only while
    /// the canvas is picking, so an ordinary click stays a pan.
    /// </summary>
    public event Action<int, int>? PointPicked;

    public float Zoom => _view.Zoom;

    public bool IsFitted => !_aimed;

    public bool IsPicking => _picking;

    public void SetPicking(bool picking)
    {
        _picking = picking;
        MouseDefaultCursorShape = picking ? CursorShape.Cross : CursorShape.Drag;
    }

    /// <summary>Which pixel of the picture a point of the panel is over.</summary>
    public bool TryPointAt(Vector2 point, out int x, out int y)
    {
        x = 0;
        y = 0;

        if (Zoom <= 0f || _imageSize.X <= 0f)
            return false;

        Vector2 at = (point - _view.Offset) / Zoom;

        if (at.X < 0f || at.Y < 0f || at.X >= _imageSize.X || at.Y >= _imageSize.Y)
            return false;

        x = (int)at.X;
        y = (int)at.Y;

        return true;
    }

    /// <summary>
    /// Which part of the picture is on screen, in its own pixels, or empty for all
    /// of it. Empty while the picture is fitted, whatever the arithmetic says: a
    /// rounding of half a pixel at the edge must not read as "the user is looking
    /// at the middle".
    /// </summary>
    public RenderRegion VisibleRegion =>
        _aimed ? _view.VisibleRegion(_imageSize, Size) : default;

    public void SetImage(Texture2D? texture)
    {
        _texture = texture;

        Vector2 size = texture is null
            ? Vector2.Zero
            : new Vector2(texture.GetWidth(), texture.GetHeight());

        // A picture of a different size is a different picture, so the view goes
        // back to fitting rather than keeping a zoom that meant something about
        // the last one.
        if (size != _imageSize)
        {
            _imageSize = size;

            Fit();
        }

        QueueRedraw();
    }

    /// <summary>
    /// Tells the canvas how big the render will be before there is one to show, so
    /// the picture does not arrive and re-fit under the eye.
    /// </summary>
    public void SetImageSize(int width, int height)
    {
        var size = new Vector2(width, height);

        if (size == _imageSize)
            return;

        _imageSize = size;

        Fit();
    }

    public void Fit()
    {
        _pending = null;
        _view = ImageView.Fit(_imageSize, Size);
        _aimed = false;

        QueueRedraw();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// Brings a part of the picture up to fill the panel. An empty region is
    /// "all of it", which is fitting.
    /// </summary>
    public void ShowRegion(RenderRegion region)
    {
        if (region.IsEmpty)
        {
            Fit();

            return;
        }

        if (Size.X <= 0f || Size.Y <= 0f)
        {
            _pending = region;

            return;
        }

        _pending = null;
        _view = ImageView.Framing(region, _imageSize, Size);
        _aimed = true;

        QueueRedraw();
        ViewChanged?.Invoke();
    }

    /// <summary>One screen pixel to one of the render — where noise is judged.</summary>
    public void ShowActualSize()
    {
        _view = _view.AtActualSize(_imageSize, Size);
        _aimed = true;

        QueueRedraw();
        ViewChanged?.Invoke();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationResized)
            return;

        if (_pending is { } wanted && Size.X > 0f && Size.Y > 0f)
        {
            ShowRegion(wanted);

            return;
        }

        // A picture nobody has aimed at follows the panel; one somebody has zoomed
        // into is held where they put it, only re-clamped so it cannot be left
        // hanging off the edge by a resize.
        _view = _aimed ? _view.Clamped(_imageSize, Size) : ImageView.Fit(_imageSize, Size);

        QueueRedraw();
        ViewChanged?.Invoke();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            // Ignored while a trackpad is scrolling: the same physical gesture
            // arrives as a pan gesture as well, and answering both would zoom
            // twice as far. A mouse brings no pan gesture with it, so a real wheel
            // is untouched.
            case InputEventMouseButton { Pressed: true } wheel
                when wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:

                if (_sincePan < PanGestureGrace)
                {
                    AcceptEvent();

                    break;
                }

                ZoomBy(
                    wheel.Position,
                    wheel.ButtonIndex == MouseButton.WheelUp ? WheelStep : 1f / WheelStep);

                AcceptEvent();

                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true }:
                if (_aimed)
                    Fit();
                else
                    ShowActualSize();

                AcceptEvent();

                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } aimed
                when _picking:

                if (TryPointAt(aimed.Position, out int pickedX, out int pickedY))
                    PointPicked?.Invoke(pickedX, pickedY);

                AcceptEvent();

                break;

            case InputEventMouseButton { Pressed: true } press
                when press.ButtonIndex is MouseButton.Left or MouseButton.Middle:

                _dragging = true;
                _grabbed = press.Position;

                AcceptEvent();

                break;

            case InputEventMouseButton { Pressed: false }:
                _dragging = false;

                // Swallowed whether or not a drag was under way: the card behind
                // this puts itself away on a press, and the release that ends a
                // pan must not reach it.
                AcceptEvent();

                break;

            // A trackpad's pinch. Godot reports it as a factor per event, which is
            // already what a zoom multiplies by — no calibration of ours in it.
            case InputEventMagnifyGesture pinch:
                ZoomBy(pinch.Position, pinch.Factor);

                AcceptEvent();

                break;

            // A trackpad's two-finger scroll zooms, which is what it already does
            // over the scene: the viewport turns the same gesture into wheel
            // notches, and a preview that panned on it instead would be the one
            // surface in the editor where the same fingers mean something else.
            // The conversion is the viewport's own so the two feel alike.
            case InputEventPanGesture scroll:
                _sincePan = 0f;

                SetProcess(true);
                ZoomBy(scroll.Position, Mathf.Pow(WheelStep, -scroll.Delta.Y * PanGestureNotches));

                AcceptEvent();

                break;

            case InputEventMouseMotion motion when _dragging:
                Pan(motion.Position - _grabbed);
                _grabbed = motion.Position;

                AcceptEvent();

                break;
        }
    }

    public override void _Process(double delta)
    {
        _sincePan += (float)delta;

        if (_sincePan > PanGestureGrace)
            SetProcess(false);
    }

    public override void _Draw()
    {
        // The panel's own ground, drawn whether or not there is a picture yet: a
        // viewport is a surface a picture sits on, and an empty one showing the
        // glass straight through reads as a hole in the card rather than as a
        // picture that has not arrived.
        DrawRect(
            new Rect2(Vector2.Zero, Size),
            GetThemeColor("inset_background", EditorThemeFactory.SemanticType));

        if (_texture is null || _imageSize.X <= 0f)
            return;

        Rect2 placement = _view.Placement(_imageSize);

        DrawChecker(placement);

        // Past one to one the pixels are what is being looked at, so they are
        // drawn as squares; below it the picture is being read as a picture and
        // smoothing is what a reduction should look like.
        TextureFilter = _view.Zoom >= 1f
            ? TextureFilterEnum.Nearest
            : TextureFilterEnum.LinearWithMipmaps;

        DrawTextureRect(_texture, placement, tile: false);

        DrawRect(placement, GetThemeColor("border", EditorThemeFactory.SemanticType), filled: false, width: 1f);
    }

    /// <summary>
    /// The ground a picture with an alpha channel is read against. Two greys in a
    /// chequer, which is what every application that can show transparency uses —
    /// and here it is also the only thing that says where an empty backdrop's
    /// edges are.
    /// </summary>
    private void DrawChecker(Rect2 placement)
    {
        Rect2 area = placement.Intersection(new Rect2(Vector2.Zero, Size));

        if (area.Size.X <= 0f || area.Size.Y <= 0f)
            return;

        Color dark = GetThemeColor("checker_dark", EditorThemeFactory.SemanticType);
        Color light = GetThemeColor("checker_light", EditorThemeFactory.SemanticType);

        DrawRect(area, dark);

        int firstColumn = (int)Math.Floor(area.Position.X / CheckerSquare);
        int firstRow = (int)Math.Floor(area.Position.Y / CheckerSquare);
        int lastColumn = (int)Math.Ceiling(area.End.X / CheckerSquare);
        int lastRow = (int)Math.Ceiling(area.End.Y / CheckerSquare);

        for (int row = firstRow; row < lastRow; row++)
        {
            for (int column = firstColumn; column < lastColumn; column++)
            {
                if ((row + column) % 2 != 0)
                    continue;

                var square = new Rect2(
                    column * CheckerSquare,
                    row * CheckerSquare,
                    CheckerSquare,
                    CheckerSquare);

                Rect2 clipped = square.Intersection(area);

                if (clipped.Size.X > 0f && clipped.Size.Y > 0f)
                    DrawRect(clipped, light);
            }
        }
    }

    private void ZoomBy(Vector2 at, float factor)
    {
        _view = _view.ScaledAt(at, factor, _imageSize, Size);
        _aimed = !Mathf.IsEqualApprox(_view.Zoom, ImageView.Fit(_imageSize, Size).Zoom);

        QueueRedraw();
        ViewChanged?.Invoke();
    }

    private void Pan(Vector2 delta)
    {
        ImageView moved = _view.Panned(delta, _imageSize, Size);

        if (moved == _view)
            return;

        _view = moved;
        _aimed = true;

        QueueRedraw();
        ViewChanged?.Invoke();
    }
}
