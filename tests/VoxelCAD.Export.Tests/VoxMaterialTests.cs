using System.Linq;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// MagicaVoxel's material chunks read as this document's appearances.
/// </summary>
/// <remarks>
/// Almost every test here is about a key being <b>ignored</b>, which is the shape
/// of the format's chief trap: MagicaVoxel writes a material for all 256 palette
/// entries whether or not anybody touched one, and writes keys that its own type
/// does not admit. A reader that trusts a key it finds turns an ordinary file half
/// transparent.
/// </remarks>
public sealed class VoxMaterialTests
{
    [Fact]
    public void AnUntouchedDiffuseMaterialArrivesAsTheDefaultAppearance()
    {
        // Exactly what an untouched entry of a 0.99.6 file says.
        ImportedMaterial material = Read(
            ("_type", "_diffuse"), ("_rough", "0.1"), ("_ior", "0.3"), ("_d", "0.05"));

        Assert.Equal(CommonTypes.MaterialAppearance.Default, material.Appearance);
    }

    [Fact]
    public void AMaterialWithNoTypeIsDiffuse()
    {
        // MagicaVoxel 0.99.7 writes no _type at all for an ordinary material.
        ImportedMaterial material = Read(("_rough", "0.1"), ("_ior", "0.3"), ("_ri", "1.3"));

        Assert.Equal(CommonTypes.MaterialAppearance.Default, material.Appearance);
    }

    /// <summary>
    /// The one that would spoil every import. MagicaVoxel offers no roughness for
    /// a diffuse material, so the 0.1 on every untouched entry is a default nobody
    /// chose — while ours is a dial on which 0.1 is very nearly a mirror.
    /// </summary>
    [Fact]
    public void ADiffuseMaterialsRoughnessIsNotRead()
    {
        ImportedMaterial material = Read(("_type", "_diffuse"), ("_rough", "0.1"));

        Assert.Equal(1f, material.Appearance.Roughness);
    }

    /// <summary>
    /// The trap measured on a real file: <c>teardown.vox</c> carries
    /// <c>_alpha 0.5</c> on all of its plain diffuse materials.
    /// </summary>
    [Fact]
    public void ATransparencyOnADiffuseMaterialIsStaleAndIgnored()
    {
        ImportedMaterial material = Read(
            ("_type", "_diffuse"), ("_alpha", "0.5"), ("_emit", "0.5"), ("_metal", "1"));

        Assert.False(material.Appearance.Transparent);
        Assert.Equal(1f, material.Color.A);
        Assert.Equal(0f, material.Appearance.Emission);
        Assert.Equal(0f, material.Appearance.Metallic);
    }

    /// <summary>
    /// Which keys a type admits is MagicaVoxel's own panel: the Surface group is
    /// offered for Metal, Glass and Blend and for nothing else — so an emitter and
    /// a cloud have no roughness either, not only a diffuse material.
    /// </summary>
    [Theory]
    [InlineData("_diffuse")]
    [InlineData("_emit")]
    [InlineData("_media")]
    public void OnlyATypeWithASurfacePanelCarriesRoughness(string type)
    {
        ImportedMaterial material = Read(("_type", type), ("_rough", "0.1"));

        Assert.Equal(1f, material.Appearance.Roughness);
    }

    [Fact]
    public void AnOldGlassCarriesItsTransparencyInTheLegacyKey()
    {
        ImportedMaterial material = Read(("_type", "_glass"), ("_weight", "0.75"));

        Assert.True(material.Appearance.Transparent);
        Assert.Equal(0.25f, material.Color.A, precision: 4);
    }

    [Fact]
    public void AMetalCarriesItsMetallicAndItsRoughness()
    {
        ImportedMaterial material = Read(("_type", "_metal"), ("_metal", "0.5"), ("_rough", "0.4"));

        Assert.Equal(0.5f, material.Appearance.Metallic);

        // Halved on the way in: MagicaVoxel's slider at its top does not look like
        // our chalk. A borrowed calibration, and said so in docs/import.md.
        Assert.Equal(0.2f, material.Appearance.Roughness, precision: 4);
    }

    /// <summary>
    /// The modern general type, whose dials say everything — so a key it does not
    /// carry means the effect is off. This is the exact key set MagicaVoxel's own
    /// material showcase writes for its three plainly dielectric samples, and
    /// reading the absent <c>_metal</c> as full strength made every one of them a
    /// mirror.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("0.5")]
    [InlineData("0.1")]
    public void ABlendWithNoMetalKeyIsNotMetal(string roughness)
    {
        ImportedMaterial material = Read(
            ("_type", "_blend"), ("_rough", roughness), ("_d", "0.05"));

        Assert.Equal(0f, material.Appearance.Metallic);
        Assert.False(material.Appearance.Transparent);
    }

