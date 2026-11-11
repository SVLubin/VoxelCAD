using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One run of an OBJ file's triangles: everything in it that wears one material,
/// as the arrays a mesh is built from.
/// </summary>
/// <remarks>
/// A surface per material rather than per group or per object, because a surface
/// is what a material is worn by — which is also the split Godot's own mesh takes.
/// </remarks>
/// <param name="MaterialName">
/// What <c>usemtl</c> named, or empty where the file never named one.
/// </param>
/// <param name="Indices">
/// Wound clockwise, Godot's front-face convention, which is the reverse of the
/// counter-clockwise order the file carries.
/// </param>
internal sealed record ObjSurface(
    string MaterialName,
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] Uvs,
    int[] Indices);
