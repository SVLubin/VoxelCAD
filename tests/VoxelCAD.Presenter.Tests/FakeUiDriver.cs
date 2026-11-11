using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// A control tree a test can spell out, and a record of what was injected into it.
/// The walk's own policy is the view's business and is tested there; what this
/// stands in for is everything above the seam.
/// </summary>
internal sealed class FakeUiDriver : IUiDriver
{
    private readonly List<UiControl> _controlsList = new();
    private readonly List<Injection> _injectionsList = new();

    public Vector2I WindowSize { get; set; } = new(1200, 800);

    public UiSnapshotOptions LastOptions { get; private set; }

    /// <summary>
    /// What the walk found before the cap, when a test wants to force the refusal.
    /// Null means "as many as were reported".
    /// </summary>
    public int? TotalInScope { get; set; }

    public int Limit { get; set; } = 500;

    public IReadOnlyList<UiControl> Controls => _controlsList;

    /// <summary>A control at a rect, with everything else left ordinary.</summary>
    public FakeUiDriver Add(
        string reference,
        Rect2 rect,
        string type = "Button",
        string text = "",
        bool visible = true,
        bool disabled = false,
        bool takesMouse = true,
        int layer = 0)
    {
        _controlsList.Add(new UiControl(
            reference,
            type,
            text,
            rect,
            visible,
            disabled,
            Focused: false,
            takesMouse,
            layer));

        return this;
    }

    /// <summary>Every gesture asked for, in order.</summary>
    public IReadOnlyList<Injection> Injections => _injectionsList;

    public Injection LastInjection => _injectionsList[^1];

    /// <summary>
    /// Resolution the naive way — by reference, with the three refusals a command
    /// test cares about seeing propagate. What the real driver does on top of this
    /// (the occlusion check, the zero-sized window) is the view's own logic and is
    /// tested there.
    /// </summary>
    public InputTargetResolution Resolve(string reference, Vector2 offset)
    {
        foreach (UiControl control in _controlsList)
        {
            if (control.Reference != reference)
                continue;

            if (!control.Visible)
                return new InputTargetResolution(false, $"'{reference}' is not on screen.", Vector2.Zero, control);

            if (control.Disabled)
                return new InputTargetResolution(false, $"'{reference}' is disabled.", Vector2.Zero, control);

            var point = new Vector2(
                control.Rect.Position.X + control.Rect.Size.X * offset.X,
                control.Rect.Position.Y + control.Rect.Size.Y * offset.Y);

            return new InputTargetResolution(true, string.Empty, point, control);
        }

        return new InputTargetResolution(
            false,
            $"No control is called '{reference}'.",
            Vector2.Zero,
            default);
    }

    public void Move(Vector2 point) =>
        _injectionsList.Add(new Injection("move") { Point = point });

    public void Click(
        Vector2 point,
        MouseButton button,
        InputModifiers modifiers,
        int clicks,
        HoldAction action) =>
        _injectionsList.Add(new Injection("click")
        {
            Point = point,
            Button = button,
            Modifiers = modifiers,
            Count = clicks,
            Action = action
        });

    public void Drag(Vector2 from, Vector2 to, int steps, MouseButton button, InputModifiers modifiers) =>
        _injectionsList.Add(new Injection("drag")
        {
            Point = from,
            To = to,
            Count = steps,
            Button = button,
            Modifiers = modifiers
        });

    public void Key(Key key, HoldAction action, InputModifiers modifiers) =>
        _injectionsList.Add(new Injection("key")
        {
            Key = key,
            Action = action,
            Modifiers = modifiers
        });

    public void TypeText(string text) =>
        _injectionsList.Add(new Injection("text") { Text = text });

    public void Wheel(Vector2 point, int notches, InputModifiers modifiers) =>
        _injectionsList.Add(new Injection("wheel")
        {
            Point = point,
            Count = notches,
            Modifiers = modifiers
        });

    public void Pan(Vector2 point, Vector2 delta, int steps, InputModifiers modifiers) =>
        _injectionsList.Add(new Injection("pan")
        {
            Point = point,
            To = delta,
            Count = steps,
            Modifiers = modifiers
        });

    public void Magnify(Vector2 point, float factor, int steps, InputModifiers modifiers) =>
        _injectionsList.Add(new Injection("pinch")
        {
            Point = point,
            Count = steps,
            Modifiers = modifiers
        });

    public bool Step() => false;

    public UiSnapshot ReadControls(UiSnapshotOptions options)
    {
        LastOptions = options;

        var matchedList = new List<UiControl>();

        foreach (UiControl control in _controlsList)
        {
            if (!control.Visible && !options.IncludeInvisible)
                continue;

            if (options.Root.Length > 0
                && !control.Reference.StartsWith(options.Root, StringComparison.Ordinal))
            {
                continue;
            }

            matchedList.Add(control);
        }

        return new UiSnapshot(matchedList, TotalInScope ?? matchedList.Count, Limit);
    }

    public Vector2I GetWindowSize() => WindowSize;
}
