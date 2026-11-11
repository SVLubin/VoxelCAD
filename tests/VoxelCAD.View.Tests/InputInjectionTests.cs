using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The rules behind driving the editor's own interface: who an event belongs to,
/// where a target lands, and what a gesture may not exceed.
/// </summary>
public sealed class InputInjectionTests
{
    // --- the suspend predicate ---

    /// <summary>Nobody's input is suspended while the user has the editor.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void NothingIsSuspendedWhileTheUserHoldsControl(bool isSynthetic, bool isOverAction)
    {
        Assert.False(InputSuspension.ShouldSuspend(holdsControl: false, isSynthetic, isOverAction));
    }

    /// <summary>The rule the banner enforces: the user's input, not input as such.</summary>
    [Fact]
    public void APhysicalEventIsSuspendedWhileAnAgentHoldsControl()
    {
        Assert.True(InputSuspension.ShouldSuspend(
            holdsControl: true,
            isSynthetic: false,
            isOverAction: false));
    }

    [Fact]
    public void ASyntheticEventPassesWhileAnAgentHoldsControl()
    {
        Assert.False(InputSuspension.ShouldSuspend(
            holdsControl: true,
            isSynthetic: true,
            isOverAction: false));
    }

    /// <summary>Stop has to keep working, or an agent could not be interrupted.</summary>
    [Fact]
    public void ThePlaquesOwnButtonKeepsTakingThePhysicalClick()
    {
        Assert.False(InputSuspension.ShouldSuspend(
            holdsControl: true,
            isSynthetic: false,
            isOverAction: true));
    }

    // --- the synthetic mark ---

    /// <summary>
    /// The keymap's own device, so a synthesized key matches the actions a real
    /// one would. Read rather than written down: the number came from whichever
    /// keyboard recorded the bindings.
    /// </summary>
    [Fact]
    public void TheKeyboardDeviceIsTheOneTheBindingsAgreeOn()
    {
        Assert.Equal(16, SyntheticInput.ChooseKeyboardDevice(new[] { 16, 16, 16 }));
    }

    /// <summary>
    /// Bindings that disagree have no single answer that would work, so it falls
    /// back rather than picking a winner nobody chose.
    /// </summary>
    [Fact]
    public void DisagreeingBindingsFallBack()
    {
        Assert.Equal(
            SyntheticInput.FallbackKeyboardDevice,
            SyntheticInput.ChooseKeyboardDevice(new[] { 16, 0 }));
    }

    [Fact]
    public void NoBindingsAtAllFallBack()
    {
        Assert.Equal(
            SyntheticInput.FallbackKeyboardDevice,
            SyntheticInput.ChooseKeyboardDevice(System.Array.Empty<int>()));
    }

    // --- target resolution ---

    [Fact]
    public void AControlResolvesToItsCentre()
    {
        InputTargetResolution resolution = Resolve(
            new[] { Control("Undo", new Rect2(100f, 40f, 80f, 20f)) },
            "Undo");

        Assert.True(resolution.Ok);
        Assert.Equal(new Vector2(140f, 50f), resolution.Point);
        Assert.Equal("Undo", resolution.Control.Reference);
    }

    /// <summary>
    /// The offset is how a row of a list is reached — a list's rows are not
    /// controls of their own, so there is nothing else to aim at inside one.
    /// </summary>
    [Fact]
    public void AnOffsetAimsSomewhereElseInsideTheRect()
    {
        InputTargetResolution resolution = Resolve(
            new[] { Control("Tree", new Rect2(0f, 0f, 200f, 400f)) },
            "Tree",
            new Vector2(0.5f, 0.1f));

        Assert.True(resolution.Ok);
        Assert.Equal(new Vector2(100f, 40f), resolution.Point);
    }

    [Fact]
    public void AnUnknownReferenceIsRefusedAndSaysWhereToLook()
    {
        InputTargetResolution resolution = Resolve(
            new[] { Control("Undo", new Rect2(0f, 0f, 10f, 10f)) },
            "Nonsense");

        Assert.False(resolution.Ok);
        Assert.Contains("Nonsense", resolution.Refusal);
        Assert.Contains("ui", resolution.Refusal);
    }

