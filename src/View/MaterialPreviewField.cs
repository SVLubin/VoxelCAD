using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The material as a block you can turn: a fold with a little lit scene in it,
/// and two buttons that walk it round.
/// </summary>
/// <remarks>
/// Turnable rather than a fixed three-quarter view, because half of what a
/// facture does is only visible at another angle — a brushed streak reads as a
/// mark from the front and as a sheen from the side, and which of those is wanted
/// is the question being asked.
/// <para>
/// <b>It is dragged, and what made that possible is one line.</b> Four earlier
/// routes to the gesture were tried and none arrived, and the common cause turned
/// out to be the <see cref="SubViewportContainer"/>: it takes a mouse event in
/// order to forward it into its own viewport, so it raises no <c>gui_input</c> of
/// its own <em>and</em> nothing laid over it is ever asked. Nothing inside the
/// stage is a control, so it has no business claiming a press —
/// <c>MouseFilter.Ignore</c> on the container, and an ordinary
/// <see cref="Control"/> over it answers like any other.
/// </para>
/// <para>
/// Folded away by default and remembered in the session: it is a live viewport,
/// so it draws every frame it is shown, and somebody working on transforms has no
/// use for it.
/// </para>
/// </remarks>
internal sealed partial class MaterialPreviewField : VBoxContainer
{
    /// <summary>Canvas units. Square, and about as tall as three rows.</summary>
    private const float Side = 132f;

    private readonly Button _toggle = new();
    private readonly PanelContainer _window = new();
    private readonly Control _stack = new();
    private readonly PreviewChequer _chequer = new();
    private readonly SubViewportContainer _frame = new();

    /// <summary>
    /// What the hand actually grabs. A control of its own, laid <em>over</em> the
    /// viewport rather than being it: a <see cref="SubViewportContainer"/> takes a
    /// mouse event in order to forward it into its own viewport, so anything under
    /// it is never asked and the container itself raises no <c>gui_input</c>.
    /// </summary>
    private readonly Control _grab = new();
    private readonly MaterialPreviewStage _stage = new();

    private bool _turning;

    public MaterialPreviewField()
    {
        _toggle.Text = "Preview";
        _toggle.ToggleMode = true;
        _toggle.Alignment = HorizontalAlignment.Left;
        _toggle.TooltipText = "A block of this material, lit on its own.";
        _toggle.Named("MaterialPreviewToggle").MouseOnly();
        _toggle.Toggled += pressed => ShownChanged?.Invoke(pressed);

        _frame.Stretch = true;
        _frame.Named("MaterialPreview");
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        // Twice the shown size, filtered down: supersampling by another name, and
        // the one form of antialiasing that needs no pipeline changes. The block
        // is nearly all straight edges at 45° to the pixel grid, which is the
        // worst case there is for a jagged silhouette, and it is small — so the
        // edges are most of what there is to look at.
        _stage.Size = new Vector2I((int)Side * 2, (int)Side * 2);

        _stage.RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible;

        _frame.AddChild(_stage);

        // Nothing inside the stage is a control, so the container has no business
        // claiming a press — and while it did, nothing laid over it could be
        // reached. This is what four earlier attempts at this gesture were losing
        // to.
        _frame.MouseFilter = MouseFilterEnum.Ignore;

        // Under the viewport, and shown only for a colour that lets light through.
        _chequer.SetAnchorsPreset(LayoutPreset.FullRect);
        _chequer.Named("MaterialPreviewChequer");

        _stack.CustomMinimumSize = new Vector2(Side, Side);
        _stack.MouseFilter = MouseFilterEnum.Ignore;
        _stack.AddChild(_chequer);
        _stack.AddChild(_frame);

        // Last, so it is drawn last and hit-tested first — Godot picks a control
        // by walking children in reverse.
        _grab.Named("MaterialPreviewGrab");
        _grab.SetAnchorsPreset(LayoutPreset.FullRect);
        _grab.MouseFilter = MouseFilterEnum.Stop;
        _grab.MouseDefaultCursorShape = CursorShape.Move;
        _grab.TooltipText = "Drag to turn the block.";
        _grab.GuiInput += OnGrabInput;
        _stack.AddChild(_grab);

        // A window of its own rather than a cube floating on the panel. The
        // viewport itself stays transparent — that is what lets the command hand
        // back a picture with nothing behind it — so the ground is painted here,
        // under it, in the editor's own colours.
        _window.Named("MaterialPreviewWindow");
        _window.AddChild(_stack);

        AddChild(_toggle);
        AddChild(_window);
    }

    /// <summary>
    /// The whole gesture. It goes through <c>_GuiInput</c> rather than through the
    /// chrome's polled idiom, and that is right here for the reason the render
    /// card's canvas gives: hover has to follow the machine's cursor whatever an
    /// agent is doing, but a press <em>captured</em> by a control does not — Godot
    /// goes on feeding motion to whatever the press grabbed, injected or not.
    /// </summary>
    private void OnGrabInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            _turning = button.Pressed;
            _grab.AcceptEvent();

            return;
        }

        if (!_turning || @event is not InputEventMouseMotion motion)
            return;

        _stage.Turn(MaterialPreviewDrag.Apply(_stage.Pose, motion.Relative, _grab.Size));
        _grab.AcceptEvent();
    }

    /// <summary>The fold was pressed. Remembered by the session, not by this control.</summary>
    public event Action<bool>? ShownChanged;

    public override void _Ready() => Repaint();

    /// <summary>
    /// A theme colour read once is exactly what a repaint cannot reach, so it is
    /// read again on the notification — the standing rule for anything in this
    /// editor that holds a colour rather than looking one up as it draws.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged)
            Repaint();
    }

    public void SetMaterial(string name, Color color, MaterialAppearance appearance)
    {
        _stage.SetMaterial(name, color, appearance);

        // Automatic rather than a switch: a flat ground is the better reading for
        // an opaque material, and there is no case where somebody wants the wrong
        // one of the two.
        //
        // Asked of the declaration rather than of the alpha. An opaque material
        // may carry an alpha nobody chose, and the chequer is what the preview
        // uses to say "you can see through this" — so reading the alpha put a
        // transparency backdrop behind a material that is not transparent.
        _chequer.Visible = appearance.OpacityOf(color.A) < 1f;
    }

    /// <summary>Reflects the remembered fold without reporting it back as a press.</summary>
    public void SetShown(bool shown)
    {
        _toggle.SetPressedNoSignal(shown);
        _window.Visible = shown;
    }

    private void Repaint()
    {
        var ground = new StyleBoxFlat
        {
            BgColor = GetThemeColor("inset_background", EditorThemeFactory.SemanticType),
            BorderColor = GetThemeColor("border", EditorThemeFactory.SemanticType)
        };

        ground.SetCornerRadiusAll(GetThemeConstant("corner_radius", EditorThemeFactory.SemanticType));
        ground.SetBorderWidthAll(1);

        _window.AddThemeStyleboxOverride("panel", ground);
    }
}
