using System.Collections.Generic;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The pivot as an agent reaches it: 'set-pivot' moves the point the position
/// channel names, 'point=center' is the one-shot centring, and every element
/// report carries the pivot beside the position it explains.
/// </summary>
public sealed class PivotCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public PivotCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("create-material name=Stone color=0.5,0.5,0.5");
    }

    [Fact]
    public void ANewElementReportsAZeroPivot()
    {
        CommandResult report = _dispatcher.RunOk("element element=Model");

        Assert.Equal(new[] { 0f, 0f, 0f }, Pivot(report));
        Assert.Equal(new[] { 0f, 0f, 0f }, Position(report));
    }

    /// <summary>
    /// The first edit centres the pivot on what it filled, and the reported
    /// position moves with it. The pivot itself reads as an offset from the
    /// content's middle, so a freshly painted model says 0 — "the grip is at
    /// the centre" — while the position says where that centre stands in the
    /// room. Cells 0..3 span [0, 4), centred on 2.
    /// </summary>
    [Fact]
    public void TheFirstEditCentresThePivotAndThePositionFollows()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=3,3,3 material=Stone");

        CommandResult report = _dispatcher.RunOk("element element=Model");

        Assert.Equal(new[] { 0f, 0f, 0f }, Pivot(report));
        Assert.Equal(new[] { 2f, 2f, 2f }, Position(report));
    }

    /// <summary>
    /// The pivot stays where the first edit put it while the box grows — what
    /// changes is its label, now measured against a middle that moved. The
    /// re-labelling is the same honesty a pivot move shows the mirror offsets.
    /// </summary>
    [Fact]
    public void LaterEditsDoNotChaseTheMiddleAndTheOffsetSaysSo()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=3,3,3 material=Stone");
        _dispatcher.RunOk("fill-box element=Model from=20,20,20 to=23,23,23 material=Stone");

        CommandResult report = _dispatcher.RunOk("element element=Model");

        // Still at (2, 2, 2) in cells; the combined box is centred on 12.
        Assert.Equal(new[] { -10f, -10f, -10f }, Pivot(report));
    }

    /// <summary>'center' recomputes once, from the cells as they stand now.</summary>
    [Fact]
    public void CenterIsAOneShotRecomputation()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=3,3,3 material=Stone");
        _dispatcher.RunOk("fill-box element=Model from=20,20,20 to=23,23,23 material=Stone");

        CommandResult result = _dispatcher.RunOk("set-pivot element=Model point=center");

        // Centred means offset zero, by construction: the report and the Center
        // operation share the same anchor.
        Assert.Equal(new[] { 0f, 0f, 0f }, ToFloats(Fields(result)["pivot"]));
    }

    [Fact]
    public void CenteringAnEmptyModelIsRefused()
    {
        string error = _dispatcher.RunFails("set-pivot element=Model point=center");

        Assert.Contains("centre", error);
    }

    [Fact]
    public void ATypedPointIsRoundedToTheHalfCellLattice()
    {
        CommandResult result = _dispatcher.RunOk("set-pivot element=Model point=1.3,0.6,-0.2");

        Assert.Equal(new[] { 1.5f, 0.5f, 0f }, ToFloats(Fields(result)["pivot"]));
    }

    /// <summary>
    /// Moving the pivot moves the reported position and nothing in the scene: the
    /// same cells, the same world geometry, a different point named.
    /// </summary>
    [Fact]
    public void MovingThePivotMovesTheNumberAndNotTheCells()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Stone");

        CommandResult before = _dispatcher.RunOk("bounds element=Model");

        // Five cells right of the content's middle, which sits at (1, 1, 1).
        CommandResult result = _dispatcher.RunOk("set-pivot element=Model point=5,0,5");

        Assert.Equal(new[] { 5f, 0f, 5f }, ToFloats(Fields(result)["pivot"]));
        Assert.Equal(new[] { 6f, 1f, 6f }, ToFloats(Fields(result)["position"]));

        CommandResult after = _dispatcher.RunOk("bounds element=Model");

        Assert.Equal(Fields(before)["min"], Fields(after)["min"]);
        Assert.Equal(Fields(before)["max"], Fields(after)["max"]);
    }

    [Fact]
    public void SettingThePivotIsUndoable()
    {
        _dispatcher.RunOk("set-pivot element=Model point=1,0,0");
        _dispatcher.RunOk("undo");

        CommandResult report = _dispatcher.RunOk("element element=Model");

        Assert.Equal(new[] { 0f, 0f, 0f }, Pivot(report));
    }

    /// <summary>
    /// Position places the pivot: with the pivot centred on a 4-cube, position
    /// (10, 10, 10) puts the middle there, and a rotation holds it.
    /// </summary>
    [Fact]
    public void PositionPlacesThePivotAndRotationHoldsIt()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=3,3,3 material=Stone");
        _dispatcher.RunOk("set-transform element=Model channel=Position value=10,10,10");
        _dispatcher.RunOk("set-transform element=Model channel=Rotation value=0,90,0");

        CommandResult report = _dispatcher.RunOk("element element=Model");

        Assert.Equal(new[] { 10f, 10f, 10f }, Position(report));
        Assert.Equal(new[] { 0f, 90f, 0f }, ToFloats(Fields(report)["rotation"]));
    }

    /// <summary>A group's centre reads the subtree, brought into the group's frame.</summary>
    [Fact]
    public void AGroupCentresOnItsContents()
    {
        _dispatcher.RunOk("create-element name=Group kind=None");
        _dispatcher.RunOk("reparent-element element=Model parent=Group");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Stone");

        CommandResult result = _dispatcher.RunOk("set-pivot element=Group point=center");

        Assert.Equal(new[] { 0f, 0f, 0f }, ToFloats(Fields(result)["pivot"]));
    }

    /// <summary>
    /// Symmetry speaks in offsets from the pivot: 0 is a mirror exactly through
    /// it, whatever the pivot's own coordinate — the two live on the same
    /// half-cell lattice, so the conversion is exact.
    /// </summary>
    [Fact]
    public void APlaneAtOffsetZeroLiesOnThePivot()
    {
        _dispatcher.RunOk("set-pivot element=Model point=3.5,1,2");

        CommandResult result = _dispatcher.RunOk("set-symmetry element=Model axis=X plane=0");

        var x = (IReadOnlyDictionary<string, object?>)Fields(result)["x"]!;

        Assert.True((bool)x["enabled"]!);
        Assert.Equal(0.0, x["plane"]);
    }

    /// <summary>
    /// The planes are stored in cells and do not move with the pivot; what moves
    /// is their label. A mirror set through the pivot at 3.5, read after the
    /// pivot walked to 5.5, is the same mirror two cells behind it.
    /// </summary>
    [Fact]
    public void MovingThePivotRelabelsThePlaneAndMovesNothing()
    {
        _dispatcher.RunOk("set-pivot element=Model point=3.5,1,2");
        _dispatcher.RunOk("set-symmetry element=Model axis=X plane=0");

        _dispatcher.RunOk("set-pivot element=Model point=5.5,1,2");

        CommandResult report = _dispatcher.RunOk("element element=Model");
        var symmetry = (IReadOnlyDictionary<string, object?>)Fields(report)["symmetry"]!;
        var x = (IReadOnlyDictionary<string, object?>)symmetry["x"]!;

        Assert.Equal(-2.0, x["plane"]);
    }

    private static IReadOnlyDictionary<string, object?> Fields(CommandResult result) =>
        (IReadOnlyDictionary<string, object?>)result.Value!;

    private static float[] Pivot(CommandResult result) => ToFloats(Fields(result)["pivot"]);

    private static float[] Position(CommandResult result) => ToFloats(Fields(result)["position"]);

    private static float[] ToFloats(object? value) => (float[])value!;
}
