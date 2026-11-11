using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>Turns a live project into documents. Knows nothing about files.</summary>
/// <remarks>
/// The walk and the encoding are two steps on purpose. <see cref="Capture"/>
/// copies what the model holds — the tree, the transforms, the cells as
/// dictionaries — and hands back a snapshot that owes nothing to the model;
/// <see cref="ProjectSnapshot.ToDocument"/> then compresses the blobs, which is
/// where almost all of a save's time goes (measured: about 110 of 130 ms on a
/// model of 170,000 cells). A caller that needs the file without stalling captures
/// on the thread that owns the model and writes on another.
/// </remarks>
internal static class ProjectWriter
{
    /// <summary>
    /// Bumped to 2 when scenes gained names, to 3 when the project gained a prefab
    /// library, and to 5 when a scene could hold cameras. Older documents still
    /// load — see <see cref="ProjectReader"/> — because each addition takes nothing
    /// away.
    /// </summary>
    public const int CurrentVersion = 5;

    /// <summary>The oldest schema this build can still read.</summary>
    public const int OldestReadableVersion = 1;

    /// <summary>The document with every blob encoded — a capture, finished at once.</summary>
    public static ProjectDocument Write(IProject project) => Capture(project).ToDocument();

    /// <summary>
    /// Everything the file will say, copied out of the model and not yet encoded.
    /// Cheap enough to take on the frame that asks for it.
    /// </summary>
    public static ProjectSnapshot Capture(IProject project)
    {
        var pendingList = new List<ProjectSnapshot.PendingBlobs>();
        var document = new ProjectDocument { Version = CurrentVersion };

        foreach (IMaterial material in project.Materials)
        {
            document.Materials.Add(new MaterialDocument
            {
                Id = material.Id.ToString("D"),
                Name = material.Name,
                Color = new[] { material.Color.R, material.Color.G, material.Color.B, material.Color.A },
                Appearance = WriteAppearance(material.Appearance, material.Color)
            });
        }

        foreach (IScene scene in project.Scenes)
            document.Scenes.Add(WriteScene(scene, pendingList));

        foreach (IPrefab prefab in project.Prefabs)
            document.Prefabs.Add(WriteScene(prefab.Content, pendingList));

        return new ProjectSnapshot(document, pendingList);
    }

    /// <summary>
    /// Null for a material still at the defaults, and per field beyond that: a
    /// file should say what was chosen and stay quiet about what was not.
    /// </summary>
    /// <remarks>
    /// Quiet has one exception, and it is the whole reason the colour is passed
    /// in. A file with nothing to say about transparency is read as the alpha
    /// declaring it, which is what carries every document written before the
    /// flag existed — so a material that is <em>opaque with an alpha below one</em>
    /// is exactly the thing silence cannot express. Written as it stands there,
    /// or a colour set to half and left opaque comes back see-through on the next
    /// open, and the seam rule then drops faces the model was meant to keep.
    /// </remarks>
    private static MaterialAppearanceDocument? WriteAppearance(
        MaterialAppearance appearance,
        Color color)
    {
        MaterialAppearance fallback = MaterialAppearance.Default;

        // What a reader makes of a material that says nothing — ProjectReader's
        // own rule, restated here because the two have to agree exactly.
        bool deduced = color.A < 1f;

        if (appearance == fallback && appearance.Transparent == deduced)
            return null;

        return new MaterialAppearanceDocument
        {
            // Written whenever silence would say the other thing, never merely
            // when it is true.
            Transparent = appearance.Transparent == deduced ? null : appearance.Transparent,
            Roughness = Written(appearance.Roughness, fallback.Roughness),
            Metallic = Written(appearance.Metallic, fallback.Metallic),
            Emission = Written(appearance.Emission, fallback.Emission),
            Finish = appearance.Finish == fallback.Finish ? null : appearance.Finish.ToString(),
            FinishScale = Written(appearance.FinishScale, fallback.FinishScale),
            FinishRelief = Written(appearance.FinishRelief, fallback.FinishRelief)
        };
    }

    private static float? Written(float value, float fallback) =>
        value == fallback ? null : value;

    private static SceneDocument WriteScene(
        IScene scene,
        List<ProjectSnapshot.PendingBlobs> pendingList)
    {
        var document = new SceneDocument
        {
            Id = scene.Id.ToString("D"),
            Name = scene.Name,
            Size = new[] { scene.Size.X, scene.Size.Y, scene.Size.Z },
            ActiveCamera = WriteActiveCamera(scene)
        };

        foreach (IHierarchyElement child in scene.Children)
            document.Elements.Add(WriteElement(child, pendingList));

        return document;
    }

    /// <summary>
    /// The choice somebody made, not the camera that happens to be standing in for
    /// it: saving the resolved answer would turn a fallback into a decision.
    /// </summary>
    private static string? WriteActiveCamera(IScene scene)
    {
        Guid chosen = scene is Scene concrete ? concrete.ChosenCameraId : Guid.Empty;

        return chosen == Guid.Empty ? null : chosen.ToString("D");
    }

