using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The state sibling presenters share. Each field is a value plus a signal, and
/// the signal is what keeps the panels from having to know about each other.
/// </summary>
public sealed class EditorSessionTests
{
    /// <summary>
    /// A fresh window points at things before it edits them. Stated in the session
    /// rather than left to the enum's zero, so the two can be checked against each
    /// other instead of one standing in for the other.
    /// </summary>
    [Fact]
    public void TheToolStartsOnTheCursor()
    {
        var session = new EditorSession();

        Assert.Equal(ToolKind.Select, session.ActiveTool);
    }

    /// <summary>What the recovery snapshot waits on: a gesture is in flight from Begin to End.</summary>
    [Fact]
    public void HasGestureFollowsBeginAndEnd()
    {
        var session = new EditorSession();
        var scope = new DisposableFlag();

        Assert.False(session.HasGesture);

        session.BeginGesture(scope);
        Assert.True(session.HasGesture);

        session.EndGesture();
        Assert.False(session.HasGesture);
        Assert.True(scope.Disposed);
    }

    private sealed class DisposableFlag : System.IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void SettingTheToolRoundTripsAndAnnouncesIt()
    {
        var session = new EditorSession();
        var changes = 0;
        session.ToolChanged += () => changes++;

        session.ActiveTool = ToolKind.Rotate;

        Assert.Equal(ToolKind.Rotate, session.ActiveTool);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// Same rule the selection follows: a value that did not change is not news,
    /// and a viewport rebuilding its handles on every keystroke of the key it is
    /// already in would be.
    /// </summary>
    [Fact]
    public void SettingTheSameToolAnnouncesNothing()
    {
        var session = new EditorSession { ActiveTool = ToolKind.Move };
        var changes = 0;
        session.ToolChanged += () => changes++;

        session.ActiveTool = ToolKind.Move;

        Assert.Equal(0, changes);
    }
}
