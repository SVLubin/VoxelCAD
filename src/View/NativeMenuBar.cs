using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The platform's own menu bar, where the platform has one — on macOS the strip
/// at the top of the screen, which is where a Mac user looks for File and Edit
/// and finds nothing if an application draws its own.
/// </summary>
/// <remarks>
/// It owns the accelerators it shows. AppKit answers a menu's key equivalent
/// before the event reaches the window, so a chord the shell also read would
/// either fire twice or read as dead; the shell asks
/// <see cref="OwnsShellShortcuts"/> and stands down. Where there is no global
/// menu the answer is false and nothing changes.
/// <para>
/// Consequence worth knowing, and the same one the native file dialog has: this
/// is <b>not a <see cref="Control"/></b>. It is outside the window, invisible to
/// <c>ui</c> and unreachable by injected input, so no menu item can be exercised
/// by driving. Everything behind it is reachable semantically (<c>new</c>,
/// <c>open</c>, <c>save</c>, <c>export</c>, <c>undo</c>), which is what keeps
/// that acceptable rather than a hole.
/// </para>
/// </remarks>
internal sealed class NativeMenuBar : IDisposable
{
    private readonly List<Rid> _menusList = new();
    private readonly List<Callable> _callablesList = new();

    /// <summary>
    /// The File menu and the index of its recovery row, so the row can be greyed
    /// while nothing is waiting — the one item on the bar whose state changes.
    /// </summary>
    private Rid _fileMenu;
    private int _recoverIndex = -1;

    private NativeMenuBar()
    {
    }

    /// <summary>
    /// Whether the shell should stop reading its own chords, because this bar is
    /// showing them and the platform is answering them.
    /// </summary>
    public bool OwnsShellShortcuts { get; private init; }

    /// <summary>
    /// Builds the bar, or answers null where the platform has no global menu —
    /// Windows and Linux, and macOS is the only reason this exists.
    /// </summary>
    public static NativeMenuBar? TryBuild(ShellMenuActions actions)
    {
        if (!NativeMenu.HasFeature(NativeMenu.Feature.GlobalMenu)
            || !NativeMenu.HasSystemMenu(NativeMenu.SystemMenus.MainMenuId))
        {
            return null;
        }

        var bar = new NativeMenuBar { OwnsShellShortcuts = true };

        Rid main = NativeMenu.GetSystemMenu(NativeMenu.SystemMenus.MainMenuId);

        // Settings belongs in the application menu on macOS, above Quit, and
        // nowhere else — a File > Preferences item is what a Windows port does.
        if (NativeMenu.HasSystemMenu(NativeMenu.SystemMenus.ApplicationMenuId))
        {
            Rid application = NativeMenu.GetSystemMenu(NativeMenu.SystemMenus.ApplicationMenuId);

            bar.AddItem(application, "Settings…", actions.Preferences, Key.Comma);
            NativeMenu.AddSeparator(application);
        }

        Rid file = bar.CreateMenu();
        bar.AddItem(file, "New", actions.New, Key.N);
        bar.AddItem(file, "Open…", actions.Open, Key.O);
        bar._fileMenu = file;
        bar._recoverIndex = bar.AddItem(file, "Recover unsaved work…", actions.Recover);
        NativeMenu.SetItemDisabled(file, bar._recoverIndex, true);
        NativeMenu.AddSeparator(file);
        bar.AddItem(file, "Save", actions.Save, Key.S);
        bar.AddItem(file, "Save As…", actions.SaveAs, Key.S, shift: true);
        NativeMenu.AddSeparator(file);
        bar.AddItem(file, "Import reference…", actions.ImportReference);
        bar.AddItem(file, "Import…", actions.Import);
        bar.AddItem(file, "Export…", actions.Export);
        bar.AddItem(file, "Render…", actions.Render, Key.R);
        NativeMenu.AddSubmenuItem(main, "File", file);

        Rid edit = bar.CreateMenu();
        bar.AddItem(edit, "Undo", actions.Undo, Key.Z);
        bar.AddItem(edit, "Redo", actions.Redo, Key.Z, shift: true);
        NativeMenu.AddSeparator(edit);
        bar.AddItem(edit, "Cut", actions.Cut, Key.X);
        bar.AddItem(edit, "Copy", actions.Copy, Key.C);
        bar.AddItem(edit, "Paste", actions.Paste, Key.V);
        bar.AddItem(edit, "Duplicate", actions.Duplicate, Key.D);
        NativeMenu.AddSeparator(edit);
        bar.AddItem(edit, "Delete", actions.Delete, Key.Backspace);
        NativeMenu.AddSubmenuItem(main, "Edit", edit);

        return bar;
    }

    /// <summary>
    /// Takes the menus down again. The system menus are the platform's and are
    /// only emptied of what was put in them; the submenus are this object's and
    /// are freed.
    /// </summary>
    public void Dispose()
    {
        NativeMenu.SystemMenus[] systemMenus =
        {
            NativeMenu.SystemMenus.MainMenuId,
            NativeMenu.SystemMenus.ApplicationMenuId
        };

        foreach (NativeMenu.SystemMenus id in systemMenus)
        {
            if (NativeMenu.HasSystemMenu(id))
                NativeMenu.Clear(NativeMenu.GetSystemMenu(id));
        }

        foreach (Rid menu in _menusList)
        {
            if (NativeMenu.HasMenu(menu))
                NativeMenu.FreeMenu(menu);
        }

        _menusList.Clear();
        _callablesList.Clear();
    }

    /// <summary>Greys or lights the recovery row; a bar with no File menu has nothing to do.</summary>
    public void SetRecoverEnabled(bool enabled)
    {
        if (_recoverIndex >= 0 && NativeMenu.HasMenu(_fileMenu))
            NativeMenu.SetItemDisabled(_fileMenu, _recoverIndex, !enabled);
    }

    private Rid CreateMenu()
    {
        Rid menu = NativeMenu.CreateMenu();
        _menusList.Add(menu);

        return menu;
    }

    /// <summary>
    /// One item, with the accelerator it will answer. The callable is kept alive
    /// here: the menu holds it across the whole session, and a delegate nobody
    /// references is a delegate the collector is free to take.
    /// </summary>
    private int AddItem(
        Rid menu,
        string label,
        Action action,
        Key accelerator = Key.None,
        bool shift = false)
    {
        var callable = Callable.From(action);
        _callablesList.Add(callable);

        Key chord = accelerator == Key.None
            ? Key.None
            : accelerator | (Key)KeyModifierMask.MaskMeta
                | (shift ? (Key)KeyModifierMask.MaskShift : Key.None);

        return NativeMenu.AddItem(menu, label, callable, accelerator: chord);
    }
}
