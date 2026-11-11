using System.Collections.Generic;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The mirror planes as an agent reaches them: 'set-symmetry' writes one slot at
/// a time on the half-cell lattice, and 'element' reports the slots back — the
/// read half of the surface, silent while everything is in its default state.
/// </summary>
public sealed class SymmetryCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public SymmetryCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
    }

    [Fact]
    public void EnablingAPlaneReportsItInCells()
    {
        CommandResult result = _dispatcher.RunOk("set-symmetry element=Model axis=X plane=3.5");

        Assert.True((bool)Plane(result, "x")["enabled"]!);
        Assert.Equal(3.5, Plane(result, "x")["plane"]);
        Assert.False((bool)Plane(result, "y")["enabled"]!);
    }

    [Fact]
    public void BoundaryAndCentrePositionsBothLand()
    {
        CommandResult boundary = _dispatcher.RunOk("set-symmetry element=Model axis=Y plane=7");
        Assert.Equal(7.0, Plane(boundary, "y")["plane"]);

        CommandResult centre = _dispatcher.RunOk("set-symmetry element=Model axis=Y plane=-2.5");
        Assert.Equal(-2.5, Plane(centre, "y")["plane"]);
    }

    /// <summary>
    /// 'off' disables the slot where it stands: the position survives, so a later
    /// re-enable — or the inspector's toggle — finds the plane where it was left.
    /// </summary>
    [Fact]
    public void TurningAPlaneOffKeepsItsPosition()
    {
        _dispatcher.RunOk("set-symmetry element=Model axis=Z plane=4.5");

        CommandResult result = _dispatcher.RunOk("set-symmetry element=Model axis=Z plane=off");

        Assert.False((bool)Plane(result, "z")["enabled"]!);
        Assert.Equal(4.5, Plane(result, "z")["plane"]);
    }

    [Fact]
    public void APlaneOffTheHalfCellLatticeIsRefused()
    {
        string error = _dispatcher.RunFails("set-symmetry element=Model axis=X plane=0.3");

        Assert.Contains("0.5", error);
    }

    [Fact]
    public void AGroupElementIsRefusedByName()
    {
        _dispatcher.RunOk("create-element name=Group");

        Assert.Contains("not a voxel model",
            _dispatcher.RunFails("set-symmetry element=Group axis=X plane=0"));
    }

    /// <summary>Default slots stay out of the report; a touched slot brings them in.</summary>
    [Fact]
    public void TheElementReportIsSilentUntilASlotLeavesItsDefault()
    {
        CommandResult before = _dispatcher.RunOk("element element=Model");
        Assert.False(Fields(before).ContainsKey("symmetry"));

        _dispatcher.RunOk("set-symmetry element=Model axis=X plane=3.5");

        CommandResult after = _dispatcher.RunOk("element element=Model");
        var symmetry = (IReadOnlyDictionary<string, object?>)Fields(after)["symmetry"]!;
        var x = (IReadOnlyDictionary<string, object?>)symmetry["x"]!;

        Assert.True((bool)x["enabled"]!);
        Assert.Equal(3.5, x["plane"]);
    }

    [Fact]
    public void SettingSymmetryIsUndoable()
    {
        _dispatcher.RunOk("set-symmetry element=Model axis=X plane=3.5");
        _dispatcher.RunOk("undo");

        CommandResult report = _dispatcher.RunOk("element element=Model");

        Assert.False(Fields(report).ContainsKey("symmetry"));
    }

    private static IReadOnlyDictionary<string, object?> Fields(CommandResult result) =>
        (IReadOnlyDictionary<string, object?>)result.Value!;

    private static IReadOnlyDictionary<string, object?> Plane(CommandResult result, string axis) =>
        (IReadOnlyDictionary<string, object?>)Fields(result)[axis]!;
}
