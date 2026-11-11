using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The whole chrome as a test can drive it: menus raise events, dialogs answer
/// whatever the test scripted, and everything the shell pushes back is recorded.
/// </summary>
internal sealed class FakeShellView : IShellView
{
    private readonly List<(string Title, string Message)> _errorsList = new();
    private readonly Dictionary<CreateKind, bool> _createEnabledByKindDictionary = new();

    public event Action? NewRequested;

    public event Action? OpenRequested;

    public event Action? RecoverRequested;

    public event Action? SaveRequested;

    public event Action? SaveAsRequested;

    public event Action? ImportRequested;

    public event Action? ExportRequested;

    public event Action? CloseRequested;

    public event Action? UndoRequested;

    public event Action? RedoRequested;

    public event Action<CreateKind>? CreateRequested;

    public event Action<IReadOnlyList<string>>? FilesDropped;

    public event Action<bool>? ReferencesVisibleRequested;

    public event Action? DeleteSelectionRequested;

    public event Action? CopyRequested;

    public event Action? CutRequested;

    public event Action? PasteRequested;

    public event Action? DuplicateRequested;

    public event Action<Guid, Guid>? ReplaceMaterialUsesRequested;

    public event Action? PreferencesRequested;

    public event Action? RenderRequested;

    public event Action<IReadOnlyList<PanelPlacement>>? PanelLayoutEdited;

    public event Action<PanelWidths>? PanelWidthsEdited;

    public event Action? CancelControlRequested;

    public event Action? StopControlRequested;

    public event Action? UserInteractionBegan;

    public event Action? UserInteractionEnded;

    /// <summary>What the next path dialog answers. Null stands for a cancel.</summary>
    public Func<FileDialogRequest, Task<string?>> OnAskPath { get; set; } =
        _ => Task.FromResult<string?>(null);

    public Func<ConfirmRequest, Task<ConfirmResult>> OnAskConfirm { get; set; } =
        _ => Task.FromResult(ConfirmResult.Cancel);

    public FakeSceneView SceneView { get; } = new();

    public FakeHierarchyView HierarchyView { get; } = new();

    public FakeInspectorView InspectorView { get; } = new();

    public FakeHistoryView HistoryView { get; } = new();

    public FakeSettingsView SettingsView { get; } = new();


    public FakeRenderView RenderView { get; } = new();

    public FakeUiDriver UiDriverView { get; } = new();

    public ISceneView Scene => SceneView;

    public IHierarchyView Hierarchy => HierarchyView;

    public IInspectorView Inspector => InspectorView;

    public IHistoryView History => HistoryView;

    public ISettingsView Settings => SettingsView;

    public IRenderView Render => RenderView;

    public IUiDriver UiDriver => UiDriverView;

    public string WindowTitle { get; private set; } = string.Empty;

    public bool Closed { get; private set; }

    public bool CanUndo { get; private set; }

    public bool CanRedo { get; private set; }

    public bool DeleteEnabled { get; private set; }

    /// <summary>Where the navigation cube was last put.</summary>
    public ViewCubeCorner ViewCubeCorner { get; private set; }

    /// <summary>
    /// How many times it was pushed, so a test can tell it being applied again —
    /// after preferences changed — from the same value simply still being there.
    /// </summary>
    public int ViewCubeCornerCount { get; private set; }

    public bool AnimationsEnabled { get; private set; } = true;

    public AnimationSpeed AnimationSpeed { get; private set; }

    public int AnimationCount { get; private set; }

    public EditorTheme Theme { get; private set; }

    public string Accent { get; private set; } = string.Empty;

    public int SkinCount { get; private set; }

    public FileDialogRequest? LastPathRequest { get; private set; }

    public ConfirmRequest? LastConfirmRequest { get; private set; }

    public int ConfirmCount { get; private set; }

    public IReadOnlyList<(string Title, string Message)> Errors => _errorsList;

    public Task<string?> AskPathAsync(FileDialogRequest request)
    {
        LastPathRequest = request;

        return OnAskPath(request);
    }

    public Task<ConfirmResult> AskConfirmAsync(ConfirmRequest request)
    {
        LastConfirmRequest = request;
        ConfirmCount++;

        return OnAskConfirm(request);
    }

    public void ShowError(string title, string message) => _errorsList.Add((title, message));

    public void SetWindowTitle(string title) => WindowTitle = title;

    public void SetUndoState(bool canUndo, bool canRedo)
    {
        CanUndo = canUndo;
        CanRedo = canRedo;
    }

    public void SetCreateEnabled(CreateKind kind, bool enabled) =>
        _createEnabledByKindDictionary[kind] = enabled;

    public bool IsCreateEnabled(CreateKind kind) =>
        _createEnabledByKindDictionary.TryGetValue(kind, out bool enabled) && enabled;

    public void SetDeleteEnabled(bool enabled) => DeleteEnabled = enabled;

