using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Every decision the voxel material is built from. These exist because the
/// material itself cannot be built without an engine, so until they were pulled
/// out here the only way to check any of them was to take a photograph — which is
/// how the emission one came to ship wrong.
/// </summary>
public sealed class VoxelMaterialLookTests
{
    private static readonly Color Opaque = new(0.7f, 0.7f, 0.72f);

    [Fact]
    public void AnOpaqueColourDoesNotBlend()
    {
        Assert.False(VoxelMaterialLook.From(Opaque, MaterialAppearance.Default).Blends);
    }

    /// <summary>
    /// The alpha decides how much shows through, but only once the material has
    /// said it is see-through at all.
    /// </summary>
    [Fact]
    public void AnAlphaOnADeclaredlyTransparentMaterialBlends()
    {
        MaterialAppearance transparent = MaterialAppearance.Default with { Transparent = true };

        Assert.True(VoxelMaterialLook.From(Opaque with { A = 0.99f }, transparent).Blends);
        Assert.True(VoxelMaterialLook.From(Opaque with { A = 0f }, transparent).Blends);
    }

    /// <summary>
    /// And the point of declaring it: an alpha nobody chose no longer puts a
    /// material into the sorted pass behind everybody's back.
    /// </summary>
    [Fact]
    public void AnAlphaOnAMaterialThatHasNotDeclaredItIsIgnored()
    {
        Assert.False(VoxelMaterialLook.From(Opaque with { A = 0.99f }, MaterialAppearance.Default).Blends);
        Assert.False(VoxelMaterialLook.From(Opaque with { A = 0f }, MaterialAppearance.Default).Blends);
    }

    [Fact]
    public void DeclaringItWithAFullAlphaStillDoesNotBlend()
    {
        MaterialAppearance transparent = MaterialAppearance.Default with { Transparent = true };

        Assert.False(VoxelMaterialLook.From(Opaque, transparent).Blends);
    }

    [Fact]
    public void APerfectlyRoughDielectricHasNoHighlightWorthComputing()
    {
        // The default appearance, which is what every material made before any of
        // this existed looks like — so this is also what keeps those unchanged.
        Assert.False(VoxelMaterialLook.From(Opaque, MaterialAppearance.Default).Shines);
    }

    [Fact]
    public void AnythingPolishedOrMetalShines()
    {
        Assert.True(VoxelMaterialLook.From(
            Opaque, MaterialAppearance.Default with { Roughness = 0.99f }).Shines);

        // Metal at full roughness still has a lobe, and it is the whole of what a
        // metal looks like: switching it off would render steel as chalk.
        Assert.True(VoxelMaterialLook.From(
            Opaque, MaterialAppearance.Default with { Metallic = 0.01f }).Shines);
    }

    [Fact]
    public void NoEmissionIsNoEnergy()
    {
        Assert.Equal(0f, VoxelMaterialLook.From(Opaque, MaterialAppearance.Default).EmissionEnergy);
    }

    /// <summary>
    /// The fix this class exists for. Emission is added after the blend, so it does
    /// not fade with the surface carrying it: a lamp at alpha 0.4 came out a flat
    /// opaque slab with no depth at all until the opacity was folded in here.
    /// </summary>
    [Fact]
    public void EmissionFadesWithTheSurfaceCarryingIt()
    {
        MaterialAppearance lamp = MaterialAppearance.Default with
        {
            Emission = 0.75f,
            Transparent = true
        };

        float solid = VoxelMaterialLook.From(Opaque, lamp).EmissionEnergy;
        float thin = VoxelMaterialLook.From(Opaque with { A = 0.4f }, lamp).EmissionEnergy;

        Assert.Equal(0.75f * VoxelMaterialLook.EmissionGain, solid, 4);
        Assert.Equal(solid * 0.4f, thin, 4);
    }

    [Fact]
    public void AnInvisibleLampGivesOffNothing()
    {
        MaterialAppearance lamp = MaterialAppearance.Default with
        {
            Emission = 1f,
            Transparent = true
        };

        Assert.Equal(0f, VoxelMaterialLook.From(Opaque with { A = 0f }, lamp).EmissionEnergy);
    }

    /// <summary>
    /// And an <em>opaque</em> lamp shines at full strength whatever alpha its
    /// colour happens to be carrying. That is the whole difference the declared
    /// flag makes here: the alpha of an opaque material is a number nobody chose,
    /// and reading it dimmed a lamp that was never asked to be see-through.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(0.4f)]
    [InlineData(0f)]
    public void AnOpaqueLampIsNotDimmedByAnAlphaNobodyChose(float alpha)
    {
        MaterialAppearance lamp = MaterialAppearance.Default with { Emission = 0.75f };

        Assert.Equal(
            0.75f * VoxelMaterialLook.EmissionGain,
            VoxelMaterialLook.From(Opaque with { A = alpha }, lamp).EmissionEnergy,
            4);
    }