    /// <summary>
    /// And the other way round for the older spelling, where the type was the only
    /// thing there was to say: <c>_type _metal</c> and no strength means metal.
    /// </summary>
    [Fact]
    public void AMetalWithNoStrengthAtAllIsFullyMetal()
    {
        ImportedMaterial material = Read(("_type", "_metal"), ("_rough", "0.2"));

        Assert.Equal(1f, material.Appearance.Metallic);
    }

    /// <summary>
    /// The legacy strength is a fallback for the key its type names, never a value
    /// of its own — so one left on a <c>_blend</c> by an older save does not leak
    /// into a dial that type would have written itself.
    /// </summary>
    [Fact]
    public void TheLegacyStrengthDoesNotReachTheModernType()
    {
        ImportedMaterial material = Read(("_type", "_blend"), ("_weight", "1"), ("_rough", "0.4"));

        Assert.Equal(0f, material.Appearance.Metallic);
        Assert.False(material.Appearance.Transparent);
    }

    /// <summary>
    /// MagicaVoxel up to 0.99.5 wrote the strength of whichever effect the type
    /// named as <c>_weight</c>; later versions gave each its own key. Both have to
    /// arrive alike, which is measurable on one file saved by both: room.vox says
    /// <c>_type _emit, _weight 0.23</c> in one version and <c>_emit 0.23</c> in
    /// the other.
    /// </summary>
    [Fact]
    public void TheOlderWeightKeyMeansWhatTheNewerNamedKeyMeans()
    {
        ImportedMaterial older = Read(("_type", "_emit"), ("_weight", "0.23"));
        ImportedMaterial newer = Read(("_type", "_emit"), ("_emit", "0.23"));

        Assert.Equal(0.23f, older.Appearance.Emission, precision: 4);
        Assert.Equal(older.Appearance, newer.Appearance);
    }

    /// <summary>
    /// MagicaVoxel's own panel labels this dial Transparency, so it is the
    /// complement of the opacity this document stores. It happens to be its own
    /// complement at a half, which is exactly the value everybody tests with — so
    /// the test uses something else.
    /// </summary>
    [Theory]
    [InlineData("_alpha")]
    [InlineData("_trans")]
    public void TransparencyIsTheComplementOfOpacity(string key)
    {
        ImportedMaterial material = Read(("_type", "_glass"), (key, "0.8"));

        Assert.True(material.Appearance.Transparent);
        Assert.Equal(0.2f, material.Color.A, precision: 4);
    }

    /// <summary>
    /// Emission belongs to <c>_emit</c> alone. MagicaVoxel's own panel offers it
    /// for no other type, so a <c>_blend</c> carrying the key — which an older
    /// save can leave behind — is not an emitter.
    /// </summary>
    [Fact]
    public void OnlyAnEmitterEmits()
    {
        Assert.Equal(0f, Read(("_type", "_blend"), ("_emit", "1")).Appearance.Emission);
        Assert.Equal(0f, Read(("_type", "_metal"), ("_emit", "1")).Appearance.Emission);
        Assert.Equal(0f, Read(("_type", "_glass"), ("_emit", "1")).Appearance.Emission);
        Assert.Equal(1f, Read(("_type", "_emit"), ("_emit", "1")).Appearance.Emission);
    }

    [Fact]
    public void GlassWithNoTransparencyStaysOpaque()
    {
        // Measured on test_material.vox: its plainly solid blend materials carry
        // no transparency key at all.
        ImportedMaterial material = Read(("_type", "_blend"), ("_rough", "0.2"));

        Assert.False(material.Appearance.Transparent);
        Assert.Equal(1f, material.Color.A);
    }

    [Fact]
    public void FullyTransparentKeepsTheLastOfItsOpacity()
    {
        // Faithful would be invisible, and an invisible import reads as a broken
        // one rather than as glass.
        ImportedMaterial material = Read(("_type", "_glass"), ("_trans", "1"));

        Assert.True(material.Appearance.Transparent);
        Assert.True(material.Color.A > 0f);
        Assert.True(material.Color.A < 0.05f);
    }

    /// <summary>
    /// Where a file describes no materials at all, the colour's own alpha is the
    /// only thing that could carry an opacity — and this editor's own writer puts
    /// one there, so ignoring it would break a round trip through our own format.
    /// </summary>
    [Fact]
    public void WithNoMaterialChunksTheColoursAlphaIsTheOpacity()
    {
        ImportedFile file = new VoxImportFormat().Read(
            VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Palette((1, 255, 255, 255, 128))
                .Build(),
            "part");

        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.True(material.Appearance.Transparent);
        Assert.Equal(128f / 255f, material.Color.A, precision: 4);
    }

