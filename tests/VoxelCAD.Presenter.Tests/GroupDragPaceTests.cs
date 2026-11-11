using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Which element of a group drag quantises the pull for the rest of them. A group
/// has to keep its shape while it is dragged, so the step has to be one every
/// member can take — which means a constrained member sets it wherever there is
/// one.
/// </summary>
public sealed class GroupDragPaceTests
{
    [Fact]
    public void AGroupWithNothingConstrainedFollowsItsAnchor()
    {
        var modes = new[] { FieldMode.Float, FieldMode.Float, FieldMode.Float };

        Assert.Equal(2, GroupDragPace.Choose(modes, anchorIndex: 2));
    }

    [Fact]
    public void AConstrainedElementSetsTheStep()
    {
        var modes = new[] { FieldMode.Float, FieldMode.Integer, FieldMode.Float };

        Assert.Equal(1, GroupDragPace.Choose(modes, anchorIndex: 0));
    }

    /// <summary>
    /// Even when the anchor — the element the handles sit on and the readout speaks
    /// for — is the free one. The group's shape wins over whose handle was grabbed.
    /// </summary>
    [Fact]
    public void EvenWhenTheAnchorIsTheFreeOne()
    {
        var modes = new[] { FieldMode.Integer, FieldMode.Float };

        Assert.Equal(0, GroupDragPace.Choose(modes, anchorIndex: 1));
    }

    /// <summary>
    /// Snap90 is the rotation channel's constrained mode and counts the same way:
    /// within one channel there are exactly two modes, one free and one not.
    /// </summary>
    [Fact]
    public void Snap90CountsAsConstrainedToo()
    {
        var modes = new[] { FieldMode.Float, FieldMode.Snap90 };

        Assert.Equal(1, GroupDragPace.Choose(modes, anchorIndex: 0));
    }

    /// <summary>
    /// Which constrained one is picked does not matter — they all quantise a pull
    /// to the same whole number of steps — so the first is taken and the answer is
    /// stated rather than left to look arbitrary.
    /// </summary>
    [Fact]
    public void TheFirstConstrainedOneIsTaken()
    {
        var modes = new[] { FieldMode.Float, FieldMode.Integer, FieldMode.Integer };

        Assert.Equal(1, GroupDragPace.Choose(modes, anchorIndex: 2));
    }
}
