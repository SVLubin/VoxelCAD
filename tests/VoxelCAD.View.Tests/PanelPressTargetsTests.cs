using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Godot;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which panel a press belongs to. Both polled gestures of the chrome used to
/// walk the column in layout order and take the first panel whose header or
/// grip contained the point — a hit test blind to what is drawn over it, in a
/// chrome where panels routinely overlap.
/// </summary>
public sealed class PanelPressTargetsTests
{
    // The reported layout, in round numbers: a pinned Project panel with its
    // resize grip at its inner-bottom corner, and the Toolbar's temporary panel
    // grown out of the run below it — clamped to the column's bottom, so it
    // stands over the pinned panel by one card, its own header across the grip.
    private static readonly Rect2 ProjectSurface = new(0f, 0f, 360f, 400f);
    private static readonly Rect2 ProjectHeader = new(2f, 2f, 356f, 28f);
    private static readonly Rect2 ProjectGrip = new(344f, 376f, 16f, 16f);

    private static readonly Rect2 ToolbarSurface = new(0f, 360f, 360f, 80f);
    private static readonly Rect2 ToolbarHeader = new(2f, 362f, 356f, 28f);
    private static readonly Rect2 ToolbarGrip = new(344f, 424f, 16f, 16f);

    /// <summary>Where the pin is drawn, and where the grip underneath is.</summary>
    private static readonly Vector2 OnThePin = new(352f, 380f);

    private static PanelPressInput Project(bool still = true) =>
        new(
            PanelKind.Project,
            still,
            ProjectSurface,
            Notch: 0f,
            StubOnRight: false,
            ProjectHeader,
            ProjectGrip,
            HeightResizable: true);

    private static PanelPressInput Toolbar(bool still = true) =>
        new(
            PanelKind.Toolbar,
            still,
            ToolbarSurface,
            Notch: 0f,
            StubOnRight: false,
            ToolbarHeader,
            ToolbarGrip,
            HeightResizable: false);

    private static IReadOnlyList<PanelPressInput> Column(bool still = true) =>
        new[] { Project(still), Toolbar(still) };

    /// <summary>
    /// The defect, stated: the pinned panel's grip is under the temporary
    /// panel's header, so a press there is not a resize — and the pin, whose
    /// centre it covered, got nothing.
    /// </summary>
    [Fact]
    public void AGripWithAPanelOverItDoesNotAnswer()
    {
        Assert.True(ProjectGrip.HasPoint(OnThePin));
        Assert.True(ToolbarHeader.HasPoint(OnThePin));

        Assert.False(
            PanelPressTargets.TryGetGrip(Column(), carried: null, OnThePin, out _, out _));
    }

    /// <summary>The other half of the same press: the panel on top owns it.</summary>
    [Fact]
    public void ThePanelOverItOwnsThatPress()
    {
        Assert.True(
            PanelPressTargets.TryGetOwner(
                Column(), carried: null, OnThePin, out PanelPressInput owner));

        Assert.Equal(PanelKind.Toolbar, owner.Kind);

        Assert.True(
            PanelPressTargets.TryGetDraggable(
                Column(), carried: null, OnThePin, out PanelKind kind));

        Assert.Equal(PanelKind.Toolbar, kind);
    }

    /// <summary>
    /// The measured case, kept in the coordinates it was measured in: the pin's
    /// own centre on the running editor, against the two rects <c>ui</c>
    /// reported. Clicking there did nothing at all, twice.
    /// </summary>
    [Fact]
    public void TheReportedPinIsNotAResize()
    {
        var project = new PanelPressInput(
            PanelKind.Project,
            Still: true,
            new Rect2(10.3f, 603.8f, 928.8f, 1024.4f),
            Notch: 0f,
            StubOnRight: false,
            new Rect2(12.9f, 606.4f, 923.6f, 72.2f),
            new Rect2(890.1f, 1579.2f, 41.3f, 41.3f),
            HeightResizable: true);

        var toolbar = new PanelPressInput(
            PanelKind.Toolbar,
            Still: true,
            new Rect2(10.3f, 1566.3f, 928.8f, 206.4f),
            Notch: 0f,
            StubOnRight: false,
            new Rect2(12.9f, 1568.8f, 923.6f, 72.2f),
            new Rect2(890.1f, 1723.7f, 41.3f, 41.3f),
            HeightResizable: false);

        var panels = new[] { project, toolbar };
        var pinCentre = new Vector2(897.8f, 1605f);

        Assert.False(
            PanelPressTargets.TryGetGrip(panels, carried: null, pinCentre, out _, out _));
    }

