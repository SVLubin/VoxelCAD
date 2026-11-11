using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A saved project must come back exactly, including the things that are easy to
/// lose: field modes, ids, and names YAML would rather read as something else.
/// </summary>
public sealed class SerializationTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public SerializationTests()
    {
        _scene = _project.Scenes[0];
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void AnEmptyProjectSurvivesTheRoundTrip()
    {
        IProject loaded = SaveAndLoad();

        Assert.Single(loaded.Materials);
        Assert.Single(loaded.Scenes);
        Assert.Empty(loaded.Scenes[0].Children);
    }

    [Fact]
    public void IdsAreStableAcrossTheRoundTrip()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid material = _project.Materials[0].Id;

        IProject loaded = SaveAndLoad();

        Assert.Equal(_scene.Id, loaded.Scenes[0].Id);
        Assert.Equal(element, loaded.Scenes[0].Children[0].Id);
        Assert.Equal(material, loaded.Materials[0].Id);
    }

    [Fact]
    public void TheHierarchyKeepsItsShapeAndOrder()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        _scene.CreateElement(parent, ComponentKind.None, "First");
        _scene.CreateElement(parent, ComponentKind.None, "Second");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Sibling");

        IScene loaded = SaveAndLoad().Scenes[0];

        Assert.Equal(
            new[] { "Parent", "Sibling" },
            loaded.Children.Select(c => c.Name).ToArray());
        Assert.Equal(
            new[] { "First", "Second" },
            loaded.Children[0].Children.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void TransformValuesAndModesBothSurvive()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(element, TransformChannel.Position, new Vector3(11f, 0f, -3f));
        _scene.SetFieldMode(element, TransformChannel.Rotation, FieldMode.Snap90);
        _scene.SetTransform(element, TransformChannel.Rotation, new Vector3(0f, 180f, 0f));
        _scene.SetFieldMode(element, TransformChannel.Scale, FieldMode.Float);
        _scene.SetTransform(element, TransformChannel.Scale, new Vector3(1.5f, 2f, 0.25f));

        IHierarchyElement loaded = SaveAndLoad().Scenes[0].Children[0];

        Assert.Equal(new Vector3(11f, 0f, -3f), loaded.Transform.Position);
        Assert.Equal(new Vector3(0f, 180f, 0f), loaded.Transform.RotationDegrees);
        Assert.Equal(new Vector3(1.5f, 2f, 0.25f), loaded.Transform.Scale);
        Assert.Equal(FieldMode.Integer, loaded.TransformModes.Position);
        Assert.Equal(FieldMode.Snap90, loaded.TransformModes.Rotation);
        Assert.Equal(FieldMode.Float, loaded.TransformModes.Scale);
    }

    [Fact]
    public void VoxelsAndTheirMaterialsSurvive()
    {
        Guid stone = _project.Materials[0].Id;
        Guid glass = _project.CreateMaterial("Glass", new Color(0.4f, 0.7f, 1f, 0.45f));
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        _scene.SetVoxels(element, new[]
        {
            new VoxelEdit(new Vector3I(0, 0, 0), stone),
            new VoxelEdit(new Vector3I(1, 2, 3), glass),
            new VoxelEdit(new Vector3I(31, 31, 31), stone)
        });

        var loaded = (IVoxelModel)SaveAndLoad().Scenes[0].Children[0].Component!;

        Assert.Equal(3, loaded.Voxels.Count);
        Assert.Equal(stone, loaded.Voxels[new Vector3I(0, 0, 0)]);
        Assert.Equal(glass, loaded.Voxels[new Vector3I(1, 2, 3)]);
        Assert.Equal(stone, loaded.Voxels[new Vector3I(31, 31, 31)]);
    }

    [Fact]
    public void AGroupElementComesBackWithoutAComponent()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.Null(SaveAndLoad().Scenes[0].Children[0].Component);
    }

    [Fact]
    public void MaterialColoursSurviveIncludingAlpha()
    {
        Guid glass = _project.CreateMaterial("Glass", new Color(0.4f, 0.7f, 1f, 0.45f));

        IMaterial loaded = SaveAndLoad().Materials.Single(m => m.Id == glass);

        Assert.Equal("Glass", loaded.Name);
        Assert.True(new Color(0.4f, 0.7f, 1f, 0.45f).IsEqualApprox(loaded.Color));
    }

    [Theory]
    [InlineData("NO")]
    [InlineData("no")]
    [InlineData("yes")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("~")]
    [InlineData("1.10")]
    [InlineData("0x10")]
    [InlineData("12:30")]
    [InlineData("- dash")]
    [InlineData("2024-01-01")]
    public void NamesYamlWouldReadAsSomethingElseSurviveVerbatim(string name)
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, name);

        Assert.Equal(name, SaveAndLoad().Scenes[0].Children[0].Name);
    }

    [Fact]
    public void TheSceneRegionSurvives()
    {
        IProject project = ModelFactory.CreateProject(new Vector3I(64, 16, 48));
        project.Save(_path);

        Assert.Equal(new Vector3I(64, 16, 48), ModelFactory.LoadProject(_path).Scenes[0].Size);
    }

    [Fact]
    public void ALoadedProjectHasNothingToUndo()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.Empty(SaveAndLoad().History.Records);
    }

    [Fact]
    public void ALoadedProjectIsFullyEditable()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(element, new[] { new VoxelEdit(Vector3I.Zero, _project.Materials[0].Id) });

        IProject loaded = SaveAndLoad();
        IScene scene = loaded.Scenes[0];

        scene.RenameElement(element, "Renamed");
        scene.SetVoxels(element, new[] { new VoxelEdit(new Vector3I(1, 1, 1), loaded.Materials[0].Id) });
        loaded.History.Undo();
        loaded.History.Undo();

        Assert.Equal("Model", scene.Element(element).Name);
    }

    [Fact]
    public void TheBlobIsWrappedSoDiffsStayLocal()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(
            element,
            Enumerable.Range(0, 512)
                .Select(i => new VoxelEdit(
                    new Vector3I(i % 8, i / 8 % 8, i / 64),
                    _project.Materials[0].Id))
                .ToArray());

        _project.Save(_path);
        var yaml = File.ReadAllText(_path);

        Assert.Contains("voxels: |", yaml);
        Assert.True(yaml.Split('\n').All(line => line.Length < 200), "a line grew unreadably long");
        Assert.DoesNotContain("\"voxels\":", yaml);
    }

    [Fact]
    public void OrdinaryScalarsStayUnquotedSoTheFileReadsWell()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");
        _project.Save(_path);

        var yaml = File.ReadAllText(_path);

        Assert.Contains("name: Bracket", yaml);

        // The number is not what this is about, and pinning it made the test fail
        // on every schema bump for no reason: what it holds is that a plain scalar
        // is written plain.
        Assert.Matches(@"(?m)^version: \d+$", yaml);
        Assert.DoesNotContain("\"name\":", yaml);
    }

    [Fact]
    public void ASaveLeavesNoWritingFileBehind()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");
        _project.Save(_path);

        Assert.False(File.Exists(_path + ".writing"));
    }

    /// <summary>
    /// A save that cannot finish must leave the file it was replacing: a truncated
    /// project is the one loss no recovery snapshot can repair.
    /// </summary>
    [Fact]
    public void AFailedSaveLeavesThePreviousFileIntact()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Kept");
        _project.Save(_path);
        byte[] before = File.ReadAllBytes(_path);

        Directory.CreateDirectory(_path + ".writing");

        try
        {
            _scene.CreateElement(_scene.Id, ComponentKind.None, "Lost");

            Assert.ThrowsAny<Exception>(() => _project.Save(_path));
            Assert.Equal(before, File.ReadAllBytes(_path));
        }
        finally
        {
            Directory.Delete(_path + ".writing");
        }
    }

    [Fact]
    public void AnUnknownExtensionIsRefused()
    {
        Assert.Throws<ArgumentException>(() => _project.Save(Path.ChangeExtension(_path, ".txt")));
    }

    [Fact]
    public void EverySceneComesBackWithItsNameAndRegion()
    {
        _project.RenameScene(_scene.Id, "Housing");
        Guid second = _project.CreateScene("Lid", new Vector3I(4, 5, 6));
        _project.TryGetScene(second, out IScene lid);
        lid.CreateElement(lid.Id, ComponentKind.None, "Hinge");

        IProject loaded = SaveAndLoad();

        Assert.Equal(new[] { "Housing", "Lid" }, loaded.Scenes.Select(scene => scene.Name));
        Assert.Equal(new Vector3I(4, 5, 6), loaded.Scenes[1].Size);
        Assert.Equal("Hinge", loaded.Scenes[1].Children[0].Name);
    }

    /// <summary>
    /// Version 1 knew one unnamed scene. Reading it is the whole point of the
    /// version number, so the addition of a name must not cost the user a file.
    /// </summary>
    [Fact]
    public void AVersionOneProjectStillLoadsAndItsSceneIsNamed()
    {
        File.WriteAllText(
            _path,
            "version: 1\n"
            + "materials:\n"
            + "- id: 11111111-1111-1111-1111-111111111111\n"
            + "  name: Default\n"
            + "  color:\n"
            + "  - 0.5\n"
            + "  - 0.5\n"
            + "  - 0.5\n"
            + "  - 1\n"
            + "scenes:\n"
            + "- id: 22222222-2222-2222-2222-222222222222\n"
            + "  size:\n"
            + "  - 32\n"
            + "  - 32\n"
            + "  - 32\n");

        IProject loaded = ModelFactory.LoadProject(_path);

        Assert.Equal("Scene", loaded.Scenes[0].Name);
        Assert.Equal(new Vector3I(32, 32, 32), loaded.Scenes[0].Size);
    }

    [Fact]
    public void ASchemaFromTheFutureIsRefused()
    {
        File.WriteAllText(_path, "version: 99\nmaterials: []\nscenes: []\n");

        Assert.Throws<InvalidDataException>(() => ModelFactory.LoadProject(_path));
    }

    /// <summary>
    /// A project with nowhere to build is not a project. Enforced where files
    /// become one, so nothing downstream has to defend against it.
    /// </summary>
    [Fact]
    public void AProjectWithoutScenesIsRefused()
    {
        File.WriteAllText(_path, "version: 2\nmaterials: []\nscenes: []\n");

        Assert.Throws<InvalidDataException>(() => ModelFactory.LoadProject(_path));
    }

    /// <summary>
    /// The library comes back with its rooms and its contents, and every instance
    /// is filled in again — nothing on disk carries what hangs under one.
    /// </summary>
    [Fact]
    public void ThePrefabLibraryAndItsInstancesSurviveTheRoundTrip()
    {
        Guid material = _project.Materials[0].Id;
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");

        _scene.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(3, 4, 5), material) });

        Guid prefabId = _scene.CreatePrefab(model);

        _scene.CreateInstance(_scene.Id, prefabId, "Second");

        IProject loaded = SaveAndLoad();

        Assert.Single(loaded.Prefabs);
        Assert.Equal("Wheel", loaded.Prefabs[0].Name);
        Assert.Equal(prefabId, loaded.Prefabs[0].Id);
        Assert.Equal(_scene.Size, loaded.Prefabs[0].Content.Size);
        Assert.Equal(2, loaded.Scenes[0].Children.Count);

        foreach (IHierarchyElement instance in loaded.Scenes[0].Children)
        {
            Assert.Equal(ComponentKind.PrefabInstance, instance.ComponentKind);
            Assert.Single(instance.Children);
            Assert.Equal(instance.Id, instance.Children[0].PrefabInstanceId);

            IVoxelModel cells = Assert.IsAssignableFrom<IVoxelModel>(
                instance.Children[0].Component);

            Assert.Equal(material, cells.Voxels[new Vector3I(3, 4, 5)]);
        }
    }

    /// <summary>
    /// The one thing a file must not carry: an instance's contents. They are
    /// derived, and a copy on disk is a copy that can disagree with its own library.
    /// </summary>
    [Fact]
    public void AnInstanceIsWrittenAsAReferenceAndNothingElse()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");

        _scene.SetVoxels(model, new[]
        {
            new VoxelEdit(new Vector3I(3, 4, 5), _project.Materials[0].Id)
        });

        Guid prefabId = _scene.CreatePrefab(model);
        _project.Save(_path);

        var yaml = File.ReadAllText(_path);

        Assert.Contains($"source: {prefabId:D}", yaml);
        Assert.Contains("prefabs:", yaml);

        // Once, in the library. The instance shows the same cells and writes none
        // of them.
        Assert.Equal(1, CountOccurrences(yaml, "voxels:"));
    }

    [Fact]
    public void AFileFromBeforePrefabsExistedStillLoads()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");
        _project.Save(_path);

        // Whatever version was written, not a literal: pinned to "version: 3" this
        // replaced nothing from the day the schema reached 4, so the file under
        // test was a current one and the test had silently stopped downgrading
        // anything.
        File.WriteAllText(
            _path,
            Regex.Replace(File.ReadAllText(_path), @"(?m)^version: \d+$", "version: 2"));

        IProject loaded = ModelFactory.LoadProject(_path);

        Assert.Empty(loaded.Prefabs);
        Assert.Single(loaded.Scenes[0].Children);
    }

    /// <summary>
    /// Smoothing was taken out of this branch, and files written while it was here
    /// carry a material's <c>smooth</c> flag and an element's <c>smoothing</c>
    /// masks. They open as the cubes they always were underneath, and the next
    /// save says nothing of either: the reader ignores a key it does not know,
    /// and this is the test that says so for these two.
    /// </summary>
    [Fact]
    public void AFileWrittenWithSmoothingStillOpensAndForgetsIt()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        Guid material = _project.Materials[0].Id;
        _scene.SetVoxels(model, new[]
        {
            new VoxelEdit(new Vector3I(0, 0, 0), material),
            new VoxelEdit(new Vector3I(1, 0, 0), material)
        });
        _project.Save(_path);

        string written = File.ReadAllText(_path);
        written = Regex.Replace(written, @"(?m)^  name: Default$", "  name: Default\n  smooth: true");
        written = Regex.Replace(
            written, @"(?m)^    name: Model$", "    name: Model\n    smoothing: H4sIAAAAAAAAE2NgZGBgBAAiu80zBAAAAA==");
        File.WriteAllText(_path, written);

        Assert.Matches(@"(?m)^  smooth: true$", written);
        Assert.Matches(@"(?m)^    smoothing: ", written);

        IProject loaded = ModelFactory.LoadProject(_path);
        var cells = (IVoxelModel)loaded.Scenes[0].Children[0].Component!;

        Assert.Equal(2, cells.Voxels.Count);
        Assert.Equal(material, cells.Voxels[new Vector3I(1, 0, 0)]);

        loaded.Save(_path);

        Assert.DoesNotMatch(@"(?m)^\s+smooth(ing)?:", File.ReadAllText(_path));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void TheMultiFileModeSaysItIsNotImplemented()
    {
        Assert.Throws<NotSupportedException>(() =>
            _project.Save(Path.ChangeExtension(_path, ".vcadproj")));
    }

    private IProject SaveAndLoad()
    {
        _project.Save(_path);

        return ModelFactory.LoadProject(_path);
    }

    /// <summary>
    /// The declared flag survives a save whatever the alpha beside it, which is
    /// the one thing making it a declaration rather than a second spelling of the
    /// colour. The case that was broken is <c>opaque with an alpha below one</c>:
    /// the writer said nothing, and silence is read as the alpha deciding — so the
    /// material came back see-through and the seam rule then dropped the faces it
    /// was meant to keep.
    /// </summary>
    [Theory]
    [InlineData(true, 1f)]
    [InlineData(true, 0.5f)]
    [InlineData(false, 1f)]
    [InlineData(false, 0.5f)]
    [InlineData(false, 0.999f)]
    [InlineData(true, 0.001f)]
    public void TransparencyIsWhatWasDeclaredHoweverTheAlphaReads(bool transparent, float alpha)
    {
        Guid id = _project.CreateMaterial("Glass", new Color(1f, 1f, 1f, alpha));

        _project.SetMaterialAppearance(
            id,
            MaterialAppearance.Default with { Transparent = transparent });

        IProject loaded = SaveAndLoad();

        Assert.True(loaded.TryGetMaterial(id, out IMaterial reloaded));
        Assert.Equal(transparent, reloaded.Appearance.Transparent);
    }

    /// <summary>
    /// And the migration it rests on is untouched: a file written before the flag
    /// says nothing, and nothing still means the alpha decides — or every model
    /// anybody has drawn changes the day they open it.
    /// </summary>
    [Fact]
    public void AFileThatSaysNothingStillLetsTheAlphaDecide()
    {
        Guid id = _project.CreateMaterial("Older", new Color(1f, 1f, 1f, 0.4f));

        _project.SetMaterialAppearance(
            id,
            MaterialAppearance.Default with { Transparent = true });

        _project.Save(_path);

        string text = File.ReadAllText(_path);

        // The flag agrees with the alpha here, so the file says nothing about it.
        Assert.DoesNotContain("transparent", text, StringComparison.OrdinalIgnoreCase);

        Assert.True(ModelFactory.LoadProject(_path).TryGetMaterial(id, out IMaterial reloaded));
        Assert.True(reloaded.Appearance.Transparent);
    }
}
