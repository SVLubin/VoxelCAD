using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Replacing a material across cells: one record however many elements it
/// reaches, undone as one, and the reach a scope names — one element, a scene,
/// or the whole project down to the prefabs' contents.
/// </summary>
public sealed class ReplaceMaterialTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _red;
    private readonly Guid _blue;

    public ReplaceMaterialTests()
    {
        _scene = _project.Scenes[0];
        _red = _project.CreateMaterial("Red", new Color(1, 0, 0));
        _blue = _project.CreateMaterial("Blue", new Color(0, 0, 1));
    }

    [Fact]
    public void ASceneWideReplaceIsOneRecordAcrossEveryElement()
    {
        Guid a = Bar("A", 0, _red);
        Guid b = Bar("B", 5, _red);
        int before = _project.History.Records.Count;

        _scene.ReplaceMaterial(_red, _blue, null);

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.ReplaceMaterial, _project.History.Records[^1].Kind);
        Assert.Equal("Red", _project.History.Records[^1].TargetName);
        Assert.Equal(_scene.Id, _project.History.Records[^1].SceneId);
        Assert.All(Model(a).Voxels.Values, worn => Assert.Equal(_blue, worn));
        Assert.All(Model(b).Voxels.Values, worn => Assert.Equal(_blue, worn));
        Assert.False(_project.IsMaterialInUse(_red));

        _project.History.Undo();

        Assert.All(Model(a).Voxels.Values, worn => Assert.Equal(_red, worn));
        Assert.All(Model(b).Voxels.Values, worn => Assert.Equal(_red, worn));
    }

    [Fact]
    public void AnElementScopeLeavesTheOthersAlone()
    {
        Guid a = Bar("A", 0, _red);
        Guid b = Bar("B", 5, _red);

        _scene.ReplaceMaterial(_red, _blue, a);

        Assert.All(Model(a).Voxels.Values, worn => Assert.Equal(_blue, worn));
        Assert.All(Model(b).Voxels.Values, worn => Assert.Equal(_red, worn));
    }

    [Fact]
    public void OnlyTheNamedMaterialIsTouched()
    {
        Guid a = Bar("A", 0, _red);
        Guid untouched = _project.Materials[0].Id;
        _scene.SetVoxels(a, new[] { new VoxelEdit(new Vector3I(0, 1, 0), untouched) });

        _scene.ReplaceMaterial(_red, _blue, null);

        Assert.Equal(untouched, Model(a).Voxels[new Vector3I(0, 1, 0)]);
    }

    [Fact]
    public void NothingWearingItRecordsNothing()
    {
        Bar("A", 0, _blue);
        int before = _project.History.Records.Count;

        _scene.ReplaceMaterial(_red, _blue, null);
        _project.ReplaceMaterial(_red, _blue);

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void TheSameMaterialTwiceIsNothing()
    {
        Bar("A", 0, _red);
        int before = _project.History.Records.Count;

        _scene.ReplaceMaterial(_red, _red, null);

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void AnUnknownTargetIsRefused()
    {
        Bar("A", 0, _red);

        Assert.Throws<ArgumentException>(() => _scene.ReplaceMaterial(_red, Guid.NewGuid(), null));
        Assert.Throws<ArgumentException>(() => _project.ReplaceMaterial(_red, Guid.NewGuid()));
    }

    /// <summary>
    /// The project-wide replace reaches a second scene and a prefab's contents,
    /// whose instances follow — and it is still one record, project-level.
    /// </summary>
    [Fact]
    public void AProjectWideReplaceReachesEverySceneAndEveryPrefab()
    {
        Guid a = Bar("A", 0, _red);
        Guid sourceId = Bar("Source", 10, _red);
        Guid prefabId = _scene.CreatePrefab(sourceId);

        Guid otherSceneId = _project.CreateScene("Other", new Vector3I(16, 16, 16));
        Assert.True(_project.TryGetScene(otherSceneId, out IScene other));
        Guid c = other.CreateElement(other.Id, ComponentKind.VoxelModel, "C");
        other.SetVoxels(c, new[] { new VoxelEdit(new Vector3I(0, 0, 0), _red) });

        int before = _project.History.Records.Count;

        _project.ReplaceMaterial(_red, _blue);

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(Guid.Empty, _project.History.Records[^1].SceneId);
        Assert.All(Model(a).Voxels.Values, worn => Assert.Equal(_blue, worn));
        Assert.All(ModelIn(other, c).Voxels.Values, worn => Assert.Equal(_blue, worn));

        Assert.True(_project.TryGetPrefab(prefabId, out IPrefab prefab));
        IHierarchyElement content = prefab.Content.Children.Single();
        Assert.All(((IVoxelModel)content.Component!).Voxels.Values, worn => Assert.Equal(_blue, worn));

        // The instance standing in the scene is derived from the source, so it
        // follows without being written — and IsMaterialInUse agrees.
        Assert.False(_project.IsMaterialInUse(_red));

        _project.History.Undo();

        Assert.True(_project.IsMaterialInUse(_red));
        Assert.All(Model(a).Voxels.Values, worn => Assert.Equal(_red, worn));
    }

    [Fact]
    public void AShippedMaterialMayBeTheTarget()
    {
        // Installed here rather than assumed: the library is the composition
        // root's to install, and a test that leans on another test having done
        // it passes or fails by the order the runner picked.
        StandardMaterials.Install();

        Guid a = Bar("A", 0, _red);
        Guid shipped = _project.StandardMaterials[0].Id;

        _scene.ReplaceMaterial(_red, shipped, null);

        Assert.All(Model(a).Voxels.Values, worn => Assert.Equal(shipped, worn));
    }

    private Guid Bar(string name, int x, Guid material)
    {
        Guid id = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

        _scene.SetVoxels(id, new[]
        {
            new VoxelEdit(new Vector3I(x, 0, 0), material),
            new VoxelEdit(new Vector3I(x + 1, 0, 0), material),
        });

        return id;
    }

    private IVoxelModel Model(Guid id) => ModelIn(_scene, id);

    private static IVoxelModel ModelIn(IScene scene, Guid id)
    {
        Assert.True(scene.TryGetElement(id, out IHierarchyElement element));

        return (IVoxelModel)element.Component!;
    }
}
