using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// Somebody else's 3D model standing in the scene to be built around: which file
/// it is and how it is drawn. How big it is and where it stands is the element's
/// own transform — one scaling mechanism rather than two.
/// </summary>
/// <remarks>
/// The model layer knows nothing about the geometry behind the path: no vertex
/// count, no bounding box, no materials. All of that belongs to whoever can load
/// the file, and a document that carried a copy of it would be a document that
/// could disagree with the file it names.
/// </remarks>
/// <param name="Path">
/// Where the model is. Absolute in memory; relative in the file whenever the asset
/// sits under the project's own directory.
/// </param>
/// <param name="Opacity">One is solid, nought is drawn not at all.</param>
/// <param name="LitByScene">
/// Whether the scene's lights fall on it. Off by default, so a reference reads as
/// a reference rather than as something in the model.
/// </param>
/// <param name="Wireframe">
/// Drawn as its own edges instead of its surfaces, which is how a silhouette is
/// traced without the reference covering what is being built inside it.
/// </param>
public readonly record struct ReferenceModelSettings(
    string Path,
    float Opacity,
    bool LitByScene,
    bool Wireframe)
{
    /// <summary>A model nobody has configured: solid, unlit, surfaces rather than edges.</summary>
    public static ReferenceModelSettings Default => new(
        Path: string.Empty,
        Opacity: 1f,
        LitByScene: false,
        Wireframe: false);

    /// <inheritdoc cref="ReferenceImageSettings.Clamped"/>
    public ReferenceModelSettings Clamped() => this with
    {
        Path = Path ?? string.Empty,
        Opacity = Math.Clamp(Opacity, ReferenceLimits.MinimumOpacity, ReferenceLimits.MaximumOpacity)
    };
}
