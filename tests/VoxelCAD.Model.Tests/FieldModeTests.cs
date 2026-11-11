using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The quantisation contract: a stored value always conforms to its field mode,
/// and Snap90 is rotation-only.
/// </summary>
public sealed class FieldModeTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;

    public FieldModeTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
    }

    [Fact]
    public void FloatModeStoresValueUnchanged()
    {
        _scene.SetFieldMode(_element, TransformChannel.Position, FieldMode.Float);
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10.7f, 0f, -3.2f));

        Assert.Equal(new Vector3(10.7f, 0f, -3.2f), Transform().Position);
    }

    [Theory]
    [InlineData(10.7f, 11f)]
    [InlineData(10.2f, 10f)]
    [InlineData(-3.2f, -3f)]
    [InlineData(-3.7f, -4f)]
    [InlineData(2.5f, 3f)]
    [InlineData(-2.5f, -3f)]
    public void IntegerModeRoundsOnWrite(float written, float expected)
    {
        _scene.SetFieldMode(_element, TransformChannel.Position, FieldMode.Integer);
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(written, 0f, 0f));

        Assert.Equal(expected, Transform().Position.X);
    }

    [Theory]
    [InlineData(47f, 90f)]
    [InlineData(44f, 0f)]
    [InlineData(-100f, -90f)]
    public void Snap90ModeRoundsToRightAngles(float written, float expected)
    {
        _scene.SetFieldMode(_element, TransformChannel.Rotation, FieldMode.Snap90);
        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, written, 0f));

        Assert.Equal(expected, Transform().RotationDegrees.Y);
    }

    /// <summary>
    /// One orientation, one spelling. A matrix round trip through a reparent hands
    /// back -180° where 180° went in, so without this the inspector's numbers
    /// could change while nothing had moved.
    /// </summary>
    [Theory]
    [InlineData(271f, -90f)]
    [InlineData(360f, 0f)]
    [InlineData(450f, 90f)]
    [InlineData(-180f, 180f)]
    [InlineData(-270f, 90f)]
    [InlineData(-360f, 0f)]
    public void Snap90ModeWrapsFullTurnsAway(float written, float expected)
    {
        _scene.SetFieldMode(_element, TransformChannel.Rotation, FieldMode.Snap90);
        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, written, 0f));

        Assert.Equal(expected, Transform().RotationDegrees.Y);
    }

    [Fact]
    public void ChangingModeQuantisesTheExistingValue()
    {
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10.7f, 0f, 0f));
        _scene.SetFieldMode(_element, TransformChannel.Position, FieldMode.Integer);

        Assert.Equal(11f, Transform().Position.X);
    }

    [Fact]
    public void ChangingModeBackDoesNotRestorePrecision()
    {
        // The lost fraction is recoverable through undo, not by toggling the mode:
        // the model keeps exactly one truth per value.
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10.7f, 0f, 0f));
        _scene.SetFieldMode(_element, TransformChannel.Position, FieldMode.Integer);
        _scene.SetFieldMode(_element, TransformChannel.Position, FieldMode.Float);

        Assert.Equal(11f, Transform().Position.X);
    }

    [Theory]
    [InlineData(TransformChannel.Position)]
    [InlineData(TransformChannel.Scale)]
    public void Snap90IsRejectedOutsideRotation(TransformChannel channel)
    {
        Assert.Throws<ArgumentException>(() =>
            _scene.SetFieldMode(_element, channel, FieldMode.Snap90));
    }

    [Fact]
    public void Snap90IsAcceptedForRotation()
    {
        _scene.SetFieldMode(_element, TransformChannel.Rotation, FieldMode.Snap90);

        Assert.Equal(FieldMode.Snap90, _scene.Modes(_element).Rotation);
    }

    [Fact]
    public void AllowedModesMatchWhatTheSceneAccepts()
    {
        Assert.Equal(
            new[] { FieldMode.Float, FieldMode.Integer },
            FieldModeHelper.GetAllowedModes(TransformChannel.Position));

        Assert.Equal(
            new[] { FieldMode.Float, FieldMode.Integer, FieldMode.Snap90 },
            FieldModeHelper.GetAllowedModes(TransformChannel.Rotation));
    }

    [Fact]
    public void ScaleDefaultsToOne()
    {
        Assert.Equal(Vector3.One, Transform().Scale);
    }

    private TransformValues Transform() => _scene.Element(_element).Transform;
}
