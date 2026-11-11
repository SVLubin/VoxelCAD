using System.Globalization;

namespace VoxelCAD.Render;

/// <summary>
/// Turns what has accumulated into pixels, on the device. One pass over the
/// buffer: divide by the samples taken, expose, roll the highlights off, encode.
/// </summary>
/// <remarks>
/// It exists because the picture is read back many times during one render, and
/// the difference is not small: without it a refresh carries sixteen bytes per
/// pixel across the bus and then walks a million of them in single-threaded C#,
/// which measured slower than the tracing it was showing — eighty-eight samples
/// in nearly seven seconds, against a hundred times that when the same render ran
/// without a preview.
/// <para>
/// The curve is <see cref="ToneMap"/>'s, and its constants are carried in from
/// there rather than written twice: the shape is one line, the numbers are what
/// would drift.
/// </para>
/// </remarks>
internal static class ResolveShader
{
    public static string Source => $$"""
        #version 450

        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

        layout(set = 0, binding = 0, std430) restrict readonly buffer Accumulation
        {
            vec4 data[];
        }
        accumulation;

        layout(set = 0, binding = 1, std430) restrict writeonly buffer Resolved
        {
            uint data[];
        }
        resolved;

        // How many samples each tile has had. Per tile rather than per pixel
        // because every pixel of a tile is traced together and so has had exactly
        // the same number — a few hundred numbers instead of a million, and no
        // counter for the tracer to increment.
        layout(set = 0, binding = 2, std430) restrict readonly buffer TileSamples
        {
            uint data[];
        }
        tileSamples;

        // The lens's scatter, on a reduced copy of the exposed picture.
        layout(set = 0, binding = 3, std430) restrict readonly buffer Glare
        {
            vec4 data[];
        }
        glare;

        layout(push_constant, std430) uniform Push
        {
            ivec2 size;
            ivec2 tiles;      // the grid, in tiles
            int tileSize;
            float exposure;
            int transparent;  // whether coverage becomes an alpha channel
            float glareAmount;
            ivec2 glareSize;
            int glareReduction;
            int pad;
        }
        push;

        // The reduced copy, read where this pixel falls in it. Bilinear rather
        // than nearest, or a glare computed at an eighth of the size would come
        // back as visible blocks.
        vec3 scattered(ivec2 pixel)
        {
            vec2 at = (vec2(pixel) + 0.5) / float(push.glareReduction) - 0.5;
            vec2 low = floor(at);
            vec2 fraction = at - low;

            ivec2 a = clamp(ivec2(low), ivec2(0), push.glareSize - 1);
            ivec2 b = clamp(ivec2(low) + ivec2(1, 0), ivec2(0), push.glareSize - 1);
            ivec2 c = clamp(ivec2(low) + ivec2(0, 1), ivec2(0), push.glareSize - 1);
            ivec2 d = clamp(ivec2(low) + ivec2(1, 1), ivec2(0), push.glareSize - 1);

            vec3 top = mix(
                glare.data[a.y * push.glareSize.x + a.x].rgb,
                glare.data[b.y * push.glareSize.x + b.x].rgb,
                fraction.x);

            vec3 bottom = mix(
                glare.data[c.y * push.glareSize.x + c.x].rgb,
                glare.data[d.y * push.glareSize.x + d.x].rgb,
                fraction.x);

            return mix(top, bottom, fraction.y);
        }

        // Narkowicz's fit of the ACES curve, with the same numbers ToneMap uses.
        float film(float value)
        {
            float x = max(value, 0.0);
            float mapped = x * ({{C(ToneMap.CurveA)}} * x + {{C(ToneMap.CurveB)}})
                / (x * ({{C(ToneMap.CurveC)}} * x + {{C(ToneMap.CurveD)}}) + {{C(ToneMap.CurveE)}});

            return clamp(mapped, 0.0, 1.0);
        }

        // The sRGB transfer, which is what a file and a panel both expect.
        float encode(float value)
        {
            return value <= 0.0031308
                ? value * 12.92
                : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
        }

        void main()
        {
            ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);

            if (pixel.x >= push.size.x || pixel.y >= push.size.y)
                return;

            int index = pixel.y * push.size.x + pixel.x;

            // Every pixel divides by what its own tile has had, which is what
            // lets one part of the picture be further along than another and
            // still be read at the right brightness.
            ivec2 tile = pixel / push.tileSize;
            uint taken = tileSamples.data[tile.y * push.tiles.x + tile.x];

            if (taken == 0u)
            {
                resolved.data[index] = 0u;

                return;
            }

            float share = 1.0 / float(taken);
            vec4 accumulated = accumulation.data[index];
            vec3 linear = accumulated.rgb * (push.exposure * share);

            // How much of the pixel the model covered. A file carries straight
            // alpha rather than premultiplied, so what was gathered over part of a
            // pixel is divided back out — otherwise every edge composites dark.
            float alpha = 1.0;

            if (push.transparent != 0)
            {
                alpha = clamp(accumulated.w * share, 0.0, 1.0);
                linear /= max(alpha, 1.0e-4);
            }

            // Mixed rather than added: a lens moves light about, it does not make
            // more of it. Applied here, on the exposed linear picture, because
            // glare happens on the way through the lens and not after the film.
            if (push.glareAmount > 0.0)
                linear = mix(linear, scattered(pixel), push.glareAmount);

            vec3 shown = vec3(
                encode(film(linear.r)),
                encode(film(linear.g)),
                encode(film(linear.b)));

            uvec3 bytes = uvec3(round(clamp(shown, 0.0, 1.0) * 255.0));
            uint opacity = uint(round(alpha * 255.0));

            resolved.data[index] = bytes.r | (bytes.g << 8) | (bytes.b << 16) | (opacity << 24);
        }
        """;

    /// <summary>
    /// A number on its way into source code is machine-readable text, and this
    /// machine's own locale writes a decimal comma. See the tracer's own note.
    /// </summary>
    private static string C(float value) =>
        value.ToString("0.0#######", CultureInfo.InvariantCulture);
}
