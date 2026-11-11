namespace VoxelCAD.CommonTypes;

/// <summary>
/// A question with up to three answers. The labels come from the presenter
/// because only it knows what is being confirmed — "Save" and "Discard" mean
/// nothing to a dialog.
/// </summary>
/// <param name="DenyLabel">
/// The third answer: proceed, but throw the work away. Empty when the question
/// has only two, and then no button is offered for it.
/// </param>
public readonly record struct ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string DenyLabel,
    string CancelLabel);