    /// <summary>
    /// And the reverse, which matters more: MagicaVoxel always writes an alpha of
    /// 255 and means nothing by it, so where the file does describe its materials
    /// the alpha is not read at all.
    /// </summary>
    [Fact]
    public void WithMaterialChunksTheColoursAlphaIsNotRead()
    {
        ImportedFile file = new VoxImportFormat().Read(
            VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Palette((1, 255, 255, 255, 64))
                .Material(1, ("_type", "_diffuse"), ("_rough", "0.1"))
                .Build(),
            "part");

        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.False(material.Appearance.Transparent);
        Assert.Equal(1f, material.Color.A);
    }

    /// <summary>
    /// The alpha rule is the file's, not the entry's. A file that describes any of
    /// its materials means nothing by the alpha it writes, so an entry it happens
    /// to have left out is an ordinary opaque material rather than a reason to
    /// start reading the alpha again for that one colour.
    /// </summary>
    [Fact]
    public void AnEntryWithNoMaterialOfItsOwnStillIgnoresTheAlpha()
    {
        ImportedFile file = new VoxImportFormat().Read(
            VoxBuilder.File()
                .Size(2, 1, 1)
                .Voxels((0, 0, 0, 1), (1, 0, 0, 2))
                .Palette((1, 255, 0, 0, 64), (2, 0, 255, 0, 64))
                .Material(1, ("_type", "_diffuse"))
                .Build(),
            "part");

        // Index 2 has no MATL of its own, and its alpha is still not an opacity.
        ImportedMaterial described = file.Materials.Single(m => m.Name == "Palette 001");
        ImportedMaterial silent = file.Materials.Single(m => m.Name == "Palette 002");

        Assert.False(described.Appearance.Transparent);
        Assert.False(silent.Appearance.Transparent);
        Assert.Equal(1f, silent.Color.A);
    }

    /// <summary>
    /// A palette chunk shorter than the full 256 entries turns up in the wild. What
    /// it did not cover is <em>absent</em> rather than transparent black, so the
    /// format's own default palette stands in — otherwise an uncovered index would
    /// arrive as a fully transparent material and its cells would be invisible.
    /// </summary>
    [Fact]
    public void AnEntryPastAShortPaletteFallsBackToTheFormatsOwn()
    {
        ImportedFile file = new VoxImportFormat().Read(
            VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 5))
                .Chunk("RGBA", new byte[8])
                .Build(),
            "part");

        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.False(material.Appearance.Transparent);
        Assert.NotEqual(0f, material.Color.A);
    }

    [Fact]
    public void AMaterialChunkDescribesTheColourIndexItNames()
    {
        ImportedFile file = new VoxImportFormat().Read(
            VoxBuilder.File()
                .Size(2, 1, 1)
                .Voxels((0, 0, 0, 3), (1, 0, 0, 4))
                .Palette((3, 255, 0, 0, 255), (4, 0, 255, 0, 255))
                .Material(3, ("_type", "_emit"), ("_emit", "0.5"))
                .Material(4, ("_type", "_diffuse"))
                .Build(),
            "part");

        ImportedMaterial emissive = file.Materials.Single(m => m.Name == "Palette 003");
        ImportedMaterial plain = file.Materials.Single(m => m.Name == "Palette 004");

        Assert.Equal(0.5f, emissive.Appearance.Emission, precision: 4);
        Assert.Equal(0f, plain.Appearance.Emission);
    }

    /// <summary>Cloud and media have no counterpart here, so only the colour survives.</summary>
    /// <summary>
    /// "NaN" and "Infinity" parse as floats, and the model's own clamp cannot
    /// remove a NaN — clamping one answers NaN. So one would settle into the
    /// document and be saved with it.
    /// </summary>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void APropertyThatIsNotAFiniteNumberIsNotRead(string text)
    {
        ImportedMaterial material = Read(
            ("_type", "_metal"), ("_rough", text), ("_metal", text));

        Assert.True(float.IsFinite(material.Appearance.Roughness));
        Assert.True(float.IsFinite(material.Appearance.Metallic));
        Assert.True(float.IsFinite(material.Color.A));
    }

    [Fact]
    public void AMediaMaterialArrivesAsItsColourAlone()
    {
        ImportedMaterial material = Read(
            ("_type", "_media"), ("_d", "12"), ("_g", "0.4"), ("_media", "1"));

        Assert.Equal(CommonTypes.MaterialAppearance.Default, material.Appearance);
    }

    private static ImportedMaterial Read(params (string Key, string Value)[] properties)
    {
        VoxBuilder builder = VoxBuilder.File()
            .Size(1, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 200, 200, 200, 255))
            .Material(1, properties);

        return Assert.Single(new VoxImportFormat().Read(builder.Build(), "part").Materials);
    }
}
