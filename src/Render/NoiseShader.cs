namespace VoxelCAD.Render;

/// <summary>
/// How noisy each tile still is, as one number per tile.
/// </summary>
/// <remarks>
/// A render spends its samples evenly, and a picture is never evenly hard: an
/// empty sky settles in four samples while a caustic under glass is still moving
/// at four thousand. What this measures is the <em>standard error of the mean</em>
/// per pixel — how far the average of the samples so far is likely to be from the
/// average they are converging on — relative to how bright the pixel is, because a
/// hundredth of a unit of error is invisible on a bright surface and obvious in a
/// shadow.
/// <para>
/// It needs one extra number per pixel: the sum of the squares of what each sample
/// carried. The sum itself is already in the accumulation, and luminance is linear,
/// so the mean comes out of what is there — only the second moment is new.
/// </para>
/// <para>
/// One thread a tile rather than a reduction per tile. A tile is a few thousand
/// pixels and there are a few hundred tiles, so the walk is the same order of work
/// as one resolve, and it runs a few times a second rather than per sample.
/// </para>
/// </remarks>
internal static class NoiseShader
{
    /// <summary>
    /// Added to the brightness a relative error is measured against. Without it a
    /// pixel converging on black has an unbounded relative error and would be
    /// sampled forever; with it, error is judged against "this pixel plus a little
    /// light", which is what the eye does.
    /// </summary>
    public const float DarkFloor = 0.01f;

    public static string Source => $$"""
        #version 450

        layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

        layout(set = 0, binding = 0, std430) restrict readonly buffer Accumulation
        {
            vec4 data[];
        }
        accumulation;

        layout(set = 0, binding = 1, std430) restrict readonly buffer Moments
        {
            float data[];
        }
        moments;

        layout(set = 0, binding = 2, std430) restrict readonly buffer TileSamples
        {
            uint data[];
        }
        tileSamples;

        layout(set = 0, binding = 3, std430) restrict writeonly buffer Noise
        {
            float data[];
        }
        noise;

        layout(push_constant, std430) uniform Push
        {
            ivec2 size;
            ivec2 tiles;
            int tileSize;
            int count;        // how many tiles there are
            int pad0;
            int pad1;
        }
        push;

        const vec3 Luma = vec3(0.2126, 0.7152, 0.0722);

        void main()
        {
            int tile = int(gl_GlobalInvocationID.x);

            if (tile >= push.count)
                return;

            uint taken = tileSamples.data[tile];

            // Nothing traced yet is not "quiet", it is "unknown" — and the least
            // sampled tile is the one the scheduler is about to serve anyway.
            if (taken < 2u)
            {
                noise.data[tile] = 1.0;

                return;
            }

            ivec2 corner = ivec2(tile % push.tiles.x, tile / push.tiles.x) * push.tileSize;
            ivec2 end = min(corner + push.tileSize, push.size);

            float samples = float(taken);
            float total = 0.0;
            float counted = 0.0;

            for (int y = corner.y; y < end.y; y++)
            {
                for (int x = corner.x; x < end.x; x++)
                {
                    int index = y * push.size.x + x;

                    float mean = dot(accumulation.data[index].rgb, Luma) / samples;
                    float meanOfSquares = moments.data[index] / samples;

                    // The spread of the samples, then the spread of their mean —
                    // which is what falls as one over the square root of how many
                    // there are, and so is what says whether more would help.
                    float spread = max(meanOfSquares - mean * mean, 0.0);
                    float error = sqrt(spread / samples);

                    total += error / (mean + {{DarkFloor.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture)}});
                    counted += 1.0;
                }
            }

            noise.data[tile] = counted > 0.0 ? total / counted : 0.0;
        }
        """;
}
