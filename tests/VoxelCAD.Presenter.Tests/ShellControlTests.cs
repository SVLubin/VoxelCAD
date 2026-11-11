using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The shell's part in the handover: it hands out the gated agent dispatcher, ties
/// the arbiter to the banner through the control presenter, and drops the whole
/// thing when the endpoint tells it the transport is gone.
/// </summary>
public sealed class ShellControlTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly FakePreferences _preferences = new();
    private readonly ShellPresenter _shell;

    public ShellControlTests() => _shell = new ShellPresenter(_view, _preferences);

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void AnAgentTakingControlDimsTheInterface()
    {
        Grant();

        Assert.Equal(ControlBannerKind.AgentWorking, _view.ControlBanner.Kind);
    }

    /// <summary>
    /// The endpoint stopping — switched off, or the editor closing — reaches the
    /// shell here, and control returns to the user rather than leaving the
    /// interface dimmed for an agent that can no longer reach it.
    /// </summary>
    [Fact]
    public void LosingTheEndpointReturnsControlToTheUser()
    {
        Grant();

        _shell.ReleaseAgentControl();

        Assert.Equal(ControlBannerKind.None, _view.ControlBanner.Kind);
    }

    [Fact]
    public void ReleasingWithNoHandoverInFlightIsHarmless()
    {
        _shell.ReleaseAgentControl();

        Assert.Equal(ControlBannerKind.None, _view.ControlBanner.Kind);
    }

    /// <summary>
    /// Drives the gated dispatcher's <c>take-control</c> and lets the countdown
    /// elapse, exactly as an agent and the frame loop would.
    /// </summary>
    private void Grant()
    {
        _shell.CreateAgentDispatchers().ForClient("test-client").Execute(
            "take-control",
            new Dictionary<string, string> { ["name"] = "Claude" });

        _shell.Tick(_preferences.CountdownSeconds);
    }
}