    [Fact]
    public void ASmoothMaterialWearsNoFinish()
    {
        VoxelMaterialLook look = VoxelMaterialLook.From(Opaque, MaterialAppearance.Default);

        Assert.False(look.WearsFinish);
        Assert.Equal(0f, look.FinishPeriod);

        // Nought rather than a relief applied at strength nought: a facture that is
        // not there is left off, textures and triplanar mapping and all.
        Assert.Equal(0f, look.NormalScale);
    }

    [Fact]
    public void AFinishTilesOverItsOwnScale()
    {
        VoxelMaterialLook look = VoxelMaterialLook.From(
            Opaque,
            MaterialAppearance.Default with { Finish = MaterialFinish.Brick, FinishScale = 4f });

        Assert.True(look.WearsFinish);

        // The dial is cells to a course and a brick tile holds four of them.
        Assert.Equal(16f, look.FinishPeriod);
        Assert.True(look.NormalScale > 0f);
    }

    [Fact]
    public void ReliefScalesTheReliefAndNothingElse()
    {
        MaterialAppearance brick = MaterialAppearance.Default with { Finish = MaterialFinish.Brick };

        VoxelMaterialLook full = VoxelMaterialLook.From(Opaque, brick with { FinishRelief = 1f });
        VoxelMaterialLook half = VoxelMaterialLook.From(Opaque, brick with { FinishRelief = 0.5f });

        Assert.Equal(full.NormalScale * 0.5f, half.NormalScale, 4);
        Assert.Equal(full.FinishPeriod, half.FinishPeriod);
    }

    [Fact]
    public void NoReliefIsNoFactureAtAll()
    {
        // FinishPeriod's own rule, restated here because it is what decides whether
        // the albedo texture goes on as well — the tracer agrees, so a facture
        // turned off is off in both places.
        VoxelMaterialLook look = VoxelMaterialLook.From(
            Opaque,
            MaterialAppearance.Default with { Finish = MaterialFinish.Brick, FinishRelief = 0f });

        Assert.False(look.WearsFinish);
    }

    // ---- what the viewport has been asked not to run ----

    /// <summary>
    /// A facture switched off is a period of nought, which is already the whole of
    /// "no facture" here — the texture, the normal map and the triplanar mapping
    /// are all left off rather than applied at strength nought.
    /// </summary>
    [Fact]
    public void FacturesSwitchedOffLeaveNoFactureAtAll()
    {
        MaterialAppearance brick = MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Brick,
            FinishRelief = 1f
        };

        VoxelMaterialLook drawn = VoxelMaterialLook.From(
            Opaque, brick, ViewportEffects.Full);

        Assert.True(drawn.WearsFinish);
        Assert.True(drawn.NormalScale > 0f);

        VoxelMaterialLook plain = VoxelMaterialLook.From(
            Opaque, brick, ViewportEffects.Full with { Factures = false });