    /// <summary>
    /// And the grip is a grip again the moment nothing stands over it — with
    /// the toolbar back in its tab, the pinned panel resizes as it always did.
    /// </summary>
    [Fact]
    public void AGripWithNothingOverItAnswers()
    {
        var tab = Toolbar() with { Surface = new Rect2(0f, 400f, 30f, 30f) };
        var panels = new[] { Project(), tab };

        Assert.True(
            PanelPressTargets.TryGetGrip(
                panels, carried: null, OnThePin, out PanelKind kind, out bool heightToo));

        Assert.Equal(PanelKind.Project, kind);
        Assert.True(heightToo);
    }

    /// <summary>The topmost panel's own grip is untouched by any of this.</summary>
    [Fact]
    public void ThePanelOnTopKeepsItsOwnGrip()
    {
        Assert.True(
            PanelPressTargets.TryGetGrip(
                Column(),
                carried: null,
                ToolbarGrip.GetCenter(),
                out PanelKind kind,
                out bool heightToo));

        Assert.Equal(PanelKind.Toolbar, kind);
        Assert.False(heightToo);
    }

    /// <summary>
    /// The same rule in the other gesture: a header with a panel's body over it
    /// cannot be picked up, and the body is not a header, so nobody is dragged.
    /// </summary>
    [Fact]
    public void AHeaderWithAPanelOverItIsNotDraggable()
    {
        // The toolbar grown far enough up that its body — not its header —
        // covers the pinned panel's title bar.
        var over = Toolbar() with
        {
            Surface = new Rect2(0f, 0f, 360f, 80f),
            Header = new Rect2(2f, 2f, 356f, 28f)
        };

        var under = Project() with
        {
            Surface = new Rect2(0f, 20f, 360f, 400f),
            Header = new Rect2(2f, 22f, 356f, 28f),
            Grip = new Rect2(344f, 396f, 16f, 16f)
        };

        // Layout order puts the covered panel first, which is what the walk
        // this replaces would have answered with.
        var panels = new[] { under, over };
        var onTheCoveredHeader = new Vector2(180f, 45f);

        Assert.True(under.Header.HasPoint(onTheCoveredHeader));
        Assert.False(over.Header.HasPoint(onTheCoveredHeader));

        Assert.False(
            PanelPressTargets.TryGetDraggable(
                panels, carried: null, onTheCoveredHeader, out _));
    }

    /// <summary>
    /// A panel still moving is drawn where it is drawn, so it stands in front of
    /// what is behind it — and has no settled place to be grabbed by, so the
    /// press is nobody's rather than the panel underneath's.
    /// </summary>
    [Fact]
    public void APanelInFlightBlocksAndAnswersNothing()
    {
        var panels = new[] { Project(), Toolbar(still: false) };

        Assert.False(
            PanelPressTargets.TryGetGrip(panels, carried: null, OnThePin, out _, out _));

        Assert.False(
            PanelPressTargets.TryGetDraggable(panels, carried: null, OnThePin, out _));
    }

    /// <summary>
    /// The notch is a hole, so what shows through it owns the press — that is
    /// the whole reason the cut exists, and the same answer the engine is given
    /// for routing the event.
    /// </summary>
    [Theory]
    [InlineData(false, 10f)]
    [InlineData(true, 350f)]
    public void WhatShowsThroughTheNotchOwnsThePress(bool stubOnRight, float x)
    {
        var grown = Toolbar() with
        {
            Surface = new Rect2(0f, 360f, 360f, 80f),
            Notch = 36f,
            StubOnRight = stubOnRight
        };

        // A tab of another panel, standing in the run the notch reveals — put
        // behind the grown panel deliberately, so nothing but the cut can let
        // it answer.
        var tab = Project() with
        {
            Surface = new Rect2(x - 5f, 400f, 30f, 30f),
            Header = new Rect2(x - 5f, 400f, 30f, 30f),
            Grip = null
        };

        var panels = new[] { tab, grown };
        var inTheNotch = new Vector2(x, 415f);

        Assert.True(grown.Surface.HasPoint(inTheNotch));

        Assert.True(
            PanelPressTargets.TryGetDraggable(
                panels, carried: null, inTheNotch, out PanelKind kind));

        Assert.Equal(PanelKind.Project, kind);
    }

