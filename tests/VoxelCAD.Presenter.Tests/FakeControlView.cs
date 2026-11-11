using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>Records the last banner the presenter set, and lets a test raise the user's buttons.</summary>
internal sealed class FakeControlView : IControlView
{
    public event Action? CancelControlRequested;

    public event Action? StopControlRequested;

    public event Action? UserInteractionBegan;

    public event Action? UserInteractionEnded;

    public ControlBannerState State { get; private set; } =
        new(ControlBannerKind.None, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty);

    /// <summary>How many times the presenter repainted, so the throttle can be checked.</summary>
    public int Repaints { get; private set; }

    public ControlBannerKind Kind => State.Kind;

    public string AgentName => State.AgentName;

    public string Topic => State.Topic;

    public int SecondsLeft => State.SecondsLeft;

    public string Elapsed => State.Elapsed;

    public void SetControlBanner(ControlBannerState state)
    {
        State = state;
        Repaints++;
    }

    public void RaiseCancel() => CancelControlRequested?.Invoke();

    public void RaiseStop() => StopControlRequested?.Invoke();

    public void RaiseInteractionBegan() => UserInteractionBegan?.Invoke();

    public void RaiseInteractionEnded() => UserInteractionEnded?.Invoke();
}
