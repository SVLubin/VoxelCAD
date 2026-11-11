using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Presets and tuning from the dispatcher. An agent must be able to do what the
/// inspector does — that is the rule, not a courtesy — and to read back what it
/// did without opening the editor.
/// </summary>
public sealed class MaterialAppearanceCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public MaterialAppearanceCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-material name=Wall color=1,1,1");
    }

    [Fact]
    public void APresetSetsTheShadingAndTheColour()
    {
        _dispatcher.RunOk("set-material-preset material=Wall preset=Brick");

        Dictionary<string, object?> wall = Material("Wall");
        var appearance = (Dictionary<string, object?>)wall["appearance"]!;

        Assert.Equal("Brick", wall["preset"]);
        Assert.Equal("Brick", appearance["finish"]);

        var color = (float[])wall["color"]!;
        Assert.True(color[0] > color[1], "Brick should be redder than it is green.");
    }

    /// <summary>
    /// Every argument optional, and what is omitted left alone: one call to nudge
    /// one value is the case this command exists for.
    /// </summary>
    [Fact]
    public void TuningOneValueLeavesTheRestAlone()
    {
        _dispatcher.RunOk("set-material-preset material=Wall preset=BrushedSteel");
        _dispatcher.RunOk("set-material-appearance material=Wall roughness=0.7");

        var appearance = (Dictionary<string, object?>)Material("Wall")["appearance"]!;

        Assert.Equal(0.7f, (float)appearance["roughness"]!, 3);
        Assert.Equal("Brushed", appearance["finish"]);
        Assert.Equal(0.85f, (float)appearance["metallic"]!, 3);
    }

    [Fact]
    public void ATunedMaterialReportsItselfAsCustom()
    {
        _dispatcher.RunOk("set-material-preset material=Wall preset=BrushedSteel");
        _dispatcher.RunOk("set-material-appearance material=Wall metallic=0.123");

        Assert.Equal("custom", Material("Wall")["preset"]);
    }

    [Fact]
    public void OutOfRangeValuesAreClampedRatherThanRefused()
    {
        _dispatcher.RunOk("set-material-appearance material=Wall scale=900 relief=-5");

        var appearance = (Dictionary<string, object?>)Material("Wall")["appearance"]!;

        Assert.Equal(16f, (float)appearance["scale"]!, 3);
        Assert.Equal(0f, (float)appearance["relief"]!, 3);
    }

    [Fact]
    public void AnUnknownPresetIsRefusedWithAReadableMessage()
    {
        var message = _dispatcher.RunFails("set-material-preset material=Wall preset=Marzipan");

        Assert.Contains("preset", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The list is on the surface too: an agent that cannot ask what the presets
    /// are has to guess at their names.
    /// </summary>
    [Fact]
    public void ThePresetsCanBeListed()
    {
        CommandResult result = _dispatcher.RunOk("material-presets");
        var rows = (IReadOnlyList<object?>)result.Value!;

        Assert.Contains(
            rows.Cast<Dictionary<string, object?>>(),
            row => (string)row["preset"]! == "BrushedSteel");

        // Grouped, so a list of nearly thirty is readable.
        Assert.Contains(
            rows.Cast<Dictionary<string, object?>>(),
            row => (string)row["group"]! == "Metal");
    }

    /// <summary>Undo takes back the appearance and the colour together.</summary>
    [Fact]
    public void APresetUndoesInOneStep()
    {
        _dispatcher.RunOk("set-material-preset material=Wall preset=Gold");
        _dispatcher.RunOk("undo");

        Dictionary<string, object?> wall = Material("Wall");
        var color = (float[])wall["color"]!;

        Assert.Equal(1f, color[0], 3);
        Assert.Equal("Plaster", wall["preset"]);
    }

    private Dictionary<string, object?> Material(string name)
    {
        CommandResult result = _dispatcher.RunOk("materials");

        return ((IReadOnlyList<object?>)result.Value!)
            .Cast<Dictionary<string, object?>>()
            .Single(row => (string)row["name"]! == name);
    }
}
