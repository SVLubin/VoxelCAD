using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>The render card as a test can open, read and press buttons on.</summary>
internal sealed class FakeRenderView : IRenderView
{
    public event Action? CloseRequested;

    public event Action? DialMoved;

    public event Action? RenderAgainRequested;

    public event Action? SaveRequested;

    public event Action<Vector2>? SizeChanged;

    public event Action<int, int>? FocusPicked;

    public event Action<bool>? DialsFolded;

    public bool IsOpen { get; private set; }

    public RenderDials Dials { get; set; }

    public string Message { get; private set; } = string.Empty;

    public int SamplesShown { get; private set; }

    public int Images { get; private set; }

    public Vector2 CardSize { get; private set; }

    public RenderProgress Progress { get; private set; }

    /// <summary>What the card would say it is looking at — set by a test that wants to aim one.</summary>
    public RenderRegion PriorityRegion { get; set; }

    public int Fits { get; private set; }

    public int ActualSizes { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool DialsOpen { get; private set; }

    public void Open(RenderDials dials, Vector2 size, bool dialsOpen)
    {
        IsOpen = true;
        Dials = dials;
        CardSize = size;
        DialsOpen = dialsOpen;
    }

    /// <summary>How many times the dials were pushed in without a turn being reported.</summary>
    public int DialsShown { get; private set; }

    public void ShowDials(RenderDials dials)
    {
        Dials = dials;
        DialsShown++;
    }

    public void Close() => IsOpen = false;

    public bool TryDismiss()
    {
        if (!IsOpen)
            return false;

        CloseRequested?.Invoke();

        return true;
    }

    public void ShowImage(Texture2D image) => Images++;

    public void ShowSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void ShowProgress(RenderProgress progress)
    {
        Progress = progress;
        SamplesShown = progress.SamplesTaken;
    }

    public void FitImage() => Fits++;

    public void ShowActualSize() => ActualSizes++;

    public void ShowRegion(RenderRegion region) => PriorityRegion = region;

    public bool IsPickingFocus { get; private set; }

    public void SetPickingFocus(bool picking) => IsPickingFocus = picking;

    /// <summary>
    /// Whether the card is offering the Focus button. Pushed by the presenter now
    /// that the blur it depends on is the camera's rather than a dial's.
    /// </summary>
    public bool FocusAvailable { get; private set; }

    public void SetFocusAvailable(bool available) => FocusAvailable = available;

    public void ShowMessage(string message) => Message = message;

    public void RaiseClose() => CloseRequested?.Invoke();

    public void RaiseDialMoved() => DialMoved?.Invoke();

    public void RaiseRenderAgain() => RenderAgainRequested?.Invoke();

    public void RaiseSave() => SaveRequested?.Invoke();

    public void RaiseSizeChanged(Vector2 size) => SizeChanged?.Invoke(size);

    public void RaiseFocusPicked(int x, int y) => FocusPicked?.Invoke(x, y);

    public void RaiseDialsFolded(bool open) => DialsFolded?.Invoke(open);
}
