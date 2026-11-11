using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What the field-mode segments say. The control itself is a Godot node, so what
/// a test can hold is the words on it — which is the half that was wrong: a mode
/// row read ".0 1 90°", and only one of those three is a thing anybody would
/// guess the meaning of.
/// </summary>
public sealed class ModeToggleTests
{
    /// <summary>
    /// Integer is a step of one <em>cell</em> on position and scale and one
    /// <em>degree</em> on rotation. Calling both "1" was the toggle saying they
    /// were the same thing.
    /// </summary>
    [Fact]
    public void TheWholeStepIsLabelledInTheChannelsOwnUnit()
    {
        Assert.Equal("1", ModeToggle.LabelFor(FieldMode.Integer, angular: false));
        Assert.Equal("1°", ModeToggle.LabelFor(FieldMode.Integer, angular: true));
    }

    /// <summary>
    /// The free mode is the one that is not a step, so it is the one that gets a
    /// word rather than a number.
    /// </summary>
    [Fact]
    public void TheFreeModeIsAWord()
    {
        Assert.Equal("Free", ModeToggle.LabelFor(FieldMode.Float, angular: false));
        Assert.Equal("Free", ModeToggle.LabelFor(FieldMode.Float, angular: true));
    }

    [Fact]
    public void QuarterTurnsSayNinetyDegrees() =>
        Assert.Equal("90°", ModeToggle.LabelFor(FieldMode.Snap90, angular: true));

    /// <summary>
    /// Every mode has a label and a hint on both sides of the angular question,
    /// so a mode added tomorrow cannot arrive as "?" with nothing to hover.
    /// Enumerated rather than asked of the model's own rule, which lives on the
    /// far side of a wall this assembly does not cross.
    /// </summary>
    [Fact]
    public void EveryModeIsSpeltOut()
    {
        foreach (FieldMode mode in Enum.GetValues<FieldMode>())
        foreach (bool angular in new[] { false, true })
        {
            Assert.NotEqual("?", ModeToggle.LabelFor(mode, angular));
            Assert.NotEqual(string.Empty, ModeToggle.HintFor(mode, angular));
        }
    }
}
