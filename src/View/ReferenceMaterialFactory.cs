using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Turns a <see cref="ReferenceLook"/> into the material a reference picture wears.
/// </summary>
/// <remarks>
/// It decides nothing: every switch comes from the look, which is engine-free and
/// therefore testable — a <see cref="StandardMaterial3D"/> cannot be built in a
/// test host at all.
/// </remarks>
internal static class ReferenceMaterialFactory
{
    /// <summary>
    /// What a plane with no picture behind it is drawn as: a flat panel, faint
    /// enough to say "something is here and it is not loading" without covering what
    /// is being built. Why it is not there is the inspector's and
    /// <c>references</c>'s to say — a colour cannot carry a reason.
    /// </summary>
    private static readonly Color Missing = new(0.55f, 0.55f, 0.58f, 0.35f);

    public static StandardMaterial3D CreatePicture(ReferenceLook look, Texture2D? picture)
    {
        var material = new StandardMaterial3D
        {
            // The picture's own alpha has to survive — a traced sprite is mostly
            // transparent — and an instance faded below one goes through this pass
            // regardless, so it is declared rather than inferred.
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,

            // A plane has no inside. Which way it faces is a fact about where
            // somebody stood, not about the picture.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,

            ShadingMode = look.Unshaded
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel,

            TextureFilter = look.Nearest
                ? BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps
                : BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };

        if (picture != null)
            material.AlbedoTexture = picture;
        else
            material.AlbedoColor = Missing;

        return material;
    }
}
