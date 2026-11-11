using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The command surface as a caller experiences it — including a caller that gets
/// things wrong, since a refused command must come back as a readable failure
/// rather than an exception.
/// </summary>
public sealed class CommandDispatcherTests : IDisposable
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public CommandDispatcherTests()
    {
        _dispatcher.RunOk("new");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void EveryCommandIsNamedAndDocumented()
    {
        Assert.NotEmpty(_dispatcher.Commands);
        Assert.All(_dispatcher.Commands, command =>
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name));
            Assert.EndsWith(".", command.Summary);
            Assert.All(command.Parameters, parameter =>
                Assert.False(string.IsNullOrWhiteSpace(parameter.Summary)));
        });

        Assert.Equal(
            _dispatcher.Commands.Count,
            _dispatcher.Commands.Select(command => command.Name).Distinct().Count());
    }

    [Fact]
    public void AnUnknownCommandIsRefusedNotThrown()
    {
        Assert.Contains("Unknown command", _dispatcher.RunFails("polish-the-bracket"));
    }

    [Fact]
    public void WorkingWithoutAProjectIsRefused()
    {
        ICommandDispatcher fresh = CommandFactory.CreateDispatcher(new CommandContext());

        Assert.Contains("No project is open", fresh.RunFails("create-element name=Group"));
    }

    [Fact]
    public void ElementsCanBeAddressedByName()
    {
        _dispatcher.RunOk("create-element name=Assembly");
        _dispatcher.RunOk("create-element name=Bracket kind=VoxelModel parent=Assembly");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;
        var elements = (List<object?>)tree["elements"]!;
        var assembly = (IReadOnlyDictionary<string, object?>)elements[0]!;

        Assert.Equal("Assembly", assembly["name"]);
        Assert.Single((List<object?>)assembly["children"]!);
    }

    [Fact]
    public void AnAmbiguousNameIsRefusedWithTheCandidates()
    {
        _dispatcher.RunOk("create-element name=Same");
        _dispatcher.RunOk("create-element name=Same");

        var error = _dispatcher.RunFails("rename-element element=Same name=Other");

        Assert.Contains("matches 2 elements", error);
        Assert.Contains("use an id", error);
    }

    [Fact]
    public void AnUnknownNameIsRefused()
    {
        Assert.Contains("No element called 'Ghost'", _dispatcher.RunFails("delete-element element=Ghost"));
    }

    [Fact]
    public void QuotedNamesSurviveTheParser()
    {
        _dispatcher.RunOk("create-element name=\"Left bracket\"");

        _dispatcher.RunOk("rename-element element=\"Left bracket\" name=\"Right bracket\"");
    }

    [Fact]
    public void AMissingArgumentSaysWhichOne()
    {
        Assert.Contains("Missing required argument 'name'", _dispatcher.RunFails("create-element"));
    }

    [Fact]
    public void AMalformedArgumentSaysWhatWasExpected()
    {
        _dispatcher.RunOk("create-element name=Group");

        var error = _dispatcher.RunFails("set-transform element=Group channel=position value=1,2");

        Assert.Contains("must be 'x,y,z'", error);
    }

    [Fact]
    public void AModelRuleRefusalBecomesAReadableFailure()
    {
        _dispatcher.RunOk("create-element name=Group");

        Assert.Contains(
            "Snap90",
            _dispatcher.RunFails("set-field-mode element=Group channel=Position mode=Snap90"));
    }

    [Fact]
    public void ACycleIsRefused()
    {
        _dispatcher.RunOk("create-element name=Parent");
        _dispatcher.RunOk("create-element name=Child parent=Parent");

        Assert.Contains("cycle", _dispatcher.RunFails("reparent-element element=Parent parent=Child"));
    }

    [Fact]
    public void VoxelsArePlacedAndCounted()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");

        var materials = (List<object?>)_dispatcher.RunOk("materials").Value!;
        var stone = (string)((IReadOnlyDictionary<string, object?>)materials[0]!)["name"]!;

        CommandResult result = _dispatcher.RunOk(
            $"set-voxels element=Model material={stone} cells=0,0,0;1,0,0;2,0,0");

        Assert.Equal(3, ((IReadOnlyDictionary<string, object?>)result.Value!)["voxels"]);
    }

    [Fact]
    public void FillBoxIsOneUndoableAction()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=3,3,3 material=Default");

        CommandResult before = _dispatcher.RunOk("element element=Model");
        Assert.Equal(64, ((IReadOnlyDictionary<string, object?>)before.Value!)["voxels"]);

        _dispatcher.RunOk("undo");

        CommandResult after = _dispatcher.RunOk("element element=Model");
        Assert.Equal(0, ((IReadOnlyDictionary<string, object?>)after.Value!)["voxels"]);
    }

    [Fact]
    public void FillBoxRefusesABoxTooLargeToHold()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");

        // A mistyped far corner would otherwise materialise a billion edits and
        // run the process out of memory before the model dropped the strays.
        var error = _dispatcher.RunFails("fill-box element=Model from=0,0,0 to=2000,2000,2000 material=Default");

        Assert.Contains("2001 by 2001 by 2001", error);

        CommandResult unchanged = _dispatcher.RunOk("element element=Model");
        Assert.Equal(0, ((IReadOnlyDictionary<string, object?>)unchanged.Value!)["voxels"]);
    }

    [Fact]
    public void OmittingTheMaterialErases()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=2,2,2 material=Default");
        _dispatcher.RunOk("set-voxels element=Model cells=0,0,0");

        CommandResult result = _dispatcher.RunOk("element element=Model");

        Assert.Equal(26, ((IReadOnlyDictionary<string, object?>)result.Value!)["voxels"]);
    }

    [Fact]
    public void HistoryReportsWhatHappenedAndWhereTheCursorIs()
    {
        _dispatcher.RunOk("create-element name=Group");
        _dispatcher.RunOk("rename-element element=Group name=Renamed");
        _dispatcher.RunOk("undo");

        var history = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("history").Value!;
        var records = (List<object?>)history["records"]!;

        Assert.Equal(0, history["currentIndex"]);
        Assert.Equal(2, records.Count);
        Assert.Equal(false, ((IReadOnlyDictionary<string, object?>)records[1]!)["applied"]);
    }

    [Fact]
    public void UndoWithNothingToUndoIsRefusedNotSilent()
    {
        Assert.Equal("Nothing to undo.", _dispatcher.RunFails("undo"));
    }

    [Fact]
    public void UndoToWalksBackToTheStart()
    {
        _dispatcher.RunOk("create-element name=First");
        _dispatcher.RunOk("create-element name=Second");
        _dispatcher.RunOk("undo-to index=-1");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;

        Assert.Empty((List<object?>)tree["elements"]!);
    }

    [Fact]
    public void MaterialsCanBeCreatedRecolouredAndRefusedWhileInUse()
    {
        _dispatcher.RunOk("create-material name=Glass color=0.4,0.7,1,0.45");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("set-voxels element=Model material=Glass cells=0,0,0");

        _dispatcher.RunOk("set-material-color material=Glass color=1,0,0");

        Assert.Contains("still in use", _dispatcher.RunFails("delete-material material=Glass"));
    }

    [Fact]
    public void ASavedProjectComesBackThroughTheSameSurface()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Default");
        _dispatcher.RunOk($"save path={_path}");

        ICommandDispatcher reopened = CommandFactory.CreateDispatcher(new CommandContext());
        reopened.RunOk($"open path={_path}");

        CommandResult result = reopened.RunOk("element element=Model");

        Assert.Equal(8, ((IReadOnlyDictionary<string, object?>)result.Value!)["voxels"]);
    }

    [Fact]
    public void SavingWithoutAPathSaysSo()
    {
        Assert.Contains("No path yet", _dispatcher.RunFails("save"));
    }

    [Fact]
    public void AnUnsupportedStorageModeIsReportedNotThrown()
    {
        Assert.Contains(
            "multi-file",
            _dispatcher.RunFails($"save path={Path.ChangeExtension(_path, ".vcadproj")}"));
    }

    /// <summary>
    /// An argument the command does not take is refused rather than dropped. It
    /// used to be dropped, which is worse than useless: 'focus point=16,1,8'
    /// framed the whole scene because the point it was given had a name 'focus'
    /// never heard of, and nothing anywhere said so. That command is the
    /// editor's; the rule is the dispatcher's, so it is checked on one of its own.
    /// </summary>
    [Fact]
    public void AnArgumentTheCommandDoesNotTakeIsRefused()
    {
        _dispatcher.RunOk("create-element kind=VoxelModel name=Model");

        string failure = _dispatcher.RunFails("bounds element=Model point=1,2,3");

        Assert.Contains("no argument 'point'", failure);
    }

    /// <summary>
    /// The refusal lists what the command does take, because not knowing what to
    /// write instead is the whole cost of the mistake.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheArgumentsTheCommandDoesTake()
    {
        _dispatcher.RunOk("create-element kind=VoxelModel name=Model");

        string failure = _dispatcher.RunFails("bounds element=Model point=1,2,3");

        Assert.Contains("'element'", failure);
    }

    /// <summary>
    /// A command that takes nothing says that rather than listing an empty set.
    /// </summary>
    [Fact]
    public void ACommandThatTakesNothingSaysSo()
    {
        Assert.Contains("takes none", _dispatcher.RunFails("undo steps=2"));
    }

    /// <summary>
    /// The check is on the name alone and ignores case, like every other name in
    /// this surface.
    /// </summary>
    [Fact]
    public void AKnownArgumentIsAcceptedWhateverItsCase()
    {
        _dispatcher.RunOk("create-element kind=VoxelModel name=Model");
        _dispatcher.RunOk("fill-box ELEMENT=Model FROM=0,0,0 TO=1,1,1 MATERIAL=Default");
    }

    /// <summary>
    /// Every command's own arguments are accepted, which is what says the check
    /// reads the same table the handlers do. A command whose handler asked for a
    /// name its descriptor never published would be refused by its own surface,
    /// and this is what would find it.
    /// </summary>
    [Fact]
    public void EveryCommandPublishesEveryArgumentItsOwnHelpNames()
    {
        foreach (CommandDescriptor descriptor in _dispatcher.Commands)
        {
            foreach (CommandParameter parameter in descriptor.Parameters)
            {
                CommandResult result = _dispatcher.Execute(
                    descriptor.Name,
                    new Dictionary<string, string> { [parameter.Name] = "1" });

                Assert.DoesNotContain("no argument", result.Error ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// The environment refusing is reported, not thrown. A file the process may
    /// not read raises <see cref="UnauthorizedAccessException"/>, which does not
    /// derive from <see cref="IOException"/> — so it used to escape the dispatcher
    /// and take the caller down with a stack trace instead of naming the reason.
    /// Every command that names a path can meet it.
    /// </summary>
    [Fact]
    public void TheFileSystemRefusingIsReportedRatherThanThrown()
    {
        var definition = new CommandDefinition(
            new CommandDescriptor("probe", "Throws what the file system throws.", Array.Empty<CommandParameter>()),
            (_, _) => throw new UnauthorizedAccessException("Access to the path is denied."));

        ICommandDispatcher dispatcher = CommandFactory.CreateDispatcher(
            new CommandContext(), new[] { definition });

        Assert.Contains("Access to the path is denied", dispatcher.RunFails("probe"));
    }
}
