using Godot;

namespace VoxelCAD.View;

/// <summary>
/// A control drawn from <see cref="PanelShapeField"/>: it carries a material and
/// keeps that material's picture of its own shape in step with its size, its
/// radius and the notch it has been given.
/// </summary>
/// <remarks>
/// The panels' glass and the landing outline are the same shape in different
/// paint, so the uniforms that <em>describe</em> the shape are pushed in one
/// place. A surface that pushed its own would be the second reading of the same
/// facts, which is the drift this exists to remove.
/// </remarks>
internal abstract partial class ShapedSurface : ColorRect
{
    private Vector2 _stub;
    private bool _stubOnRight;
    private float _cornerRadius = 3f;
    private float _borderWidth = 1f;

    protected ShapedSurface(ShaderMaterial material)
    {
        Material = material;

        // Spanning the cut, it would eat the presses meant for the tabs the
        // notch was carved to reveal — the wrapper rule this chrome has paid
        // twice. The panel refuses its notch in _HasPoint; these never take the
        // mouse at all.
        MouseFilter = MouseFilterEnum.Ignore;

        Resized += Push;
    }

    /// <summary>
    /// The notch this surface keeps and which side it stands on. The shape's
    /// rectangle is the control's own, so only the cut crosses here.
    /// </summary>
    public void SetShape(PanelShape shape)
    {
        if (_stub == shape.Stub && _stubOnRight == shape.StubOnRight)
            return;

        _stub = shape.Stub;
        _stubOnRight = shape.StubOnRight;

        Push();
    }

    public override void _Ready() => ReadTheme();

    /// <summary>
    /// Re-read on a theme change, the way the panels re-read their icons: both
    /// of these are the theme's numbers, and a surface built before it settled
    /// would otherwise draw the defaults for the rest of the session.
    /// </summary>
    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            ReadTheme();
    }

    private void ReadTheme()
    {
        _cornerRadius = GetThemeConstant("corner_radius", EditorThemeFactory.SemanticType);
        _borderWidth = GetThemeConstant("border_width", EditorThemeFactory.SemanticType);

        Push();
    }

    private void Push()
    {
        SetInstanceShaderParameter("rect_size", Size);
        SetInstanceShaderParameter("corner_radius", _cornerRadius);
        SetInstanceShaderParameter("border_width", _borderWidth);
        SetInstanceShaderParameter("stub", _stub);
        SetInstanceShaderParameter("stub_on_right", _stubOnRight ? 1f : 0f);
    }
}
