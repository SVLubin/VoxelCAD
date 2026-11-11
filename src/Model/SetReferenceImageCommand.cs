using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Restores a reference plane's whole settings in both directions — the same shape
/// as <see cref="SetCameraLensCommand"/>: it was A, now it is B, and nothing in
/// the scene moves either way.
/// </summary>
internal sealed class SetReferenceImageCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly ReferenceImage _reference;
    private readonly ReferenceImageSettings _before;
    private readonly ReferenceImageSettings _after;

    public SetReferenceImageCommand(
        Scene scene,
        HierarchyElement element,
        ReferenceImage reference,
        ReferenceImageSettings before,
        ReferenceImageSettings after)
    {
        _scene = scene;
        _element = element;
        _reference = reference;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(ActionKind.ChangeReference, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyReferenceImage(_element, _reference, _after);

    public void Undo() => _scene.ApplyReferenceImage(_element, _reference, _before);
}
