using System;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// What the tracer's specular lobe returns when it scatters once. The compensation
/// that hands the rest back is only as right as this is, and the fit it replaced
/// was a fit of a different integral — so what is held here is that the table
/// describes <em>this</em> estimator, and the picture that says it is a furnace.
/// </summary>
public sealed class SpecularAlbedoTableTests
{
    /// <summary>
    /// A mirror loses nothing: at roughness nought the lobe is a direction and the
    /// whole of the light comes back, however steeply it is looked at.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.7f)]
    [InlineData(0.2f)]
    public void AMirrorReturnsEverything(float normalDotView)
    {
        Assert.Equal(1f, SpecularAlbedoTable.Sample(normalDotView, 0f), 2);
    }

    /// <summary>
    /// And a fully rough one loses most of it, which is the whole reason the
    /// compensation exists: measured head-on, a single scattering off a mirror-white
    /// surface at roughness one comes back at about three tenths.
    /// </summary>
    [Fact]
    public void AFullyRoughSurfaceLosesTwoThirdsOfIt()
    {
        float returned = SpecularAlbedoTable.Sample(1f, 1f);

        Assert.InRange(returned, 0.28f, 0.33f);
    }

    /// <summary>
    /// Away from edge on, rougher only ever returns less. The compensation divides
    /// by this, so a bump would be a ring of extra light around one roughness — and
    /// there is none anywhere the eye usually is.
    /// </summary>
    [Fact]
    public void RoughnessTakesLightAwayWhereverTheSurfaceIsFaced()
    {
        for (int view = 3; view <= 10; view++)
        {
            float normalDotView = view / 10f;
            float previous = float.MaxValue;

            for (int step = 0; step <= 200; step++)
            {
                float value = SpecularAlbedoTable.Sample(normalDotView, step / 200f);

                Assert.True(
                    value <= previous + 1e-4f,
                    $"At N·V {normalDotView:0.00} roughness {step / 200f:0.000} returned "
                        + $"{value:0.0000}, more than {previous:0.0000} before it.");

                previous = value;
            }
        }
    }

    /// <summary>
    /// Near edge on it is <em>not</em> monotone, and that is the estimator rather
    /// than the table: at a twentieth from grazing the albedo falls to about two
    /// thirds by roughness 0.2, climbs back to about three quarters by 0.6, and
    /// only then falls away. The lobe tilts far enough to catch more than the
    /// smoother one did.
    /// </summary>
    /// <remarks>
    /// Held rather than described because the first version of the test opposite
    /// asserted monotonicity everywhere, and the numbers refused. What it means for
    /// the picture: at a grazing angle the compensation is larger in the middle of
    /// the roughness range than at either end, which is what the surface actually
    /// asks for.
    /// </remarks>
    [Fact]
    public void NearEdgeOnItDipsAndComesBack()
    {
        float mirror = SpecularAlbedoTable.Sample(0.05f, 0f);
        float dip = SpecularAlbedoTable.Sample(0.05f, 0.2f);
        float back = SpecularAlbedoTable.Sample(0.05f, 0.6f);
        float rough = SpecularAlbedoTable.Sample(0.05f, 1f);

        Assert.Equal(1f, mirror, 2);
        Assert.InRange(dip, 0.62f, 0.70f);
        Assert.InRange(back, 0.72f, 0.78f);
        Assert.InRange(rough, 0.55f, 0.62f);

        Assert.True(back > dip, "The albedo does come back up between them.");
    }

    /// <summary>
    /// Nothing in it is outside nought and one — a surface cannot return more light
    /// than it was given, and the compensation would multiply by a negative number
    /// if it could.
    /// </summary>
    [Fact]
    public void EveryValueIsAFractionOfWhatCameIn()
    {
        Assert.Equal(
            SpecularAlbedoTable.Side * SpecularAlbedoTable.Side * 2,
            SpecularAlbedoTable.Values.Length);

        foreach (float value in SpecularAlbedoTable.Values)
            Assert.InRange(value, 0f, 1.001f);
    }

