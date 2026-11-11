using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// How a control is addressed, and what belongs in one reading of the tree. The
/// walk itself needs the engine; every decision it makes is here.
/// </summary>
public sealed class UiSnapshotTests
{
    // --- references ---

    /// <summary>A name somebody chose is the address; that is the whole point of naming them.</summary>
    [Fact]
    public void AChosenNameIsTheSegment()
    {
        Assert.Equal("Undo", UiReference.ComposeSegment("Undo", "Button", 3));
    }

    /// <summary>
    /// Godot's own names carry a global counter, so they differ every run and are
    /// useless as an address. Position among siblings does not.
    /// </summary>
    [Fact]
    public void AGeneratedNameBecomesTypeAndPosition()
    {
        Assert.Equal("Button@3", UiReference.ComposeSegment("@Button@1174", "Button", 3));
        Assert.Equal("HBoxContainer@0", UiReference.ComposeSegment(string.Empty, "HBoxContainer", 0));
    }

    /// <summary>
    /// The two forms cannot be confused, because Godot refuses '@' in a name
    /// anybody chose — so a segment carrying one is always the generated kind.
    /// </summary>
    [Fact]
    public void TheGeneratedFormIsMarkedByACharacterAChosenNameCannotHold()
    {
        Assert.Contains('@', UiReference.ComposeSegment("@Button@9", "Button", 1));
        Assert.DoesNotContain('@', UiReference.ComposeSegment("Undo", "Button", 1));
    }

    [Fact]
    public void SegmentsJoinIntoAPath()
    {
        string path = UiReference.Append(UiReference.Append(string.Empty, "UiRoot"), "HierarchyDock");

        Assert.Equal("UiRoot/HierarchyDock", path);
        Assert.Equal("UiRoot/HierarchyDock/Undo", UiReference.Append(path, "Undo"));
    }

    [Theory]
    [InlineData("UiRoot/Dock/Undo", "UiRoot/Dock", true)]
    [InlineData("UiRoot/Dock", "UiRoot/Dock", true)]
    [InlineData("UiRoot/Dock/Undo", "", true)]
    [InlineData("UiRoot/Other/Undo", "UiRoot/Dock", false)]
    public void ContainmentIsTestedOnWholeSegments(string reference, string root, bool expected)
    {
        Assert.Equal(expected, UiReference.IsUnder(reference, root));
    }

    /// <summary>
    /// A prefix that stops mid-segment is not containment: an "Undo" root must not
    /// swallow "UndoRedoRow".
    /// </summary>
    [Fact]
    public void APartialSegmentIsNotContainment()
    {
        Assert.False(UiReference.IsUnder("UiRoot/UndoRedoRow", "UiRoot/Undo"));
    }

    [Fact]
    public void DepthIsCountedFromTheRootTheCallerAskedFor()
    {
        Assert.Equal(0, UiReference.DepthUnder("UiRoot/Dock", "UiRoot/Dock"));
        Assert.Equal(2, UiReference.DepthUnder("UiRoot/Dock/Row/Undo", "UiRoot/Dock"));
        Assert.Equal(4, UiReference.DepthUnder("UiRoot/Dock/Row/Undo", string.Empty));
    }

    // --- policy ---

    [Fact]
    public void EverythingVisibleIsReportedByDefault()
    {
        Assert.True(UiSnapshotPolicy.ShouldReport("UiRoot/Dock/Undo", visible: true, Options()));
    }

    /// <summary>An invisible control cannot be clicked, so it is noise until it is asked for.</summary>
    [Fact]
    public void InvisibleIsOmittedUnlessAskedFor()
    {
        Assert.False(UiSnapshotPolicy.ShouldReport("UiRoot/Undo", visible: false, Options()));
        Assert.True(UiSnapshotPolicy.ShouldReport(
            "UiRoot/Undo",
            visible: false,
            Options(includeInvisible: true)));
    }

    [Fact]
    public void ARootFiltersToOnePanel()
    {
        UiSnapshotOptions options = Options(root: "UiRoot/HierarchyDock");

        Assert.True(UiSnapshotPolicy.ShouldReport("UiRoot/HierarchyDock/Undo", true, options));
        Assert.False(UiSnapshotPolicy.ShouldReport("UiRoot/InspectorDock/Field", true, options));
    }

    [Fact]
    public void DepthStopsShortOfTheLeaves()
    {
        UiSnapshotOptions options = Options(root: "UiRoot", maxDepth: 1);

        Assert.True(UiSnapshotPolicy.ShouldReport("UiRoot/HierarchyDock", true, options));
        Assert.False(UiSnapshotPolicy.ShouldReport("UiRoot/HierarchyDock/Row", true, options));
    }

    /// <summary>
    /// A branch on the way down to the root has to be entered even though nothing
    /// in it is reported, or the root would never be reached.
    /// </summary>
    [Fact]
    public void TheWalkDescendsTowardsTheRootAndIntoIt()
    {
        UiSnapshotOptions options = Options(root: "UiRoot/HierarchyDock/Row");

        Assert.True(UiSnapshotPolicy.ShouldDescend("UiRoot", options));
        Assert.True(UiSnapshotPolicy.ShouldDescend("UiRoot/HierarchyDock", options));
        Assert.True(UiSnapshotPolicy.ShouldDescend("UiRoot/HierarchyDock/Row/Undo", options));
        Assert.False(UiSnapshotPolicy.ShouldDescend("UiRoot/InspectorDock", options));
    }