    public Func<IReadOnlyList<MaterialChoice>>? MaterialChoices { get; set; }

    public Func<SelectionTarget, bool>? CanReplaceMaterialUses { get; set; }

    /// <summary>The materials a picker opened right now would offer.</summary>
    public IReadOnlyList<MaterialChoice> AskMaterialChoices() =>
        MaterialChoices?.Invoke() ?? Array.Empty<MaterialChoice>();

    /// <summary>Whether a menu built on that row right now would offer the item.</summary>
    public bool AskCanReplaceMaterialUses(SelectionTarget target) =>
        CanReplaceMaterialUses?.Invoke(target) == true;

    public void RaiseReplaceMaterialUses(Guid fromId, Guid toId) =>
        ReplaceMaterialUsesRequested?.Invoke(fromId, toId);

    /// <summary>Whether the File menu's recovery row was last pushed live.</summary>
    public bool RecoverEnabled { get; private set; }

    public void SetRecoverEnabled(bool enabled) => RecoverEnabled = enabled;

    /// <summary>How the switch is drawn — starts lit, as the viewport starts drawn.</summary>
    public bool ReferencesVisible { get; private set; } = true;

    public void SetReferencesVisible(bool visible) => ReferencesVisible = visible;

    /// <summary>Whether the chrome was last told to frost its panels.</summary>
    public bool GlassPanels { get; private set; } = true;

    public void SetGlassPanels(bool frosted) => GlassPanels = frosted;

    public void SetSkin(EditorTheme theme, string accent)
    {
        Theme = theme;
        Accent = accent;
        SkinCount++;
    }

    public void SetAnimation(bool enabled, AnimationSpeed speed)
    {
        AnimationsEnabled = enabled;
        AnimationSpeed = speed;
        AnimationCount++;
    }

    public void SetViewCubeCorner(ViewCubeCorner corner)
    {
        ViewCubeCorner = corner;
        ViewCubeCornerCount++;
    }

    public IReadOnlyList<PanelPlacement> PanelLayout { get; private set; } =
        Array.Empty<PanelPlacement>();

    public int PanelLayoutCount { get; private set; }

    public void SetPanelLayout(IReadOnlyList<PanelPlacement> placements)
    {
        PanelLayout = placements;
        PanelLayoutCount++;
    }

    public PanelWidths PanelWidths { get; private set; }

    public int PanelWidthsCount { get; private set; }

    public void SetPanelWidths(PanelWidths widths)
    {
        PanelWidths = widths;
        PanelWidthsCount++;
    }

    public void RaisePanelWidthsEdited(PanelWidths widths) =>
        PanelWidthsEdited?.Invoke(widths);

    public void RaisePanelLayoutEdited(IReadOnlyList<PanelPlacement> placements) =>
        PanelLayoutEdited?.Invoke(placements);

    public void Close() => Closed = true;

    public ControlBannerState ControlBanner { get; private set; }

    public void SetControlBanner(ControlBannerState state) => ControlBanner = state;

    public void RaiseNew() => NewRequested?.Invoke();

    public void RaiseOpen() => OpenRequested?.Invoke();

    public void RaiseRecover() => RecoverRequested?.Invoke();

    public void RaiseSave() => SaveRequested?.Invoke();

    public void RaiseSaveAs() => SaveAsRequested?.Invoke();

    public void RaiseImport() => ImportRequested?.Invoke();

    public void RaiseExport() => ExportRequested?.Invoke();

    public void RaiseClose() => CloseRequested?.Invoke();

    public void RaiseUndo() => UndoRequested?.Invoke();

    public void RaiseRedo() => RedoRequested?.Invoke();

    public void RaiseCreate(CreateKind kind) => CreateRequested?.Invoke(kind);

    public void RaiseFilesDropped(params string[] paths) => FilesDropped?.Invoke(paths);

    public void RaiseReferencesVisible(bool visible) =>
        ReferencesVisibleRequested?.Invoke(visible);

    public void RaiseCreateGroup() => RaiseCreate(CreateKind.Group);

    public void RaiseCreateVoxelModel() => RaiseCreate(CreateKind.VoxelModel);

    public void RaiseDeleteSelection() => DeleteSelectionRequested?.Invoke();

    public void RaiseCopy() => CopyRequested?.Invoke();

    public void RaiseCut() => CutRequested?.Invoke();

    public void RaisePaste() => PasteRequested?.Invoke();

    public void RaiseDuplicate() => DuplicateRequested?.Invoke();

    public void RaisePreferences() => PreferencesRequested?.Invoke();

    public void RaiseRender() => RenderRequested?.Invoke();

    public void RaiseCancelControl() => CancelControlRequested?.Invoke();

    public void RaiseStopControl() => StopControlRequested?.Invoke();

    public void RaiseInteractionBegan() => UserInteractionBegan?.Invoke();

    public void RaiseInteractionEnded() => UserInteractionEnded?.Invoke();
}
