using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// A context menu as a glass card rather than a <see cref="PopupMenu"/>: the
/// same pane the panels are made of, blurring everything already drawn beneath
/// it — the chrome included, since it is built last and draws over all of it.
/// </summary>
/// <remarks>
/// Not the engine's popup, and for three reasons that all pull the same way.
/// A <see cref="PopupMenu"/> paints its own panel and its own item rows, so
/// there is nowhere to put glass behind the items and no second screen read
/// worth taking. Its items are not <see cref="Control"/>s either, so an agent
/// can only aim into it by offset — the ledger's standing list-row gap — while
/// these are ordinary named buttons with references of their own. And an
/// embedded popup is a <see cref="Window"/>, which the clean-capture path and
/// the whole "one CanvasLayer of chrome" assumption would rather not grow more
/// of.
/// <para>
/// It stops the mouse across the whole window on purpose: a click outside is
/// how a menu is dismissed, and eating that click is what keeps the dismissal
/// from also pressing whatever was under it.
/// </para>
/// </remarks>
internal sealed partial class ContextMenuView : Control
{
    private const float MinimumWidth = 190f;

    /// <summary>How far the card is inset from the window when it would overflow.</summary>
    private const float WindowMargin = 6f;

    /// <summary>What a chord keeps between itself and the row's own edge.</summary>
    private const float ShortcutInset = 8f;

    /// <summary>
    /// What a row leaves free at its end for a chord to be written in. A menu
    /// with accelerators has to be wide enough for the longest of them, and a
    /// label sitting over the words would be unreadable rather than merely tight.
    /// </summary>
    private const float ShortcutRoom = 66f;

    private readonly PanelContainer _card;
    private readonly VBoxContainer _items = new() { Name = "Items" };
    private readonly GlassBackdrop _glass;

    public ContextMenuView()
    {
        Name = "ContextMenu";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _glass = new GlassBackdrop();

        _card = new PanelContainer
        {
            Name = "Card",
            ThemeTypeVariation = "GlassPanel",
            MouseFilter = MouseFilterEnum.Stop
        };
        _card.AddChild(_glass);
        _card.AddChild(Ui.Margin(_items, 4).Named("CardBody"));

        AddChild(_card);
    }

    public event Action<int>? ItemChosen;

    public void Open(Vector2 at, IReadOnlyList<ContextMenuItem> items)
    {
        foreach (Node child in _items.GetChildren())
        {
            _items.RemoveChild(child);
            child.QueueFree();
        }

        foreach (ContextMenuItem item in items)
            _items.AddChild(item.IsSeparator ? new HSeparator() : CreateRow(item));

        Visible = true;

        // Laid out after showing, because a hidden container reports no size:
        // the card has to know how big it is before it can be kept on screen.
        _card.Position = at;
        _card.CustomMinimumSize = new Vector2(MinimumWidth + ChordRoom(items), 0f);
        _card.ResetSize();
        CallDeferred(MethodName.PlaceCard, at);
    }

    public void Close()
    {
        if (!Visible)
            return;

        Visible = false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Anything the card did not claim landed outside it, which is the
        // ordinary way a menu is dismissed.
        if (@event is InputEventMouseButton { Pressed: true })
        {
            Close();
            AcceptEvent();
        }
    }

    /// <summary>
    /// Escape, asked by whoever owns this menu. Answers whether there was a menu
    /// to put away.
    /// </summary>
    /// <remarks>
    /// A question rather than a route of its own, and that is the whole of why the
    /// key used to do nothing here: the shell answers Escape in <c>_Input</c>,
    /// ahead of GUI routing, and marks it handled — so an override on the
    /// unhandled route was dead code the day that landed. The same lesson the
    /// scene's own gestures learned, one surface over.
    /// </remarks>
    public bool TryDismiss()
    {
        if (!Visible)
            return false;

        Close();

        return true;
    }

    /// <summary>
    /// The room the widest chord in this menu needs, or none where no row has
    /// one. Asked of the items rather than reserved always: the hierarchy's menu
    /// carries no accelerators and would otherwise be a third wider for nothing.
    /// </summary>
    private static float ChordRoom(IReadOnlyList<ContextMenuItem> items)
    {
        foreach (ContextMenuItem item in items)
        {
            if (item.Shortcut.Length > 0)
                return ShortcutRoom;
        }

        return 0f;
    }

    private void PlaceCard(Vector2 at)
    {
        Vector2 room = Size - _card.Size - new Vector2(WindowMargin, WindowMargin);

        _card.Position = new Vector2(
            Mathf.Clamp(at.X, WindowMargin, Mathf.Max(WindowMargin, room.X)),
            Mathf.Clamp(at.Y, WindowMargin, Mathf.Max(WindowMargin, room.Y)));
    }

    private Button CreateRow(ContextMenuItem item)
    {
        int id = item.Id;

        var row = new Button
        {
            Text = item.Label,
            Disabled = !item.Enabled,
            Alignment = HorizontalAlignment.Left,
            ThemeTypeVariation = "ContextMenuRow"
        }.MouseOnly().Named(item.Name);

        if (item.Icon.Length > 0)
            row.Icon = GetThemeIcon(item.Icon, EditorThemeFactory.SemanticType);

        if (item.Shortcut.Length > 0)
            row.AddChild(CreateShortcut(item.Shortcut));

        // Deferred like every row event: what answers this may rebuild the very
        // panels the menu is drawn over. See DeferredReport.
        row.Pressed += () =>
        {
            Close();
            DeferredReport.Raise(() => ItemChosen?.Invoke(id));
        };

        return row;
    }

    /// <summary>
    /// The chord, at the far end of the row it belongs to. A child of the button
    /// rather than a second column of a grid: the accelerator is part of the
    /// thing you press, so it must light up with it, and a grid would also make
    /// every row as wide as the widest chord whether it had one or not.
    /// </summary>
    private static Label CreateShortcut(string chord)
    {
        var label = new Label
        {
            Text = chord,
            Name = "Shortcut",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,

            // Quieter than the words it stands beside: a chord is a hint about
            // the row, not a second thing to read.
            ThemeTypeVariation = "ContextMenuShortcut"
        };

        label.SetAnchorsPreset(LayoutPreset.FullRect);
        label.SetOffsetsPreset(LayoutPreset.FullRect);
        label.OffsetRight = -ShortcutInset;

        return label;
    }
}