        Assert.False(plain.WearsFinish);
        Assert.Equal(0f, plain.FinishPeriod);
        Assert.Equal(0f, plain.NormalScale);
    }

    /// <summary>
    /// And it takes nothing else with it: what the material <em>is</em> — its
    /// blending, its highlight, its own light — is untouched by an effect the
    /// viewport was asked to skip.
    /// </summary>
    [Fact]
    public void SwitchingAnEffectOffChangesNothingAboutWhatTheMaterialIs()
    {
        MaterialAppearance glass = MaterialAppearance.Default with
        {
            Transparent = true,
            Finish = MaterialFinish.Brick,
            Roughness = 0.2f,
            Emission = 0.5f
        };

        Color color = Opaque with { A = 0.5f };

        VoxelMaterialLook full = VoxelMaterialLook.From(color, glass, ViewportEffects.Full);
        VoxelMaterialLook bare = VoxelMaterialLook.From(
            color, glass, new ViewportEffects(false, false, false));

        Assert.Equal(full.Blends, bare.Blends);
        Assert.Equal(full.Shines, bare.Shines);
        Assert.Equal(full.EmissionEnergy, bare.EmissionEnergy, 5);
    }

    /// <summary>
    /// The occlusion is the vertex colour multiplying the albedo, so ignoring it is
    /// exactly a surface that keeps its colour into its creases.
    /// </summary>
    [Fact]
    public void OcclusionSwitchedOffStopsTheVertexColourBeingRead()
    {
        Assert.True(VoxelMaterialLook.From(
            Opaque, MaterialAppearance.Default, ViewportEffects.Full).Occluded);

        Assert.False(VoxelMaterialLook.From(
            Opaque,
            MaterialAppearance.Default,
            ViewportEffects.Full with { Occlusion = false }).Occluded);
    }

    /// <summary>
    /// The shadows are the key light's, not a material's, so nothing here reads
    /// them at all — stated as a test because the alternative is somebody folding
    /// a third switch into this struct and quietly rebuilding every material for a
    /// setting that only ever touches one light.
    /// </summary>
    [Fact]
    public void ShadowsAreNoneOfAMaterialsBusiness()
    {
        MaterialAppearance brick = MaterialAppearance.Default with { Finish = MaterialFinish.Brick };

        Assert.Equal(
            VoxelMaterialLook.From(Opaque, brick, ViewportEffects.Full),
            VoxelMaterialLook.From(Opaque, brick, ViewportEffects.Full with { Shadows = false }));
    }

    /// <summary>
    /// A preview shows the material as the document describes it, whatever this
    /// viewport has been asked to skip — so the short form means everything on.
    /// </summary>
    [Fact]
    public void TheShortFormIsTheMaterialAsTheDocumentDescribesIt()
    {
        MaterialAppearance brick = MaterialAppearance.Default with { Finish = MaterialFinish.Brick };

        Assert.Equal(
            VoxelMaterialLook.From(Opaque, brick, ViewportEffects.Full),
            VoxelMaterialLook.From(Opaque, brick));
    }

    /// <summary>
    /// <b>A scanned finish has a period, and without one the whole shipped
    /// library previewed as a blank white cube.</b> A scan has no recipe — there
    /// is nothing to generate — so the period, which every drawn facture takes
    /// from its recipe, came out nought; and a period of nought is the whole of
    /// "no facture" here, so the maps were loaded from the pack and then not
    /// applied to anything.
    /// </summary>
    /// <remarks>
    /// The viewport never showed it, because a shipped material there is drawn by
    /// the atlas shader rather than by a <c>StandardMaterial3D</c> at all. Only
    /// the preview and 'material-preview' went through this path, which is why it
    /// shipped: the picture that would have shown it is the one nobody had.
    /// </remarks>
    [Fact]
    public void AScannedFinishWearsItsMapsRatherThanLoadingAndDroppingThem()
    {
        var appearance = MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Standard,
            FinishScale = 8f,
            FinishRelief = 1f
        };

        VoxelMaterialLook look = VoxelMaterialLook.From(
            Colors.White, appearance, ViewportEffects.Full, scanned: true);

        Assert.True(look.WearsFinish);
        Assert.Equal(8f, look.FinishPeriod);
    }

    /// <summary>
    /// A scan's tile is one unit — the pack's texels <em>are</em> the pattern —
    /// so the dial is the period rather than a multiple of it. This is what makes
    /// the preview agree with the atlas, which takes this very number per material
    /// (<see cref="AtlasPlacement"/>). It used to say "at the same eight", which
    /// was true of every material that shipped and false of the dial: the viewport
    /// tiled a scan at a constant while these two tiled it by the dial, and the
    /// three agreed only because nothing editable could wear a scan.
    /// </summary>
    [Theory]
    [InlineData(2f)]
    [InlineData(8f)]
    [InlineData(16f)]
    public void AScansPeriodIsItsDialExactly(float scale)
    {
        var appearance = MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Standard,
            FinishScale = scale,
            FinishRelief = 1f
        };

        Assert.Equal(scale, FinishPeriod.GetCells(appearance, scanned: true));
    }

    /// <summary>
    /// And a material whose finish says scan but whose name the pack has never
    /// heard of still wears nothing: there is no recipe behind it to draw, so a
    /// period would point the triplanar at a texture that does not exist.
    /// </summary>
    [Fact]
    public void AStandardFinishWithNoScanBehindItWearsNothing()
    {
        var appearance = MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Standard,
            FinishScale = 8f,
            FinishRelief = 1f
        };

        Assert.Equal(0f, FinishPeriod.GetCells(appearance, scanned: false));
    }

    /// <summary>
    /// The relief dial still switches it off, on the same terms as every drawn
    /// facture: nought is no facture rather than a facture at no strength.
    /// </summary>
    [Fact]
    public void AScanWithNoReliefWearsNothingEither()
    {
        var appearance = MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Standard,
            FinishScale = 8f,
            FinishRelief = 0f
        };

        Assert.Equal(0f, FinishPeriod.GetCells(appearance, scanned: true));
    }
}
