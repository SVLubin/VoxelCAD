using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Restores a camera's whole lens in both directions — the same shape as
/// <see cref="SetSymmetryCommand"/>: it was A, now it is B, and nothing in the
/// scene moves either way.
/// </summary>
internal sealed class SetCameraLensCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly Camera _camera;
    private readonly CameraLens _before;
    private readonly CameraLens _after;

    public SetCameraLensCommand(
        Scene scene,
        HierarchyElement element,
        Camera camera,
        CameraLens before,
        CameraLens after)
    {
        _scene = scene;
        _element = element;
        _camera = camera;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(ActionKind.ChangeCamera, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyCameraLens(_element, _camera, _after);

    public void Undo() => _scene.ApplyCameraLens(_element, _camera, _before);
}
