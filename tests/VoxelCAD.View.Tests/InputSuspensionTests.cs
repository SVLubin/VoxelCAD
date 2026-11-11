using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which input is swallowed while an agent holds the editor. This is the gate
/// that protects the user's session, and it decides by <b>origin</b> — never by
/// where the pointer is — so the dimmer's hole cannot be a way in: a physical
/// click aimed into it is refused exactly like one aimed anywhere else.
/// </summary>
public sealed class InputSuspensionTests
{
    [Fact]
    public void WithNobodyHoldingControlNothingIsSuspended()
    {
        Assert.False(InputSuspension.ShouldSuspend(
            holdsControl: false, isSynthetic: false, isOverAction: false));
    }

    [Fact]
    public void TheUsersInputIsSwallowedWhileAnAgentWorks()
    {
        Assert.True(InputSuspension.ShouldSuspend(
            holdsControl: true, isSynthetic: false, isOverAction: false));
    }

    [Fact]
    public void TheAgentsOwnInjectedInputPasses()
    {
        Assert.False(InputSuspension.ShouldSuspend(
            holdsControl: true, isSynthetic: true, isOverAction: false));
    }

    /// <summary>The one thing a suspended user keeps: the button that stops the agent.</summary>
    [Fact]
    public void TheStopButtonKeepsTakingTheUsersClick()
    {
        Assert.False(InputSuspension.ShouldSuspend(
            holdsControl: true, isSynthetic: false, isOverAction: true));
    }

    /// <summary>
    /// An event that lost its mark reads as physical and is suspended: the
    /// failure is one-sided by construction, costing an agent a gesture rather
    /// than costing the user their protection.
    /// </summary>
    [Fact]
    public void AnUnmarkedEventIsTreatedAsTheUsers()
    {
        Assert.True(InputSuspension.ShouldSuspend(
            holdsControl: true, isSynthetic: false, isOverAction: false));
    }
}
