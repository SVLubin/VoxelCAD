using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The presenter side of the handover: the countdown, the banner it paints, and
/// the history attribution it sets when an agent takes over.
/// </summary>
public sealed class ControlPresenterTests
{
    private const string Client = "agent-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeControlView _view = new();
    private readonly FakeSceneView _sceneView = new();
    private readonly FakePreferences _preferences = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ControlPresenter _presenter;

    public ControlPresenterTests()
    {
        _presenter = new ControlPresenter(_arbiter, _view, _sceneView, _project.History, _preferences);
    }

    [Fact]
    public void NothingIsShownWhileTheUserIsInControl()
    {
        Assert.Equal(ControlBannerKind.None, _view.Kind);
    }

    [Fact]
    public void ARequestStartsACountdown()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        Assert.Equal(ControlBannerKind.AgentCountdown, _view.Kind);
        Assert.Equal("Claude", _view.AgentName);
    }

    [Fact]
    public void ABusyUserPausesTheCountdown()
    {
        _arbiter.BeginInteraction();
        _arbiter.Request(Client, "Claude", string.Empty);

        _presenter.Tick(10.0);

        Assert.Equal(ControlBannerKind.AgentWaiting, _view.Kind);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
    }

    [Fact]
    public void TheCountdownGrantsControlWhenItElapses()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        _presenter.Tick(1.5);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        _presenter.Tick(2.0);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.Equal(ControlBannerKind.AgentWorking, _view.Kind);
    }

    [Fact]
    public void TheCountdownIsAsLongAsThePreferencesSay()
    {
        _preferences.Apply(_preferences.GetValues() with { CountdownSeconds = 10.0 });
        _arbiter.Request(Client, "Claude", string.Empty);

        _presenter.Tick(5.0);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        _presenter.Tick(6.0);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    /// <summary>
    /// Turning the countdown off removes the grace period, not the waiting: a busy
    /// user still holds the handover, which is the point of the token.
    /// </summary>
    [Fact]
    public void WithNoCountdownControlPassesAtOnceButStillNotOverABusyUser()
    {
        _preferences.Apply(_preferences.GetValues() with { CountdownEnabled = false });

        _view.RaiseInteractionBegan();
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(0.1);

        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        _view.RaiseInteractionEnded();
        _presenter.Tick(0.1);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    [Fact]
    public void GrantingMarksTheHistoryAsTheAgents()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(5.0);

        Assert.Equal(ActorKind.Agent, _project.History.CurrentActor.Kind);
        Assert.Equal("Claude", _project.History.CurrentActor.Name);
    }

    [Fact]
    public void ReleasingReturnsTheHistoryToTheUser()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(5.0);
        _arbiter.Release(Client);

        Assert.Equal(ActorKind.User, _project.History.CurrentActor.Kind);
        Assert.Equal(ControlBannerKind.None, _view.Kind);
    }

    [Fact]
    public void StartingToTypeDuringTheCountdownHoldsTheHandoverOff()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(2.0);

        _view.RaiseInteractionBegan();

        Assert.Equal(ControlBannerKind.AgentWaiting, _view.Kind);

        // Ten seconds is well inside the thirty-second maximum wait, so the busy
        // user still holds it; falling through past that is its own test.
        _presenter.Tick(10.0);

        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
    }

    [Fact]
    public void FinishingTypingRestartsTheCountdownRatherThanResumingIt()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(2.5);

        _view.RaiseInteractionBegan();
        _view.RaiseInteractionEnded();

        // Had it resumed, half a second would have been enough to grant.
        _presenter.Tick(1.0);

        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        _presenter.Tick(2.5);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    [Fact]
    public void OverlappingInteractionsBothHaveToFinish()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        _view.RaiseInteractionBegan();
        _view.RaiseInteractionBegan();
        _view.RaiseInteractionEnded();

        // One token is still held, and ten seconds stays under the maximum wait.
        _presenter.Tick(10.0);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        _view.RaiseInteractionEnded();
        _presenter.Tick(5.0);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    /// <summary>
    /// This presenter outlives the document. When one is opened, the stamp has to
    /// follow it — and let go of the history it was stamping before.
    /// </summary>
    [Fact]
    public void TheActorStampFollowsTheDocument()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(5.0);

        IProject opened = ModelFactory.CreateProject();
        _presenter.SetHistory(opened.History);

        Assert.Equal(ActorKind.Agent, opened.History.CurrentActor.Kind);
        Assert.Equal("Claude", opened.History.CurrentActor.Name);
        Assert.Equal(ActorKind.User, _project.History.CurrentActor.Kind);
    }

    [Fact]
    public void OpeningADocumentDoesNotInterruptAHandover()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(2.0);

        _presenter.SetHistory(ModelFactory.CreateProject().History);

        // Had the countdown restarted, one more second would not have been enough.
        _presenter.Tick(1.5);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    [Fact]
    public void CancellingTheCountdownRefusesTheRequest()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        _view.RaiseCancel();

        Assert.Equal(ControlStatus.Denied, _arbiter.Status);
        Assert.Equal(ControlBannerKind.None, _view.Kind);
    }

    [Fact]
    public void StoppingReturnsControlToTheUser()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(5.0);

        _view.RaiseStop();

        Assert.True(_arbiter.IsUserInControl);
        Assert.Equal(ActorKind.User, _project.History.CurrentActor.Kind);
        Assert.Equal(ControlBannerKind.None, _view.Kind);
    }

    // --- the maximum wait ---

    /// <summary>
    /// A busy user holds the handover, but not for good: past the maximum wait the
    /// countdown runs whether or not they are still interacting, so a leaked token
    /// heals instead of stalling an agent until restart.
    /// </summary>
    [Fact]
    public void PastTheMaximumWaitTheCountdownRunsDespiteABusyUser()
    {
        _view.RaiseInteractionBegan();
        _arbiter.Request(Client, "Claude", string.Empty);

        _presenter.Tick(29.0);
        Assert.Equal(ControlBannerKind.AgentWaiting, _view.Kind);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        // Crossing the thirty-second wait turns the quiet plaque into a visible
        // countdown — control is never seized without warning — and it still has
        // not changed hands.
        _presenter.Tick(2.0);
        Assert.Equal(ControlBannerKind.AgentCountdown, _view.Kind);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);

        _presenter.Tick(3.0);
        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    // --- a silent agent ---

    /// <summary>
    /// Long work is not the enemy — silence is. Nothing happens for a whole minute
    /// of it, then the banner says the agent is not responding, and control still
    /// stays put until the grace period is up.
    /// </summary>
    [Fact]
    public void SilenceIsWarnedBeforeControlIsTaken()
    {
        Grant();

        _presenter.Tick(59.0);
        Assert.Equal(ControlBannerKind.AgentWorking, _view.Kind);

        _presenter.Tick(2.0);
        Assert.Equal(ControlBannerKind.AgentSilent, _view.Kind);
        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    [Fact]
    public void SilenceReturnsControlOnceTheGraceIsSpent()
    {
        Grant();

        // A minute's warning plus thirty seconds' grace before the editor gives up.
        _presenter.Tick(61.0);
        Assert.Equal(ControlBannerKind.AgentSilent, _view.Kind);

        _presenter.Tick(30.0);

        Assert.Equal(ControlStatus.TimedOut, _arbiter.Status);
        Assert.Equal(ControlBannerKind.None, _view.Kind);
        Assert.Equal(ActorKind.User, _project.History.CurrentActor.Kind);
    }

    /// <summary>
    /// Every command is proof of life, so one arriving during the warning pulls the
    /// agent back from the brink — even a poll that changes nothing.
    /// </summary>
    [Fact]
    public void ACommandDuringTheWarningKeepsTheAgentAlive()
    {
        Grant();

        _presenter.Tick(61.0);
        Assert.Equal(ControlBannerKind.AgentSilent, _view.Kind);

        _arbiter.NoteAgentCommand(Client);
        Assert.Equal(ControlBannerKind.AgentWorking, _view.Kind);

        // The clock started over, so another near-minute passes before it warns.
        _presenter.Tick(59.0);
        Assert.Equal(ControlBannerKind.AgentWorking, _view.Kind);
        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    [Fact]
    public void ACommandBeforeControlIsHeldDoesNotDisturbTheCountdown()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(1.0);

        // A poll while merely waiting is not a silence to reset; the countdown is
        // unaffected and grants on schedule.
        _arbiter.NoteAgentCommand(Client);
        _presenter.Tick(2.5);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    // --- the running clock ---

    [Fact]
    public void TheBannerCarriesHowLongTheAgentHasWorked()
    {
        Grant();

        _presenter.Tick(5.0);

        Assert.Equal(ControlBannerKind.AgentWorking, _view.Kind);
        Assert.Equal("5s", _view.Elapsed);
    }

    /// <summary>
    /// The clock runs every frame but reads in whole seconds, so the banner is
    /// repainted when the second changes and not on the frames between.
    /// </summary>
    [Fact]
    public void TheClockDoesNotRepaintEveryFrame()
    {
        Grant();

        int painted = _view.Repaints;

        // Half a second of frames, all inside the same displayed second.
        for (var i = 0; i < 10; i++)
            _presenter.Tick(0.05);

        Assert.Equal(painted, _view.Repaints);

        // Crossing into the next second is worth exactly one repaint.
        _presenter.Tick(0.6);

        Assert.Equal(painted + 1, _view.Repaints);
    }

    // --- the transport going away ---

    [Fact]
    public void AbandoningControlClearsTheBanner()
    {
        Grant();

        _arbiter.Abandon();

        Assert.Equal(ControlBannerKind.None, _view.Kind);
        Assert.Equal(ActorKind.User, _project.History.CurrentActor.Kind);
    }

    [Fact]
    public void AbandoningAPendingRequestClearsTheBanner()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        _arbiter.Abandon();

        Assert.Equal(ControlBannerKind.None, _view.Kind);
    }

    // --- the focus marker ---

    /// <summary>
    /// The agent's clean frame must never carry the user's orbit marker, so taking
    /// control hides it and giving it back reveals it.
    /// </summary>
    [Fact]
    public void GrantingHidesTheFocusMarkerAndReleasingShowsIt()
    {
        Grant();
        Assert.True(_sceneView.AgentControlled);

        _arbiter.Release(Client);
        Assert.False(_sceneView.AgentControlled);
    }

    /// <summary>Every exit reveals the marker again — a time-out as much as a clean release.</summary>
    [Fact]
    public void ATimeoutAlsoShowsTheFocusMarkerAgain()
    {
        Grant();
        Assert.True(_sceneView.AgentControlled);

        _presenter.Tick(91.0);
        Assert.Equal(ControlStatus.TimedOut, _arbiter.Status);
        Assert.False(_sceneView.AgentControlled);
    }

    /// <summary>A countdown the user never grants leaves the marker alone.</summary>
    [Fact]
    public void APendingRequestDoesNotTouchTheFocusMarker()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        Assert.False(_sceneView.AgentControlled);
    }

    // --- the camera across a handover ---

    /// <summary>
    /// The agent gets the user's own camera and drives it; when control returns,
    /// the view it started on is put back, the agent's edits left in place.
    /// </summary>
    [Fact]
    public void TheUsersCameraIsRestoredWhenControlReturns()
    {
        _sceneView.CameraPose = UserPose;
        Grant();

        // The agent aims somewhere else while it works.
        _sceneView.CameraPose = AgentPose;
        _arbiter.Release(Client);

        Assert.Equal(UserPose, _sceneView.CameraPose);
    }

    /// <summary>The restore does not care which exit it is — a time-out puts the view back too.</summary>
    [Fact]
    public void TheCameraIsRestoredOnATimeoutAsWell()
    {
        _sceneView.CameraPose = UserPose;
        Grant();
        _sceneView.CameraPose = AgentPose;

        _presenter.Tick(91.0);

        Assert.Equal(ControlStatus.TimedOut, _arbiter.Status);
        Assert.Equal(UserPose, _sceneView.CameraPose);
    }

    /// <summary>
    /// An agent that wants to leave the user looking at what it built says so on
    /// release, and its view is kept rather than the user's restored.
    /// </summary>
    [Fact]
    public void AnAgentCanKeepItsCameraOnRelease()
    {
        _sceneView.CameraPose = UserPose;
        Grant();
        _sceneView.CameraPose = AgentPose;

        _presenter.ReleaseControl(Client, keepCamera: true);

        Assert.Equal(AgentPose, _sceneView.CameraPose);
    }

    /// <summary>
    /// Keep-camera is for that one release only: the next handover restores by
    /// default, or a leaked opt-out would silently disable restore for good.
    /// </summary>
    [Fact]
    public void KeepingTheCameraDoesNotLeakIntoTheNextHandover()
    {
        Grant();
        _presenter.ReleaseControl(Client, keepCamera: true);

        _sceneView.CameraPose = UserPose;
        Grant();
        _sceneView.CameraPose = AgentPose;
        _arbiter.Release(Client);

        Assert.Equal(UserPose, _sceneView.CameraPose);
    }

    private static readonly CameraPose UserPose =
        new(new Godot.Vector3(1f, 2f, 3f), 10f, -20f, 24f);

    private static readonly CameraPose AgentPose =
        new(new Godot.Vector3(9f, 9f, 9f), 90f, 0f, 40f);

    private void Grant()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _presenter.Tick(3.0);
        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
    }

    /// <summary>
    /// The topic follows the agent through every phase of the banner, and leaves
    /// with it: the countdown shows what the agent came to do, the working plaque
    /// follows its report-topic, and a released editor claims nothing.
    /// </summary>
    [Fact]
    public void TheBannerCarriesTheAgentsTopic()
    {
        _arbiter.Request(Client, "Claude", "Building a tank");
        Assert.Equal("Building a tank", _view.Topic);

        _presenter.Tick(5.0);
        Assert.Equal(ControlBannerKind.AgentWorking, _view.Kind);
        Assert.Equal("Building a tank", _view.Topic);

        _arbiter.SetTopic(Client, "Painting the turret");
        Assert.Equal("Painting the turret", _view.Topic);

        _arbiter.Release(Client);
        Assert.Equal(string.Empty, _view.Topic);
    }

    /// <summary>
    /// Restating the topic is not a new session: the running clock keeps
    /// counting. The arbiter's Changed fires for the restatement too, and the
    /// presenter must reset its clocks only on a real transition.
    /// </summary>
    [Fact]
    public void RestatingTheTopicDoesNotResetTheClock()
    {
        _arbiter.Request(Client, "Claude", "Building");
        _presenter.Tick(5.0);

        _presenter.Tick(30.0);
        _arbiter.SetTopic(Client, "Painting");
        _presenter.Tick(1.0);

        Assert.Equal(ElapsedTimeHelper.Format(31.0), _view.Elapsed);
    }
}
