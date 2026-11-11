using System.Globalization;

namespace VoxelCAD.Render;

/// <summary>
/// The tracer itself, as GLSL. A string rather than a <c>.glsl</c> resource for
/// the reason the panel shaders are strings: it is compiled from source at
/// runtime, so it needs no import step and can carry constants from this side.
/// </summary>
/// <remarks>
/// What it is, and what it deliberately is not.
/// <para>
/// Each element is marched in its <b>own</b> cells, so a rotated or scaled model
/// traces exactly rather than being baked into a shared world grid — the hole the
/// exporter's <c>UnbakeableElements</c> records, which this side does not have.
/// The ray's direction is carried across unnormalised, which keeps the ray
/// parameter in world distance and lets hits from differently scaled elements be
/// compared against each other.
/// </para>
/// <para>
/// Light reaches a surface two ways: the sun is <b>aimed at</b> (next-event
/// estimation over the cone it subtends, which is where its soft shadow and its
/// highlight both come from), and everything else — the sky, and one voxel
/// lighting another — arrives by the path carrying on. There is deliberately
/// <b>no sun disc in the sky</b>, so no path can find the sun twice and no
/// multiple-importance weighting is needed to say so. The price is a roughness
/// floor: a perfect mirror cannot be aimed at a light.
/// </para>
/// <para>
/// Transparency is a medium the path is in, not a surface it passes through. The
/// march is told what it is currently inside and reports the first cell that
/// differs from it, so the far face of a glass block is found as an interface
/// rather than as another front face — which is what a plain "first filled cell"
/// march reports, and why one would make glass a mess of nested entries.
/// </para>
/// </remarks>
internal static class PathTraceShader
{
    /// <summary>
    /// The document's emission is a nought-to-one dial, and a light has to be far
    /// brighter than a lit surface to light anything: a Lamp at 0.75 against a
    /// wall at 0.8 albedo would be a slightly pale wall. This is what the dial is
    /// a fraction of.
    /// </summary>
    public const float EmissionRadianceGain = 24f;

    /// <summary>
    /// No surface is smoother than this. A specular lobe of zero width cannot be
    /// aimed at the sun, and the sun is most of the light here.
    /// </summary>
    public const float MinimumRoughness = 0.03f;

    /// <summary>
    /// The most one indirect bounce may add. The sun is aimed at, so it converges;
    /// an emissive material is not, so a path that stumbles onto one carries thirty
    /// times the radiance of its neighbours and stays a bright speck for thousands
    /// of samples. This is the standard trade: a little energy lost from the
    /// brightest indirect paths, against a picture that converges at all.
    /// </summary>
    /// <remarks>
    /// Deliberately not applied to what the camera sees directly, so a lamp still
    /// reads as a lamp. The proper fix is aiming at emissive materials the way the
    /// sun is aimed at, which needs a list of them and is its own piece of work.
    /// </remarks>
    public const float IndirectClamp = 6f;

    /// <summary>
    /// A number on its way into source code is machine-readable text, and this
    /// machine's own locale writes a decimal comma. Interpolated as it came, the
    /// roughness floor arrived as <c>0,03</c> and the whole shader failed to parse
    /// on line seven — so anything crossing into GLSL is formatted invariantly, and
    /// always with a point in it so an integer-looking constant is still a float.
    /// </summary>
    private static string Glsl(float value) =>
        value.ToString("0.0#######", CultureInfo.InvariantCulture);

