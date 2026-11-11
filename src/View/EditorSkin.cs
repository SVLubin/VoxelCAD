using System;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The editor's look, live: one <see cref="Theme"/> and the palette it was
/// written from, repainted in place when the setting changes.
/// </summary>
/// <remarks>
/// Shared and mutable, on the same terms as <see cref="MotionTiming"/> and for
/// the same reason — a copy would be a snapshot, so pressing Save would leave
/// half the editor wearing the old look. Everything that draws holds this one
/// object, or the one <see cref="Theme"/> inside it, and follows.
/// <para>
/// Three things need saying about how far the theme resource reaches. Styleboxes,
/// colours and constants are <b>looked up as a control draws</b>, so overwriting
/// them repaints the chrome with nobody being told. Icons are <b>held</b> by
/// whoever was handed one, which is why <see cref="EditorIconFactory"/> repaints
/// its textures rather than replacing them. And the two shader materials and the
/// scene's own selection colour are outside the theme entirely, so they are
/// re-tinted here — the short list of places a look has to be pushed to rather
/// than read from.
/// </para>
/// </remarks>
internal sealed class EditorSkin
{
    private readonly Theme _theme = new();

    private EditorTheme _kind = EditorTheme.Dark;
    private Color _accent = EditorPalette.DefaultAccent;

    public EditorSkin()
    {
        Palette = EditorPalette.Dark();
        Repaint();
    }

    /// <summary>Raised after the look has changed, for whatever cannot be looked up.</summary>
    public event Action? Changed;

    public Theme Theme => _theme;

    /// <summary>Which way round the chrome is drawn, as it stands.</summary>
    public EditorTheme Kind => _kind;

    public EditorPalette Palette { get; private set; }

    /// <summary>
    /// Repaints the editor, or does nothing where the answer is the one already
    /// on screen — rebuilding a theme is a few hundred styleboxes and every
    /// icon, and the preferences push their whole set on every save.
    /// </summary>
    public void Apply(EditorTheme kind, Color accent)
    {
        if (kind == _kind && accent == _accent)
            return;

        _kind = kind;
        _accent = accent;
        Palette = EditorPalette.For(kind, accent);

        Repaint();
        Changed?.Invoke();
    }

    /// <summary>
    /// The accent as the preferences carry it — six hex digits — falling back to
    /// the shipped one where the text is not a colour. The parsing is here
    /// because this is the layer that has colours in it; the preferences keep the
    /// string canonical and never have to know what it means.
    /// </summary>
    public static Color ReadAccent(string accent) =>
        string.IsNullOrWhiteSpace(accent)
            ? EditorPalette.DefaultAccent
            : Color.FromString(accent, EditorPalette.DefaultAccent);

    private void Repaint()
    {
        EditorThemeFactory.Fill(_theme, Palette);

        GlassPanelMaterial.Retint(Palette);
        GhostOutlineMaterial.Retint(Palette);
    }
}
