using System;
using System.Collections.Generic;
using System.IO;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Which files may stand in a scene as a reference, and which kind of component
/// each one becomes.
/// </summary>
/// <remarks>
/// The list is the model's because the component kind is: an import names a file
/// and the document has to decide what it just acquired. It is deliberately the
/// set the viewport can actually draw — offering a format nothing could display
/// would be an import that succeeds into an empty square.
/// </remarks>
public static class ReferenceFormats
{
    /// <summary>Exactly what the engine's own image loader reads from a file on disk.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = new[]
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tga"
    };

    /// <summary>
    /// glTF is the engine's own runtime format; the other two are read by parsers
    /// of ours. FBX is absent on purpose — reading one at runtime needs an external
    /// converter shipped for three platforms.
    /// </summary>
    public static IReadOnlyList<string> ModelExtensions { get; } = new[]
    {
        ".gltf", ".glb", ".obj", ".stl"
    };

    /// <summary>
    /// What importing this file would create, or false where nothing here can
    /// stand for it. Decided by the name rather than by the contents, which is what
    /// lets a missing file still be described.
    /// </summary>
    public static bool TryGetKind(string path, out ComponentKind kind)
    {
        kind = ComponentKind.None;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        string extension = Path.GetExtension(path);

        if (Contains(ImageExtensions, extension))
            kind = ComponentKind.ReferenceImage;
        else if (Contains(ModelExtensions, extension))
            kind = ComponentKind.ReferenceModel;
        else
            return false;

        return true;
    }

    /// <summary>
    /// Whether a component kind is one of the two references. Asked wherever a
    /// walk has to treat both alike — named as what qualifies rather than as a list
    /// of refusals, so a third reference kind settles the question by existing.
    /// </summary>
    public static bool IsReference(ComponentKind kind) =>
        kind is ComponentKind.ReferenceImage or ComponentKind.ReferenceModel;

    private static bool Contains(IReadOnlyList<string> extensions, string extension)
    {
        foreach (string candidate in extensions)
        {
            if (string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
