using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The <c>import</c> surface as an agent meets it, and <c>open</c> on a voxel
/// file. What matters here is not the reading — that has its own tests — but that
/// an import is one thing the user did: one record, undone whole, materials and
/// all, and a refusal that leaves the document exactly as it was.
/// </summary>
public sealed class ImportCommandTests : IDisposable
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "voxelcad-import-" + Path.GetRandomFileName());

    public ImportCommandTests()
    {
        Directory.CreateDirectory(_directory);
        _dispatcher.RunOk("new");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ImportChangesTheDocumentAndListingFormatsDoesNot()
    {
        Assert.True(Command("import").Mutates);
        Assert.False(Command("import-formats").Mutates);
    }

    [Fact]
    public void BothVoxelFormatsAreOffered()
    {
        var formats = (IReadOnlyList<object?>)_dispatcher.RunOk("import-formats").Value!;
        IEnumerable<string> extensions = formats
            .Cast<IReadOnlyDictionary<string, object?>>()
            .Select(format => (string)format["extension"]!);

        Assert.Equal(new[] { ".vox", ".qb" }, extensions);
    }

    [Fact]
    public void AWholeImportIsOneHistoryRecord()
    {
        string path = Write("part.vox", TwoColours());

        _dispatcher.RunOk($"import path={path}");

        Assert.Equal(1, Records());
    }

    /// <summary>
    /// The record has to take the materials with it, or an undo would leave the
    /// registry holding what nothing wears — and a redo would have to invent them
    /// again.
    /// </summary>
    [Fact]
    public void UndoTakesBackTheCellsAndTheMaterialsTogether()
    {
        string path = Write("part.vox", TwoColours());
        int materialsBefore = Materials();

        _dispatcher.RunOk($"import path={path}");

        Assert.Equal(materialsBefore + 2, Materials());
        Assert.NotEmpty(Elements());

        _dispatcher.RunOk("undo");

        Assert.Equal(materialsBefore, Materials());
        Assert.Empty(Elements());

        _dispatcher.RunOk("redo");

        Assert.Equal(materialsBefore + 2, Materials());
        Assert.NotEmpty(Elements());
    }

    /// <summary>
    /// The rule this project has paid for three times: a second use of the same
    /// gesture is where per-gesture state goes wrong.
    /// </summary>
    [Fact]
    public void ASecondImportOfTheSameFileLandsBesideTheFirst()
    {
        string path = Write("part.vox", TwoColours());

        _dispatcher.RunOk($"import path={path}");
        _dispatcher.RunOk($"import path={path}");

        Assert.Equal(2, Records());
        Assert.Equal(2, Elements().Count);

        // Neither the elements nor the materials may collide by name, or either
        // would stop being addressable by one.
        Assert.Equal(2, Elements().Select(Name).Distinct().Count());
        Assert.Equal(
            Materials(),
            MaterialNames().Distinct().Count());
    }

    [Fact]
    public void AnImportCanBePutUnderAnElement()
    {
        string path = Write("part.vox", TwoColours());

        _dispatcher.RunOk("create-element name=Holder kind=None");
        _dispatcher.RunOk($"import path={path} parent=Holder");

        IReadOnlyDictionary<string, object?> holder = Assert.Single(Elements());

        Assert.Equal("Holder", Name(holder));
        Assert.NotEmpty((IReadOnlyList<object?>)holder["children"]!);
    }

    [Fact]
    public void AFileThatIsNotWhatItsNameClaimsIsRefusedAndChangesNothing()
    {
        string path = Write("broken.vox", System.Text.Encoding.ASCII.GetBytes("not a vox file"));

        string error = _dispatcher.RunFails($"import path={path}");

        Assert.Contains("cannot be read", error);
        Assert.Equal(0, Records());
        Assert.Empty(Elements());
    }

    [Fact]
    public void AnExtensionWithNoReaderNamesTheFormatsThereAre()
    {
        string path = Write("part.gox", new byte[] { 1, 2, 3 });

        string error = _dispatcher.RunFails($"import path={path}");

        Assert.Contains(".gox", error);
        Assert.Contains(".vox", error);
        Assert.Contains(".qb", error);
    }

    [Fact]
    public void AMissingFileIsSaidSoRatherThanThrown()
    {
        string error = _dispatcher.RunFails($"import path={Path.Combine(_directory, "absent.vox")}");

        Assert.Contains("no file at", error);
    }

    /// <summary>
    /// The room a voxel file opens into is what arrived, floored at the default on
    /// every axis. A model smaller than the default cannot tell those two rules
    /// apart, so this uses one larger than it on one axis and smaller on another.
    /// </summary>
    [Fact]
    public void OpeningAVoxelFileStartsAnUntitledDocumentSizedToWhatArrived()
    {
        string path = Write("wide.vox", Wide());

        CommandResult result = _dispatcher.RunOk($"open path={path}");
        var imported = (IReadOnlyDictionary<string, object?>)result.Field("imported")!;
        var room = (int[])result.Field("size")!;
        var size = (int[])imported["size"]!;

        // 90 cells along X, one along the others: the room follows the model where
        // the model is the larger, and the default where it is not.
        Assert.Equal(new[] { 90, 1, 1 }, size);
        Assert.Equal(new[] { 90, 32, 32 }, room);

        // Untitled: adopting the path would put a .vcad inside somebody else's
        // .vox the first time the user pressed Save.
        Assert.Contains("No path yet", _dispatcher.RunFails("save"));
    }

    /// <summary>
    /// An import lands in the scene being worked on, not in the first one. A
    /// document with one scene cannot tell those apart.
    /// </summary>
    [Fact]
    public void AnImportLandsInTheActiveScene()
    {
        string path = Write("part.vox", TwoColours());

        _dispatcher.RunOk("create-scene name=Second");
        _dispatcher.RunOk("activate-scene scene=Second");
        _dispatcher.RunOk($"import path={path}");

        Assert.NotEmpty(Elements());

        _dispatcher.RunOk("activate-scene scene=Scene");

        Assert.Empty(Elements());
    }

    /// <summary>
    /// A project file somebody has damaged is refused with the reason, not thrown.
    /// A deserializer meeting nonsense throws whatever its own parser throws, and
    /// none of those are in the dispatcher's list — so before this it escaped every
    /// transport with a stack trace.
    /// </summary>
    [Fact]
    public void ADamagedProjectFileIsRefusedWithItsReason()
    {
        string path = Write("broken.vcad", System.Text.Encoding.ASCII.GetBytes(
            "this is not: [valid yaml at all\n  - {{{\n"));

        string error = _dispatcher.RunFails($"open path={path}");

        Assert.Contains("could not be read as a project", error);
    }

    [Fact]
    public void ADamagedFileLeavesTheOpenDocumentAlone()
    {
        _dispatcher.RunOk("create-element name=Mine kind=VoxelModel");

        string path = Write("broken.vox", new byte[] { 1, 2, 3, 4 });

        Assert.Contains("cannot be read", _dispatcher.RunFails($"open path={path}"));
        Assert.Equal("Mine", Name(Assert.Single(Elements())));
    }

    /// <summary>Ninety cells along one axis and one along the others.</summary>
    private static byte[] Wide()
    {
        var voxels = new List<byte>();

        for (var x = 0; x < 90; x++)
        {
            voxels.Add((byte)x);
            voxels.Add(0);
            voxels.Add(0);
            voxels.Add(1);
        }

        return Vox(
            Bytes(w =>
            {
                w.Write(90);
                w.Write(1);
                w.Write(1);
            }),
            Bytes(w =>
            {
                w.Write(90);
                w.Write(voxels.ToArray());
            }));
    }

    /// <summary>A model wearing one colour, and a second colour it does not wear.</summary>
    private static byte[] TwoColours() => Vox(
        Bytes(w =>
        {
            w.Write(2);
            w.Write(1);
            w.Write(1);
        }),
        Bytes(w =>
        {
            w.Write(2);
            w.Write(new byte[] { 0, 0, 0, 1 });
            w.Write(new byte[] { 1, 0, 0, 2 });
        }));

    /// <summary>A whole file around one model, with a palette both colours fit in.</summary>
    private static byte[] Vox(byte[] size, byte[] voxels)
    {
        using var children = new MemoryStream();
        using var childWriter = new BinaryWriter(children);

        WriteChunk(childWriter, "SIZE", size);
        WriteChunk(childWriter, "XYZI", voxels);

        var palette = new byte[256 * 4];

        palette[0] = 255;
        palette[3] = 255;
        palette[6] = 255;
        palette[7] = 255;

        WriteChunk(childWriter, "RGBA", palette);
        childWriter.Flush();

        byte[] childBytes = children.ToArray();

        return Bytes(writer =>
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("VOX "));
            writer.Write(150);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("MAIN"));
            writer.Write(0);
            writer.Write(childBytes.Length);
            writer.Write(childBytes);
        });
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] content)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes(id));
        writer.Write(content.Length);
        writer.Write(0);
        writer.Write(content);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        write(writer);
        writer.Flush();

        return buffer.ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllBytes(path, bytes);

        return path;
    }

    private CommandDescriptor Command(string name) =>
        _dispatcher.Commands.Single(command => command.Name == name);

    private int Records() =>
        ((IReadOnlyList<object?>)_dispatcher.RunOk("history").Field("records")!).Count;

    private int Materials() => MaterialNames().Count();

    private IEnumerable<string> MaterialNames() =>
        ((IReadOnlyList<object?>)_dispatcher.RunOk("materials").Value!)
        .Cast<IReadOnlyDictionary<string, object?>>()
        .Select(material => (string)material["name"]!);

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Elements() =>
        ((IReadOnlyList<object?>)_dispatcher.RunOk("tree").Field("elements")!)
        .Cast<IReadOnlyDictionary<string, object?>>()
        .ToList();

    private static string Name(IReadOnlyDictionary<string, object?> element) =>
        (string)element["name"]!;
}