    /// <summary>
    /// A dielectric hardly reflects, and that is the number the diffuse half has to
    /// give up. Scaled by the Fresnel at the view angle instead — four per cent —
    /// a white matte surface came back 2.8% dark in a furnace, because the lobe it
    /// was giving four per cent to returns one and a bit.
    /// </summary>
    [Fact]
    public void ADielectricGivesUpWhatItsLobeActuallyReturns()
    {
        float reflected = SpecularAlbedoTable.Reflectance(1f, 1f, 0.04f);

        Assert.InRange(reflected, 0.005f, 0.02f);

        // And a mirror-white one gives up nearly all of it, which is the other end
        // of the same sum.
        Assert.InRange(SpecularAlbedoTable.Reflectance(1f, 0f, 1f), 0.99f, 1.001f);
    }

    /// <summary>
    /// A mirror-white metal returns the room, at every roughness: what one
    /// scattering gives back, times what a second would have added, is one. That is
    /// the furnace stated as arithmetic, and it is what the compensation exists to
    /// make true — the picture is what says the shader agrees with it.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.7f)]
    [InlineData(0.3f)]
    public void AWhiteMetalReturnsTheRoomAtEveryRoughness(float normalDotView)
    {
        for (int step = 0; step <= 20; step++)
        {
            float roughness = step / 20f;

            float reflected = SpecularAlbedoTable.Reflectance(normalDotView, roughness, 1f);
            float white = MathF.Max(SpecularAlbedoTable.Sample(normalDotView, roughness), 0.01f);
            float returned = reflected * (1f + 1f * (1f / white - 1f));

            Assert.True(
                MathF.Abs(returned - 1f) < 0.005f,
                $"At N·V {normalDotView:0.00} roughness {roughness:0.00} a white metal "
                    + $"returned {returned:0.0000}.");
        }
    }

    /// <summary>
    /// And a dielectric gives up a fraction of a per cent to a few, never the four
    /// its Fresnel would suggest at high roughness — which is the whole of the
    /// 2.8% a white matte surface used to be dark by.
    /// </summary>
    [Fact]
    public void ADielectricNeverGivesUpItsFresnelAtHighRoughness()
    {
        float given = SpecularAlbedoTable.Reflectance(1f, 1f, 0.04f);
        float compensated = given * (1f + 0.04f * (1f / SpecularAlbedoTable.Sample(1f, 1f) - 1f));

        Assert.InRange(compensated, 0.005f, 0.025f);
    }

    /// <summary>
    /// The fit this replaced, kept as the reason: it is a fit of the split-sum
    /// environment term, and against the estimator the tracer actually uses it is
    /// out by a fifth in the middle of the range and by a third at the top. Those
    /// two numbers are what a furnace measured on screen, so they are held here
    /// rather than described.
    /// </summary>
    [Theory]
    [InlineData(0.35f, 0.80f, 0.86f)]
    [InlineData(1.00f, 1.40f, 1.55f)]
    public void TheFitItReplacedDisagreesByThisMuch(float roughness, float least, float most)
    {
        float table = SpecularAlbedoTable.Sample(1f, roughness);
        float fitted = KarisFit(1f, roughness);

        Assert.InRange(fitted / table, least, most);
    }

    /// <summary>
    /// Karis's <c>EnvBRDFApprox</c> summed for a white Fresnel, as the shader used
    /// to compute it. Here only to be disagreed with.
    /// </summary>
    private static float KarisFit(float normalDotView, float roughness)
    {
        float x0 = roughness * -1f + 1f;
        float x1 = roughness * -0.0275f + 0.0425f;
        float x2 = roughness * -0.572f + 1.04f;
        float x3 = roughness * 0.022f + -0.04f;

        float scaled =
            MathF.Min(x0 * x0, MathF.Pow(2f, -9.28f * normalDotView)) * x0 + x1;

        return (-1.04f * scaled + x2) + (1.04f * scaled + x3);
    }
}
