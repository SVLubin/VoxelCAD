using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What a material picker calls its rows. Two cases the registry actually
/// reaches, and both of them make a name alone unusable.
/// </summary>
public sealed class MaterialChoiceLabelsTests
{
    [Fact]
    public void AnOrdinaryNameIsItself()
    {
        Assert.Equal(
            new[] { "Red", "Blue" },
            MaterialChoiceLabels.Compose(new[]
            {
                new MaterialChoice(Guid.NewGuid(), "Red"),
                new MaterialChoice(Guid.NewGuid(), "Blue"),
            }));
    }

    /// <summary>
    /// A blank label is how a context menu spells a separator, so a nameless
    /// material was drawn as a line and could not be pressed at all — and nothing
    /// in the model refuses an empty name.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ANamelessMaterialIsNamedAsNameless(string name)
    {
        IReadOnlyList<string> labels =
            MaterialChoiceLabels.Compose(new[] { new MaterialChoice(Guid.NewGuid(), name) });

        Assert.Equal(MaterialChoiceLabels.Unnamed, labels[0]);
        Assert.NotEmpty(labels[0]);
    }

    /// <summary>
    /// Names are not unique, and two identical rows repaint every cell in one of
    /// them with nothing on the card saying which.
    /// </summary>
    [Fact]
    public void TwoMaterialsSharingANameCarryTheHeadOfTheirIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        IReadOnlyList<string> labels = MaterialChoiceLabels.Compose(new[]
        {
            new MaterialChoice(first, "Red"),
            new MaterialChoice(second, "Red"),
            new MaterialChoice(Guid.NewGuid(), "Blue"),
        });

        Assert.NotEqual(labels[0], labels[1]);
        Assert.StartsWith("Red", labels[0]);
        Assert.Contains(first.ToString("N")[..MaterialChoiceLabels.IdLength], labels[0]);
        Assert.Contains(second.ToString("N")[..MaterialChoiceLabels.IdLength], labels[1]);

        // And the one that is not shared is left alone.
        Assert.Equal("Blue", labels[2]);
    }

    /// <summary>The order is the caller's, because the picker numbers its references by it.</summary>
    [Fact]
    public void OneLabelPerChoiceInTheOrderGiven()
    {
        var choices = new[]
        {
            new MaterialChoice(Guid.NewGuid(), "C"),
            new MaterialChoice(Guid.NewGuid(), "A"),
            new MaterialChoice(Guid.NewGuid(), "B"),
        };

        Assert.Equal(new[] { "C", "A", "B" }, MaterialChoiceLabels.Compose(choices));
    }
}
