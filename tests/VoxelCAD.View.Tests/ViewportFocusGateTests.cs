using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What may hand the inactive viewport the keyboard. Everything else it refuses
/// while another panel is the focused one — which is the whole point: a zoom or a
/// trackpad gesture over a viewport nobody is working in must not turn the camera.
/// </summary>
public sealed class ViewportFocusGateTests
{
    [Fact]
    public void APressOnARealButtonActivates()
    {
        Assert.True(ViewportFocusGate.Activates(MouseButton.Left, pressed: true));
        Assert.True(ViewportFocusGate.Activates(MouseButton.Middle, pressed: true));
        Assert.True(ViewportFocusGate.Activates(MouseButton.Right, pressed: true));
    }

    /// <summary>
    /// A release is the tail of a gesture that started somewhere else; taking the
    /// keyboard on it would move focus on letting go of a button pressed in a dock.
    /// </summary>
    [Fact]
    public void AReleaseDoesNot()
    {
        Assert.False(ViewportFocusGate.Activates(MouseButton.Left, pressed: false));
        Assert.False(ViewportFocusGate.Activates(MouseButton.Middle, pressed: false));
    }

    /// <summary>
    /// The case this gate exists for: a scroll is not a click, so it neither acts
    /// on the inactive viewport nor takes the keyboard from the panel being used.
    /// </summary>
    [Fact]
    public void NoWheelDirectionActivates()
    {
        Assert.False(ViewportFocusGate.Activates(MouseButton.WheelUp, pressed: true));
        Assert.False(ViewportFocusGate.Activates(MouseButton.WheelDown, pressed: true));
        Assert.False(ViewportFocusGate.Activates(MouseButton.WheelLeft, pressed: true));
        Assert.False(ViewportFocusGate.Activates(MouseButton.WheelRight, pressed: true));
    }

    [Fact]
    public void TheWheelDirectionsAreTheOnesCountedAsWheels()
    {
        Assert.True(ViewportFocusGate.IsWheel(MouseButton.WheelUp));
        Assert.True(ViewportFocusGate.IsWheel(MouseButton.WheelDown));
        Assert.True(ViewportFocusGate.IsWheel(MouseButton.WheelLeft));
        Assert.True(ViewportFocusGate.IsWheel(MouseButton.WheelRight));

        Assert.False(ViewportFocusGate.IsWheel(MouseButton.Left));
        Assert.False(ViewportFocusGate.IsWheel(MouseButton.Middle));
        Assert.False(ViewportFocusGate.IsWheel(MouseButton.Right));
    }
}
