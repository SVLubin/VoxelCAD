using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// One undoable change. Internal by design: the outside world calls
/// <see cref="IScene"/> methods and never assembles commands itself.
/// </summary>
/// <remarks>
/// <see cref="Do"/> may run more than once — the same instance is reused on redo,
/// which is what keeps element identity stable across undo/redo cycles. Both
/// directions go through the scene's own mutators so that views receive the same
/// change events either way.
/// </remarks>
internal interface ICommand
{
    HistoryRecord Record { get; }

    void Do();

    void Undo();
}
