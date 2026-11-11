using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="SetReferenceImageCommand"/>
internal sealed class SetReferenceModelCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly ReferenceModel _reference;
    private readonly ReferenceModelSettings _before;
    private readonly ReferenceModelSettings _after;

    public SetReferenceModelCommand(
        Scene scene,
        HierarchyElement element,
        ReferenceModel reference,
        ReferenceModelSettings before,
        ReferenceModelSettings after)
    {
        _scene = scene;
        _element = element;
        _reference = reference;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(ActionKind.ChangeReference, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyReferenceModel(_element, _reference, _after);

    public void Undo() => _scene.ApplyReferenceModel(_element, _reference, _before);
}
