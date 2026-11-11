using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One material of an OBJ file's companion library, cut down to what a reference
/// needs: what colour it is, how much you can see through it, and which picture is
/// on it.
/// </summary>
/// <remarks>
/// The rest of the format — specular colour, exponent, ambient, illumination
/// models, bump maps — is deliberately dropped. A reference stands in the scene to
/// be traced over, and it is drawn unshaded by default, under which none of that
/// says anything at all.
/// </remarks>
/// <param name="DiffuseMap">
/// The picture's path exactly as the file names it, relative to the library. It is
/// resolved by whoever knows where the library was read from.
/// </param>
internal readonly record struct ObjMaterial(Color Diffuse, float Opacity, string DiffuseMap)
{
    public static ObjMaterial Default => new(new Color(0.8f, 0.8f, 0.8f), 1f, string.Empty);
}
