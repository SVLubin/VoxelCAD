using System.Globalization;

namespace VoxelCAD.Render;

/// <summary>
/// The glare a lens puts around a bright thing, as two small passes over a
/// reduced copy of the picture.
/// </summary>
/// <remarks>
/// A render without it reads as arithmetic: an emissive material comes out as a
/// bright patch with a hard edge, where a camera — or an eye — would show it
/// spreading into what is beside it. It is the one post-process this renderer
/// wants, and it is deliberately not a denoiser: noise is answered with samples.
/// <para>
/// <b>No threshold.</b> The whole picture is convolved and a small fraction of
/// the result is mixed back, which is what a lens actually does — scatter a
/// little of everything — rather than the bright-pass-and-add that haloes edges
/// and invents light that was never there. Mixed rather than added, so it moves
/// light about instead of making more of it.
/// </para>
/// <para>
/// It works on the <em>exposed linear</em> picture, before the film curve: glare
/// is something that happens to light on its way through a lens, and doing it
/// after the curve would spread the tone map's own compression instead.
/// </para>
/// </remarks>
internal static class BloomShader
{
    /// <summary>
    /// How much smaller the copy the glare is computed on is. A blur of nine taps
    /// at an eighth of the size reaches about seventy pixels of the full picture,
    /// which is a glare rather than a smear, and costs a sixty-fourth of the work.
    /// </summary>
    public const int Reduction = 8;

    /// <summary>
    /// Averages the exposed picture down, one output pixel per block. The average
    /// is what makes it energy-preserving: a bright pixel spreads its light rather
    /// than being copied into every neighbour.
    /// </summary>
    public static string DownSource => $$"""
        #version 450

        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

        layout(set = 0, binding = 0, std430) restrict readonly buffer Accumulation
        {
            vec4 data[];
        }
        accumulation;

        layout(set = 0, binding = 1, std430) restrict writeonly buffer Reduced
        {
            vec4 data[];
        }
        reduced;

        layout(set = 0, binding = 2, std430) restrict readonly buffer TileSamples
        {
            uint data[];
        }
        tileSamples;

        layout(push_constant, std430) uniform Push
        {
            ivec2 size;       // the full picture
            ivec2 small;      // the reduced one
            ivec2 tiles;      // the grid, in tiles
            int tileSize;
            float exposure;
        }
        push;

        void main()
        {
            ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);

            if (pixel.x >= push.small.x || pixel.y >= push.small.y)
                return;

            vec3 sum = vec3(0.0);
            float count = 0.0;

            for (int y = 0; y < {{Reduction}}; y++)
            {
                for (int x = 0; x < {{Reduction}}; x++)
                {
                    ivec2 source = pixel * {{Reduction}} + ivec2(x, y);

                    if (source.x >= push.size.x || source.y >= push.size.y)
                        continue;

                    // Each pixel divides by what its own tile has had, exactly as
                    // the resolve does — a tile further along than its neighbour
                    // must not read as brighter here either.
                    ivec2 tile = source / push.tileSize;
                    uint taken = tileSamples.data[tile.y * push.tiles.x + tile.x];

                    if (taken == 0u)
                        continue;

                    sum += accumulation.data[source.y * push.size.x + source.x].rgb / float(taken);
                    count += 1.0;
                }
            }

            reduced.data[pixel.y * push.small.x + pixel.x] =
                vec4(count > 0.0 ? sum * (push.exposure / count) : vec3(0.0), 1.0);
        }
        """;

    /// <summary>
    /// One axis of a separable Gaussian. Run twice with the direction turned, which
    /// is what makes a nine-tap kernel a two-dimensional blur for the cost of
    /// eighteen reads instead of eighty-one.
    /// </summary>
    public static string BlurSource => $$"""
        #version 450

        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

        layout(set = 0, binding = 0, std430) restrict readonly buffer Source
        {
            vec4 data[];
        }
        source;

        layout(set = 0, binding = 1, std430) restrict writeonly buffer Target
        {
            vec4 data[];
        }
        target;

        layout(push_constant, std430) uniform Push
        {
            ivec2 size;
            ivec2 direction;  // (1,0) then (0,1)
        }
        push;

        const float Weights[5] = float[5]({{Weight(0)}}, {{Weight(1)}}, {{Weight(2)}}, {{Weight(3)}}, {{Weight(4)}});

        void main()
        {
            ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);

            if (pixel.x >= push.size.x || pixel.y >= push.size.y)
                return;

            vec3 sum = source.data[pixel.y * push.size.x + pixel.x].rgb * Weights[0];
            float total = Weights[0];

            for (int i = 1; i < 5; i++)
            {
                // Clamped at the edge rather than wrapped or darkened: a bright
                // thing at the border of the picture glares inwards, and treating
                // what is past the border as black would make it dimmer for it.
                ivec2 low = clamp(pixel - push.direction * i, ivec2(0), push.size - 1);
                ivec2 high = clamp(pixel + push.direction * i, ivec2(0), push.size - 1);

                sum += source.data[low.y * push.size.x + low.x].rgb * Weights[i];
                sum += source.data[high.y * push.size.x + high.x].rgb * Weights[i];
                total += 2.0 * Weights[i];
            }

            target.data[pixel.y * push.size.x + pixel.x] = vec4(sum / total, 1.0);
        }
        """;

    /// <summary>
    /// A Gaussian of standard deviation two, sampled at nought to four. Normalised
    /// in the shader rather than here, so clamping at an edge cannot dim it.
    /// </summary>
    private static string Weight(int offset) =>
        System.MathF.Exp(-(offset * offset) / 8f).ToString("0.0######", CultureInfo.InvariantCulture);
}
