using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// Naming what an import creates. The rules are the model's own — a free name is
/// kept, a taken one grows the next free number from its own trailing one — and
/// the one thing added here is that it stays cheap when every part of a file
/// falls back to the same name.
/// </summary>
public sealed class ImportNamesTests
{
    [Fact]
    public void AFreeNameIsKeptExactly()
    {
        var names = new ImportNames();

        Assert.Equal("Wheel", names.Compose("Wheel"));
    }

    [Fact]
    public void ATakenNameGrowsTheNextFreeNumber()
    {
        var names = new ImportNames();

        Assert.Equal("Wheel", names.Compose("Wheel"));
        Assert.Equal("Wheel 2", names.Compose("Wheel"));
        Assert.Equal("Wheel 3", names.Compose("Wheel"));
    }

    /// <summary>A copy of "Wheel 2" is "Wheel 3", never "Wheel 2 2".</summary>
    [Fact]
    public void ANumberedNameGrowsFromItsOwnNumber()
    {
        var names = new ImportNames();

        names.Reserve("Wheel 2");

        Assert.Equal("Wheel 3", names.Compose("Wheel 2"));
    }

    [Fact]
    public void ANameNothingHasIsKeptEvenWhereItsStemIsTaken()
    {
        var names = new ImportNames();

        names.Reserve("Wheel");

        Assert.Equal("Axle", names.Compose("Axle"));
    }

    [Fact]
    public void AnEmptyNameBecomesSomething()
    {
        var names = new ImportNames();

        Assert.Equal("Model", names.Compose("   "));
        Assert.Equal("Model 2", names.Compose(string.Empty));
    }

    /// <summary>
    /// Every part of a MagicaVoxel scene that carries no name of its own falls back
    /// to the file's, so composing one name several hundred times is the ordinary
    /// path rather than an edge case. Searching from the start each time makes that
    /// quadratic.
    /// </summary>
    /// <remarks>
    /// The bound is wall clock and deliberately enormous, because the thing being
    /// separated is enormous: fifty thousand names cost about six milliseconds
    /// linear and something over ten seconds quadratic. A tighter bound would be
    /// measuring the machine — a first attempt at this asserted a ratio between two
    /// small runs and was pure noise, reading eight thousand names as slower than
    /// sixteen thousand.
    /// </remarks>
    [Fact]
    public void ComposingOneNameManyTimesStaysCheap()
    {
        const int count = 50_000;

        var watch = Stopwatch.StartNew();
        List<string> composedList = Compose(count);

        watch.Stop();

        Assert.Equal(count, composedList.Count);

        // Every one of them different, which is the point of the exercise.
        Assert.Equal(count, new HashSet<string>(composedList).Count);

        Assert.True(
            watch.Elapsed.TotalSeconds < 2,
            $"composing {count:N0} names took {watch.Elapsed.TotalSeconds:F2} s");
    }

    private static List<string> Compose(int count)
    {
        var names = new ImportNames();
        var composedList = new List<string>(count);

        for (var i = 0; i < count; i++)
            composedList.Add(names.Compose("Model"));

        return composedList;
    }
}
