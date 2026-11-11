using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A reference is an element that stands for somebody else's file: the document
/// owns where it stands and how it is drawn, the file belongs to the disk.
/// </summary>
public sealed class ReferenceTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-{Guid.NewGuid():N}");

    public ReferenceTests()
    {
        _scene = _project.Scenes[0];
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void TheKindFollowsTheExtension()
    {
        Assert.Equal(
            ComponentKind.ReferenceImage,
            _scene.Element(Import(Picture("plan.png", 64, 32))).ComponentKind);

        Assert.Equal(
            ComponentKind.ReferenceModel,
            _scene.Element(Import(Touch("bust.glb"))).ComponentKind);
    }

    /// <summary>
    /// The list is the set the viewport can go on to draw, so a format nothing
    /// could display is refused at the import rather than arriving as an empty
    /// square nobody can explain.
    /// </summary>
    [Fact]
    public void AFileNothingCouldDrawIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Import(Touch("notes.txt")));

        Assert.Contains(".png", error.Message);
        Assert.Contains(".glb", error.Message);
        Assert.Empty(_scene.Children);
        Assert.Empty(_project.History.Records);
    }

    /// <summary>
    /// One pixel on one voxel is what a reference is for, so it is where a picture
    /// starts rather than a button somebody has to find.
    /// </summary>
    [Fact]
    public void APictureArrivesOnePixelToOneVoxel()
    {
        ReferenceImageSettings settings = Image(Import(Picture("sprite.png", 48, 24)));

        Assert.Equal(new Vector2I(48, 24), settings.PixelSize);
        Assert.Equal(new Vector2(48f, 24f), settings.SizeInCells);
        Assert.Equal(new Vector2(1f, 1f), settings.PixelsPerCell);
    }

    /// <summary>
    /// A picture that cannot be measured still arrives: a reference may name a file
    /// that is not there, and refusing the import would leave nothing to re-point.
    /// </summary>
    [Fact]
    public void APictureThatIsNotThereStillArrives()
    {
        ReferenceImageSettings settings =
            Image(Import(Path.Combine(_directory, "absent.png")));

        Assert.Equal(Vector2I.Zero, settings.PixelSize);
        Assert.Equal(ReferenceImageSettings.Default.SizeInCells, settings.SizeInCells);
        Assert.Equal(Vector2.Zero, settings.PixelsPerCell);
    }

    /// <summary>
    /// The grip belongs on the picture rather than at its bottom corner: a
    /// quarter-turn about a corner would swing the whole drawing out of the room.
    /// Reported as an offset from the plane's own middle, so a fresh one reads
    /// nought rather than half its size.
    /// </summary>
    [Fact]
    public void APictureIsGrippedInItsMiddle()
    {
        Guid id = Import(Picture("plan.png", 64, 32));
        IHierarchyElement element = _scene.Element(id);

        Assert.Equal(new Vector3(32f, 16f, 0f), element.PivotPoint);
        Assert.Equal(Vector3.Zero, PivotUnits.ToOffset(element.PivotPoint, element));
    }

    /// <summary>
    /// Quantisation acts on the stored origin, so the corner of the picture stays
    /// on the cell lattice however the pivot is placed — which is the whole of what
    /// makes one pixel land on one voxel.
    /// </summary>
    [Fact]
    public void ThePictureCornerStaysOnTheLatticeWhateverThePivotIs()
    {
        Guid id = Import(Picture("odd.png", 63, 31));

        _scene.SetTransform(id, TransformChannel.Position, new Vector3(4.4f, 8.7f, 2.2f));

        Vector3 corner = _scene.Element(id).WorldTransform.Origin;

        Assert.Equal(corner, corner.Round());
    }

    /// <summary>
    /// A picture is hung on a wall, so whole cells and right angles; a model is
    /// stood beside the work by eye, and Integer scale could not express the 0.4 a
    /// two-metre character needs to become sixteen cells.
    /// </summary>
    [Fact]
    public void TheFieldModesFollowWhatEachKindIsFor()
    {
        TransformModes picture = _scene.Element(Import(Picture("plan.png", 8, 8))).TransformModes;

        Assert.Equal(FieldMode.Integer, picture.Position);
        Assert.Equal(FieldMode.Snap90, picture.Rotation);
        Assert.Equal(FieldMode.Float, picture.Scale);

        TransformModes model = _scene.Element(Import(Touch("bust.glb"))).TransformModes;

        Assert.Equal(FieldMode.Float, model.Position);
        Assert.Equal(FieldMode.Float, model.Rotation);
        Assert.Equal(FieldMode.Float, model.Scale);
    }

    /// <summary>
    /// An import is one thing the user did. Two records would also mean a reference
    /// pointing at nothing standing in the scene between them.
    /// </summary>
    [Fact]
    public void AnImportIsOneRecord()
    {
        Import(Picture("plan.png", 16, 16));

        Assert.Single(_project.History.Records);
        Assert.Equal(ActionKind.CreateElement, _project.History.Records[0].Kind);

        _project.History.Undo();

        Assert.Empty(_scene.Children);
    }

    [Fact]
    public void AnEditIsOneRecordAndUndoPutsItBack()
    {
        Guid id = Import(Picture("plan.png", 16, 16));
        ReferenceImageSettings before = Image(id);

        _scene.SetReferenceImage(id, before with { Opacity = 0.25f, Filtered = true });

        Assert.Equal(2, _project.History.Records.Count);
        Assert.Equal(ActionKind.ChangeReference, _project.History.Records[1].Kind);
        Assert.Equal(0.25f, Image(id).Opacity);

        _project.History.Undo();

        Assert.Equal(before, Image(id));
    }

    [Fact]
    public void SettingsEqualToTheCurrentOnesRecordNothing()
    {
        Guid id = Import(Picture("plan.png", 16, 16));

        _scene.SetReferenceImage(id, Image(id));

        Guid modelId = Import(Touch("bust.glb"));

        _scene.SetReferenceModel(modelId, Model(modelId));

        Assert.Equal(2, _project.History.Records.Count);
    }

    /// <summary>
    /// The model's standing bargain with every settable value: out of range is
    /// clamped rather than refused, so a form and a command cannot raise an
    /// exception between them.
    /// </summary>
    [Fact]
    public void OutOfRangeIsClampedRatherThanRefused()
    {
        Guid id = Import(Picture("plan.png", 16, 16));

        _scene.SetReferenceImage(id, Image(id) with
        {
            Opacity = 4f,
            SizeInCells = new Vector2(-3f, 1e9f)
        });

        Assert.Equal(ReferenceLimits.MaximumOpacity, Image(id).Opacity);
        Assert.Equal(ReferenceLimits.MinimumSizeInCells, Image(id).SizeInCells.X);
        Assert.Equal(ReferenceLimits.MaximumSizeInCells, Image(id).SizeInCells.Y);
    }

    /// <summary>
    /// The document may not carry a resolution that contradicts the file it names,
    /// so a caller cannot store one picture's resolution against another picture's
    /// path — and the size it asked for is left exactly as it asked, because how
    /// big the plane is is a decision rather than a fact about the file.
    /// </summary>
    [Fact]
    public void RePointingAtAnotherPictureMeasuresItAndRespectsTheSizeAsked()
    {
        Guid id = Import(Picture("small.png", 8, 8));

        _scene.SetReferenceImage(id, Image(id) with
        {
            Path = Picture("large.png", 200, 100),
            PixelSize = new Vector2I(1, 1),
            SizeInCells = new Vector2(20f, 10f)
        });

        Assert.Equal(new Vector2I(200, 100), Image(id).PixelSize);
        Assert.Equal(new Vector2(20f, 10f), Image(id).SizeInCells);
    }

    /// <summary>
    /// Re-pointing at a picture that is not there keeps the last resolution
    /// standing: a stale number beside a missing-file mark says more than a nought.
    /// </summary>
    [Fact]
    public void RePointingAtNothingKeepsTheLastKnownResolution()
    {
        Guid id = Import(Picture("small.png", 8, 8));

        _scene.SetReferenceImage(id, Image(id) with
        {
            Path = Path.Combine(_directory, "absent.png")
        });

        Assert.Equal(new Vector2I(8, 8), Image(id).PixelSize);
    }

    [Fact]
    public void EverythingAboutAReferenceSurvivesTheFile()
    {
        Guid id = Import(Picture("plan.png", 40, 20));

        _scene.SetReferenceImage(id, Image(id) with
        {
            Opacity = 0.4f,
            Filtered = true,
            LitByScene = true,
            SizeInCells = new Vector2(10f, 5f)
        });

        Guid modelId = Import(Touch("bust.glb"));

        _scene.SetReferenceModel(modelId, Model(modelId) with
        {
            Opacity = 0.75f,
            Wireframe = true
        });

        IScene reopened = SaveAndLoad().Scenes[0];

        Assert.Equal(Image(id), Image(reopened, id));
        Assert.Equal(Model(modelId), Model(reopened, modelId));
    }

    /// <summary>
    /// A project and its references move together, which is the whole reason the
    /// path is stored relative: an absolute one in the file would name a directory
    /// that exists on exactly one machine.
    /// </summary>
    [Fact]
    public void AnAssetUnderTheProjectIsStoredRelative()
    {
        string picture = Picture("plan.png", 8, 8);

        Import(picture);

        string path = Path.Combine(_directory, "part.vcad");

        _project.Save(path);

        string yaml = File.ReadAllText(path);

        Assert.DoesNotContain(_directory, yaml);
        Assert.Contains("plan.png", yaml);

        Assert.Equal(
            picture,
            Image(ModelFactory.LoadProject(path).Scenes[0], FirstElement()).Path);
    }

    /// <summary>
    /// An asset outside the project's own folder cannot be named relatively without
    /// climbing out of it, so it is stored as it is and says so by staying rooted.
    /// </summary>
    [Fact]
    public void AnAssetOutsideTheProjectStaysAbsolute()
    {
        string outside = Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.png");

        WritePicture(outside, 8, 8);

        try
        {
            Import(outside);

            string path = Path.Combine(_directory, "part.vcad");

            _project.Save(path);

            Assert.Contains(outside, File.ReadAllText(path));

            IProject reopened = ModelFactory.LoadProject(path);

            Assert.Equal(outside, Image(reopened.Scenes[0], FirstElement()).Path);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    /// <summary>
    /// Loading is not an edit, and a project has to open on a machine the asset
    /// never travelled to — so a read that measured its pictures would make opening
    /// a document depend on what else happens to be mounted.
    /// </summary>
    [Fact]
    public void ReadingADocumentDoesNotMeasureAnything()
    {
        string picture = Picture("plan.png", 40, 20);
        Guid id = Import(picture);

        _scene.SetReferenceImage(id, Image(id) with { SizeInCells = new Vector2(4f, 2f) });

        string path = Path.Combine(_directory, "part.vcad");

        _project.Save(path);
        File.Delete(picture);

        ReferenceImageSettings settings = Image(ModelFactory.LoadProject(path).Scenes[0], id);

        Assert.Equal(new Vector2(4f, 2f), settings.SizeInCells);
        Assert.Equal(new Vector2I(40, 20), settings.PixelSize);
    }

    /// <summary>
    /// A clip refers to nothing, and a reference's path is exactly that: text. So
    /// copying one into another document needs no resolution of any kind.
    /// </summary>
    [Fact]
    public void ACopyCarriesThePathAndTheSettings()
    {
        Guid id = Import(Picture("plan.png", 40, 20));

        _scene.SetReferenceImage(id, Image(id) with { Opacity = 0.3f, Filtered = true });

        Guid copy = _scene.PasteElement(_scene.Id, _scene.CaptureElement(id));

        Assert.Equal(Image(id), Image(copy));
        Assert.NotEqual(id, copy);
    }

    /// <summary>
    /// A prefab is a scene, and a reference is an element, so one inside the other
    /// needs nothing of its own — the path is text and travels as text, which is the
    /// whole of what linking rather than embedding gives away for free.
    /// </summary>
    [Fact]
    public void AReferenceSurvivesBeingMadeIntoAPrefabAndInstantiated()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Blueprints");
        string picture = Picture("plan.png", 24, 12);

        _scene.CreateReference(group, "Plan", picture);

        Guid prefab = _scene.CreatePrefab(group);
        Guid instance = _scene.CreateInstance(_scene.Id, prefab);

        // The transfer puts the element itself in the library, so the instance's
        // own child is the group and the reference is under that.
        IHierarchyElement copied = _scene.Element(instance).Children[0].Children[0];

        Assert.Equal("Plan", copied.Name);

        Assert.Equal(ComponentKind.ReferenceImage, copied.ComponentKind);
        Assert.Equal(picture, ((IReferenceImage)copied.Component!).Settings.Path);
        Assert.Equal(new Vector2(24f, 12f), ((IReferenceImage)copied.Component!).Settings.SizeInCells);

        // Derived content, so every edit is refused — a reference in a prefab is
        // changed where the prefab is, like everything else under an instance.
        Assert.NotNull(copied.PrefabInstanceId);
        Assert.Throws<InvalidOperationException>(
            () => _scene.SetReferenceImage(copied.Id, ReferenceImageSettings.Default));
    }

    /// <summary>
    /// Nothing that walks the tree for cells may find a reference: it holds no
    /// geometry, so the mesher, the exporter and the render's own compile pass over
    /// it without being told to.
    /// </summary>
    [Fact]
    public void AReferenceHoldsNoVoxelModel()
    {
        Assert.Null(_scene.Element(Import(Picture("plan.png", 8, 8))).Component as IVoxelModel);
        Assert.Null(_scene.Element(Import(Touch("bust.glb"))).Component as IVoxelModel);
    }

    [Fact]
    public void TheWrongKindOfElementIsRefusedByName()
    {
        Guid picture = Import(Picture("plan.png", 8, 8));
        Guid model = Import(Touch("bust.glb"));

        Assert.Throws<InvalidOperationException>(
            () => _scene.SetReferenceModel(picture, ReferenceModelSettings.Default));

        Assert.Throws<InvalidOperationException>(
            () => _scene.SetReferenceImage(model, ReferenceImageSettings.Default));
    }

    /// <summary>
    /// In memory a path is absolute or it is nothing. The storage layer resolves a
    /// relative one against whatever file it reads it back from, so a relative path
    /// written into a recovery snapshot would come back pointing under the
    /// snapshot's own directory — and then into the user's file, absolute and wrong.
    /// </summary>
    [Fact]
    public void ARelativeReferencePathIsRefusedByName()
    {
        Guid picture = Import(Picture("plan.png", 8, 8));

        Assert.Throws<ArgumentException>(
            () => _scene.CreateReference(_scene.Id, "Relative", "pictures/plan.png"));
        Assert.Throws<ArgumentException>(
            () => _scene.SetReferenceImage(picture, Image(picture) with { Path = "elsewhere/plan.png" }));
        Assert.Throws<ArgumentException>(
            () => _scene.SetReferenceModel(
                Import(Touch("bust.glb")), ReferenceModelSettings.Default with { Path = "bust.glb" }));
    }

    private Guid Import(string path) => _scene.CreateReference(_scene.Id, "Reference", path);

    private ReferenceImageSettings Image(Guid id) => Image(_scene, id);

    private static ReferenceImageSettings Image(IScene scene, Guid id) =>
        ((IReferenceImage)scene.Element(id).Component!).Settings;

    private ReferenceModelSettings Model(Guid id) => Model(_scene, id);

    private static ReferenceModelSettings Model(IScene scene, Guid id) =>
        ((IReferenceModel)scene.Element(id).Component!).Settings;

    private Guid FirstElement() => _scene.Children[0].Id;

    private IProject SaveAndLoad()
    {
        string path = Path.Combine(_directory, "roundtrip.vcad");

        _project.Save(path);

        return ModelFactory.LoadProject(path);
    }

    private string Picture(string name, int width, int height)
    {
        string path = Path.Combine(_directory, name);

        WritePicture(path, width, height);

        return path;
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });

        return path;
    }

    private static void WritePicture(string path, int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        bytes.AddRange(new byte[] { 0, 0, 0, 13 });
        bytes.AddRange(new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        bytes.AddRange(new[]
        {
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width
        });
        bytes.AddRange(new[]
        {
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height
        });
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });

        File.WriteAllBytes(path, bytes.ToArray());
    }
}
