using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Which camera the scene renders through, in both directions.
/// </summary>
/// <remarks>
/// What it restores is the <em>choice</em>, not the answer: the scene falls back
/// to the first camera it holds when the chosen one is gone, so undoing back to a
/// choice whose camera no longer exists puts the choice back and the fallback
/// goes on standing in for it. That is the same shape as a deleted scene falling
/// back to another one, and it is what makes deleting the active camera and
/// undoing it hand the picture back to the camera it was taken through.
/// </remarks>
internal sealed class SetActiveCameraCommand : ICommand
{
    private readonly Scene _scene;
    private readonly Guid _before;
    private readonly Guid _after;

    public SetActiveCameraCommand(Scene scene, HierarchyElement element, Guid before, Guid after)
    {
        _scene = scene;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(
            ActionKind.ChangeActiveCamera, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyActiveCamera(_after);

    public void Undo() => _scene.ApplyActiveCamera(_before);
}
