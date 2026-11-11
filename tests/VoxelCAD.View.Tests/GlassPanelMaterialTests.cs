using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The one promise the frosting switch makes: off, the screen is not read.
/// </summary>
/// <remarks>
/// Held as a property of the shader <em>source</em> rather than of a picture,
/// because that is where the promise actually lives. Godot copies the back buffer
/// for a canvas item whose shader <b>declares</b> <c>hint_screen_texture</c>,
/// whether or not a branch went on to sample it — so a uniform switch inside one
/// shader would have turned the blur off and gone on paying for it, and a
/// screenshot could not tell the two apart. A photograph says the panel is solid;
/// only this says it is free.
/// </remarks>
public sealed class GlassPanelMaterialTests
{
    private const string ScreenHint = "hint_screen_texture";

    [Fact]
    public void TheFrostedPaneReadsTheScreen()
    {
        Assert.Contains(ScreenHint, GlassPanelMaterial.FrostedCode);
    }

    /// <summary>
    /// The whole of the setting. If this ever fails, the switch has become a
    /// picture of an optimisation rather than one.
    /// </summary>
    [Fact]
    public void TheSolidPaneDeclaresNoScreenTextureAtAll()
    {
        Assert.DoesNotContain(ScreenHint, GlassPanelMaterial.SolidCode);
        Assert.DoesNotContain("SCREEN_UV", GlassPanelMaterial.SolidCode);
        Assert.DoesNotContain("SCREEN_TEXTURE", GlassPanelMaterial.SolidCode);
    }

    /// <summary>
    /// And it is the same surface otherwise — the shape and its hairline come from
    /// the one field both shaders concatenate, so a solid panel is a panel rather
    /// than a rectangle.
    /// </summary>
    [Fact]
    public void BothPanesAreTheSameShape()
    {
        Assert.Contains("panel_coverage", GlassPanelMaterial.FrostedCode);
        Assert.Contains("panel_coverage", GlassPanelMaterial.SolidCode);
        Assert.Contains("border_color", GlassPanelMaterial.SolidCode);

        // The item's own modulate, which a canvas_item fragment that assigns COLOR
        // would otherwise discard — the bug that made a fading card's pane stand at
        // full strength and then vanish whole.
        Assert.Contains("COLOR.a", GlassPanelMaterial.SolidCode);
    }
}
