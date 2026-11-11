using System;
using System.Linq;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A ready-made material whose colour carries an alpha has to declare itself
/// see-through, or the alpha means nothing and the preset is a solid slab.
/// </summary>
public class PresetTransparencyTests
{
    [Fact]
    public void EveryPresetWithAnAlphaDeclaresItself()
    {
        foreach (MaterialPreset preset in MaterialPresetTable.All)
        {
            MaterialPresetEntry entry = MaterialPresetTable.Get(preset);

            if (entry.Color.A < 1f)
                Assert.True(entry.Appearance.Transparent, $"{preset} has an alpha and hides it");
        }
    }

    /// <summary>
    /// And the other way: declaring it with a full alpha would be a switch that
    /// turns the sorted pass on for a surface nothing shows through.
    /// </summary>
    [Fact]
    public void NoPresetDeclaresItselfWithoutMeaningIt()
    {
        foreach (MaterialPreset preset in MaterialPresetTable.All)
        {
            MaterialPresetEntry entry = MaterialPresetTable.Get(preset);

            if (entry.Appearance.Transparent)
                Assert.True(entry.Color.A < 1f, $"{preset} says see-through and is not");
        }
    }

    /// <summary>
    /// A preset is matched on every field of the appearance, transparency
    /// included. Left out, a material stayed "chosen" as Water or Glass after its
    /// transparency was switched off — the inspector naming a preset whose
    /// defining property the material no longer has.
    /// </summary>
    [Fact]
    public void APresetIsNoLongerMatchedOnceItsTransparencyIsSwitchedOff()
    {
        foreach (MaterialPreset preset in MaterialPresetTable.All)
        {
            MaterialAppearance appearance = MaterialPresetTable.Get(preset).Appearance;

            if (!appearance.Transparent)
                continue;

            Assert.Equal(preset, MaterialPresetTable.Match(appearance));
            Assert.Null(MaterialPresetTable.Match(appearance with { Transparent = false }));
        }
    }

    /// <summary>
    /// And an opaque preset is not matched by the same values turned see-through,
    /// which is the same rule from the other side.
    /// </summary>
    [Fact]
    public void AnOpaquePresetIsNotMatchedOnceItIsMadeSeeThrough()
    {
        foreach (MaterialPreset preset in MaterialPresetTable.All)
        {
            MaterialAppearance appearance = MaterialPresetTable.Get(preset).Appearance;

            if (appearance.Transparent)
                continue;

            Assert.Equal(preset, MaterialPresetTable.Match(appearance));
            Assert.Null(MaterialPresetTable.Match(appearance with { Transparent = true }));
        }
    }
}