    public static string Source => $$"""
        #version 450

        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

        const float Pi = 3.14159265359;
        const float EmissionGain = {{Glsl(EmissionRadianceGain)}};
        const float MinimumRoughness = {{Glsl(MinimumRoughness)}};
        const float IndirectClamp = {{Glsl(IndirectClamp)}};

        // Far enough that nothing in a buildable region is behind it, near enough
        // that a float keeps its precision along the way.
        const float RayMaximum = 1.0e5;

        const float SurfaceOffset = 1.0e-3;

        // The side of the specular albedo table, which is SpecularAlbedoTable's.
        const int AlbedoSide = {{SpecularAlbedoTable.Side}};
        const float Tiny = 1.0e-8;

        struct Element
        {
            mat4 worldToLocal;
            vec4 boundsMin;
            vec4 boundsMax;
            ivec4 gridMinAndOffset;
            ivec4 gridSize;
        };

        struct Material
        {
            vec4 albedoOpacity;        // rgb, a = opacity
            vec4 roughMetalEmission;   // x rough, y metal, z emission, w facture period
            vec4 finishTerms;          // x depth, y albedo floor, z bump strength,
                                       // w how much of the hemisphere is reachable
            ivec4 finish;              // x offset into the atlas, y tile side (0 = none),
                                       // z spare, w floats a texel
        };

        layout(set = 0, binding = 0, std430) restrict buffer Accumulation
        {
            vec4 data[];
        }
        accumulation;

        layout(set = 0, binding = 1, std430) restrict readonly buffer Elements
        {
            Element data[];
        }
        elements;

        layout(set = 0, binding = 2, std430) restrict readonly buffer Voxels
        {
            uint data[];
        }
        voxels;

        layout(set = 0, binding = 3, std430) restrict readonly buffer Materials
        {
            Material data[];
        }
        materials;

        // How far the camera ray reached, one float a pixel. Written by the first
        // sample and by every one after it — the value is the same within a
        // pixel's own jitter — so that focusing the lens on something can be a
        // buffer read rather than a second trace of the whole picture.
        // The sum of the squares of what each sample carried, one float a pixel.
        // The sum itself is in the accumulation and luminance is linear, so this is
        // the only thing a noise estimate needs that is not already there.
        layout(set = 0, binding = 8, std430) restrict buffer Moments
        {
            float data[];
        }
        moments;

        layout(set = 0, binding = 7, std430) restrict buffer Depth
        {
            float data[];
        }
        depth;

        layout(set = 0, binding = 6, std430) restrict readonly buffer Lights
        {
            vec4 data[];   // two per light: xyz centre + w radius, then element + cell as ints
        }
        lights;

        layout(set = 0, binding = 5, std430) restrict readonly buffer Finishes
        {
            float data[];
        }
        finishes;

        layout(set = 0, binding = 9, std430) restrict readonly buffer SpecularAlbedo
        {
            float data[];
        } specularAlbedo;

        layout(set = 0, binding = 4, std430) restrict readonly buffer Params
        {
            vec4 cameraOrigin;    // xyz origin, w tan of half the vertical fov
            vec4 cameraRight;     // xyz, w aspect
            vec4 cameraUp;        // xyz
            vec4 cameraForward;   // xyz
            vec4 sun;             // xyz towards the sun, w cosine of its angular radius
            vec4 sunRadiance;     // xyz, w one when emission is aimed at rather than found
            vec4 skyZenith;       // xyz, w sky intensity
            vec4 skyHorizon;      // xyz, w ground height
            vec4 skyGround;       // xyz, w ground enabled
            vec4 groundColor;     // xyz, w how many lights there are to aim at
            ivec4 counts;         // x width, y height, z elements, w bounces
            vec4 lens;            // x orthographic height (nought is perspective), y backdrop
            vec4 backdropColor;   // xyz
        }
        params;

        layout(push_constant, std430) uniform Push
        {
            ivec4 frame;          // x sample index within this tile
            ivec4 tile;           // xy origin in pixels, zw size
        }
        push;

        // What the eye makes of a colour, which is what a noise estimate is about:
        // a green speck reads as noise where the same error in blue does not.
        float luminance(vec3 colour)
        {
            return dot(colour, vec3(0.2126, 0.7152, 0.0722));
        }

        uint rngState;

        uint hashed(uint value)
        {
            uint state = value * 747796405u + 2891336453u;
            uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;

            return (word >> 22u) ^ word;
        }

        float randomFloat()
        {
            rngState = hashed(rngState);

            return float(rngState) * (1.0 / 4294967296.0);
        }

        // An orthonormal frame about a unit vector, without the branch a
        // cross-with-up needs. Duff and others, 2017.
        void buildFrame(vec3 normal, out vec3 tangent, out vec3 bitangent)
        {
            float orientation = normal.z >= 0.0 ? 1.0 : -1.0;
            float a = -1.0 / (orientation + normal.z);
            float b = normal.x * normal.y * a;

            tangent = vec3(
                1.0 + orientation * normal.x * normal.x * a,
                orientation * b,
                -orientation * normal.x);

            bitangent = vec3(b, orientation + normal.y * normal.y * a, -normal.y);
        }

        vec3 toWorld(vec3 local, vec3 normal)
        {
            vec3 tangent;
            vec3 bitangent;

            buildFrame(normal, tangent, bitangent);

            return local.x * tangent + local.y * bitangent + local.z * normal;
        }

        vec3 sampleCosine(vec3 normal)
        {
            float u1 = randomFloat();
            float u2 = randomFloat();
            float radius = sqrt(u1);
            float phi = 2.0 * Pi * u2;

            return toWorld(
                vec3(radius * cos(phi), radius * sin(phi), sqrt(max(0.0, 1.0 - u1))),
                normal);
        }

        // A direction inside the cone the sun subtends, and the density it was
        // drawn with. A wider sun is a softer shadow and nothing else.
        vec3 sampleSun(out float pdf)
        {
            float cosMaximum = params.sun.w;
            float cosTheta = mix(cosMaximum, 1.0, randomFloat());
            float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
            float phi = 2.0 * Pi * randomFloat();

            pdf = 1.0 / (2.0 * Pi * max(1.0 - cosMaximum, 1.0e-6));

            return toWorld(vec3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta), params.sun.xyz);
        }

        vec3 skyRadiance(vec3 direction)
        {
            float height = direction.y;

            vec3 color = height >= 0.0
                ? mix(params.skyHorizon.xyz, params.skyZenith.xyz, sqrt(height))
                : mix(params.skyHorizon.xyz, params.skyGround.xyz, sqrt(-height));

            return color * params.skyZenith.w;
        }

        // A tile, filtered by hand. There is no sampler on this path — the tracer
        // asks for a value at a point — so the wrap and the bilinear are here.
        // Where the four texels around a point sit, and how far between them it is.
        // Shared by every channel so a tile's height, colour and gloss are read at
        // exactly the same place.
        void locate(int size, vec2 uv, out ivec2 low, out ivec2 high, out vec2 between)
        {
            vec2 texel = fract(uv) * float(size) - 0.5;
            vec2 corner = floor(texel);

            between = texel - corner;
            low = ivec2(mod(corner, float(size)));
            high = ivec2(mod(corner + 1.0, float(size)));
        }

        float sampleHeight(int offset, int size, int stride, vec2 uv)
        {
            ivec2 low;
            ivec2 high;
            vec2 between;
            locate(size, uv, low, high, between);

            float lowLow = finishes.data[offset + (low.y * size + low.x) * stride];
            float lowHigh = finishes.data[offset + (low.y * size + high.x) * stride];
            float highLow = finishes.data[offset + (high.y * size + low.x) * stride];
            float highHigh = finishes.data[offset + (high.y * size + high.x) * stride];

            return mix(
                mix(lowLow, lowHigh, between.x),
                mix(highLow, highHigh, between.x),
                between.y);
        }

        // The tile's own colour, already linear. Only a scanned facture has one:
        // a drawn facture's albedo is a monochrome scale of its height, which the
        // caller forms from the height itself.
        vec3 sampleColor(int offset, int size, int stride, vec2 uv)
        {
            ivec2 low;
            ivec2 high;
            vec2 between;
            locate(size, uv, low, high, between);

            int lowLow = offset + (low.y * size + low.x) * stride + 1;
            int lowHigh = offset + (low.y * size + high.x) * stride + 1;
            int highLow = offset + (high.y * size + low.x) * stride + 1;
            int highHigh = offset + (high.y * size + high.x) * stride + 1;

            vec3 a = vec3(finishes.data[lowLow], finishes.data[lowLow + 1], finishes.data[lowLow + 2]);
            vec3 b = vec3(finishes.data[lowHigh], finishes.data[lowHigh + 1], finishes.data[lowHigh + 2]);
            vec3 c = vec3(finishes.data[highLow], finishes.data[highLow + 1], finishes.data[highLow + 2]);
            vec3 d = vec3(finishes.data[highHigh], finishes.data[highHigh + 1], finishes.data[highHigh + 2]);

            return mix(mix(a, b, between.x), mix(c, d, between.x), between.y);
        }

        // How much of the sky a texel can see. Only a scanned facture has one, and
        // it is deliberately not applied to the sun — see where it is used.
        float sampleOcclusion(int offset, int size, int stride, vec2 uv)
        {
            ivec2 low;
            ivec2 high;
            vec2 between;
            locate(size, uv, low, high, between);

            float lowLow = finishes.data[offset + (low.y * size + low.x) * stride + 5];
            float lowHigh = finishes.data[offset + (low.y * size + high.x) * stride + 5];
            float highLow = finishes.data[offset + (high.y * size + low.x) * stride + 5];
            float highHigh = finishes.data[offset + (high.y * size + high.x) * stride + 5];

            return mix(
                mix(lowLow, lowHigh, between.x),
                mix(highLow, highHigh, between.x),
                between.y);
        }

        // Which part of the surface is metal. Only a shipped material has one, and
        // it has to be read where it exists: its own dial is open so that the map
        // may govern, which without the map is a mirror.
        float sampleMetal(int offset, int size, int stride, vec2 uv)
        {
            ivec2 low;
            ivec2 high;
            vec2 between;
            locate(size, uv, low, high, between);

            float lowLow = finishes.data[offset + (low.y * size + low.x) * stride + 6];
            float lowHigh = finishes.data[offset + (low.y * size + high.x) * stride + 6];
            float highLow = finishes.data[offset + (high.y * size + low.x) * stride + 6];
            float highHigh = finishes.data[offset + (high.y * size + high.x) * stride + 6];

            return mix(
                mix(lowLow, lowHigh, between.x),
                mix(highLow, highHigh, between.x),
                between.y);
        }

        float sampleGloss(int offset, int size, int stride, vec2 uv)
        {
            ivec2 low;
            ivec2 high;
            vec2 between;
            locate(size, uv, low, high, between);

            float lowLow = finishes.data[offset + (low.y * size + low.x) * stride + 4];
            float lowHigh = finishes.data[offset + (low.y * size + high.x) * stride + 4];
            float highLow = finishes.data[offset + (high.y * size + low.x) * stride + 4];
            float highHigh = finishes.data[offset + (high.y * size + high.x) * stride + 4];

            return mix(
                mix(lowLow, lowHigh, between.x),
                mix(highLow, highHigh, between.x),
                between.y);
        }

        float distributionGgx(float normalDotHalf, float roughness)
        {
            float a = roughness * roughness;
            float aa = a * a;
            float denominator = normalDotHalf * normalDotHalf * (aa - 1.0) + 1.0;

            return aa / max(Pi * denominator * denominator, 1.0e-9);
        }

        float geometrySmith(float normalDotView, float normalDotLight, float roughness)
        {
            float k = roughness * roughness * 0.5;
            float view = normalDotView / (normalDotView * (1.0 - k) + k);
            float light = normalDotLight / (normalDotLight * (1.0 - k) + k);

            return view * light;
        }

        vec3 fresnelSchlick(vec3 f0, float cosine)
        {
            return f0 + (vec3(1.0) - f0) * pow(clamp(1.0 - cosine, 0.0, 1.0), 5.0);
        }

        vec2 albedoAt(int view, int roughness)
        {
            int x = clamp(view, 0, AlbedoSide - 1);
            int y = clamp(roughness, 0, AlbedoSide - 1);
            int at = (y * AlbedoSide + x) * 2;

            return vec2(specularAlbedo.data[at], specularAlbedo.data[at + 1]);
        }

        // The split-sum pair: what a surface returns from one scattering is its own
        // f0 times the first plus the second, so one table serves every colour.
        vec2 specularTerms(float normalDotView, float roughness)
        {
            float x = clamp(normalDotView, 0.0, 1.0) * float(AlbedoSide - 1);
            float y = clamp(roughness, 0.0, 1.0) * float(AlbedoSide - 1);

            int x0 = int(floor(x));
            int y0 = int(floor(y));

            float fx = x - float(x0);
            float fy = y - float(y0);

            vec2 lower = mix(albedoAt(x0, y0), albedoAt(x0 + 1, y0), fx);
            vec2 upper = mix(albedoAt(x0, y0 + 1), albedoAt(x0 + 1, y0 + 1), fx);

            return mix(lower, upper, fy);
        }

        // How much of the light a rough microfacet surface returns when it is
        // allowed to scatter only once — read from a table integrated over this
        // very estimator rather than approximated.
        //
        // It was Karis's analytic fit of the split-sum environment term, which is a
        // fit of a different integral: measured in a furnace, the compensation
        // built on it came out +21% at roughness 0.35 and -32% at 1, adding light
        // where none had been lost and losing it where it had.
        float singleScatterAlbedo(float normalDotView, float roughness)
        {
            vec2 terms = specularTerms(normalDotView, roughness);

            return terms.x + terms.y;
        }

        // What this surface returns from one scattering, its own colour applied —
        // and what the diffuse half must therefore be scaled down by. Scaled by the
        // Fresnel at the view angle instead, a white matte surface came back 2.8%
        // dark in a furnace: four per cent was taken away for a lobe that returns
        // one and a bit at roughness one.
        vec3 specularReflectance(vec3 f0, float normalDotView, float roughness)
        {
            vec2 terms = specularTerms(normalDotView, roughness);

            return f0 * terms.x + vec3(terms.y);
        }

        // The same, with what a second scattering would have added — which is what
        // actually leaves the surface, so it is what the diffuse half gives up.
        vec3 specularShare(vec3 f0, float normalDotView, float roughness);

        // What the rest of that light would have come back as. A microfacet that
        // shadows another one scatters again, and a single-scattering model simply
        // loses it: measured in a furnace, a white metal at roughness one came back
        // forty per cent darker than the room it stood in. This is the standard
        // compensation — the missing energy returned tinted by the surface, since
        // that is what a second bounce off it would have been.
        vec3 energyCompensation(vec3 f0, float normalDotView, float roughness)
        {
            float returned = clamp(singleScatterAlbedo(normalDotView, roughness), 0.01, 1.0);

            return vec3(1.0) + f0 * (1.0 / returned - 1.0);
        }

        vec3 specularShare(vec3 f0, float normalDotView, float roughness)
        {
            return clamp(
                specularReflectance(f0, normalDotView, roughness)
                    * energyCompensation(f0, normalDotView, roughness),
                vec3(0.0),
                vec3(1.0));
        }

        // The whole reflectance towards one direction, times the cosine — what a
        // directly sampled light is multiplied by.
        vec3 evaluateBsdf(vec3 normal, vec3 view, vec3 light, Material material, bool diffuseOnly)
        {
            float normalDotLight = dot(normal, light);
            float normalDotView = dot(normal, view);

            if (normalDotLight <= 0.0 || normalDotView <= 0.0)
                return vec3(0.0);

            vec3 albedo = material.albedoOpacity.rgb;
            float roughness = max(material.roughMetalEmission.x, MinimumRoughness);
            float metallic = material.roughMetalEmission.y;

            vec3 halfway = normalize(view + light);
            vec3 f0 = mix(vec3(0.04), albedo, metallic);
            vec3 fresnel = fresnelSchlick(f0, dot(view, halfway));

            float distribution = distributionGgx(max(dot(normal, halfway), 0.0), roughness);
            float geometry = geometrySmith(normalDotView, normalDotLight, roughness);

            vec3 specular = diffuseOnly
                ? vec3(0.0)
                : fresnel * distribution * geometry
                    * energyCompensation(f0, normalDotView, roughness)
                    / max(4.0 * normalDotView * normalDotLight, 1.0e-6);

            // Scaled by what the specular half actually takes, not by the Fresnel
            // at this angle: the two are the same only for a mirror.
            vec3 taken = diffuseOnly
                ? fresnel
                : specularShare(f0, normalDotView, roughness);

            vec3 diffuse = albedo * (vec3(1.0) - taken) * (1.0 - metallic) / Pi;

            return (diffuse + specular) * normalDotLight;
        }

        // A half vector drawn from the roughness lobe itself.
        vec3 sampleHalfway(vec3 normal, float roughness)
        {
            float a = roughness * roughness;
            float u1 = randomFloat();
            float cosTheta = sqrt((1.0 - u1) / max(1.0 + (a * a - 1.0) * u1, 1.0e-9));
            float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
            float phi = 2.0 * Pi * randomFloat();

            return normalize(
                toWorld(vec3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta), normal));
        }

        // A direction to carry an opaque path on in, and what the throughput
        // becomes. The lobe is chosen at random and the weight already accounts
        // for the choice, so nothing downstream has to know which one it was.
        bool sampleOpaque(
            vec3 normal,
            vec3 view,
            Material material,
            bool diffuseOnly,
            out vec3 direction,
            out vec3 weight)
        {
            vec3 albedo = material.albedoOpacity.rgb;
            float roughness = max(material.roughMetalEmission.x, MinimumRoughness);
            float metallic = material.roughMetalEmission.y;

            float normalDotView = max(dot(normal, view), 1.0e-4);
            vec3 f0 = mix(vec3(0.04), albedo, metallic);
            vec3 fresnel = fresnelSchlick(f0, normalDotView);

            float specularProbability = clamp(max(fresnel.r, max(fresnel.g, fresnel.b)), 0.04, 0.95);
            specularProbability = mix(specularProbability, 1.0, metallic);

            // The part of a see-through material that did not let light through
            // scatters it, and nothing more: its highlight is the dielectric
            // interface beside this, already counted. Both would be the reflection
            // twice — and once through a lobe as narrow as glass's, which is what
            // turns into a field of bright speckles nothing converges away.
            if (diffuseOnly)
                specularProbability = 0.0;

            if (randomFloat() < specularProbability)
            {
                vec3 halfway = sampleHalfway(normal, roughness);

                direction = reflect(-view, halfway);

                float normalDotLight = dot(normal, direction);

                if (normalDotLight <= 0.0)
                    return false;

                // The classic simplification of f * cos / pdf when the half vector
                // was drawn from the distribution.
                float geometry = geometrySmith(normalDotView, normalDotLight, roughness);
                float viewDotHalf = max(dot(view, halfway), 1.0e-4);
                float normalDotHalf = max(dot(normal, halfway), 1.0e-4);

                weight = fresnel * geometry * viewDotHalf
                    * energyCompensation(f0, normalDotView, roughness)
                    / (normalDotView * normalDotHalf * specularProbability);

                return true;
            }

            direction = sampleCosine(normal);

            if (dot(normal, direction) <= 0.0)
                return false;

            // The same energy split the direct term keeps: what the diffuse half
            // gives up is what the specular one returns, over the whole lobe.
            vec3 taken = diffuseOnly
                ? fresnel
                : specularShare(f0, normalDotView, roughness);

            weight = albedo * (vec3(1.0) - taken) * (1.0 - metallic)
                / (1.0 - specularProbability);

            return true;
        }

        // An interface between air and a material that lets light through:
        // reflect by Fresnel, otherwise refract, with the roughness scattering the
        // normal it does either about.
        bool sampleDielectric(
            vec3 normal,
            vec3 view,
            Material material,
            bool entering,
            out vec3 direction,
            out vec3 weight,
            out bool transmitted)
        {
            float roughness = max(material.roughMetalEmission.x, MinimumRoughness);
            float eta = entering ? (1.0 / 1.5) : 1.5;

            vec3 scattered = roughness > MinimumRoughness
                ? sampleHalfway(normal, roughness)
                : normal;

            float cosine = clamp(dot(view, scattered), 0.0, 1.0);
            float reflectance = fresnelSchlick(vec3(0.04), cosine).r;
            vec3 refracted = refract(-view, scattered, eta);

            transmitted = false;

            // Nothing came through: past the critical angle the surface is a mirror.
            if (dot(refracted, refracted) < Tiny || randomFloat() < reflectance)
            {
                direction = reflect(-view, scattered);
                weight = vec3(1.0);

                return dot(direction, normal) > 0.0;
            }

            direction = normalize(refracted);
            transmitted = true;

            // The colour is taken once, on the way in, rather than by the distance
            // travelled through the glass: the document gives a material a colour,
            // not a density, and a voxel is not thick enough for the difference to
            // be the thing anybody notices.
            weight = entering ? material.albedoOpacity.rgb : vec3(1.0);

            return true;
        }

        // A cell's word names its material in its low sixteen bits, zero being
        // air; the high half is spare. RenderSceneBuilder.MaterialMask is the same
        // number, and a disagreement would read every material as nonsense.
        const uint MaterialMask = 65535u;

        struct Hit
        {
            float distance;
            vec3 normal;
            vec3 localPoint;   // where it landed, in the element's own cells
            vec3 localNormal;  // the face it crossed, in the same frame
            ivec3 cell;        // which cell of the element, for telling one light from another
            uint value;        // the cell entered: zero is air, else material index plus one
            int element;       // which element it belongs to, or -1 for the ground
            bool ground;
        };

        // One element, marched in its own cells. The direction is carried in
        // unnormalised on purpose, so the parameter along it stays world distance.
        // 'medium' is what the ray is currently inside: the march reports the first
        // cell that differs from it, which is what finds the far face of a glass
        // block rather than its front face all over again.
        bool traceElement(
            int index,
            vec3 origin,
            vec3 direction,
            uint medium,
            inout Hit hit)
        {
            Element element = elements.data[index];

            vec3 localOrigin = (element.worldToLocal * vec4(origin, 1.0)).xyz;
            vec3 localDirection = mat3(element.worldToLocal) * direction;

            // A component of exactly zero divides into an infinity and then
            // multiplies into a NaN against a slab it lies in. Nudged off zero,
            // which is avoiding a division rather than tuning a result.
            localDirection.x = abs(localDirection.x) < Tiny ? Tiny : localDirection.x;
            localDirection.y = abs(localDirection.y) < Tiny ? Tiny : localDirection.y;
            localDirection.z = abs(localDirection.z) < Tiny ? Tiny : localDirection.z;

            ivec3 gridMin = element.gridMinAndOffset.xyz;
            ivec3 gridSize = element.gridSize.xyz;
            int offset = element.gridMinAndOffset.w;

            vec3 inverseDirection = 1.0 / localDirection;
            vec3 firstPlane = (vec3(gridMin) - localOrigin) * inverseDirection;
            vec3 secondPlane = (vec3(gridMin + gridSize) - localOrigin) * inverseDirection;
            vec3 nearPlane = min(firstPlane, secondPlane);
            vec3 farPlane = max(firstPlane, secondPlane);

            float enter = max(max(nearPlane.x, nearPlane.y), nearPlane.z);
            float leave = min(min(farPlane.x, farPlane.y), farPlane.z);

            if (leave <= max(enter, 0.0) || enter >= hit.distance)
                return false;

            int axis = nearPlane.x > nearPlane.y
                ? (nearPlane.x > nearPlane.z ? 0 : 2)
                : (nearPlane.y > nearPlane.z ? 1 : 2);

            float travelled = max(enter, 0.0);
            vec3 point = localOrigin + localDirection * (travelled + 1.0e-4);
            ivec3 cell = clamp(ivec3(floor(point)), gridMin, gridMin + gridSize - 1);

            ivec3 stepping = ivec3(sign(localDirection));
            vec3 delta = abs(inverseDirection);
            vec3 boundary = vec3(cell) + max(vec3(stepping), vec3(0.0));
            vec3 next = (boundary - localOrigin) * inverseDirection;

            int steps = gridSize.x + gridSize.y + gridSize.z + 3;

            for (int i = 0; i < steps; i++)
            {
                ivec3 local = cell - gridMin;
                uint word = voxels.data[
                    offset + local.x + gridSize.x * (local.y + gridSize.y * local.z)];
                uint value = word & MaterialMask;

                if (value != medium && travelled < hit.distance)
                {
                    // The face crossed always faces back along the ray, whichever
                    // side of it the ray was on.
                    vec3 localNormal = vec3(0.0);
                    localNormal[axis] = -float(stepping[axis]);

                    hit.distance = travelled;
                    hit.normal = normalize(localNormal * mat3(element.worldToLocal));
                    hit.localPoint = localOrigin + localDirection * travelled;
                    hit.localNormal = localNormal;
                    hit.cell = cell;
                    hit.value = value;
                    hit.element = index;
                    hit.ground = false;

                    return true;
                }

                if (next.x < next.y && next.x < next.z)
                {
                    travelled = next.x;
                    next.x += delta.x;
                    cell.x += stepping.x;
                    axis = 0;
                }
                else if (next.y < next.z)
                {
                    travelled = next.y;
                    next.y += delta.y;
                    cell.y += stepping.y;
                    axis = 1;
                }
                else
                {
                    travelled = next.z;
                    next.z += delta.z;
                    cell.z += stepping.z;
                    axis = 2;
                }

                bool outside = any(lessThan(cell, gridMin))
                    || any(greaterThanEqual(cell, gridMin + gridSize));

                if (travelled >= hit.distance)
                    return false;

                if (outside)
                {
                    // Leaving the grid while inside something is an interface too:
                    // the last cell's far face, with air on the other side.
                    if (medium != 0u)
                    {
                        vec3 localNormal = vec3(0.0);
                        localNormal[axis] = -float(stepping[axis]);

                        hit.distance = travelled;
                        hit.normal = normalize(localNormal * mat3(element.worldToLocal));
                        hit.localPoint = localOrigin + localDirection * travelled;
                        hit.localNormal = localNormal;
                        hit.cell = cell;
                        hit.value = 0u;
                        hit.element = index;
                        hit.ground = false;

                        return true;
                    }

                    return false;
                }
            }

            return false;
        }

        bool traceScene(
            vec3 origin,
            vec3 direction,
            float limit,
            uint medium,
            int insideElement,
            out Hit hit)
        {
            hit.distance = limit;
            hit.normal = vec3(0.0, 1.0, 0.0);
            hit.localPoint = vec3(0.0);
            hit.localNormal = vec3(0.0, 1.0, 0.0);
            hit.cell = ivec3(0);
            hit.value = 0u;
            hit.element = -1;
            hit.ground = false;

            bool found = false;

            if (params.skyGround.w > 0.5 && medium == 0u && abs(direction.y) > Tiny)
            {
                float travelled = (params.skyHorizon.w - origin.y) / direction.y;

                if (travelled > 1.0e-4 && travelled < hit.distance)
                {
                    hit.distance = travelled;
                    hit.normal = vec3(0.0, 1.0, 0.0);
                    hit.ground = true;
                    hit.element = -1;
                    found = true;
                }
            }

            for (int i = 0; i < params.counts.z; i++)
            {
                Element element = elements.data[i];

                // The world box first: most elements are not on most rays, and a
                // slab test is a fraction of what marching one costs.
                vec3 safe = vec3(
                    abs(direction.x) < Tiny ? Tiny : direction.x,
                    abs(direction.y) < Tiny ? Tiny : direction.y,
                    abs(direction.z) < Tiny ? Tiny : direction.z);

                vec3 inverseDirection = 1.0 / safe;
                vec3 firstPlane = (element.boundsMin.xyz - origin) * inverseDirection;
                vec3 secondPlane = (element.boundsMax.xyz - origin) * inverseDirection;
                vec3 nearPlane = min(firstPlane, secondPlane);
                vec3 farPlane = max(firstPlane, secondPlane);

                float enter = max(max(nearPlane.x, nearPlane.y), nearPlane.z);
                float leave = min(min(farPlane.x, farPlane.y), farPlane.z);

                if (leave <= max(enter, 0.0) || enter >= hit.distance)
                    continue;

                uint elementMedium = i == insideElement ? medium : 0u;

                if (traceElement(i, origin, direction, elementMedium, hit))
                    found = true;
            }

            return found;
        }

        Material groundMaterial()
        {
            Material ground;
            ground.albedoOpacity = vec4(params.groundColor.xyz, 1.0);
            ground.roughMetalEmission = vec4(1.0, 0.0, 0.0, 0.0);

            // Every field, not the two the ground has an opinion about: a local in
            // GLSL is undefined until it is written, and the ground reaches the
            // same shading code every voxel does. It cost a picture already — the
            // facture branch is gated on this not being the ground, so finishTerms
            // went unread for as long as nothing outside that branch looked at it,
            // and the day the occlusion did, every ground hit multiplied the path
            // by whatever had been left on the stack.
            ground.finishTerms = vec4(0.0, 1.0, 0.0, 1.0);
            ground.finish = ivec4(0, 0, 0, 1);

            return ground;
        }

        // How much of a light survives the way to a point. A transparent voxel
        // does not black out what is under it, or a glass model would stand in a
        // solid shadow.
        vec3 visibility(vec3 origin, vec3 direction)
        {
            vec3 through = vec3(1.0);
            vec3 point = origin;
            uint medium = 0u;
            int insideElement = -1;

            for (int i = 0; i < 6; i++)
            {
                Hit hit;

                if (!traceScene(point, direction, RayMaximum, medium, insideElement, hit))
                    return through;

                if (hit.ground)
                    return vec3(0.0);

                if (hit.value != 0u)
                {
                    Material entered = materials.data[hit.value - 1u];
                    float opacity = entered.albedoOpacity.a;

                    if (opacity > 0.999)
                        return vec3(0.0);

                    through *= mix(vec3(1.0), entered.albedoOpacity.rgb, opacity);

                    if (max(through.r, max(through.g, through.b)) < 0.01)
                        return vec3(0.0);
                }

                medium = hit.value;
                insideElement = hit.element;
                point = point + direction * (hit.distance + SurfaceOffset);
            }

            return through;
        }

        // One emissive cell, aimed at rather than found. The cone that bounds it is
        // sampled and the sample is kept only if the ray lands on that very cell —
        // which is what makes this exact with more than one light in the scene: a
        // direction drawn towards this one that lands on another is the other one's
        // light, at the wrong density.
        vec3 sampleLights(
            vec3 point,
            vec3 normal,
            vec3 view,
            Material surface,
            bool diffuseOnly)
        {
            int count = int(params.groundColor.w);

            if (count <= 0)
                return vec3(0.0);

            int index = min(int(randomFloat() * float(count)), count - 1);
            vec4 sphere = lights.data[index * 2];
            ivec4 identity = floatBitsToInt(lights.data[index * 2 + 1]);

            vec3 toLight = sphere.xyz - point;
            float span = length(toLight);

            // Standing inside a light is not a direction anything can be sampled in.
            if (span <= sphere.w)
                return vec3(0.0);

            float sine = sphere.w / span;
            float cosMaximum = sqrt(max(0.0, 1.0 - sine * sine));
            float cosTheta = mix(cosMaximum, 1.0, randomFloat());
            float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
            float phi = 2.0 * Pi * randomFloat();

            vec3 towards = toWorld(
                vec3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta),
                toLight / span);

            vec3 response = evaluateBsdf(normal, view, towards, surface, diffuseOnly);

            if (response.r + response.g + response.b <= 0.0)
                return vec3(0.0);

            Hit probe;

            if (!traceScene(point + normal * SurfaceOffset, towards, RayMaximum, 0u, -1, probe))
                return vec3(0.0);

            bool landed = !probe.ground
                && probe.value != 0u
                && probe.element == identity.x
                && !any(notEqual(probe.cell, identity.yzw));

            if (!landed)
                return vec3(0.0);

            Material lit = materials.data[probe.value - 1u];
            vec3 radiance = lit.albedoOpacity.rgb * lit.roughMetalEmission.z * EmissionGain;

            // One light was chosen out of all of them, so its answer stands for all
            // of them; the cone it was drawn from is the rest of the density.
            float pdf = 1.0 / (2.0 * Pi * max(1.0 - cosMaximum, 1.0e-6) * float(count));

            return response * radiance / pdf;
        }

        // Modulates the colour by the field and bends the normal by its slope,
        // then hands back the material to be shaded with. The normal is bent in the
        // element's own frame and carried out through the same transform the face's
        // own normal took, so a rotated element wears its facture with it.
        Material wearFacture(Material surface, inout Hit hit, inout vec3 normal)
        {
            int offset = surface.finish.x;
            int size = surface.finish.y;
            int stride = max(surface.finish.w, 1);

            // Godot's own triplanar convention, so a facture runs the same way here
            // as it does in the viewport.
            vec3 placed = hit.localPoint / surface.roughMetalEmission.w * vec3(1.0, -1.0, 1.0);
            vec3 facing = abs(hit.localNormal);

            vec2 uv;
            vec3 tangent;
            vec3 bitangent;

            if (facing.x >= facing.y && facing.x >= facing.z)
            {
                uv = placed.zy;
                tangent = vec3(0.0, 0.0, 1.0);
                bitangent = vec3(0.0, -1.0, 0.0);
            }
            else if (facing.y >= facing.z)
            {
                uv = placed.xz;
                tangent = vec3(1.0, 0.0, 0.0);
                bitangent = vec3(0.0, 0.0, 1.0);
            }
            else
            {
                uv = placed.xy;
                tangent = vec3(1.0, 0.0, 0.0);
                bitangent = vec3(0.0, -1.0, 0.0);
            }

            float texelStep = 1.0 / float(size);
            float height = sampleHeight(offset, size, stride, uv);

            // Central differences, exactly as the viewport's normal map is built
            // from the very same heights.
            float bump = surface.finishTerms.z;
            vec3 slope = vec3(
                (sampleHeight(offset, size, stride, uv - vec2(texelStep, 0.0))
                    - sampleHeight(offset, size, stride, uv + vec2(texelStep, 0.0))) * bump,
                (sampleHeight(offset, size, stride, uv - vec2(0.0, texelStep))
                    - sampleHeight(offset, size, stride, uv + vec2(0.0, texelStep))) * bump,
                1.0);

            // The engine reads a normal map's depth as a blend towards flat rather
            // than as a factor on the slope, and this follows it.
            slope = normalize(mix(vec3(0.0, 0.0, 1.0), normalize(slope), surface.finishTerms.x));

            // A drawn facture dims the material's colour by its own height; a
            // scanned one carries the colour itself and replaces it, which is what
            // the viewport does too — a brick photograph times a brick-red material
            // would be twice as red.
            if (stride >= 4)
            {
                surface.albedoOpacity.rgb = sampleColor(offset, size, stride, uv);

                if (stride >= 5)
                    surface.roughMetalEmission.x *= sampleGloss(offset, size, stride, uv);

                if (stride >= 6)
                    surface.finishTerms.w = sampleOcclusion(offset, size, stride, uv);

                if (stride >= 7)
                    surface.roughMetalEmission.y *= sampleMetal(offset, size, stride, uv);
            }
            else
            {
                surface.albedoOpacity.rgb *= mix(surface.finishTerms.y, 1.0, height);
            }

            vec3 bent = normalize(
                tangent * slope.x + bitangent * slope.y + hit.localNormal * slope.z);

            vec3 world = normalize(bent * mat3(elements.data[hit.element].worldToLocal));

            // The shading normal follows the face it replaced, including whichever
            // way round that face had to be turned to face the ray.
            normal = dot(world, normal) < 0.0 ? -world : world;

            return surface;
        }

        vec4 trace(vec3 origin, vec3 direction, out float firstDistance)
        {
            // Nought is "the camera saw nothing there", which is what the sky is.
            firstDistance = 0.0;

            vec3 radiance = vec3(0.0);
            vec3 throughput = vec3(1.0);

            // Nought only where the camera itself saw nothing, which is what a
            // transparent backdrop is a picture of.
            float coverage = 1.0;

            uint medium = 0u;
            int insideElement = -1;

            // Whether a light this path lands on may be counted. It may from the
            // camera, and after a bounce that could not be aimed anywhere; after a
            // bounce that aimed at every light it may not, or the light is counted
            // twice.
            bool mayCountEmission = true;

            for (int bounce = 0; bounce <= params.counts.w; bounce++)
            {
                Hit hit;

                bool found = traceScene(origin, direction, RayMaximum, medium, insideElement, hit);

                // A transparent backdrop is a picture of the model and nothing
                // else, and the floor is scenery rather than something built — so
                // the camera does not see it either. Light still bounces off it,
                // which is what keeps an underside lit rather than black.
                if (found && bounce == 0 && hit.ground && int(params.lens.y) == 2)
                    found = false;

                if (bounce == 0 && found)
                    firstDistance = hit.distance;

                if (!found)
                {
                    // Only the camera's own ray sees the backdrop. A ray deeper in
                    // is asking what lights this point, and the answer to that is
                    // the sky whatever is being drawn behind the model.
                    if (bounce > 0)
                    {
                        radiance += throughput * skyRadiance(normalize(direction));

                        break;
                    }

                    coverage = 0.0;

                    int backdrop = int(params.lens.y);

                    if (backdrop == 1)
                        radiance += throughput * params.backdropColor.xyz;
                    else if (backdrop == 0)
                        radiance += throughput * skyRadiance(normalize(direction));

                    break;
                }

                vec3 point = origin + direction * hit.distance;
                vec3 view = -normalize(direction);
                vec3 normal = hit.normal;

                if (dot(normal, view) < 0.0)
                    normal = -normal;

                bool entering = medium == 0u;

                Material surface;
                bool opaque;

                if (hit.ground)
                {
                    surface = groundMaterial();
                    opaque = true;
                }
                else if (hit.value != 0u)
                {
                    surface = materials.data[hit.value - 1u];
                    opaque = surface.albedoOpacity.a > 0.999;
                }
                else
                {
                    // Air on the far side: the interface belongs to what is being
                    // left, which is the only place its colour and roughness are.
                    surface = materials.data[medium - 1u];
                    opaque = false;
                }

                // The facture: the same tiling height field the viewport lays over
                // this material, sampled here rather than through a sampler. The
                // faces of a voxel are axis-aligned, so the triplanar mapping picks
                // one plane exactly instead of blending three.
                if (!hit.ground && surface.finish.y > 0)
                    surface = wearFacture(surface, hit, normal);

                // Only on the way in, or a pane of glass would be counted twice.
                if (entering && mayCountEmission)
                {
                    vec3 emitted = throughput
                        * surface.albedoOpacity.rgb
                        * surface.roughMetalEmission.z
                        * EmissionGain;

                    radiance += bounce == 0 ? emitted : min(emitted, vec3(IndirectClamp));
                }

                // A see-through material is an interface most of the time and a
                // surface the rest of it, in the proportion its alpha says — the
                // same bargain the viewport's own blending makes with that number.
                bool dielectric = !opaque
                    && (!entering || randomFloat() >= surface.albedoOpacity.a);

                if (!dielectric)
                {
                    // The sun, aimed at rather than found: this is what a soft
                    // shadow and a highlight both come from.
                    float pdf;
                    vec3 toSun = sampleSun(pdf);
                    vec3 response = evaluateBsdf(normal, view, toSun, surface, !opaque);

                    if (response.r + response.g + response.b > 0.0)
                    {
                        vec3 reaches = visibility(point + normal * SurfaceOffset, toSun);

                        vec3 direct = throughput * response * params.sunRadiance.xyz * reaches / pdf;

                        radiance += bounce == 0 ? direct : min(direct, vec3(IndirectClamp));
                    }

                    // And the emissive materials, on the same terms. Deliberately
                    // not clamped: this one is aimed at, so it converges — clamping
                    // it would take light out of a picture that was going to be
                    // right.
                    radiance += throughput * sampleLights(point, normal, view, surface, !opaque);
                }

                vec3 next;
                vec3 weight;
                bool transmitted = false;
                bool ok = dielectric
                    ? sampleDielectric(normal, view, surface, entering, next, weight, transmitted)
                    : sampleOpaque(normal, view, surface, !opaque, next, weight);

                if (!ok)
                    break;

                // The facture's own occlusion, and only here. Everything the tracer
                // can see for itself it has already followed; what a map of one
                // number per texel adds is the sky a crevice *below the texel*
                // cannot reach, so it dims what arrives from the whole hemisphere —
                // the sky and every bounce — and leaves alone the two things that
                // were aimed at, the sun and the emissive materials, whose
                // directions are known and about which this map has no opinion.
                // That is also what the viewport does with it, where the term it
                // attenuates is the ambient one.
                throughput *= weight * surface.finishTerms.w;

                if (transmitted)
                {
                    medium = hit.value;
                    insideElement = hit.element;
                }

                // A surface that was aimed at has already had every light counted;
                // one that only reflected has not, and must be allowed to find one.
                mayCountEmission = dielectric || params.sunRadiance.w < 0.5;

                origin = point + next * SurfaceOffset;
                direction = next;

                // Russian roulette, once a path has had a chance to be
                // interesting. Unbiased: what survives is scaled by its odds.
                if (bounce >= 2)
                {
                    float survival = clamp(
                        max(throughput.r, max(throughput.g, throughput.b)),
                        0.05,
                        1.0);

                    if (randomFloat() > survival)
                        break;

                    throughput /= survival;
                }
            }

            return vec4(radiance, coverage);
        }

        void main()
        {
            // The dispatch covers one tile, so an invocation is a pixel of that
            // tile rather than of the picture. Both bounds are checked: the tile's
            // own, which a partial workgroup runs past, and the image's, which a
            // tile is already clipped to and which is what makes a bad one quiet.
            if (gl_GlobalInvocationID.x >= uint(push.tile.z)
                || gl_GlobalInvocationID.y >= uint(push.tile.w))
                return;

            ivec2 pixel = push.tile.xy + ivec2(gl_GlobalInvocationID.xy);

            if (pixel.x >= params.counts.x || pixel.y >= params.counts.y)
                return;

            int index = pixel.y * params.counts.x + pixel.x;

            // The sample index is the tile's own, not the picture's: two tiles at
            // different counts must not draw the same sequence, and they cannot,
            // the pixel index being in the hash as well.
            rngState = hashed(uint(index) * 9781u + uint(push.frame.x) * 6271u + 1u);

            // A jitter inside the pixel, so what accumulates also antialiases the
            // edges rather than needing a second mechanism for them.
            float x = (float(pixel.x) + randomFloat()) / float(params.counts.x) * 2.0 - 1.0;
            float y = 1.0 - (float(pixel.y) + randomFloat()) / float(params.counts.y) * 2.0;

            float tangent = params.cameraOrigin.w;

            vec3 direction = normalize(
                params.cameraForward.xyz
                + params.cameraRight.xyz * (x * tangent * params.cameraRight.w)
                + params.cameraUp.xyz * (y * tangent));

            vec3 origin = params.cameraOrigin.xyz;
            float orthographic = params.lens.x;

            if (orthographic > 0.0)
            {
                // Parallel rays across a window of the given height: no
                // convergence, so parallel edges stay parallel and a cube seen
                // from its corner draws as an isometric cube. A lens has nothing
                // to do here — there is no cone to open.
                float extent = orthographic * 0.5;

                origin += params.cameraRight.xyz * (x * extent * params.cameraRight.w)
                    + params.cameraUp.xyz * (y * extent);

                direction = params.cameraForward.xyz;

                float reached;
                vec4 carried = min(trace(origin, direction, reached), vec4(64.0));

                accumulation.data[index] += carried;
                depth.data[index] = reached;
                moments.data[index] += luminance(carried.rgb) * luminance(carried.rgb);

                return;
            }

            float aperture = params.cameraUp.w;

            if (aperture > 0.0)
            {
                // A thin lens: the ray still passes through the same point on the
                // plane in focus, but leaves from somewhere on the lens rather
                // than from its centre — so what is on that plane stays sharp and
                // everything else spreads by how far off it is.
                float ahead = max(dot(direction, params.cameraForward.xyz), 1.0e-4);
                vec3 focal = origin + direction * (params.cameraForward.w / ahead);

                float angle = 2.0 * Pi * randomFloat();
                float radius = aperture * sqrt(randomFloat());
                vec2 lens = vec2(cos(angle), sin(angle)) * radius;

                origin += params.cameraRight.xyz * lens.x + params.cameraUp.xyz * lens.y;
                direction = normalize(focal - origin);
            }

            // Past this, a bright pixel is a sampling artefact rather than a bright
            // thing, and one of them survives a thousand samples on screen. The
            // coverage rides in the fourth channel and is never above one anyway.
            float reached;
            vec4 carried = min(trace(origin, direction, reached), vec4(64.0));

            accumulation.data[index] += carried;
            depth.data[index] = reached;
            moments.data[index] += luminance(carried.rgb) * luminance(carried.rgb);
        }
        """;
}
