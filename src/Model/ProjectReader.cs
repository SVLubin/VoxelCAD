using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>Rebuilds a live project from documents. Knows nothing about files.</summary>
internal static class ProjectReader
{
    public static Project Read(ProjectDocument document)
    {
        // Older schemas are read, newer ones refused: this build knows what it
        // used to write, and cannot know what a later one will.
        if (document.Version < ProjectWriter.OldestReadableVersion
            || document.Version > ProjectWriter.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Project schema version {document.Version} is not supported; "
                + $"this build reads {ProjectWriter.OldestReadableVersion} "
                + $"to {ProjectWriter.CurrentVersion}.");
        }

        Project project = Project.CreateEmpty();

        foreach (MaterialDocument material in document.Materials)
        {
            // A file written before the library existed carries the shipped
            // materials as its own. Skipped rather than merged: the project has
            // already seeded them, and reading them again would put two materials
            // with one id in the registry.
            if (StandardMaterials.Contains(ParseId(material.Id)))
                continue;

            project.AddLoadedMaterial(
                ParseId(material.Id),
                new MaterialSnapshot(
                    material.Name,
                    ToColor(material.Color),
                    ToAppearance(material.Appearance, ToColor(material.Color))));
        }

        foreach (SceneDocument scene in document.Scenes)
            ReadScene(project, scene);

        // Order-free with the scenes: an instance is read as a bare reference
        // wherever it stands, and every one of them is filled in below, once the
        // whole library exists.
        foreach (SceneDocument prefab in document.Prefabs)
            ReadPrefab(project, prefab);

        // Enforced here rather than defended against everywhere: a project with
        // nowhere to build is not a project, and this is where files become one.
        if (project.Scenes.Count == 0)
            throw new InvalidDataException("A project must contain at least one scene.");

        project.ExpandLoadedInstances();

