using System;
using System.Threading.Tasks;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <inheritdoc cref="IDialogService"/>
/// <remarks>
/// A dialog is an interaction like any other, so this raises the same
/// began/ended pair a focused inspector field does — an agent asking for control
/// waits while a question is on screen instead of seizing the editor out from
/// under it.
/// <para>
/// Two surfaces, and the split is the platform's rather than ours. Picking a
/// file is the operating system's own window, which is what a desktop user
/// expects and what this cannot draw; asking a question is a
/// <see cref="DialogCard"/> in the shell, drawn out of the same glass as
/// everything else. So this is a <see cref="Control"/> now: it holds a card, and
/// the card has to live where the chrome does.
/// </para>
/// </remarks>
internal sealed partial class DialogHost : Control, IDialogService
{
    private readonly FileDialog _fileDialog = new();
    private readonly DialogCard _card;

    private TaskCompletionSource<string?>? _pathSource;
    private TaskCompletionSource<ConfirmResult>? _confirmSource;

    public DialogHost(MotionTiming timing)
    {
        // Not a field initializer: a question arrives at the editor's own pace
        // like every transition, and lands at once when they are off.
        _card = new DialogCard(timing);

        Name = "DialogHost";

        // A pass-through: the card inside it is what stops the mouse, and only
        // while a question is up.
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Native where the platform has one, and Godot's own where it does not.
        // That fallback is why this is a FileDialog rather than a DisplayServer
        // call: the engine picks, and the code above never has to.
        _fileDialog.UseNativeDialog = true;
        _fileDialog.Access = FileDialog.AccessEnum.Filesystem;

        _fileDialog.FileSelected += OnFileSelected;
        _fileDialog.Canceled += OnFileCanceled;

        _card.Answered += OnAnswered;

        AddChild(_fileDialog);
        AddChild(_card);
    }

    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    public Task<string?> AskPathAsync(FileDialogRequest request)
    {
        // One at a time. A second request while the picker is up would orphan the
        // first task, which its caller could not tell from a cancel.
        if (_pathSource != null)
            return Task.FromResult<string?>(null);

        _pathSource = new TaskCompletionSource<string?>();

        _fileDialog.Title = request.Title;
        _fileDialog.FileMode = request.Mode == FileDialogMode.Open
            ? FileDialog.FileModeEnum.OpenFile
            : FileDialog.FileModeEnum.SaveFile;

        _fileDialog.ClearFilters();

        foreach (FileDialogFilter filter in request.Filters)
            _fileDialog.AddFilter(filter.Glob, filter.Label);

        if (!string.IsNullOrEmpty(request.SuggestedPath))
            _fileDialog.CurrentPath = request.SuggestedPath;

        InteractionBegan?.Invoke();
        _fileDialog.PopupCentered(new Vector2I(900, 600));

        return _pathSource.Task;
    }

    public Task<ConfirmResult> AskConfirmAsync(ConfirmRequest request)
    {
        // One at a time, the same rule the picker keeps: a second question over
        // the first would orphan a task its caller could not tell from a cancel.
        if (_confirmSource != null)
            return Task.FromResult(ConfirmResult.Cancel);

        _confirmSource = new TaskCompletionSource<ConfirmResult>();

        InteractionBegan?.Invoke();
        _card.Ask(request);

        return _confirmSource.Task;
    }

    public void ShowError(string title, string message)
    {
        // An error is a question with one answer, and it is answered by being
        // acknowledged — so it goes through the same path and its result is
        // dropped. Anything waiting on a real question is left standing.
        if (_confirmSource != null)
            return;

        InteractionBegan?.Invoke();
        _card.ShowError(title, message);
    }

    /// <summary>
    /// Whether a question is on screen. The shell asks before answering a chord:
    /// a modal that stops the mouse and lets Cmd+S through is modal about one
    /// half of the keyboard's worth of ways to leave it.
    /// </summary>
    public bool IsAsking => _card.IsOpen;

    /// <summary>
    /// Escape, arriving from the viewport's own report — the one route a key
    /// downstream of it can take. See <see cref="DialogCard"/>.
    /// </summary>
    public bool TryDismiss() => _card.TryDismiss();

    private void OnFileSelected(string path) => CompletePath(path);

    private void OnFileCanceled() => CompletePath(null);

    private void CompletePath(string? path)
    {
        TaskCompletionSource<string?>? source = _pathSource;

        if (source == null)
            return;

        _pathSource = null;

        InteractionEnded?.Invoke();
        Resume(() => source.TrySetResult(path));
    }

    private void OnAnswered(ConfirmResult result)
    {
        TaskCompletionSource<ConfirmResult>? source = _confirmSource;

        _confirmSource = null;
        InteractionEnded?.Invoke();

        if (source != null)
            Resume(() => source.TrySetResult(result));
    }

    /// <summary>
    /// Hands a waiting caller its answer a frame later, on purpose. A continuation
    /// runs inline on whichever thread completes the task, and here that is the
    /// main thread inside a dialog's own close handling — where opening the next
    /// dialog is not safe.
    /// </summary>
    private static void Resume(Action complete) => Callable.From(complete).CallDeferred();
}
