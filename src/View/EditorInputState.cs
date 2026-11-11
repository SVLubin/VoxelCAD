using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Which party's devices the editor's tools read. The one place the handover's
/// "control is exclusive" reaches polled input state, the way it already reaches
/// commands and events.
/// </summary>
/// <remarks>
/// Driven by the signal that already exists — the control presenter tells the
/// viewport when an agent holds the editor — so no new authority is invented and
/// there is exactly one answer to "who is driving", not two that can disagree.
/// </remarks>
internal sealed class EditorInputState : IInputState
{
    private readonly IInputState _user;
    private readonly InjectedInputState _agent;

    private bool _agentDriving;

    public EditorInputState(IInputState user, InjectedInputState agent)
    {
        _user = user;
        _agent = agent;
    }

    /// <summary>
    /// Hands the tools over to the agent's pointer and keys, or back. Each grant
    /// starts from nothing held and no pointer, so a session cannot inherit the
    /// last one's state.
    /// </summary>
    public void SetAgentDriving(bool driving)
    {
        if (driving == _agentDriving)
            return;

        _agentDriving = driving;

        if (driving)
            _agent.Clear();
    }

    public bool TryGetPointer(out Vector2 position) => Active.TryGetPointer(out position);

    public bool IsKeyDown(Key key) => Active.IsKeyDown(key);

    public bool IsButtonDown(MouseButton button) => Active.IsButtonDown(button);

    private IInputState Active => _agentDriving ? _agent : _user;
}
