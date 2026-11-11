using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// The dials, and the one claim among them that could quietly become false: that
/// a render is lit from where the editor is lit from.
/// </summary>
public sealed class RenderSettingsTests
{
    /// <summary>
    /// The viewport's key light is a <c>DirectionalLight3D</c> at Euler
    /// (-70, 120, 0), and the settings say the same aim in the two angles a person
    /// reads. Computed from that triple rather than written down, so moving one
    /// and not the other fails here instead of in a picture nobody compares.
    /// </summary>
    [Fact]
    public void TheSunStandsWhereTheViewportsKeyLightDoes()
    {
        Basis keyLight = Basis.FromEuler(
            new Vector3(Mathf.DegToRad(-70f), Mathf.DegToRad(120f), 0f),
            EulerOrder.Yxz);

        // A light shines down its own -Z, so the direction towards it is +Z.
        Vector3 towardsTheLight = keyLight.Column2;

        Assert.True(
            RenderSettings.Default.SunDirection.IsEqualApprox(towardsTheLight),
            $"{RenderSettings.Default.SunDirection} is not {towardsTheLight}");
    }

    [Fact]
    public void TheSunDirectionIsAUnitVector()
    {
        RenderSettings settings = RenderSettings.Default with
        {
            SunElevationDegrees = 23f,
            SunAzimuthDegrees = -145f
        };

        Assert.Equal(1f, settings.SunDirection.Length(), 4);
    }

    [Fact]
    public void ElevationRunsFromTheHorizonUpwards()
    {
        RenderSettings overhead = RenderSettings.Default with { SunElevationDegrees = 89.9f };

        Assert.True(overhead.SunDirection.Y > 0.99f);

        RenderSettings underneath = RenderSettings.Default with { SunElevationDegrees = -89.9f };

        Assert.True(underneath.SunDirection.Y < -0.99f);
    }

    [Fact]
    public void EveryValueIsHeldInsideItsBounds()
    {
        RenderSettings absurd = RenderSettings.Default with
        {
            Width = 1,
            Height = 100000,
            Samples = -4,
            Bounces = 900,
            SunElevationDegrees = 900f,
            SunAngularRadiusDegrees = -3f,
            SunIntensity = -1f,
            SkyIntensity = 1e9f,
            Exposure = 0f
        };

        RenderSettings held = absurd.Clamped();

        Assert.Equal(RenderSettings.MinimumSize, held.Width);
        Assert.Equal(RenderSettings.MaximumSize, held.Height);
        Assert.Equal(1, held.Samples);
        Assert.Equal(RenderSettings.MaximumBounces, held.Bounces);
        Assert.Equal(89f, held.SunElevationDegrees);
        Assert.Equal(0.05f, held.SunAngularRadiusDegrees);
        Assert.Equal(0f, held.SunIntensity);
        Assert.Equal(100f, held.SkyIntensity);
        Assert.Equal(0.01f, held.Exposure);
    }

    /// <summary>
    /// A lens focused on nothing in particular is the default, and it must mean
    /// "on whatever is being looked at" rather than "at zero", which is the camera
    /// itself.
    /// </summary>
    [Fact]
    public void APinholeIsTheDefaultAndSurvivesClamping()
    {
        Assert.Equal(0f, RenderSettings.Default.Aperture);
        Assert.Equal(0f, RenderSettings.Default.FocusDistance);
        Assert.Equal(0f, RenderSettings.Default.Clamped().Aperture);
    }

    [Fact]
    public void ALensIsHeldInsideItsBounds()
    {
        RenderSettings held = (RenderSettings.Default with
        {
            Aperture = 900f,
            FocusDistance = -5f
        }).Clamped();

        Assert.Equal(8f, held.Aperture);
        Assert.Equal(0f, held.FocusDistance);
    }

    /// <summary>
    /// The viewport's own projection, so a render is the shot that was framed
    /// rather than a different one.
    /// </summary>
    [Fact]
    public void PerspectiveAndTheSkyAreTheDefaults()
    {
        Assert.Equal(RenderProjection.Perspective, RenderSettings.Default.Projection);
        Assert.Equal(RenderBackdrop.Environment, RenderSettings.Default.Backdrop);
        Assert.Equal(0f, RenderSettings.Default.OrthographicHeight);
    }

    [Fact]
    public void AnOrthographicWindowIsHeldInsideItsBounds()
    {
        Assert.Equal(0f, (RenderSettings.Default with { OrthographicHeight = -3f }).Clamped()
            .OrthographicHeight);
        Assert.Equal(10000f, (RenderSettings.Default with { OrthographicHeight = 1e9f }).Clamped()
            .OrthographicHeight);
    }

    [Fact]
    public void ClampingWhatIsAlreadyLegalChangesNothing()
    {
        Assert.Equal(RenderSettings.Default, RenderSettings.Default.Clamped());
    }

    /// <summary>
    /// The sun's size decides how soft its shadow is and nothing else. Held here
    /// rather than judged from a picture, because the first render got it wrong in
    /// exactly the direction nobody would notice as an error: it simply looked like
    /// an overcast day.
    /// </summary>
    [Fact]
    public void WideningTheSunDoesNotBrightenIt()
    {
        float delivered = -1f;

        foreach (float radius in new[] { 0.25f, 1f, 2f, 8f, 25f })
        {
            RenderSettings settings = RenderSettings.Default with
            {
                SunAngularRadiusDegrees = radius
            };

            // What a surface facing the sun actually receives: the radiance of the
            // disc, over the cone it covers.
            float received = settings.SunRadiance.X * settings.SunSolidAngle;

            if (delivered >= 0f)
                Assert.Equal(delivered, received, 4);

            delivered = received;
        }
    }

    [Fact]
    public void TheSunDeliversWhatItsIntensitySays()
    {
        RenderSettings settings = RenderSettings.Default with { SunIntensity = 5f };

        Color linear = settings.SunColor.SrgbToLinear();
        float received = settings.SunRadiance.X * settings.SunSolidAngle;

        Assert.Equal(linear.R * 5f, received, 4);
    }

    [Fact]
    public void ASunOfNoSizeStillHasAFiniteRadiance()
    {
        RenderSettings settings = (RenderSettings.Default with
        {
            SunAngularRadiusDegrees = 0f
        }).Clamped();

        Assert.True(float.IsFinite(settings.SunRadiance.X));
        Assert.True(settings.SunSolidAngle > 0f);
    }
}
