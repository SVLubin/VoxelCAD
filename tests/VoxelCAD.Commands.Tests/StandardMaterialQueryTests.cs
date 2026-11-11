using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Reading the shipped library. What is held here is that the answer is bounded
/// and says so — three hundred materials with their appearances is 64 KB, which
/// no client will carry, so the unbounded version's default call was the one
/// call that could not be made.
/// </summary>
public sealed class StandardMaterialQueryTests
{
    private readonly ICommandDispatcher _dispatcher;

    public StandardMaterialQueryTests()
    {
        StandardMaterials.Install();
        _dispatcher = CommandFactory.CreateDispatcher(new CommandContext());
        _dispatcher.RunOk("new");
    }

    private IReadOnlyDictionary<string, object?> Read(string line) =>
        (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk(line).Value!;

    private static int Count(IReadOnlyDictionary<string, object?> answer) =>
        ((IReadOnlyList<object?>)answer["materials"]!).Count;

    private static string NameAt(IReadOnlyDictionary<string, object?> answer, int index) =>
        (string)((IReadOnlyDictionary<string, object?>)
            ((IReadOnlyList<object?>)answer["materials"]!)[index]!)["name"]!;

    [Fact]
    public void OneAnswerIsBoundedHoweverManyMatch()
    {
        IReadOnlyDictionary<string, object?> answer = Read("standard-materials");

        Assert.True((int)answer["matched"]! > Count(answer));
        Assert.Equal(50, Count(answer));
    }

    /// <summary>
    /// A page that stopped short in silence would read as the whole library,
    /// which is the ledger's own "no silent caps".
    /// </summary>
    [Fact]
    public void APageThatStoppedShortSaysSoAndSaysHowToGoOn()
    {
        IReadOnlyDictionary<string, object?> answer = Read("standard-materials");

        Assert.Contains("from=50", (string)answer["more"]!);
    }

    [Fact]
    public void TheLastPageClaimsNoMore()
    {
        int matched = (int)Read("standard-materials")["matched"]!;

        IReadOnlyDictionary<string, object?> last =
            Read($"standard-materials from={matched - 1}");

        Assert.Equal(1, Count(last));
        Assert.False(last.ContainsKey("more"));
    }

    /// <summary>
    /// Walking the pages reaches every material exactly once, which is what makes
    /// the cap a window rather than a truncation.
    /// </summary>
    [Fact]
    public void ThePagesTogetherAreTheWholeLibraryWithNothingRepeated()
    {
        var seenList = new List<string>();
        int matched = (int)Read("standard-materials")["matched"]!;

        for (int from = 0; from < matched; from += 50)
        {
            IReadOnlyDictionary<string, object?> page = Read($"standard-materials from={from}");

            for (int index = 0; index < Count(page); index++)
                seenList.Add(NameAt(page, index));
        }

        Assert.Equal(matched, seenList.Count);
        Assert.Equal(seenList.Count, seenList.Distinct().Count());
    }

    [Fact]
    public void AFilterNarrowsTheMatchesRatherThanThePage()
    {
        IReadOnlyDictionary<string, object?> answer =
            Read("standard-materials contains=herringbone");

        Assert.True((int)answer["matched"]! > 0);
        Assert.True((int)answer["matched"]! < (int)answer["of"]!);
        Assert.Equal((int)answer["matched"]!, Count(answer));
    }

    [Fact]
    public void AFromPastTheEndAnswersWithNothingRatherThanFailing()
    {
        Assert.Equal(0, Count(Read("standard-materials from=100000")));
    }
}
