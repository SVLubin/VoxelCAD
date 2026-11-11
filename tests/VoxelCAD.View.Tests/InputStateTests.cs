using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Whose pointer and keys the tools read. Control is exclusive on commands and on
/// events; this is the third channel, and it was the one that leaked — while an
/// agent held the editor, the user's Shift still flipped the brush to erase and
/// their mouse button could end the agent's drag.
/// </summary>
public sealed class InputStateTests
{
    private readonly FakeInputState _user = new();
    private readonly InjectedInputState _agent = new();
    private readonly EditorInputState _editor;

    public InputStateTests()
    {
        _editor = new EditorInputState(_user, _agent);
    }

    // --- the selector ---

    /// <summary>
    /// The ordinary path, and the one that must not change: with nobody else
    /// driving, the tools read the machine exactly as they always did.
    /// </summary>
    [Fact]
    public void TheUsersDevicesAnswerWhileTheUserDrives()
    {
        _user.Pointer = new Vector2(120f, 40f);
        _user.Hold(Key.Shift);
        _user.Hold(MouseButton.Left);

        Assert.True(_editor.TryGetPointer(out Vector2 pointer));
        Assert.Equal(new Vector2(120f, 40f), pointer);
        Assert.True(_editor.IsKeyDown(Key.Shift));
        Assert.True(_editor.IsButtonDown(MouseButton.Left));
    }

    /// <summary>The leak, in one assertion: a held Shift stops reaching the tools.</summary>
    [Fact]
    public void TheUsersKeysStopAnsweringWhileAnAgentDrives()
    {
        _user.Hold(Key.Shift);
        _user.Hold(Key.Alt);
        _user.Hold(MouseButton.Left);

        _editor.SetAgentDriving(true);

        Assert.False(_editor.IsKeyDown(Key.Shift));
        Assert.False(_editor.IsKeyDown(Key.Alt));
        Assert.False(_editor.IsButtonDown(MouseButton.Left));
    }

    [Fact]
    public void TheUsersPointerStopsAnsweringWhileAnAgentDrives()
    {
        _user.Pointer = new Vector2(500f, 300f);

        _editor.SetAgentDriving(true);

        Assert.False(_editor.TryGetPointer(out _));
    }

    [Fact]
    public void TheAgentsOwnStateAnswersWhileItDrives()
    {
        _editor.SetAgentDriving(true);
        _agent.NotePointer(new Vector2(64f, 32f));
        _agent.NoteKey(Key.Shift, pressed: true);
        _agent.NoteButton(MouseButton.Left, pressed: true);

        Assert.True(_editor.TryGetPointer(out Vector2 pointer));
        Assert.Equal(new Vector2(64f, 32f), pointer);
        Assert.True(_editor.IsKeyDown(Key.Shift));
        Assert.True(_editor.IsButtonDown(MouseButton.Left));
    }

    [Fact]
    public void TheUsersDevicesAnswerAgainOnRelease()
    {
        _user.Pointer = new Vector2(10f, 10f);
        _user.Hold(Key.Shift);

        _editor.SetAgentDriving(true);
        _editor.SetAgentDriving(false);

        Assert.True(_editor.IsKeyDown(Key.Shift));
        Assert.True(_editor.TryGetPointer(out _));
    }

    /// <summary>
    /// A handover starts from nothing held: a pointer left over from the last
    /// agent's session would put a preview on screen nobody had asked for, and a
    /// key it never released would keep the brush in erase mode.
    /// </summary>
    [Fact]
    public void EachGrantStartsTheAgentFromNothing()
    {
        _editor.SetAgentDriving(true);
        _agent.NotePointer(new Vector2(64f, 32f));
        _agent.NoteKey(Key.Shift, pressed: true);
        _editor.SetAgentDriving(false);

        _editor.SetAgentDriving(true);

        Assert.False(_editor.TryGetPointer(out _));
        Assert.False(_editor.IsKeyDown(Key.Shift));
    }

    // --- the agent's own state ---

    /// <summary>
    /// Not the origin, not the last user position — nowhere. A tool with no
    /// pointer must offer nothing rather than aim at a default.
    /// </summary>
    [Fact]
    public void AnAgentThatHasMovedNoPointerHasNone()
    {
        Assert.False(_agent.TryGetPointer(out _));
        Assert.False(_agent.IsKeyDown(Key.Shift));
        Assert.False(_agent.IsButtonDown(MouseButton.Left));
    }

    [Fact]
    public void ReleasingAKeyStopsItBeingHeld()
    {
        _agent.NoteKey(Key.Shift, pressed: true);
        Assert.True(_agent.IsKeyDown(Key.Shift));

        _agent.NoteKey(Key.Shift, pressed: false);
        Assert.False(_agent.IsKeyDown(Key.Shift));
    }

    [Fact]
    public void SeveralKeysAreHeldAtOnce()
    {
        _agent.NoteKey(Key.Shift, pressed: true);
        _agent.NoteKey(Key.Alt, pressed: true);
        _agent.NoteKey(Key.Shift, pressed: false);

        Assert.False(_agent.IsKeyDown(Key.Shift));
        Assert.True(_agent.IsKeyDown(Key.Alt));
    }

    [Fact]
    public void ThePointerFollowsWhereverItWasLastPut()
    {
        _agent.NotePointer(new Vector2(1f, 2f));
        _agent.NotePointer(new Vector2(3f, 4f));

        Assert.True(_agent.TryGetPointer(out Vector2 pointer));
        Assert.Equal(new Vector2(3f, 4f), pointer);
    }

    /// <summary>The machine, as a test can hold it still.</summary>
    private sealed class FakeInputState : IInputState
    {
        private readonly HashSet<Key> _keysHashSet = new();
        private readonly HashSet<MouseButton> _buttonsHashSet = new();

        public Vector2 Pointer { get; set; }

        public void Hold(Key key) => _keysHashSet.Add(key);

        public void Hold(MouseButton button) => _buttonsHashSet.Add(button);

        public bool TryGetPointer(out Vector2 position)
        {
            position = Pointer;

            return true;
        }

        public bool IsKeyDown(Key key) => _keysHashSet.Contains(key);

        public bool IsButtonDown(MouseButton button) => _buttonsHashSet.Contains(button);
    }
}