    [Fact]
    public void AnInvisibleControlIsRefused()
    {
        InputTargetResolution resolution = Resolve(
            new[] { Control("Undo", new Rect2(0f, 0f, 10f, 10f), visible: false) },
            "Undo");

        Assert.False(resolution.Ok);
        Assert.Contains("not on screen", resolution.Refusal);
    }

    [Fact]
    public void ADisabledControlIsRefused()
    {
        InputTargetResolution resolution = Resolve(
            new[] { Control("Undo", new Rect2(0f, 0f, 10f, 10f), disabled: true) },
            "Undo");

        Assert.False(resolution.Ok);
        Assert.Contains("disabled", resolution.Refusal);
    }

    /// <summary>
    /// How a window the platform drew outside this one reads, which is the one
    /// thing here that genuinely cannot be aimed at.
    /// </summary>
    [Fact]
    public void AZeroSizedControlIsRefused()
    {
        InputTargetResolution resolution = Resolve(
            new[] { Control("SettingsDialog", new Rect2(0f, 0f, 0f, 0f)) },
            "SettingsDialog");

        Assert.False(resolution.Ok);
        Assert.Contains("no size", resolution.Refusal);
    }

    /// <summary>
    /// The case worth spending a hit test on: an overlay across the whole window —
    /// the handover dimmer — over the button an agent meant to press.
    /// </summary>
    [Fact]
    public void AControlWithSomethingDrawnOverItIsRefusedNamingTheCover()
    {
        InputTargetResolution resolution = Resolve(
            new[]
            {
                Control("UiRoot/Undo", new Rect2(100f, 40f, 80f, 20f)),
                Control("UiRoot/Banner/Dimmer", new Rect2(0f, 0f, 1000f, 800f))
            },
            "UiRoot/Undo");

        Assert.False(resolution.Ok);
        Assert.Contains("covered", resolution.Refusal);
        Assert.Contains("Dimmer", resolution.Refusal);
    }

    /// <summary>An overlay that ignores the mouse covers nothing; it is only paint.</summary>
    [Fact]
    public void AnOverlayThatDoesNotTakeTheMouseIsNotACover()
    {
        InputTargetResolution resolution = Resolve(
            new[]
            {
                Control("UiRoot/Undo", new Rect2(100f, 40f, 80f, 20f)),
                Control("UiRoot/Tint", new Rect2(0f, 0f, 1000f, 800f), takesMouse: false)
            },
            "UiRoot/Undo");

        Assert.True(resolution.Ok);
    }

    /// <summary>
    /// A control's own child answering for it is the button's label taking the
    /// click, which is reaching the button, not covering it.
    /// </summary>
    [Fact]
    public void ADescendantAnsweringForTheTargetStillReachesIt()
    {
        InputTargetResolution resolution = Resolve(
            new[]
            {
                Control("UiRoot/Undo", new Rect2(100f, 40f, 80f, 20f)),
                Control("UiRoot/Undo/Label", new Rect2(105f, 45f, 70f, 10f))
            },
            "UiRoot/Undo");

        Assert.True(resolution.Ok);
    }

    /// <summary>
    /// And a container answering for a control that ignores the mouse is the
    /// common case, not a failure — refusing it would refuse most labels.
    /// </summary>
    [Fact]
    public void AnAncestorAnsweringForTheTargetStillReachesIt()
    {
        InputTargetResolution resolution = Resolve(
            new[]
            {
                Control("UiRoot/Row", new Rect2(0f, 0f, 400f, 60f)),
                Control("UiRoot/Row/Label", new Rect2(10f, 10f, 80f, 20f), takesMouse: false)
            },
            "UiRoot/Row/Label");

        Assert.True(resolution.Ok);
    }

