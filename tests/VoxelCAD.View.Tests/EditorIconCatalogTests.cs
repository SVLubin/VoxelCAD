using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The catalogue names icons and the factory registers them; these hold the two
/// sides together, so a row cannot ask the theme for a glyph nobody drew.
/// </summary>
public sealed class EditorIconCatalogTests
{
    [Fact]
    public void EveryActionWearsARegisteredIcon()
    {
        foreach (ActionKind kind in Enum.GetValues<ActionKind>())
            Assert.Contains(EditorIconCatalog.ForAction(kind), EditorIconFactory.Names);
    }

    [Fact]
    public void EveryRowWearsARegisteredIconOrNone()
    {
        foreach (SelectionKind kind in Enum.GetValues<SelectionKind>())
        foreach (ComponentKind component in Enum.GetValues<ComponentKind>())
        {
            string? icon = EditorIconCatalog.ForRow(kind, component);

            if (icon is not null)
                Assert.Contains(icon, EditorIconFactory.Names);
        }
    }

    [Fact]
    public void EveryToolWearsARegisteredIcon()
    {
        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
            Assert.Contains(EditorIconCatalog.ForTool(tool), EditorIconFactory.Names);
    }

    /// <summary>
    /// The registration test above cannot see a tool falling through to the
    /// brush's glyph, because the brush's glyph is registered. Two tools on one
    /// strip wearing one picture is a tool nobody can find.
    /// </summary>
    [Fact]
    public void NoTwoToolsWearTheSameGlyph()
    {
        var seen = new System.Collections.Generic.HashSet<string>();

        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
            Assert.True(seen.Add(EditorIconCatalog.ForTool(tool)), $"{tool} shares its glyph");
    }

    [Fact]
    public void EveryPanelWearsARegisteredIcon()
    {
        foreach (PanelKind kind in Enum.GetValues<PanelKind>())
            Assert.Contains(EditorIconCatalog.ForPanel(kind), EditorIconFactory.Names);
    }

    [Fact]
    public void EveryCreateKindWearsARegisteredPlusBadgedIcon()
    {
        foreach (CreateKind kind in Enum.GetValues<CreateKind>())
            Assert.Contains(EditorIconCatalog.ForCreate(kind), EditorIconFactory.Names);
    }

    /// <summary>
    /// The rail is a picture per section, so a section added tomorrow must not
    /// be the one row with a blank beside its word.
    /// </summary>
    [Fact]
    public void EverySettingsSectionWearsARegisteredIcon()
    {
        foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
            Assert.Contains(EditorIconCatalog.ForSettingsSection(section), EditorIconFactory.Names);
    }

    /// <summary>Named separately because nothing enumerates it into place.</summary>
    [Fact]
    public void TheSearchGlyphIsRegistered() =>
        Assert.Contains(EditorIconCatalog.Search, EditorIconFactory.Names);

    /// <summary>The one distinction the target's kind cannot make on its own.</summary>
    [Fact]
    public void ElementRowsAreToldApartByTheirComponent()
    {
        string? group = EditorIconCatalog.ForRow(SelectionKind.Element, ComponentKind.None);
        string? model = EditorIconCatalog.ForRow(SelectionKind.Element, ComponentKind.VoxelModel);
        string? prefab = EditorIconCatalog.ForRow(SelectionKind.Element, ComponentKind.PrefabInstance);

        Assert.NotNull(group);
        Assert.NotEqual(group, model);
        Assert.NotEqual(group, prefab);
        Assert.NotEqual(model, prefab);
    }

    /// <summary>A material row's chip is its own swatch, not a shared glyph.</summary>
    [Fact]
    public void AMaterialRowCarriesNoCatalogueIcon() =>
        Assert.Null(EditorIconCatalog.ForRow(SelectionKind.Material, ComponentKind.None));
}
