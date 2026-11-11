using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// How a shipped material tiles in the viewport, and that it is the same number
/// the other two ways of seeing it use.
/// </summary>
/// <remarks>
/// The viewport tiled every scan at a hard-coded eight while the tracer and the
/// preview tiled it by the material's own dial. They agreed only because every
/// shipped material ships at eight and none can be edited — so nothing on screen
/// was wrong, and nothing said the number had two sources. A project material
/// wearing a pack name at a scale of its own is the case that separates them.
/// </remarks>
public sealed class AtlasTilingTests
{
    private static MaterialAppearance Scan(float scale, float relief = 1f) =>
        MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Standard,
            FinishScale = scale,
            FinishRelief = relief
        };

    [Fact]
    public void ThePeriodIsTheMaterialsOwnDial()
    {
        Assert.Equal(4f, AtlasPlacement.For(2, Scan(4f)).Period);
        Assert.Equal(2, AtlasPlacement.For(2, Scan(4f)).Layer);
    }

    /// <summary>
    /// The agreement the whole change is about, asserted against the preview's own
    /// answer rather than against a number written here twice: whatever
    /// <see cref="VoxelMaterialLook"/> divides by, the atlas divides by.
    /// </summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    [InlineData(8f)]
    [InlineData(16f)]
    public void TheAtlasAndThePreviewTileAScanAlike(float scale)
    {
        MaterialAppearance appearance = Scan(scale);

        VoxelMaterialLook look = VoxelMaterialLook.From(
            Colors.White, appearance, ViewportEffects.Full, scanned: true);

        Assert.Equal(look.FinishPeriod, AtlasPlacement.For(0, appearance).Period);
    }

    /// <summary>
    /// And with the tracer, whose period is the dial times the units in a tile —
    /// one, for a scan, because the pack's own texels are the pattern. Restated
    /// from <c>StandardMaterialPack</c>'s value rather than from the number eight.
    /// </summary>
    [Fact]
    public void TheAtlasAndTheTracerTileAScanAlike()
    {
        MaterialAppearance appearance = Scan(3f);

        Assert.Equal(
            appearance.FinishScale * StandardMaterialPack.ScanUnitsPerTile,
            AtlasPlacement.For(0, appearance).Period);
    }

    /// <summary>
    /// A relief of nought means "no facture at all" to a drawn pattern, which is
    /// nothing but its relief — and must not mean it to a scan, whose albedo is
    /// the material. A period of nought there would be a division by nought
    /// across the surface, and before that it would have thrown the scan away.
    /// </summary>
    [Fact]
    public void AScanWithNoReliefStillTiles()
    {
        AtlasPlacement placement = AtlasPlacement.For(0, Scan(4f, relief: 0f));

        Assert.Equal(4f, placement.Period);
        Assert.Equal(0f, FinishPeriod.GetCells(Scan(4f, relief: 0f), scanned: true));
    }

    /// <summary>The shader divides by this, so nothing may reach it as nought.</summary>
    [Fact]
    public void NothingReachesTheShaderAsAPeriodOfNought()
    {
        Assert.Equal(AtlasPlacement.MinimumPeriod, new AtlasPlacement(0, 0f).SafePeriod);
        Assert.Equal(AtlasPlacement.MinimumPeriod, AtlasPlacement.None.SafePeriod);
        Assert.Equal(5f, new AtlasPlacement(0, 5f).SafePeriod);
    }

    /// <summary>
    /// And nothing reaches it as a NaN either, which the floor alone does not
    /// stop: every comparison with a NaN is false, so a max hands one on. The
    /// model refuses one where the value arrives; this is the last guard before
    /// the vertex, and what it protects is a whole surface.
    /// </summary>
    [Fact]
    public void NothingReachesTheShaderAsANaN()
    {
        Assert.Equal(AtlasPlacement.MinimumPeriod, new AtlasPlacement(0, float.NaN).SafePeriod);
        Assert.Equal(
            AtlasPlacement.MinimumPeriod,
            new AtlasPlacement(0, float.PositiveInfinity).SafePeriod);
    }

    [Fact]
    public void NoLayerIsNoPlaceInTheAtlas()
    {
        Assert.False(AtlasPlacement.None.HasLayer);
        Assert.True(AtlasPlacement.For(0, Scan(8f)).HasLayer);
    }
}
