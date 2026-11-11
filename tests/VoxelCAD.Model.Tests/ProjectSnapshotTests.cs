using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A capture owes nothing to the model it was taken from: it is the document as
/// it stood, and it can be written from wherever is convenient.
/// </summary>
public sealed class ProjectSnapshotTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-snapshot-{Guid.NewGuid():N}");

    public ProjectSnapshotTests()
    {
        _scene = _project.Scenes[0];
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// The whole point: an edit made after the capture is not in the file, so a
    /// write that runs later — on another thread — cannot see a model in motion.
    /// </summary>
    [Fact]
    public void AnEditAfterTheCaptureIsNotInTheFile()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        Guid material = _project.Materials[0].Id;
        _scene.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(1, 1, 1), material) });

        IProjectSnapshot snapshot = _project.Capture();

        _scene.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(2, 2, 2), material) });
        _scene.RenameElement(model, "Renamed");

        string path = Path.Combine(_directory, "captured.vcad");
        snapshot.Write(path);

        IVoxelModel loaded = (IVoxelModel)ModelFactory.LoadProject(path).Scenes[0].Children[0].Component!;

        Assert.Equal("Model", loaded.Owner.Name);
        Assert.Single(loaded.Voxels);
        Assert.True(loaded.Voxels.ContainsKey(new Vector3I(1, 1, 1)));
    }

    [Fact]
    public void ACaptureWrittenOnAnotherThreadIsByteForByteASave()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        Guid material = _project.Materials[0].Id;
        _scene.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(3, 0, 3), material) });

        string saved = Path.Combine(_directory, "saved.vcad");
        string captured = Path.Combine(_directory, "captured.vcad");

        _project.Save(saved);
        IProjectSnapshot snapshot = _project.Capture();

        Task.Run(() => snapshot.Write(captured)).GetAwaiter().GetResult();

        Assert.Equal(File.ReadAllBytes(saved), File.ReadAllBytes(captured));
    }

    [Fact]
    public void ASnapshotIsWrittenOnce()
    {
        IProjectSnapshot snapshot = _project.Capture();

        snapshot.Write(Path.Combine(_directory, "once.vcad"));

        Assert.Throws<InvalidOperationException>(
            () => snapshot.Write(Path.Combine(_directory, "twice.vcad")));
    }
}