        return project;
    }

    /// <summary>
    /// Absent fields fall back to the default rather than to zero, so a file that
    /// recorded only a finish still reads back as a matte surface wearing it. An
    /// unknown finish name reads as no facture: a build that does not have a
    /// pattern should show the material flat, not refuse to open the project.
    /// </summary>
    /// <summary>
    /// The colour is needed because of one field: a file written before
    /// transparency was declared says so by its alpha, and reading that back as
    /// the flag is what keeps every model anybody has drawn looking as it did.
    /// </summary>
    private static MaterialAppearance ToAppearance(MaterialAppearanceDocument? document, Color color)
    {
        MaterialAppearance fallback = MaterialAppearance.Default;

        if (document is null)
            return fallback with { Transparent = color.A < 1f };

        return new MaterialAppearance(
            document.Roughness ?? fallback.Roughness,
            document.Metallic ?? fallback.Metallic,
            document.Emission ?? fallback.Emission,
            ToFinish(document.Finish, fallback.Finish),
            document.FinishScale ?? fallback.FinishScale,
            document.FinishRelief ?? fallback.FinishRelief,
            document.Transparent ?? color.A < 1f);
    }

    private static MaterialFinish ToFinish(string? name, MaterialFinish fallback)
    {
        if (string.IsNullOrEmpty(name))
            return fallback;

        return Enum.TryParse(name, ignoreCase: true, out MaterialFinish finish)
            ? finish
            : MaterialFinish.Smooth;
    }

    private static void ReadScene(Project project, SceneDocument document)
    {
        Scene scene = project.AddLoadedScene(
            ParseId(document.Id),
            string.IsNullOrEmpty(document.Name) ? Project.DefaultSceneName : document.Name,
            ToSize(document));

        ReadElements(scene, document);
    }

    private static void ReadPrefab(Project project, SceneDocument document)
    {
        Scene content = project.AddLoadedPrefab(
            ParseId(document.Id),
            string.IsNullOrEmpty(document.Name) ? Project.DefaultPrefabName : document.Name,
            ToSize(document));

        ReadElements(content, document);
    }

    private static void ReadElements(Scene scene, SceneDocument document)
    {
        foreach (ElementDocument element in document.Elements)
            ReadElement(scene, element, scene.Id);

        // After the elements, deliberately, though nothing here checks that the id
        // names one: a choice is restored as it was written, and the scene's own
        // fallback answers over it where it names nothing.
        if (!string.IsNullOrEmpty(document.ActiveCamera))
            scene.RestoreActiveCamera(ParseId(document.ActiveCamera));
    }

    private static Vector3I ToSize(SceneDocument document) =>
        new(document.Size[0], document.Size[1], document.Size[2]);

    private static void ReadElement(Scene scene, ElementDocument document, Guid parentId)
    {
        HierarchyElement element = scene.AddLoadedElement(
            ParseId(document.Id),
            parentId,
            document.Name,
            Parse<ComponentKind>(document.Component),
            document.Source == null ? null : ParseId(document.Source));

        element.ElementTransform.Restore(
            new TransformValues(
                ToVector(document.Position),
                ToVector(document.Rotation),
                ToVector(document.Scale)),
            new TransformModes(
                Parse<FieldMode>(document.PositionMode),
                Parse<FieldMode>(document.RotationMode),
                Parse<FieldMode>(document.ScaleMode)));

        element.ApplyPivot(ReadPivot(document));

        // Absent means shown — files from before hiding existed, and every element
        // nobody has hidden since.
        element.SetVisible(document.Hidden != true);

        if (element.Component is VoxelModel model)
        {
            if (document.Voxels != null)
            {
                foreach (KeyValuePair<Vector3I, Guid> voxel in VoxelBlobCodec.Decode(document.Voxels))
                    model.Apply(voxel.Key, voxel.Value);
            }

            model.ApplySymmetry(ReadSymmetry(document));
        }

        if (element.Component is Camera camera)
            camera.ApplyLens(ReadLens(document.Camera));

        if (element.Component is ReferenceImage image)
            image.ApplySettings(ReadReferenceImage(document.Reference));

        if (element.Component is ReferenceModel reference)
            reference.ApplySettings(ReadReferenceModel(document.Reference));

        foreach (ElementDocument child in document.Children)
            ReadElement(scene, child, element.Id);
    }

    /// <summary>
    /// Absent fields are the defaults, and the picture is deliberately <b>not</b>
    /// measured here: loading is not an edit, a project has to open on a machine
    /// the asset never travelled to, and a read that touched the disk once per
    /// reference would make opening a document depend on what else is mounted.
    /// </summary>
    private static ReferenceImageSettings ReadReferenceImage(ReferenceDocument? document)
    {
        ReferenceImageSettings fallback = ReferenceImageSettings.Default;

        if (document == null)
            return fallback;

        return new ReferenceImageSettings(
            document.Path,
            document.SizeCells is { Length: 2 } size
                ? new Vector2(size[0], size[1])
                : fallback.SizeInCells,
            document.PixelSize is { Length: 2 } pixels
                ? new Vector2I(pixels[0], pixels[1])
                : fallback.PixelSize,
            document.Opacity ?? fallback.Opacity,
            document.Filtered ?? fallback.Filtered,
            document.Lit ?? fallback.LitByScene);
    }

    private static ReferenceModelSettings ReadReferenceModel(ReferenceDocument? document)
    {
        ReferenceModelSettings fallback = ReferenceModelSettings.Default;

        if (document == null)
            return fallback;

        return new ReferenceModelSettings(
            document.Path,
            document.Opacity ?? fallback.Opacity,
            document.Lit ?? fallback.LitByScene,
            document.Wireframe ?? fallback.Wireframe);
    }

    /// <summary>
    /// Absent fields are the defaults — a camera nobody has changed, and every file
    /// from before cameras existed. Clamped by the component on the way in, so a
    /// damaged file cannot put a lens in the document that no form could express.
    /// </summary>
    private static CameraLens ReadLens(CameraDocument? document)
    {
        CameraLens fallback = CameraLens.Default;

        if (document == null)
            return fallback;

        return new CameraLens(
            ToProjection(document.Projection, fallback.Projection),
            document.Fov ?? fallback.FovDegrees,
            document.OrthographicHeight ?? fallback.OrthographicHeight,
            document.Aperture ?? fallback.Aperture,
            document.FocusDistance ?? fallback.FocusDistance);
    }

    /// <summary>
    /// An unreadable name falls back rather than stopping the load, the way an
    /// unreadable finish does: it is a picture setting, not the geometry.
    /// </summary>
    private static RenderProjection ToProjection(string? name, RenderProjection fallback)
    {
        if (string.IsNullOrEmpty(name))
            return fallback;

        return Enum.TryParse(name, ignoreCase: true, out RenderProjection projection)
            ? projection
            : fallback;
    }

    /// <summary>Absent means never set — files from before pivots existed.</summary>
    private static Vector3I? ReadPivot(ElementDocument document)
    {
        if (document.PivotDoubled is not { } doubled)
            return null;

        if (doubled.Length != 3)
            throw new InvalidDataException("pivotDoubled must have three entries.");

        return new Vector3I(doubled[0], doubled[1], doubled[2]);
    }

    /// <summary>
    /// Absent fields are the default slots — files from before symmetry existed —
    /// while fields that are present must be complete: a lone or short array is a
    /// damaged document, not a preference to fall back from.
    /// </summary>
    private static SymmetryValues ReadSymmetry(ElementDocument document)
    {
        if (document.SymmetryPlanes == null && document.SymmetryEnabled == null)
            return default;

        if (document.SymmetryPlanes is not { Length: 3 } planes
            || document.SymmetryEnabled is not { Length: 3 } enabled)
        {
            throw new InvalidDataException(
                "symmetryPlanes and symmetryEnabled must both be present, with three entries each.");
        }

        return new SymmetryValues(
            new SymmetryPlane(enabled[0], planes[0]),
            new SymmetryPlane(enabled[1], planes[1]),
            new SymmetryPlane(enabled[2], planes[2]));
    }

    private static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);

    private static Color ToColor(float[] value) =>
        new(value[0], value[1], value[2], value[3]);

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out Guid id))
            throw new InvalidDataException($"'{value}' is not a valid id.");

        return id;
    }

    private static T Parse<T>(string value) where T : struct, Enum
    {
        if (!Enum.TryParse(value, out T parsed))
            throw new InvalidDataException($"'{value}' is not a valid {typeof(T).Name}.");

        return parsed;
    }
}