    /// <summary>
    /// Null for a camera still at every default, and per field beyond that.
    /// </summary>
    private static CameraDocument? WriteCamera(CameraLens lens)
    {
        CameraLens fallback = CameraLens.Default;

        if (lens == fallback)
            return null;

        return new CameraDocument
        {
            Projection = lens.Projection == fallback.Projection
                ? null
                : lens.Projection.ToString(),
            Fov = Written(lens.FovDegrees, fallback.FovDegrees),
            OrthographicHeight = Written(lens.OrthographicHeight, fallback.OrthographicHeight),
            Aperture = Written(lens.Aperture, fallback.Aperture),
            FocusDistance = Written(lens.FocusDistance, fallback.FocusDistance)
        };
    }

    /// <summary>
    /// The path is always written — it is the whole of what a reference is, and a
    /// reference with no path is one nobody could put back. Everything else is
    /// written only where it differs from the default.
    /// </summary>
    private static ReferenceDocument WriteReferenceImage(ReferenceImageSettings settings)
    {
        ReferenceImageSettings fallback = ReferenceImageSettings.Default;

        return new ReferenceDocument
        {
            Path = settings.Path,
            // The size is the plane's only geometry, so it is written whatever it
            // is: a default that happened to match would come back as a default
            // rather than as the size somebody set.
            SizeCells = new[] { settings.SizeInCells.X, settings.SizeInCells.Y },
            PixelSize = settings.PixelSize == Vector2I.Zero
                ? null
                : new[] { settings.PixelSize.X, settings.PixelSize.Y },
            Opacity = Written(settings.Opacity, fallback.Opacity),
            Filtered = settings.Filtered == fallback.Filtered ? null : settings.Filtered,
            Lit = settings.LitByScene == fallback.LitByScene ? null : settings.LitByScene
        };
    }

    private static ReferenceDocument WriteReferenceModel(ReferenceModelSettings settings)
    {
        ReferenceModelSettings fallback = ReferenceModelSettings.Default;

        return new ReferenceDocument
        {
            Path = settings.Path,
            Opacity = Written(settings.Opacity, fallback.Opacity),
            Lit = settings.LitByScene == fallback.LitByScene ? null : settings.LitByScene,
            Wireframe = settings.Wireframe == fallback.Wireframe ? null : settings.Wireframe
        };
    }

    private static ElementDocument WriteElement(
        IHierarchyElement element,
        List<ProjectSnapshot.PendingBlobs> pendingList)
    {
        // The stored form — the local origin — not the public pivot form. A file
        // that carried the pivot's location would re-derive the origin on load
        // through float arithmetic; the origin itself round-trips exactly, and the
        // pivot travels beside it as the lattice point it is.
        var stored = (HierarchyElement)element;
        TransformValues values = stored.ElementTransform.Values;
        TransformModes modes = element.TransformModes;

        var document = new ElementDocument
        {
            Id = element.Id.ToString("D"),
            Name = element.Name,
            Component = element.ComponentKind.ToString(),
            Position = ToArray(values.Position),
            Rotation = ToArray(values.RotationDegrees),
            Scale = ToArray(values.Scale),
            PositionMode = modes.Position.ToString(),
            RotationMode = modes.Rotation.ToString(),
            ScaleMode = modes.Scale.ToString(),
            Hidden = element.Visible ? null : true
        };

        if (stored.PivotDoubled is { } pivotDoubled)
            document.PivotDoubled = new[] { pivotDoubled.X, pivotDoubled.Y, pivotDoubled.Z };

        if (element.Component is IVoxelModel model)
        {
            // Copied rather than encoded: the copy is the whole of what makes the
            // snapshot independent of the model, and the encoding is the whole of
            // what makes a save slow.
            if (model.Voxels.Count > 0)
            {
                pendingList.Add(new ProjectSnapshot.PendingBlobs(
                    document,
                    new Dictionary<Vector3I, Guid>(model.Voxels)));
            }

            if (model.Symmetry != default)
            {
                SymmetryValues symmetry = model.Symmetry;

                document.SymmetryPlanes = new[]
                {
                    symmetry.X.PlaneDoubled, symmetry.Y.PlaneDoubled, symmetry.Z.PlaneDoubled
                };
                document.SymmetryEnabled = new[]
                {
                    symmetry.X.Enabled, symmetry.Y.Enabled, symmetry.Z.Enabled
                };
            }
        }

        if (element.Component is ICamera camera)
            document.Camera = WriteCamera(camera.Lens);

        if (element.Component is IReferenceImage image)
            document.Reference = WriteReferenceImage(image.Settings);

        if (element.Component is IReferenceModel reference)
            document.Reference = WriteReferenceModel(reference.Settings);

        // An instance is written as a reference and nothing else: what hangs under
        // it is derived from the prefab it names, and a file that carried a copy of
        // it would be a file that could disagree with its own library.
        if (element.Component is IPrefabInstance instance)
        {
            document.Source = instance.SourceId.ToString("D");

            return document;
        }

        foreach (IHierarchyElement child in element.Children)
            document.Children.Add(WriteElement(child, pendingList));

        return document;
    }

    private static float[] ToArray(Vector3 value) => new[] { value.X, value.Y, value.Z };
}
