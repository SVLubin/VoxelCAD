using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// What crosses to the device. A layout that disagrees with the shader's own
/// struct is not a wrong picture, it is nonsense — and nothing about it is
/// visible from a screenshot, so it is held here.
/// </summary>
public sealed class PackingTests
{
    /// <summary>
    /// The shader's <c>Element</c> is a mat4 then two vec4s then two ivec4s, every
    /// member on a sixteen-byte boundary. Thirty-two floats, in that order.
    /// </summary>
    [Fact]
    public void AnElementIsWrittenInTheShadersOwnLayout()
    {
        var transform = new Transform3D(Basis.Identity, new Vector3(1f, 2f, 3f));

        var element = new RenderElement(
            WorldToLocal: transform,
            GridMin: new Vector3I(-4, 5, -6),
            GridSize: new Vector3I(7, 8, 9),
            VoxelOffset: 1234,
            BoundsMin: new Vector3(0.5f, 1.5f, 2.5f),
            BoundsMax: new Vector3(3.5f, 4.5f, 5.5f));

        float[] buffer = new float[RenderElement.Floats];

        element.Write(buffer, 0);

        // The basis, column by column, each padded to four.
        Assert.Equal(new[] { 1f, 0f, 0f, 0f }, buffer[0..4]);
        Assert.Equal(new[] { 0f, 1f, 0f, 0f }, buffer[4..8]);
        Assert.Equal(new[] { 0f, 0f, 1f, 0f }, buffer[8..12]);

        // The origin is the fourth column, and its w is one.
        Assert.Equal(new[] { 1f, 2f, 3f, 1f }, buffer[12..16]);

        Assert.Equal(new[] { 0.5f, 1.5f, 2.5f, 0f }, buffer[16..20]);
        Assert.Equal(new[] { 3.5f, 4.5f, 5.5f, 0f }, buffer[20..24]);

        // The integer members travel as their own bits, not as float values.
        Assert.Equal(-4, System.BitConverter.SingleToInt32Bits(buffer[24]));
        Assert.Equal(5, System.BitConverter.SingleToInt32Bits(buffer[25]));
        Assert.Equal(-6, System.BitConverter.SingleToInt32Bits(buffer[26]));
        Assert.Equal(1234, System.BitConverter.SingleToInt32Bits(buffer[27]));
        Assert.Equal(7, System.BitConverter.SingleToInt32Bits(buffer[28]));
        Assert.Equal(8, System.BitConverter.SingleToInt32Bits(buffer[29]));
        Assert.Equal(9, System.BitConverter.SingleToInt32Bits(buffer[30]));
    }

    [Fact]
    public void AnElementIsWrittenAtItsOwnOffset()
    {
        var element = new RenderElement(
            Transform3D.Identity,
            Vector3I.Zero,
            Vector3I.One,
            VoxelOffset: 0,
            BoundsMin: Vector3.Zero,
            BoundsMax: Vector3.One);

        float[] buffer = new float[RenderElement.Floats * 2];

        element.Write(buffer, RenderElement.Floats);

        Assert.Equal(0f, buffer[0]);
        Assert.Equal(1f, buffer[RenderElement.Floats]);
    }

    /// <summary>
    /// A material's colour is written in sRGB by the picker and by the document,
    /// and light is linear. Getting this wrong is a render in the wrong colours
    /// that still looks like a render, which is the worst kind of wrong.
    /// </summary>
    [Fact]
    public void AMaterialsColourIsCarriedIntoLinearLight()
    {
        var mid = new Color(0.5f, 0.5f, 0.5f);

        RenderMaterial material = RenderMaterial.From("m", mid, MaterialAppearance.Default);

        Assert.Equal(mid.SrgbToLinear().R, material.Albedo.R, 5);
        Assert.True(material.Albedo.R < 0.25f, "sRGB 0.5 is well under linear 0.5.");
    }

    /// <summary>
    /// Opacity is not a colour and must not be transferred like one, or every
    /// pane of glass would change how much it lets through.
    /// </summary>
    [Fact]
    public void OpacityIsCarriedStraightAcross()
    {
        RenderMaterial material = RenderMaterial.From("m",
            new Color(0.5f, 0.5f, 0.5f, 0.25f),
            MaterialAppearance.Default with { Transparent = true });

        Assert.Equal(0.25f, material.Opacity, 5);
    }

