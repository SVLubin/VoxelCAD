using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Turns a colour and an appearance into the material a voxel surface wears.
/// </summary>
/// <remarks>
/// Standard PBR throughout, never a bespoke shader: voxels answer to the same
/// lights as everything else, and a facture is a real normal map rather than a
/// look faked with baked-in shading. Triplanar is what makes that free here —
/// voxel faces are axis-aligned unit squares, so mapping by position is exact and
/// the mesh needs no UVs at all.
/// <para>
/// It decides nothing: every number and every switch comes from
/// <see cref="VoxelMaterialLook"/>, which is engine-free and therefore testable —
/// a <see cref="StandardMaterial3D"/> cannot be built in a test host at all.
/// </para>
/// </remarks>
internal static class VoxelMaterialFactory
{
    public static Material Create(
        string name,
        Color color,
        MaterialAppearance appearance,
        ViewportEffects effects)
    {
        // Only here are both known: the appearance says the finish is a scan, and
        // the name says whether the pack actually holds one under it.
        bool scanned = appearance.Finish == MaterialFinish.Standard
            && StandardMaterialPack.Has(name);

        VoxelMaterialLook look = VoxelMaterialLook.From(color, appearance, effects, scanned);

        FinishTexturePair maps = FinishTextures.Get(name, appearance.Finish);

        // NOTE: A drawn facture's albedo is a grey shade to be multiplied by
        // the material's colour; a photographed one *is* the colour, and
        // multiplying a brick photograph by a brick-red material makes it twice as
        // red. So a facture that carries colour reduces the material's own to a
        // tint — and the alpha has to survive, since transparency is the colour's
        // alpha and nothing else.
        Color albedo = look.WearsFinish && maps.CarriesColor
            ? new Color(1f, 1f, 1f, color.A)
            : color;

        var material = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Roughness = appearance.Roughness,
            Metallic = appearance.Metallic,

            Transparency = look.Blends
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,

            SpecularMode = look.Shines
                ? BaseMaterial3D.SpecularModeEnum.SchlickGgx
                : BaseMaterial3D.SpecularModeEnum.Disabled,

            // The vertex colour is the mesher's ambient occlusion and nothing
            // else, so it multiplies the albedo. Stated rather than defaulted:
            // read as sRGB, a shade of 0.6 lands near 0.32 and a 40% darkening
            // becomes a 68% one.
            VertexColorUseAsAlbedo = look.Occluded,
            VertexColorIsSrgb = false
        };

        if (look.EmissionEnergy > 0f)
        {
            // Self-lit, still shaded: the light in the scene goes on reaching it,
            // which is what keeps an emissive block a block rather than a decal.
            material.EmissionEnabled = true;
            material.Emission = color;
            material.EmissionEnergyMultiplier = look.EmissionEnergy;
        }

        ApplyFinish(material, appearance.Finish, look, maps);

        return material;
    }

    private static void ApplyFinish(
        StandardMaterial3D material,
        MaterialFinish finish,
        VoxelMaterialLook look,
        FinishTexturePair textures)
    {
        if (!look.WearsFinish)
            return;

        // Local rather than world triplanar: the facture belongs to the model, so
        // it rides with the element instead of sliding across it as it is moved.
        // The mesh is built in cells, so a scale of one over the period is
        // literally "one repeat every N voxels".
        material.Uv1Triplanar = true;
        material.Uv1Scale = Vector3.One / look.FinishPeriod;

        material.AlbedoTexture = textures.Albedo;
        material.NormalEnabled = true;
        material.NormalTexture = textures.Normal;
        material.NormalScale = look.NormalScale;

        // NOTE: The one channel a height field has no opinion about: a scanned
        // set knows that mortar is matte where the brick beside it is not, and gloss
        // varying across a surface is a large part of what the eye reads as real.
        // The material's own roughness becomes the ceiling the map works under.
        if (textures.Roughness is not null)
        {
            material.RoughnessTexture = textures.Roughness;
            material.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
        }

        // NOTE: The occlusion the surface's own relief owes itself. The
        // mesher's is measured in *cells* and cannot see a mortar joint, so the two
        // are about different scales and compose rather than compete. Ambient only
        // (AOLightAffect stays at nought), which is what occlusion means: it removes
        // the sky a crevice cannot see and never argues with a light that reaches it.
        if (textures.Occlusion is not null)
        {
            material.AOEnabled = true;
            material.AOTexture = textures.Occlusion;
            material.AOTextureChannel = BaseMaterial3D.TextureChannel.Red;
        }

        // NOTE: Godot *multiplies* this map by the material's own metallic, so
        // a map under the default nought does nothing at all — the whole surface
        // would be dielectric whatever the map says. Handing the map the authority
        // means opening the dial fully; for a stone set that map is black and the
        // answer is the same, which is the point: it is answered rather than
        // assumed.
        if (textures.Metallic is not null)
        {
            material.Metallic = 1f;
            material.MetallicTexture = textures.Metallic;
            material.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
        }

        // NOTE: Godot's own parallax is not usable here. Godot's parallax works on UV1 in a
        // tangent frame and triplanar has neither, so HeightmapEnabled is inert
        // here. Measured rather than assumed — with it switched on, the three sets
        // whose shipped normal is flat rendered as printed patterns with no shading
        // whatsoever. The displacement is put to work by being differentiated into
        // the normal instead; see ScannedFinishes.
        material.TextureFilter = textures.CarriesColor

            // Nearest, and with mipmaps: the squares stay square close up, and a
            // surface seen edge-on still has something to fall back on rather than
            // fizzing. This is the whole of "pixelated" at the sampler.
            ? BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps

            // A streaked facture is sampled at wildly different rates along its two
            // axes, and plain mipmapping picks one level for both: the fine
            // direction is blurred away and a large surface smears. Anisotropic
            // filtering is the fix for exactly that case.
            : BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
    }
}
