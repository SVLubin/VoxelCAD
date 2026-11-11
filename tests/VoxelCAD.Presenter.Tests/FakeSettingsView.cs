using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>The settings form as a test can fill in and change things on.</summary>
internal sealed class FakeSettingsView : ISettingsView
{
    public event Action<PreferenceValues>? Edited;

    public event Action? CloseRequested;

    public event Action? RegenerateTokenRequested;

    public bool IsOpen { get; private set; }

    public PreferenceValues Shown { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public string Endpoint { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string AutosaveDirectory { get; private set; } = string.Empty;

    public string AutosaveStatus { get; private set; } = string.Empty;

    public void Fill(PreferenceValues values, string token, string endpoint, string autosaveDirectory)
    {
        Shown = values;
        Token = token;
        Endpoint = endpoint;
        AutosaveDirectory = autosaveDirectory;
    }

    public void SetStatus(string message) => Status = message;

    public void SetAutosaveStatus(string message) => AutosaveStatus = message;

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void RaiseEdited(PreferenceValues values) => Edited?.Invoke(values);

    public void RaiseClose() => CloseRequested?.Invoke();

    public void RaiseRegenerateToken() => RegenerateTokenRequested?.Invoke();
}