    [Fact]
    public void WithNoRootEveryBranchIsWorthEntering()
    {
        Assert.True(UiSnapshotPolicy.ShouldDescend("Anything/At/All", Options()));
    }

    // --- coordinates ---

    /// <summary>
    /// The factor that makes this class exist: a control's rect is in canvas units
    /// and input is aimed in window pixels, and on this project the two have been
    /// measured 2.58 apart.
    /// </summary>
    [Fact]
    public void ARectGoesThroughTheWindowStretchIntoInputCoordinates()
    {
        var stretch = new Transform2D(new Vector2(2.58f, 0f), new Vector2(0f, 2.58f), Vector2.Zero);

        Rect2 input = UiCoordinates.ToInput(new Rect2(100f, 50f, 200f, 40f), stretch);

        Assert.Equal(258f, input.Position.X, 2);
        Assert.Equal(129f, input.Position.Y, 2);
        Assert.Equal(516f, input.Size.X, 2);
        Assert.Equal(103.2f, input.Size.Y, 2);
    }

    [Fact]
    public void AnIdentityStretchLeavesARectAlone()
    {
        Rect2 input = UiCoordinates.ToInput(new Rect2(10f, 20f, 30f, 40f), Transform2D.Identity);

        Assert.Equal(new Rect2(10f, 20f, 30f, 40f), input);
    }

    [Fact]
    public void AnUnqualifiedAimGoesToTheCentre()
    {
        Assert.Equal(new Vector2(110f, 40f), UiCoordinates.GetCentre(new Rect2(10f, 20f, 200f, 40f)));
    }

    /// <summary>
    /// The offset runs across the rect rather than in pixels, so it means the same
    /// thing whatever the control's size.
    /// </summary>
    [Fact]
    public void AnOffsetIsAFractionOfTheRect()
    {
        var rect = new Rect2(10f, 20f, 200f, 40f);

        Assert.Equal(new Vector2(10f, 20f), UiCoordinates.GetPointInside(rect, Vector2.Zero));
        Assert.Equal(new Vector2(210f, 60f), UiCoordinates.GetPointInside(rect, Vector2.One));
        Assert.Equal(new Vector2(60f, 30f), UiCoordinates.GetPointInside(rect, new Vector2(0.25f, 0.25f)));
    }

    /// <summary>Out of range is clamped, not refused: asking for 1.5 means the far edge.</summary>
    [Fact]
    public void AnOffsetOutsideTheRectIsClampedToIt()
    {
        var rect = new Rect2(10f, 20f, 200f, 40f);

        Assert.Equal(new Vector2(210f, 60f), UiCoordinates.GetPointInside(rect, new Vector2(1.5f, 9f)));
        Assert.Equal(new Vector2(10f, 20f), UiCoordinates.GetPointInside(rect, new Vector2(-2f, -1f)));
    }

    private static UiSnapshotOptions Options(
        string root = "",
        int maxDepth = int.MaxValue,
        bool includeInvisible = false) =>
        new(root, maxDepth, includeInvisible);

    /// <summary>
    /// The search reads a control's reference, its words and its type, because
    /// all three are things somebody looks for. Without one the whole shell comes
    /// back — around 130 controls in the shipped layout, most of them containers.
    /// </summary>
    [Fact]
    public void TheSearchMatchesTheReference()
    {
        Assert.True(UiSnapshotPolicy.MatchesSearch(
            "UiRoot/Settings/CheckBox@23", string.Empty, "CheckBox", Searching("settings")));
    }

    [Fact]
    public void TheSearchMatchesTheWords()
    {
        Assert.True(UiSnapshotPolicy.MatchesSearch(
            "UiRoot/CheckBox@23", "Draw the surface relief", "CheckBox", Searching("relief")));
    }

    [Fact]
    public void TheSearchMatchesTheType()
    {
        Assert.True(UiSnapshotPolicy.MatchesSearch(
            "UiRoot/Thing@2", string.Empty, "SegmentedRow", Searching("segmented")));
    }

    [Fact]
    public void TheSearchIgnoresCase()
    {
        Assert.True(UiSnapshotPolicy.MatchesSearch(
            "UiRoot/Undo", string.Empty, "Button", Searching("UNDO")));
    }

    [Fact]
    public void WhatAnswersNoneOfTheThreeIsLeftOut()
    {
        Assert.False(UiSnapshotPolicy.MatchesSearch(
            "UiRoot/LeftColumn", string.Empty, "EdgeColumn", Searching("relief")));
    }

    /// <summary>
    /// No search is every control, which is what keeps the option from changing
    /// what an answer means when nobody asked for it.
    /// </summary>
    [Fact]
    public void NoSearchMatchesEverything()
    {
        Assert.True(UiSnapshotPolicy.MatchesSearch(
            "UiRoot/LeftColumn", string.Empty, "EdgeColumn", Options()));
    }

    /// <summary>
    /// The search narrows what is reported and never what is walked: almost
    /// everything worth finding sits under containers that answer nothing, so a
    /// search folded into the descent would find none of it.
    /// </summary>
    [Fact]
    public void TheSearchDoesNotDecideWhatIsWalked()
    {
        Assert.True(UiSnapshotPolicy.ShouldDescend("UiRoot/LeftColumn", Searching("relief")));
    }

    private static UiSnapshotOptions Searching(string contains) =>
        Options() with { Contains = contains };
}