    /// <summary>
    /// A popup is a child of whichever button owns it, so tree order puts it under
    /// half the interface — while the engine draws it over all of it. Ranking by
    /// position alone refused a click on an open File menu, naming the dock behind
    /// it as the cover.
    /// </summary>
    [Fact]
    public void AWindowDrawnOverTheRestIsNotCoveredByWhatComesAfterItInTheTree()
    {
        InputTargetResolution resolution = Resolve(
            new[]
            {
                Control("UiRoot/Dock/FileMenu/Popup", new Rect2(26f, 103f, 400f, 462f), layer: 1),
                Control("UiRoot/Dock/HierarchyTree", new Rect2(26f, 438f, 722f, 621f))
            },
            "UiRoot/Dock/FileMenu/Popup",
            new Vector2(0.4f, 0.88f));

        Assert.True(resolution.Ok);
    }

    /// <summary>And the other way round: what is under a popup really is covered.</summary>
    [Fact]
    public void AControlUnderAWindowIsCoveredByIt()
    {
        InputTargetResolution resolution = Resolve(
            new[]
            {
                Control("UiRoot/Dock/FileMenu/Popup", new Rect2(0f, 0f, 400f, 400f), layer: 1),
                Control("UiRoot/Dock/HierarchyTree", new Rect2(0f, 0f, 700f, 600f))
            },
            "UiRoot/Dock/HierarchyTree");

        Assert.False(resolution.Ok);
        Assert.Contains("Popup", resolution.Refusal);
    }

    // --- limits and the drag path ---

    [Fact]
    public void ADragEndsExactlyWhereItWasAimed()
    {
        IReadOnlyList<Vector2> path = InputLimits.ComposePath(
            new Vector2(0f, 0f),
            new Vector2(100f, 50f),
            4);

        Assert.Equal(4, path.Count);
        Assert.Equal(new Vector2(25f, 12.5f), path[0]);
        Assert.Equal(new Vector2(100f, 50f), path[^1]);
    }

    /// <summary>
    /// A gesture holds the queue while it runs, so an unbounded one is an editor
    /// that has stopped answering. Clamped rather than refused, like every other
    /// out-of-range value in this project.
    /// </summary>
    [Fact]
    public void AnAbsurdStepCountIsClampedRatherThanRefused()
    {
        Assert.Equal(InputLimits.MaximumSteps, InputLimits.ClampSteps(100_000));
        Assert.Equal(InputLimits.MinimumSteps, InputLimits.ClampSteps(0));
        Assert.Equal(InputLimits.MaximumSteps, InputLimits.ComposePath(Vector2.Zero, Vector2.One, 9999).Count);
    }

    [Fact]
    public void ClicksAndNotchesAreClampedTheSameWay()
    {
        Assert.Equal(InputLimits.MaximumClicks, InputLimits.ClampClicks(50));
        Assert.Equal(1, InputLimits.ClampClicks(0));
        Assert.Equal(InputLimits.MaximumNotches, InputLimits.ClampNotches(900));
        Assert.Equal(-InputLimits.MaximumNotches, InputLimits.ClampNotches(-900));
    }

    /// <summary>
    /// A point outside the window is a caller's arithmetic being off, and the edge
    /// is the nearest place they could have meant.
    /// </summary>
    [Fact]
    public void APointOutsideTheWindowIsClampedIntoIt()
    {
        var window = new Vector2I(1489, 649);

        Assert.Equal(new Vector2(1488f, 648f), InputLimits.ClampPoint(new Vector2(9000f, 9000f), window));
        Assert.Equal(new Vector2(0f, 0f), InputLimits.ClampPoint(new Vector2(-40f, -1f), window));
    }

    private static InputTargetResolution Resolve(
        IReadOnlyList<UiControl> controlsList,
        string reference) =>
        Resolve(controlsList, reference, new Vector2(0.5f, 0.5f));

    private static InputTargetResolution Resolve(
        IReadOnlyList<UiControl> controlsList,
        string reference,
        Vector2 offset) =>
        InputTargeting.Resolve(controlsList, reference, offset);

    private static UiControl Control(
        string reference,
        Rect2 rect,
        bool visible = true,
        bool disabled = false,
        bool takesMouse = true,
        int layer = 0) =>
        new(
            reference,
            "Button",
            string.Empty,
            rect,
            visible,
            disabled,
            Focused: false,
            takesMouse,
            layer);
}
