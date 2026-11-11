using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Reparenting preserves world position. Where that collides with a field mode,
/// the mode degrades and the geometry stays put — never the other way round.
/// </summary>
public sealed class ReparentTransformTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public ReparentTransformTests()
    {
        _scene = _project.Scenes[0];
    }

    [Fact]
    public void WorldTransformSurvivesAMoveUnderARotatedParent()
    {
        Guid rotated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rotated");
        _scene.SetTransform(rotated, TransformChannel.Position, new Vector3(5f, 0f, 0f));
        _scene.SetTransform(rotated, TransformChannel.Rotation, new Vector3(0f, 37f, 0f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 2f, -3f));
        _scene.SetTransform(moving, TransformChannel.Rotation, new Vector3(0f, 20f, 0f));

        Transform3D before = _scene.WorldTransform(moving);
        _scene.ReparentElement(moving, rotated, 0);

        Assert.True(before.IsEqualApprox(_scene.WorldTransform(moving)));
    }

    [Fact]
    public void WorldTransformSurvivesAMoveUnderAScaledParent()
    {
        Guid scaled = _scene.CreateElement(_scene.Id, ComponentKind.None, "Scaled");
        _scene.SetTransform(scaled, TransformChannel.Scale, new Vector3(2f, 2f, 2f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 2f, -3f));

        Transform3D before = _scene.WorldTransform(moving);
        _scene.ReparentElement(moving, scaled, 0);

        Assert.True(before.IsEqualApprox(_scene.WorldTransform(moving)));
    }

    [Fact]
    public void LocalTransformIsRecomputedAgainstTheNewParent()
    {
        Guid translated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Translated");
        _scene.SetTransform(translated, TransformChannel.Position, new Vector3(2f, 0f, 3f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 0f, -3f));

        _scene.ReparentElement(moving, translated, 0);

        Assert.Equal(new Vector3(9f, 0f, -6f), _scene.Element(moving).Transform.Position);
    }

    [Fact]
    public void ModeDegradesWhenTheNewLocalValueCannotConform()
    {
        Guid rotated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rotated");
        _scene.SetFieldMode(rotated, TransformChannel.Rotation, FieldMode.Float);
        _scene.SetTransform(rotated, TransformChannel.Rotation, new Vector3(0f, 37f, 0f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetFieldMode(moving, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 0f, -3f));

        Transform3D before = _scene.WorldTransform(moving);
        _scene.ReparentElement(moving, rotated, 0);

        Assert.Equal(FieldMode.Float, _scene.Modes(moving).Position);
        Assert.True(before.IsEqualApprox(_scene.WorldTransform(moving)));
    }

    [Fact]
    public void Snap90DegradesUnderAnOffAxisParent()
    {
        Guid rotated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rotated");
        _scene.SetFieldMode(rotated, TransformChannel.Rotation, FieldMode.Float);
        _scene.SetTransform(rotated, TransformChannel.Rotation, new Vector3(0f, 37f, 0f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetFieldMode(moving, TransformChannel.Rotation, FieldMode.Snap90);
        _scene.SetTransform(moving, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        _scene.ReparentElement(moving, rotated, 0);

        Assert.Equal(FieldMode.Float, _scene.Modes(moving).Rotation);
    }

    [Fact]
    public void ModeSurvivesWhenTheNewLocalValueStillConforms()
    {
        Guid translated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Translated");
        _scene.SetTransform(translated, TransformChannel.Position, new Vector3(2f, 0f, 3f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetFieldMode(moving, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(moving, TransformChannel.Position, new Vector3(11f, 0f, -3f));

        _scene.ReparentElement(moving, translated, 0);

        Assert.Equal(FieldMode.Integer, _scene.Modes(moving).Position);
        Assert.Equal(new Vector3(9f, 0f, -6f), _scene.Element(moving).Transform.Position);
    }

    [Fact]
    public void Snap90SurvivesAQuarterTurnParent()
    {
        Guid rotated = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rotated");
        _scene.SetTransform(rotated, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");
        _scene.SetFieldMode(moving, TransformChannel.Rotation, FieldMode.Snap90);
        _scene.SetTransform(moving, TransformChannel.Rotation, new Vector3(0f, 180f, 0f));

        _scene.ReparentElement(moving, rotated, 0);

        Assert.Equal(FieldMode.Snap90, _scene.Modes(moving).Rotation);
    }
}
