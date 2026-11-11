using System;
using System.Collections.Generic;
using System.IO;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The reference surface: naming a file, changing which one it is and how it is
/// drawn, and reading back whether the file is actually there. Moving one is
/// deliberately not here — a reference is an element, so 'set-transform' does it.
/// </summary>
public sealed class ReferenceCommandTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ICommandDispatcher _dispatcher;
    private readonly IScene _scene;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-{Guid.NewGuid():N}");

    public ReferenceCommandTests()
    {
        _scene = _project.Scenes[0];
        _dispatcher = CommandFactory.CreateDispatcher(new CommandContext(_project));

        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AnImportedPictureIsNamedAfterItsFileAndSizedByItsPixels()
    {
        _dispatcher.RunOk($"import-reference path={Picture("hull-side.png", 64, 32)}");

        Assert.Equal("hull-side", _scene.Children[0].Name);
        Assert.Equal(ComponentKind.ReferenceImage, _scene.Children[0].ComponentKind);

        Dictionary<string, object?> reference = Reference("hull-side");

        Assert.Equal(new[] { 64f, 32f }, reference["sizeCells"]);
        Assert.Equal(new[] { 1f, 1f }, reference["pixelsPerCell"]);
        Assert.True((bool)reference["found"]!);
    }

    /// <summary>
    /// The first thing a reader needs, and the reason the report costs a look at
    /// the disk: a size beside a path nobody can open is how "the reference is
    /// blank" becomes an hour of looking in the wrong place.
    /// </summary>
    [Fact]
    public void AMissingFileSaysSoRatherThanBeingRefused()
    {
        _dispatcher.RunOk($"import-reference path={Path.Combine(_directory, "absent.png")}");

        Assert.False((bool)Reference("absent")["found"]!);

        CommandResult listed = _dispatcher.RunOk("references");

        Assert.Equal(1, listed.Field("missing"));
    }

    /// <summary>
    /// A reference with no file would stand in the scene as a placeholder until
    /// somebody found the second command, so the import is the only way in.
    /// </summary>
    [Fact]
    public void APathlessReferenceCannotBeCreated()
    {
        string error = _dispatcher.RunFails("create-element name=Plan kind=ReferenceImage");

        Assert.Contains("import-reference", error);
        Assert.Empty(_scene.Children);
    }

    [Fact]
    public void AFileNothingCouldDrawIsRefusedByName()
    {
        string error = _dispatcher.RunFails($"import-reference path={Touch("notes.txt")}");

        Assert.Contains(".png", error);
        Assert.Empty(_scene.Children);
    }

    /// <summary>
    /// Named rather than whole, so one dial can be turned without restating the
    /// others — and so a call cannot silently undo what somebody else set.
    /// </summary>
    [Fact]
    public void SettingOneValueLeavesTheOthersWhereTheyWere()
    {
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 40, 20)} name=Plan");
        _dispatcher.RunOk("set-reference reference=Plan opacity=0.4 filtered=true");
        _dispatcher.RunOk("set-reference reference=Plan lit=true");

        Dictionary<string, object?> reference = Reference("Plan");

        Assert.Equal(0.4f, reference["opacity"]);
        Assert.Equal(true, reference["filtered"]);
        Assert.Equal(true, reference["lit"]);
        Assert.Equal(new[] { 40f, 20f }, reference["sizeCells"]);
    }

    /// <summary>
    /// One number is a width with the height following the picture's own
    /// proportions, which is what "fit this drawing into thirty-two cells" means.
    /// </summary>
    [Fact]
    public void OneNumberIsAWidthAndTheAspectFollows()
    {
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 200, 100)} name=Plan");
        _dispatcher.RunOk("set-reference reference=Plan size=32");

        Assert.Equal(new[] { 32f, 16f }, Reference("Plan")["sizeCells"]);

        _dispatcher.RunOk("set-reference reference=Plan size=10,3");

        Assert.Equal(new[] { 10f, 3f }, Reference("Plan")["sizeCells"]);
    }

    [Fact]
    public void PixelsPerCellIsTheOtherWayOfSayingTheSameSize()
    {
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 64, 32)} name=Plan");
        _dispatcher.RunOk("set-reference reference=Plan pixels-per-cell=4");

        Assert.Equal(new[] { 16f, 8f }, Reference("Plan")["sizeCells"]);

        _dispatcher.RunOk("set-reference reference=Plan pixels-per-cell=1");

        Assert.Equal(new[] { 64f, 32f }, Reference("Plan")["sizeCells"]);
    }

    /// <summary>
    /// There is nothing to divide, so the answer would be a guess. Saying so beats
    /// a plane that silently kept the size it had under a command that claimed to
    /// change it.
    /// </summary>
    [Fact]
    public void PixelsPerCellNeedsAPictureThatCouldBeMeasured()
    {
        _dispatcher.RunOk($"import-reference path={Path.Combine(_directory, "absent.png")} name=Plan");

        string error = _dispatcher.RunFails("set-reference reference=Plan pixels-per-cell=1");

        Assert.Contains("no picture to measure", error);
    }

    /// <summary>
    /// The path arrives before the size, so re-pointing and resizing in one call
    /// measures against the picture that is arriving rather than the one being left.
    /// </summary>
    [Fact]
    public void RePointingAndResizingInOneCallMeasuresTheNewPicture()
    {
        _dispatcher.RunOk($"import-reference path={Picture("small.png", 8, 8)} name=Plan");
        _dispatcher.RunOk(
            $"set-reference reference=Plan path={Picture("large.png", 300, 150)} size=30");

        Dictionary<string, object?> reference = Reference("Plan");

        Assert.Equal(new[] { 300, 150 }, reference["pixels"]);
        Assert.Equal(new[] { 30f, 15f }, reference["sizeCells"]);
    }

    /// <summary>
    /// Leaving the size alone would draw the new picture stretched, and a reference
    /// that lies about the shape of the thing it stands for is worse than one that
    /// moved. Same proportions, and nothing moves at all.
    /// </summary>
    [Fact]
    public void RePointingWithNothingSaidAboutSizeKeepsTheWidthAndFollowsTheAspect()
    {
        _dispatcher.RunOk($"import-reference path={Picture("square.png", 100, 100)} name=Plan");
        _dispatcher.RunOk("set-reference reference=Plan size=20");
        _dispatcher.RunOk($"set-reference reference=Plan path={Picture("wide.png", 400, 100)}");

        Assert.Equal(new[] { 20f, 5f }, Reference("Plan")["sizeCells"]);

        _dispatcher.RunOk($"set-reference reference=Plan path={Picture("wider.png", 800, 200)}");

        Assert.Equal(new[] { 20f, 5f }, Reference("Plan")["sizeCells"]);
    }

    /// <summary>
    /// A component is fixed for an element's lifetime, so obeying this quietly is
    /// impossible — and a picture that became a model would take its size, its
    /// filtering and everything it was placed for with it.
    /// </summary>
    [Fact]
    public void APictureCannotBecomeAModel()
    {
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 8, 8)} name=Plan");

        string error = _dispatcher.RunFails($"set-reference reference=Plan path={Touch("bust.glb")}");

        Assert.Contains("ReferenceImage", error);
        Assert.Contains("own reference", error);
    }

    /// <summary>
    /// The dispatcher's own "an argument nobody knows does nothing and says
    /// nothing", one level in: the caller meant something by naming it.
    /// </summary>
    [Fact]
    public void AnArgumentForTheOtherKindIsRefusedRatherThanIgnored()
    {
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 8, 8)} name=Plan");
        _dispatcher.RunOk($"import-reference path={Touch("bust.glb")} name=Bust");

        Assert.Contains("models", _dispatcher.RunFails("set-reference reference=Plan wireframe=true"));
        Assert.Contains("pictures", _dispatcher.RunFails("set-reference reference=Bust size=4"));
        Assert.Contains(
            "pictures",
            _dispatcher.RunFails("set-reference reference=Bust filtered=true"));
    }

    [Fact]
    public void AModelWearsItsOwnTwoSwitches()
    {
        _dispatcher.RunOk($"import-reference path={Touch("bust.glb")} name=Bust");
        _dispatcher.RunOk("set-reference reference=Bust wireframe=true opacity=0.5 lit=true");

        Dictionary<string, object?> reference = Reference("Bust");

        Assert.Equal(true, reference["wireframe"]);
        Assert.Equal(0.5f, reference["opacity"]);
        Assert.Equal(true, reference["lit"]);
    }

    [Fact]
    public void SomethingThatIsNotAReferenceIsRefusedByName()
    {
        _dispatcher.RunOk("create-element name=Hull kind=VoxelModel");

        string error = _dispatcher.RunFails("set-reference reference=Hull opacity=0.5");

        Assert.Contains("not a reference", error);
        Assert.Contains("import-reference", error);
    }

    /// <summary>
    /// The listing is the tree's own order and reports both kinds, since what a
    /// reader wants is "everything standing in this scene that I did not build".
    /// </summary>
    [Fact]
    public void TheListingHoldsBothKindsInTreeOrder()
    {
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 8, 8)} name=Plan");
        _dispatcher.RunOk("create-element name=Group");
        _dispatcher.RunOk($"import-reference path={Touch("bust.glb")} name=Bust parent=Group");

        var listed = (List<object?>)_dispatcher.RunOk("references").Field("references")!;

        Assert.Equal(2, listed.Count);
        Assert.Equal("Plan", Name(listed[0]));
        Assert.Equal("Bust", Name(listed[1]));
    }

    /// <summary>
    /// An element report and a reference report describe the same file, so they go
    /// through one function — the arrangement a camera's lens already has.
    /// </summary>
    [Fact]
    public void TheElementReportCarriesTheSameDescription()
    {
        string id = _dispatcher
            .RunOk($"import-reference path={Picture("plan.png", 8, 8)} name=Plan")
            .Id();

        CommandResult element = _dispatcher.RunOk($"element element={id}");

        Assert.Equal(Reference("Plan"), element.Field("reference"));
    }

    [Fact]
    public void AParentPutsTheReferenceUnderIt()
    {
        _dispatcher.RunOk("create-element name=Blueprints");
        _dispatcher.RunOk($"import-reference path={Picture("plan.png", 8, 8)} parent=Blueprints");

        Assert.Equal("Blueprints", _scene.Children[0].Name);
        Assert.Equal("plan", _scene.Children[0].Children[0].Name);
    }

    private static string Name(object? described) =>
        (string)((Dictionary<string, object?>)described!)["name"]!;

    private Dictionary<string, object?> Reference(string name) =>
        (Dictionary<string, object?>)_dispatcher
            .RunOk($"element element={name}")
            .Field("reference")!;

    private string Picture(string name, int width, int height)
    {
        string path = Path.Combine(_directory, name);
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

        return path;
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });

        return path;
    }
}
