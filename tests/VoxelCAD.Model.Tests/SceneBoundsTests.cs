using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The buildable region, and what it does not bound. Deliberately loose rules: element
/// positions are clamped to the region grown by half its size, rotation and scale
/// are not constrained at all, and the region does not bound a model's cells —
/// a model may hang out of the box, and what hangs out has to be reachable.
/// </summary>
public sealed class SceneBoundsTests
{
    private readonly IProject _project = ModelFactory.CreateProject(new Vector3I(32, 32, 32));
    private readonly IScene _scene;
    private readonly Guid _stone;
    private readonly Guid _element;

    public SceneBoundsTests()
    {
        _scene = _project.Scenes[0];
        _stone = _project.Materials[0].Id;
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    [Fact]
    public void ADefaultSceneIsThirtyTwoCubed()
    {
        Assert.Equal(new Vector3I(32, 32, 32), ModelFactory.CreateProject().Scenes[0].Size);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(31, 31, 31)]
    [InlineData(15, 0, 31)]
    public void CellsInsideTheRegionAreAccepted(int x, int y, int z)
    {
        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(x, y, z), _stone) });

        Assert.Single(Model().Voxels);
    }

    /// <summary>
    /// The region bounds the room, not the model. It used to drop these, which
    /// also meant a model parked half outside could not be extended outwards —
    /// and, because the same test ran over every edit, that the cells already out
    /// there could not be erased either.
    /// </summary>
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(32, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 32)]
    [InlineData(99, 99, 99)]
    public void CellsOutsideTheRegionAreAccepted(int x, int y, int z)
    {
        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(x, y, z), _stone) });

        Assert.Single(Model().Voxels);
    }

    [Fact]
    public void AStrokeCrossingAWallKeepsGoing()
    {
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(30, 0, 0), _stone),
            new VoxelEdit(new Vector3I(31, 0, 0), _stone),
            new VoxelEdit(new Vector3I(32, 0, 0), _stone),
            new VoxelEdit(new Vector3I(33, 0, 0), _stone)
        });

        Assert.Equal(4, Model().Voxels.Count);
    }

    /// <summary>The half of it that was a plain bug: what is outside must be erasable.</summary>
    [Fact]
    public void ACellOutsideTheRegionCanBeErased()
    {
        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(1, 0, 0), _stone) });
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(-16f, 0f, 0f));

        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(1, 0, 0), null) });

        Assert.Empty(Model().Voxels);
    }

    [Fact]
    public void CellsAlreadyPlacedSurviveAMoveThatTakesThemOutside()
    {
        // Existing cells travel with their model wherever it goes.
        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(1, 0, 0), _stone) });
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(-16f, 0f, 0f));

        Assert.Single(Model().Voxels);
    }

    [Fact]
    public void AnEditThatChangesNothingStillRecordsNothing()
    {
        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(99, 99, 99), _stone) });

        var before = _project.History.Records.Count;

        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(99, 99, 99), _stone) });

        Assert.Equal(before, _project.History.Records.Count);
    }

    /// <summary>
    /// Nothing bounds where an element may stand. The position used to be leashed
    /// to the region grown by half its size, which made a part staged well outside
    /// the room impossible; the region is the room and the ruler, not a fence.
    /// </summary>
    [Theory]
    [InlineData(-100f)]
    [InlineData(100f)]
    [InlineData(100000f)]
    public void PositionIsNotBounded(float requested)
    {
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(requested, 0f, 0f));

        Assert.Equal(requested, _scene.Element(_element).Transform.Position.X);
    }

    [Fact]
    public void EveryAxisTravelsIndependentlyAndUnbounded()
    {
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(500f, 10f, -500f));

        Assert.Equal(new Vector3(500f, 10f, -500f), _scene.Element(_element).Transform.Position);
    }

    /// <summary>
    /// The one rule left on the way in. A far-away position is still rounded to the
    /// channel's lattice, so removing the leash did not remove the quantisation.
    /// </summary>
    [Fact]
    public void AFarPositionIsStillQuantised()
    {
        _scene.SetFieldMode(_element, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(999.4f, 0f, 0f));

        Assert.Equal(999f, _scene.Element(_element).Transform.Position.X);
        Assert.Equal(FieldMode.Integer, _scene.Modes(_element).Position);
    }

    [Fact]
    public void AnOddSizedSceneBoundsNothingEither()
    {
        IScene scene = ModelFactory.CreateProject(new Vector3I(33, 33, 33)).Scenes[0];
        Guid element = scene.CreateElement(scene.Id, ComponentKind.None, "Element");

        scene.SetFieldMode(element, TransformChannel.Position, FieldMode.Integer);
        scene.SetTransform(element, TransformChannel.Position, new Vector3(999f, 0f, 0f));

        Assert.Equal(999f, scene.Element(element).Transform.Position.X);
    }

    [Fact]
    public void SettingTheCurrentValueRecordsNothing()
    {
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(999f, 0f, 0f));
        var before = _project.History.Records.Count;

        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(999.2f, 0f, 0f));

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void RotationAndScaleAreNotConstrained()
    {
        _scene.SetFieldMode(_element, TransformChannel.Rotation, FieldMode.Float);
        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, 3600f, 0f));
        _scene.SetTransform(_element, TransformChannel.Scale, new Vector3(500f, 500f, 500f));

        Assert.Equal(3600f, _scene.Element(_element).Transform.RotationDegrees.Y);
        Assert.Equal(500f, _scene.Element(_element).Transform.Scale.X);
    }

    [Fact]
    public void UndoRestoresThePreviousPosition()
    {
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10f, 0f, 0f));
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(999f, 0f, 0f));

        _project.History.Undo();

        Assert.Equal(10f, _scene.Element(_element).Transform.Position.X);
    }

    private IVoxelModel Model() => (IVoxelModel)_scene.Element(_element).Component!;
}