    /// <summary>
    /// The notch is cut below the stub alone, so the same column of pixels one
    /// card higher is still the panel's — the header runs the whole width.
    /// </summary>
    [Fact]
    public void TheNotchDoesNotReachTheHeader()
    {
        var grown = Toolbar() with { Notch = 36f };
        var panels = new[] { Project(), grown };

        // Inside the notch's own column, and inside the header above it.
        var onTheHeader = new Vector2(10f, 374f);

        Assert.True(grown.Header.HasPoint(onTheHeader));

        Assert.True(
            PanelPressTargets.TryGetDraggable(
                panels, carried: null, onTheHeader, out PanelKind kind));

        Assert.Equal(PanelKind.Toolbar, kind);
    }

    /// <summary>
    /// The carried panel is drawn last and so owns every press it covers — the
    /// depth this asks is the very one the column hands Godot.
    /// </summary>
    [Fact]
    public void TheCarriedPanelOwnsWhatItCovers()
    {
        var carried = Project() with
        {
            Surface = new Rect2(0f, 360f, 360f, 80f),
            Header = new Rect2(2f, 362f, 356f, 28f),
            Grip = new Rect2(344f, 424f, 16f, 16f)
        };

        var panels = new[] { carried, Toolbar() };

        Assert.True(
            PanelPressTargets.TryGetOwner(
                panels, PanelKind.Project, OnThePin, out PanelPressInput owner));

        Assert.Equal(PanelKind.Project, owner.Kind);

        // And without the carry, the same two rects answer with the other one.
        Assert.True(
            PanelPressTargets.TryGetOwner(
                panels, carried: null, OnThePin, out PanelPressInput settled));

        Assert.Equal(PanelKind.Toolbar, settled.Kind);
    }

    /// <summary>
    /// The rule this replaced, restated here so the guard means something: the
    /// first panel in layout order whose grip or header contains the point.
    /// Written out rather than described, because a test that only asserts the
    /// new answer cannot say whether the old one was ever different.
    /// </summary>
    private static bool FirstInLayoutOrder(
        IReadOnlyList<PanelPressInput> panels, Vector2 point, out PanelKind kind)
    {
        foreach (PanelPressInput panel in panels)
        {
            if (!panel.Still || panel.Grip is not { } grip || !grip.HasPoint(point))
                continue;

            kind = panel.Kind;

            return true;
        }

        kind = default;

        return false;
    }

    /// <summary>
    /// The two walks disagree exactly where the defect was: over a pinned
    /// panel's grip with a temporary panel drawn across it, the old one starts
    /// a resize of the panel underneath and the new one lets the press reach
    /// the pin it was aimed at.
    /// </summary>
    [Fact]
    public void TheWalkThisReplacedAnswersWithTheCoveredPanel()
    {
        Assert.True(FirstInLayoutOrder(Column(), OnThePin, out PanelKind shipped));
        Assert.Equal(PanelKind.Project, shipped);

        Assert.False(
            PanelPressTargets.TryGetGrip(Column(), carried: null, OnThePin, out _, out _));
    }

    /// <summary>
    /// And they agree everywhere nothing overlaps, which is why this went
    /// unnoticed for as long as it did: with the toolbar in its tab, both walks
    /// hand the same grip to the same panel.
    /// </summary>
    [Fact]
    public void TheTwoWalksAgreeWhereNothingOverlaps()
    {
        var tab = Toolbar() with { Surface = new Rect2(0f, 400f, 30f, 30f) };
        var panels = new[] { Project(), tab };

        Assert.True(FirstInLayoutOrder(panels, OnThePin, out PanelKind shipped));

        Assert.True(
            PanelPressTargets.TryGetGrip(
                panels, carried: null, OnThePin, out PanelKind now, out _));

        Assert.Equal(shipped, now);
    }

    [Fact]
    public void APointPastEveryPanelIsNobodys()
    {
        var past = new Vector2(500f, 500f);

        Assert.False(
            PanelPressTargets.TryGetOwner(Column(), carried: null, past, out _));

        Assert.False(
            PanelPressTargets.TryGetGrip(Column(), carried: null, past, out _, out _));

        Assert.False(
            PanelPressTargets.TryGetDraggable(Column(), carried: null, past, out _));
    }

    [Fact]
    public void AnEmptyColumnAnswersNothing() =>
        Assert.False(
            PanelPressTargets.TryGetOwner(
                Array.Empty<PanelPressInput>(), carried: null, OnThePin, out _));
}
