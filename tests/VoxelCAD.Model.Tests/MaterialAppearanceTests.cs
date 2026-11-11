using System;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// What a material's appearance is, what a preset does to it, and what survives a
/// save. The preset table is the piece two surfaces share — the inspector and the
/// dispatcher — so what it answers is worth pinning.
/// </summary>
public class MaterialAppearanceTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"appearance-{Guid.NewGuid():N}.vcad");

    private readonly IProject _project = ModelFactory.CreateProject();

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void ANewMaterialIsMatte()
    {
        Guid id = _project.CreateMaterial("Slate", Colors.Gray);
        _project.TryGetMaterial(id, out IMaterial material);

        Assert.Equal(MaterialAppearance.Default, material.Appearance);
        Assert.Equal(MaterialFinish.Smooth, material.Appearance.Finish);
    }

    /// <summary>
    /// One record for the appearance and the colour together. Undoing a preset
    /// must not leave a material wearing Brick's colour and Steel's shading.
    /// </summary>
    [Fact]
    public void APresetAppliesItsColourAndItsShadingAsOneAct()
    {
        Guid id = _project.CreateMaterial("Wall", Colors.White);
        int before = _project.History.Records.Count;

        _project.SetMaterialPreset(id, MaterialPreset.Brick);
        _project.TryGetMaterial(id, out IMaterial material);

        MaterialPresetEntry entry = MaterialPresetTable.Get(MaterialPreset.Brick);

        Assert.Equal(entry.Appearance, material.Appearance);
        Assert.Equal(entry.Color, material.Color);
        Assert.Equal(before + 1, _project.History.Records.Count);

        _project.History.Undo();
        _project.TryGetMaterial(id, out material);

        Assert.Equal(Colors.White, material.Color);
        Assert.Equal(MaterialAppearance.Default, material.Appearance);
    }

    /// <summary>
    /// Every preset has to be somebody's answer to "which one is this", or the
    /// list can offer a material the row can never show as selected.
    /// </summary>
    [Fact]
    public void EveryPresetMatchesItself()
    {
        foreach (MaterialPreset preset in MaterialPresetTable.All)
        {
            MaterialPresetEntry entry = MaterialPresetTable.Get(preset);

            Assert.Equal(preset, MaterialPresetTable.Match(entry.Appearance));
        }
    }

    /// <summary>
    /// Matched on the appearance alone. A preset list that fell to Custom the
    /// moment somebody picked a shade would be no use at all.
    /// </summary>
    [Fact]
    public void RecolouringLeavesAMaterialOnItsPreset()
    {
        Guid id = _project.CreateMaterial("Wall", Colors.White);

        _project.SetMaterialPreset(id, MaterialPreset.Brick);
        _project.SetMaterialColor(id, new Color(0.2f, 0.6f, 0.3f));
        _project.TryGetMaterial(id, out IMaterial material);

        Assert.Equal(MaterialPreset.Brick, MaterialPresetTable.Match(material.Appearance));
    }

    /// <summary>
    /// Two presets with the same numbers would be one entry the list can never
    /// show as chosen, because which preset a material is gets asked of its values
    /// alone. The three metals nearly were exactly that, differing only in colour.
    /// </summary>
    [Fact]
    public void NoTwoPresetsShareAnAppearance()
    {
        var seenDictionary = new System.Collections.Generic.Dictionary<MaterialAppearance, MaterialPreset>();

        foreach (MaterialPreset preset in MaterialPresetTable.All)
        {
            MaterialAppearance appearance = MaterialPresetTable.Get(preset).Appearance;

            Assert.False(
                seenDictionary.ContainsKey(appearance),
                $"{preset} has the same appearance as {(seenDictionary.TryGetValue(appearance, out MaterialPreset other) ? other : default)}");

            seenDictionary[appearance] = preset;
        }
    }

    /// <summary>
    /// A material nobody has touched reads as Plaster rather than as Custom: white
    /// and matte is what plaster is, and Custom is a word for a decision somebody
    /// made.
    /// </summary>
    [Fact]
    public void AnUntouchedMaterialIsPlaster()
    {
        Guid id = _project.CreateMaterial("Fresh", Colors.White);
        _project.TryGetMaterial(id, out IMaterial material);

        Assert.Equal(MaterialPreset.Plaster, MaterialPresetTable.Match(material.Appearance));
    }

    [Fact]
    public void TuningOneValueLeavesTheListOnCustom()
    {
        Guid id = _project.CreateMaterial("Wall", Colors.White);

        _project.SetMaterialPreset(id, MaterialPreset.Brick);
        _project.TryGetMaterial(id, out IMaterial material);

        _project.SetMaterialAppearance(id, material.Appearance with { Roughness = 0.123f });
        _project.TryGetMaterial(id, out material);

        Assert.Null(MaterialPresetTable.Match(material.Appearance));
    }

    /// <summary>Clamped rather than refused: a slider must not be able to throw.</summary>
    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        Guid id = _project.CreateMaterial("Odd", Colors.White);

        _project.SetMaterialAppearance(id, new MaterialAppearance(
            Roughness: 9f,
            Metallic: -4f,
            Emission: 12f,
            Finish: (MaterialFinish)999,
            FinishScale: 900f,
            FinishRelief: -3f));

        _project.TryGetMaterial(id, out IMaterial material);

        Assert.Equal(1f, material.Appearance.Roughness);
        Assert.Equal(0f, material.Appearance.Metallic);
        Assert.Equal(1f, material.Appearance.Emission);
        Assert.Equal(MaterialFinish.Smooth, material.Appearance.Finish);
        Assert.Equal(MaterialAppearanceLimits.MaximumScale, material.Appearance.FinishScale);
        Assert.Equal(0f, material.Appearance.FinishRelief);
    }

    [Fact]
    public void AnAppearanceSurvivesASaveAndALoad()
    {
        Guid id = _project.CreateMaterial("Panel", Colors.White);
        _project.SetMaterialPreset(id, MaterialPreset.BrushedSteel);
        _project.TryGetMaterial(id, out IMaterial before);

        _project.Save(_path);

        IProject reopened = ModelFactory.LoadProject(_path);
        reopened.TryGetMaterial(id, out IMaterial after);

        Assert.Equal(before.Appearance, after.Appearance);
        Assert.Equal(before.Color, after.Color);
    }

    /// <summary>
    /// A material still at the defaults writes nothing, so a file that never
    /// touched appearance is the file it always was.
    /// </summary>
    [Fact]
    public void ADefaultMaterialWritesNoAppearanceAtAll()
    {
        _project.CreateMaterial("Plain", Colors.White);
        _project.Save(_path);

        var yaml = File.ReadAllText(_path);

        Assert.DoesNotContain("appearance:", yaml);
    }

    /// <summary>
    /// The finish is written as a name, so a build that does not know it shows the
    /// material flat rather than refusing to open the project.
    /// </summary>
    [Fact]
    public void AnUnknownFinishReadsBackAsNone()
    {
        Guid id = _project.CreateMaterial("Panel", Colors.White);
        _project.SetMaterialPreset(id, MaterialPreset.BrushedSteel);
        _project.Save(_path);

        File.WriteAllText(_path, File.ReadAllText(_path).Replace("Brushed", "Sparkles"));

        IProject reopened = ModelFactory.LoadProject(_path);
        reopened.TryGetMaterial(id, out IMaterial material);

        Assert.Equal(MaterialFinish.Smooth, material.Appearance.Finish);

        // And nothing else was lost with it.
        Assert.Equal(
            MaterialPresetTable.Get(MaterialPreset.BrushedSteel).Appearance.Metallic,
            material.Appearance.Metallic);
    }
}