    /// <summary>
    /// And an alpha the material has not declared means nothing: the tracer would
    /// otherwise refract a surface the viewport draws solid.
    /// </summary>
    [Fact]
    public void AnUndeclaredAlphaReachesTheTracerAsOpaque()
    {
        RenderMaterial material = RenderMaterial.From("m",
            new Color(0.5f, 0.5f, 0.5f, 0.25f),
            MaterialAppearance.Default);

        Assert.Equal(1f, material.Opacity, 5);
    }

    [Fact]
    public void TheAppearanceCrossesUnchanged()
    {
        var appearance = new MaterialAppearance(
            Roughness: 0.3f,
            Metallic: 0.85f,
            Emission: 0.75f,
            Finish: MaterialFinish.Brushed,
            FinishScale: 2f,
            FinishRelief: 1f);

        RenderMaterial material = RenderMaterial.From("m", Colors.White, appearance);

        Assert.Equal(0.3f, material.Roughness);
        Assert.Equal(0.85f, material.Metallic);
        Assert.Equal(0.75f, material.Emission);
    }

    [Fact]
    public void AMaterialWithoutAFactureIsWrittenFlat()
    {
        RenderMaterial material = RenderMaterial.From("m", 
            new Color(1f, 1f, 1f, 0.5f),
            MaterialAppearance.Default with
            {
                Roughness = 0.25f, Metallic = 0.5f, Emission = 0.75f, Transparent = true
            });

        float[] buffer = new float[RenderMaterial.Floats];

        material.Write(buffer, 0, FinishHeightField.None, atlasOffset: 0);

        Assert.Equal(1f, buffer[0], 4);
        Assert.Equal(0.5f, buffer[3]);
        Assert.Equal(0.25f, buffer[4]);
        Assert.Equal(0.5f, buffer[5]);
        Assert.Equal(0.75f, buffer[6]);

        // A tile of no side is what the shader reads as "no facture".
        Assert.Equal(0, System.BitConverter.SingleToInt32Bits(buffer[13]));
    }

    /// <summary>
    /// The period the tracer tiles by is the material's dial times the units the
    /// tile holds — the very product the viewport's own materials are given, and
    /// restated here because a render that tiled a facture differently from the
    /// screen it was framed on would not be a picture of what the user framed.
    /// </summary>
    [Fact]
    public void ThePeriodIsTheDialTimesTheUnitsInTheTile()
    {
        var field = new FinishHeightField(
            Heights: new float[4],
            Size: 2,
            AlbedoFloor: 0.7f,
            NormalScale: 1.5f,
            BumpStrength: 3f,
            UnitsPerTile: 4f);

        float[] buffer = new float[RenderMaterial.Floats];

        RenderMaterial.From("m", Colors.White, MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Brick,
            FinishScale = 2.5f
        }).Write(buffer, 0, field, atlasOffset: 0);

        Assert.Equal(10f, buffer[7]);
    }

    /// <summary>
    /// A material that applies none of a facture's relief wears none of it, and
    /// the atlas is not consulted for it at all.
    /// </summary>
    [Fact]
    public void NoReliefIsNoFacture()
    {
        var field = new FinishHeightField(new float[16], 4, 0.7f, 1.5f, 3f, 2f);

        float[] buffer = new float[RenderMaterial.Floats];

        RenderMaterial.From("m", Colors.White, MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Brick,
            FinishRelief = 0f
        }).Write(buffer, 0, field, atlasOffset: 64);

        Assert.Equal(0, System.BitConverter.SingleToInt32Bits(buffer[13]));
        Assert.Equal(0, System.BitConverter.SingleToInt32Bits(buffer[12]));
    }

    [Fact]
    public void AFactureCarriesItsTilesPlaceAndTerms()
    {
        var field = new FinishHeightField(new float[64], 8, 0.65f, 1.4f, 2.5f, 3f);

        float[] buffer = new float[RenderMaterial.Floats];

        RenderMaterial.From("m", Colors.White, MaterialAppearance.Default with
        {
            Finish = MaterialFinish.Brushed,
            FinishRelief = 0.5f
        }).Write(buffer, 0, field, atlasOffset: 128);

        // The depth is the finish's own scale times how much of it the material
        // asked for.
        Assert.Equal(0.7f, buffer[8], 4);
        Assert.Equal(0.65f, buffer[9], 4);
        Assert.Equal(2.5f, buffer[10], 4);
        Assert.Equal(128, System.BitConverter.SingleToInt32Bits(buffer[12]));
        Assert.Equal(8, System.BitConverter.SingleToInt32Bits(buffer[13]));
    }
}
