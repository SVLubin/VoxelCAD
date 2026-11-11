using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Control is exclusive and handed over as a negotiation. These are the
/// transitions the presenter drives; the timing decisions around them are its
/// own.
/// </summary>
public sealed class ControlArbiterTests
{
    private const string Client = "agent-a";
    private const string Other = "agent-b";

    private readonly ControlArbiter _arbiter = new();

    [Fact]
    public void TheUserStartsInControl()
    {
        Assert.Equal(ControlStatus.Available, _arbiter.Status);
        Assert.True(_arbiter.IsUserInControl);
    }

    [Fact]
    public void ARequestBecomesPendingAndNamesTheAgent()
    {
        _arbiter.Request(Client, "Claude", string.Empty);

        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
        Assert.Equal("Claude", _arbiter.AgentName);
        Assert.True(_arbiter.IsUserInControl);
    }

    [Fact]
    public void GrantingHandsControlToTheAgent()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.False(_arbiter.IsUserInControl);
    }

    [Fact]
    public void CancellingTheCountdownIsARefusal()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Deny();

        Assert.Equal(ControlStatus.Denied, _arbiter.Status);
        Assert.True(_arbiter.IsUserInControl);
    }

    [Fact]
    public void TheAgentCanGiveControlBack()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();
        _arbiter.Release(Client);

        Assert.Equal(ControlStatus.Available, _arbiter.Status);
        Assert.Equal(string.Empty, _arbiter.AgentName);
    }

    [Fact]
    public void TheUserCanTakeControlBackWhileTheAgentHoldsIt()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();
        _arbiter.Stop();

        // The agent should read this as a refusal, not an idle editor.
        Assert.Equal(ControlStatus.Denied, _arbiter.Status);
        Assert.True(_arbiter.IsUserInControl);
    }

    [Fact]
    public void ASilentAgentTimesOutDistinctlyFromARefusal()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();
        _arbiter.TimeOut();

        // Both hand the editor back, and they mean opposite things: the agent
        // should read this as its own silence, not the user's decision.
        Assert.Equal(ControlStatus.TimedOut, _arbiter.Status);
        Assert.True(_arbiter.IsUserInControl);
        Assert.Equal(string.Empty, _arbiter.AgentName);
    }

    [Fact]
    public void OnlyAGrantedAgentCanTimeOut()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.TimeOut();

        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
    }

    [Fact]
    public void AbandoningReturnsAGrantedEditorToTheUser()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();
        _arbiter.Abandon();

        // A crashed agent is not there to read a verdict, so this is the clean
        // Available rather than a refusal or a timeout.
        Assert.Equal(ControlStatus.Available, _arbiter.Status);
        Assert.Equal(string.Empty, _arbiter.AgentName);
    }

    [Fact]
    public void AbandoningAlsoDropsAPendingRequest()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Abandon();

        Assert.Equal(ControlStatus.Available, _arbiter.Status);
    }

    [Fact]
    public void AbandoningAnAlreadyAvailableEditorSaysNothing()
    {
        var changes = 0;
        _arbiter.Changed += () => changes++;

        _arbiter.Abandon();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void EveryAgentCommandIsAnnounced()
    {
        var commands = 0;
        _arbiter.AgentCommandReceived += _ => commands++;

        _arbiter.NoteAgentCommand(Client);
        _arbiter.NoteAgentCommand(Client);

        Assert.Equal(2, commands);
    }

    [Fact]
    public void GrantingWithoutAPendingRequestDoesNothing()
    {
        _arbiter.Grant();

        Assert.Equal(ControlStatus.Available, _arbiter.Status);
    }

    /// <summary>
    /// The refusal that makes control exclusive to <b>one</b> agent. It is a
    /// returned false rather than a silent no-op, because the caller has to report
    /// it as a refusal: an agent that reads "granted" and cannot tell whether it was
    /// granted to itself goes on to edit a document another agent has its hands in.
    /// </summary>
    [Fact]
    public void ASecondAgentIsRefusedWhileTheFirstHoldsControl()
    {
        _arbiter.Request(Client, "First", string.Empty);
        _arbiter.Grant();

        Assert.False(_arbiter.Request(Other, "Second", "Sneaking in"));

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.Equal("First", _arbiter.AgentName);
        Assert.True(_arbiter.IsHeldBy(Client));
        Assert.False(_arbiter.IsHeldBy(Other));
        Assert.True(_arbiter.IsClaimedByAnother(Other));
    }

    /// <summary>A countdown is claimed too — the request ahead is not up for grabs.</summary>
    [Fact]
    public void ASecondAgentIsRefusedWhileTheFirstIsPending()
    {
        _arbiter.Request(Client, "First", "Building a tank");

        Assert.False(_arbiter.Request(Other, "Second", "Something else"));

        Assert.Equal("First", _arbiter.AgentName);
        Assert.Equal("Building a tank", _arbiter.Topic);
    }

    /// <summary>
    /// Asking again is not asking anew: the same agent keeps its place in the
    /// negotiation and only restates who it is and what it is doing.
    /// </summary>
    [Fact]
    public void TheSameAgentAskingAgainKeepsItsPlaceAndRestatesItself()
    {
        _arbiter.Request(Client, "Claude", "Building a tank");
        _arbiter.Grant();

        Assert.True(_arbiter.Request(Client, "Claude", "Painting the turret"));

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.Equal("Painting the turret", _arbiter.Topic);
    }

    /// <summary>
    /// Releasing a session somebody else opened is the sharpest form of the
    /// confusion identity exists to prevent, so it is refused outright.
    /// </summary>
    [Fact]
    public void OnlyTheHolderCanReleaseControl()
    {
        _arbiter.Request(Client, "First", string.Empty);
        _arbiter.Grant();

        Assert.False(_arbiter.Release(Other));

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.True(_arbiter.Release(Client));
        Assert.Equal(ControlStatus.Available, _arbiter.Status);
    }

    /// <summary>The plaque is the user's window onto who is working; a bystander must not write on it.</summary>
    [Fact]
    public void OnlyTheAgentInTheHandoverCanSetTheTopic()
    {
        _arbiter.Request(Client, "First", "Building a tank");
        _arbiter.Grant();

        Assert.False(_arbiter.SetTopic(Other, "Something else"));

        Assert.Equal("Building a tank", _arbiter.Topic);
    }

    /// <summary>
    /// A refusal is news for the agent that was refused and for nobody else — an
    /// unrelated agent reading the status finds an editor that is simply the user's.
    /// </summary>
    [Fact]
    public void AVerdictBelongsToTheAgentItWasAimedAt()
    {
        _arbiter.Request(Client, "First", string.Empty);
        _arbiter.Grant();
        _arbiter.Stop();

        Assert.Equal(ControlStatus.Denied, _arbiter.Status);
        Assert.True(_arbiter.IsVerdictFor(Client));
        Assert.False(_arbiter.IsVerdictFor(Other));
    }

    [Fact]
    public void ANewRequestClearsAnEarlierDenial()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Deny();

        _arbiter.Request(Client, "Claude", string.Empty);

        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
    }

    [Fact]
    public void InteractionTokensStackAndClear()
    {
        Assert.False(_arbiter.IsUserBusy);

        _arbiter.BeginInteraction();
        _arbiter.BeginInteraction();
        Assert.True(_arbiter.IsUserBusy);

        _arbiter.EndInteraction();
        Assert.True(_arbiter.IsUserBusy);

        _arbiter.EndInteraction();
        Assert.False(_arbiter.IsUserBusy);
    }

    [Fact]
    public void EndingMoreThanBegunDoesNotGoNegative()
    {
        _arbiter.EndInteraction();
        _arbiter.BeginInteraction();

        Assert.True(_arbiter.IsUserBusy);
    }

    [Fact]
    public void EveryStatusChangeIsAnnouncedOnce()
    {
        var changes = 0;
        _arbiter.Changed += () => changes++;

        _arbiter.Request(Client, "Claude", string.Empty);   // Available -> Pending
        _arbiter.Grant();             // Pending -> Granted
        _arbiter.Grant();             // no-op
        _arbiter.Stop();              // Granted -> Denied

        Assert.Equal(3, changes);
    }

    [Fact]
    public void ARequestCarriesTheAgentsTopic()
    {
        _arbiter.Request(Client, "Claude", "Building a tank");

        Assert.Equal("Building a tank", _arbiter.Topic);
    }

    [Fact]
    public void TheTopicCanBeRestatedWhilePendingOrGranted()
    {
        _arbiter.Request(Client, "Claude", "Building a tank");
        _arbiter.SetTopic(Client, "Placing the hull");
        Assert.Equal("Placing the hull", _arbiter.Topic);

        _arbiter.Grant();
        _arbiter.SetTopic(Client, "Painting the turret");
        Assert.Equal("Painting the turret", _arbiter.Topic);
    }

    /// <summary>A topic with nobody behind it is a stale claim, so every exit clears it.</summary>
    [Fact]
    public void EveryExitClearsTheTopic()
    {
        _arbiter.Request(Client, "Claude", "Building a tank");
        _arbiter.Grant();
        _arbiter.Release(Client);
        Assert.Equal(string.Empty, _arbiter.Topic);

        _arbiter.Request(Client, "Claude", "Back again");
        _arbiter.Grant();
        _arbiter.Stop();
        Assert.Equal(string.Empty, _arbiter.Topic);

        _arbiter.Request(Client, "Claude", "Third time");
        _arbiter.Grant();
        _arbiter.TimeOut();
        Assert.Equal(string.Empty, _arbiter.Topic);

        _arbiter.Request(Client, "Claude", "Fourth");
        _arbiter.Abandon();
        Assert.Equal(string.Empty, _arbiter.Topic);
    }

    [Fact]
    public void ATopicArrivingAfterControlWasLostNamesNobody()
    {
        _arbiter.Request(Client, "Claude", "Building a tank");
        _arbiter.Grant();
        _arbiter.Stop();

        _arbiter.SetTopic(Client, "Still here?");

        Assert.Equal(string.Empty, _arbiter.Topic);
    }
}
