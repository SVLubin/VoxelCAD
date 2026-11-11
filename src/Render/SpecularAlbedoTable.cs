using System;

namespace VoxelCAD.Render;

/// <summary>
/// How much light the tracer's own specular lobe returns when it is allowed to
/// scatter only once, as a table over how steeply the surface is being looked at
/// and how rough it is. What is left over is what a second scattering would have
/// carried, and the shader hands it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Computed from the estimator rather than approximated.</b> It used to be
/// Karis's analytic fit of the split-sum environment term, which is a fit of a
/// <em>different</em> integral and does not describe what this tracer draws: a
/// furnace measured it +21% at roughness 0.35 and −32% at 1, so the compensation
/// was adding energy where none was lost and losing it where it was. This is the
/// same integral, done exactly.
/// </para>
/// <para>
/// Deterministic on purpose — a Hammersley sequence rather than a random one — so
/// the table is the same every run and a test can hold it to a number.
/// </para>
/// </remarks>
public static class SpecularAlbedoTable
{
    /// <summary>
    /// Samples along each axis. Thirty-two is where the bilinear error falls below
    /// the tenth of a per cent the whole compensation is trying to buy back, and
    /// the table is a kilobyte either way.
    /// </summary>
    public const int Side = 32;

    /// <summary>
    /// Directions integrated per cell. The estimator is smooth in both arguments,
    /// so this is about the tail of the lobe rather than about noise.
    /// </summary>
    private const int Samples = 2048;

    private static float[]? _values;

    /// <summary>
    /// The table, row-major with roughness slowest and <b>two floats a cell</b>:
    /// index <c>(roughness * Side + view) * 2</c> is the scale on the surface's own
    /// Fresnel and the one after it is the bias, so a surface of any colour reads
    /// its own albedo as <c>A · f0 + B</c>. Both axes run end to end rather than
    /// over cell centres, so a mirror and a fully rough surface are read exactly
    /// rather than extrapolated — which at roughness one is the difference between
    /// returning the room and returning 95% of it.
    /// </summary>
    public static float[] Values => _values ??= Build();

    /// <summary>
    /// What a surface with this Fresnel returns from one scattering: the split-sum
    /// pair applied. It is what the <em>diffuse</em> half has to be scaled by —
    /// scaled by the Fresnel at the view angle instead, a white matte surface came
    /// back 2.8% dark, because 4% was taken away for a lobe returning 1.2%.
    /// </summary>
    public static float Reflectance(float normalDotView, float roughness, float f0)
    {
        (float scale, float bias) = SampleTerms(normalDotView, roughness);

        return scale * f0 + bias;
    }

    /// <summary>
    /// The table read the way the shader reads it — bilinear, clamped at the edges
    /// — so a test asks the same question the tracer does.
    /// </summary>
    public static float Sample(float normalDotView, float roughness)
    {
        (float scale, float bias) = SampleTerms(normalDotView, roughness);

        return scale + bias;
    }

    /// <summary>The scale and bias themselves, bilinear, clamped at the edges.</summary>
    public static (float Scale, float Bias) SampleTerms(float normalDotView, float roughness)
    {
        float[] values = Values;

        float x = normalDotView * (Side - 1);
        float y = roughness * (Side - 1);

        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);

        float fx = x - x0;
        float fy = y - y0;

        (float scale, float bias) lowLeft = At(values, x0, y0);
        (float scale, float bias) lowRight = At(values, x0 + 1, y0);
        (float scale, float bias) highLeft = At(values, x0, y0 + 1);
        (float scale, float bias) highRight = At(values, x0 + 1, y0 + 1);

        return (
            Lerp(Lerp(lowLeft.scale, lowRight.scale, fx), Lerp(highLeft.scale, highRight.scale, fx), fy),
            Lerp(Lerp(lowLeft.bias, lowRight.bias, fx), Lerp(highLeft.bias, highRight.bias, fx), fy));
    }

    private static (float Scale, float Bias) At(float[] values, int view, int roughness)
    {
        int x = Math.Clamp(view, 0, Side - 1);
        int y = Math.Clamp(roughness, 0, Side - 1);
        int at = (y * Side + x) * 2;

        return (values[at], values[at + 1]);
    }

    private static float Lerp(float from, float to, float amount) =>
        from + (to - from) * amount;

    private static float[] Build()
    {
        var values = new float[Side * Side * 2];

        for (int y = 0; y < Side; y++)
        {
            float roughness = y / (float)(Side - 1);

            for (int x = 0; x < Side; x++)
            {
                // Never quite edge on: every term of the estimator has the view's
                // cosine under it, and the limit is what the row is for rather
                // than the singularity.
                float normalDotView = MathF.Max(x / (float)(Side - 1), 1e-3f);

                (float scale, float bias) = Integrate(normalDotView, roughness);

                values[(y * Side + x) * 2] = scale;
                values[(y * Side + x) * 2 + 1] = bias;
            }
        }

        return values;
    }

    /// <summary>
    /// The tracer's own specular estimator, averaged over the lobe and split by the
    /// Fresnel: draw a half vector from the normal distribution, reflect, and take
    /// <c>G · (V·H) / ((N·V)(N·H))</c> — which is what <c>f · cos / pdf</c>
    /// simplifies to when the half vector came from that distribution. The shader
    /// computes this very expression per bounce; here it is integrated.
    /// </summary>
    private static (float Scale, float Bias) Integrate(float normalDotView, float roughness)
    {
        float alpha = roughness * roughness;
        float k = alpha * 0.5f;

        // The view in the surface's own frame, with the normal along z.
        float viewX = MathF.Sqrt(MathF.Max(0f, 1f - normalDotView * normalDotView));
        float viewZ = normalDotView;

        double scaled = 0d;
        double biased = 0d;

        for (int i = 0; i < Samples; i++)
        {
            float u1 = (i + 0.5f) / Samples;
            float u2 = RadicalInverse(i);

            float cosTheta = MathF.Sqrt((1f - u1) / MathF.Max(1f + (alpha * alpha - 1f) * u1, 1e-9f));
            float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
            float phi = 2f * MathF.PI * u2;

            // The view lies in the xz plane, so the half vector's own y falls out
            // of every term below and is not formed.
            float halfX = sinTheta * MathF.Cos(phi);
            float halfZ = cosTheta;

            float viewDotHalf = viewX * halfX + viewZ * halfZ;

            if (viewDotHalf <= 0f)
                continue;

            // The reflected direction, of which only the z component is needed.
            float normalDotLight = 2f * viewDotHalf * halfZ - viewZ;

            if (normalDotLight <= 0f)
                continue;

            float geometry =
                normalDotView / (normalDotView * (1f - k) + k)
                * (normalDotLight / (normalDotLight * (1f - k) + k));

            float visible = geometry * viewDotHalf / (normalDotView * halfZ);

            // The Fresnel split: what a surface returns is its own f0 times the
            // first of these plus the second, so one table serves every colour.
            float grazing = MathF.Pow(1f - viewDotHalf, 5f);

            scaled += (1f - grazing) * visible;
            biased += grazing * visible;
        }

        return ((float)(scaled / Samples), (float)(biased / Samples));
    }

    /// <summary>Van der Corput's radical inverse in base two.</summary>
    private static float RadicalInverse(int index)
    {
        uint bits = (uint)index;

        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);

        return bits * 2.3283064365386963e-10f;
    }
}
