using System;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The shell's chords, and the modifier they are spelled with. The second half is
/// what this exists for: the handler used to name Ctrl literally, so on macOS —
/// where a command is Cmd — undo answered only to a chord nobody presses.
/// </summary>
public sealed class ShellShortcutsTests
{
    [Fact]
    public void ThePlatformsCommandModifierCounts()
    {
        // What Godot answers on macOS for Cmd, and everywhere else for Ctrl.
        Assert.True(ShellShortcuts.IsCommand(commandOrControl: true, control: false));
    }

    /// <summary>
    /// Ctrl is accepted on top of the platform's own spelling, so the chord a user
    /// arriving from Windows presses on a Mac still works.
    /// </summary>
    [Fact]
    public void PlainControlCountsToo()
    {
        Assert.True(ShellShortcuts.IsCommand(commandOrControl: false, control: true));
        Assert.False(ShellShortcuts.IsCommand(commandOrControl: false, control: false));
    }

    [Fact]
    public void CommandZUndoes()
    {
        Assert.Equal(
            ShellShortcut.Undo,
            ShellShortcuts.Resolve(Key.Z, command: true, shift: false));
    }

    [Fact]
    public void ShiftMakesTheSameChordRedo()
    {
        Assert.Equal(
            ShellShortcut.Redo,
            ShellShortcuts.Resolve(Key.Z, command: true, shift: true));

        Assert.Equal(
            ShellShortcut.Redo,
            ShellShortcuts.Resolve(Key.Y, command: true, shift: false));
    }

    [Fact]
    public void ShiftMakesSaveIntoSaveAs()
    {
        Assert.Equal(
            ShellShortcut.Save,
            ShellShortcuts.Resolve(Key.S, command: true, shift: false));

        Assert.Equal(
            ShellShortcut.SaveAs,
            ShellShortcuts.Resolve(Key.S, command: true, shift: true));
    }

    [Fact]
    public void NewAndOpenAreThere()
    {
        Assert.Equal(
            ShellShortcut.New,
            ShellShortcuts.Resolve(Key.N, command: true, shift: false));

        Assert.Equal(
            ShellShortcut.Open,
            ShellShortcuts.Resolve(Key.O, command: true, shift: false));
    }

    /// <summary>
    /// A bare letter belongs to whichever panel holds the keyboard — the tool keys
    /// above all, which are single letters with no modifier.
    /// </summary>
    [Fact]
    public void WithoutTheCommandModifierNothingIsAShellChord()
    {
        Assert.Equal(
            ShellShortcut.None,
            ShellShortcuts.Resolve(Key.Z, command: false, shift: false));

        Assert.Equal(
            ShellShortcut.None,
            ShellShortcuts.Resolve(Key.S, command: false, shift: false));
    }

    [Fact]
    public void AnUnboundKeyAsksForNothing()
    {
        Assert.Equal(
            ShellShortcut.None,
            ShellShortcuts.Resolve(Key.Q, command: true, shift: false));

        Assert.Equal(
            ShellShortcut.None,
            ShellShortcuts.Resolve(Key.B, command: true, shift: true));
    }

    /// <summary>
    /// Cmd+Backspace is what a macOS user presses to throw something away, and the
    /// forward-delete key is the same chord on a full keyboard.
    /// </summary>
    [Theory]
    [InlineData(Key.Backspace)]
    [InlineData(Key.Delete)]
    public void CommandBackspaceDeletes(Key keycode)
    {
        Assert.Equal(
            ShellShortcut.Delete,
            ShellShortcuts.Resolve(keycode, command: true, shift: false));
    }

    /// <summary>
    /// And a bare one is nobody's chord: it belongs to whichever panel holds the
    /// keyboard, where it is a character being erased or a row being searched for.
    /// </summary>
    [Theory]
    [InlineData(Key.Backspace)]
    [InlineData(Key.Delete)]
    public void ABareBackspaceIsNotAShellChord(Key keycode)
    {
        Assert.Equal(
            ShellShortcut.None,
            ShellShortcuts.Resolve(keycode, command: false, shift: false));
    }

    /// <summary>
    /// The settings form, bound here as well as on the platform's own menu bar,
    /// because Windows and Linux have no platform menu to bind it on.
    /// </summary>
    [Fact]
    public void CommandCommaOpensThePreferences()
    {
        Assert.Equal(
            ShellShortcut.Preferences,
            ShellShortcuts.Resolve(Key.Comma, command: true, shift: false));
    }

    /// <summary>
    /// The invariant the printed accelerators rest on: every chord a menu can
    /// write beside a row resolves back to the shortcut it was written for. The
    /// two directions are one table for exactly this reason — a second one would
    /// be an edit away from promising a chord nothing answers.
    /// </summary>
    [Fact]
    public void EveryPrintedChordResolvesBackToItsOwnShortcut()
    {
        foreach (ShellShortcut shortcut in Enum.GetValues<ShellShortcut>())
        {
            if (!ShellShortcuts.TryGetChord(shortcut, out Key key, out bool shift))
                continue;

            Assert.Equal(shortcut, ShellShortcuts.Resolve(key, command: true, shift));
        }
    }

    /// <summary>
    /// And the other way, so a shortcut added without a chord is noticed rather
    /// than quietly printing nothing beside its row.
    /// </summary>
    [Fact]
    public void EveryShortcutButNoneHasAChord()
    {
        foreach (ShellShortcut shortcut in Enum.GetValues<ShellShortcut>())
        {
            Assert.Equal(
                shortcut != ShellShortcut.None,
                ShellShortcuts.TryGetChord(shortcut, out _, out _));
        }
    }

    [Fact]
    public void NothingIsPrintedForNoShortcut()
    {
        Assert.Equal(
            string.Empty,
            ShellShortcuts.Describe(ShellShortcut.None, commandIsMeta: true));
    }

    /// <summary>The platform's own spelling: symbols, with no separator.</summary>
    [Fact]
    public void OnAMacAChordIsWrittenInSymbols()
    {
        Assert.Equal("\u2318N", ShellShortcuts.Describe(ShellShortcut.New, commandIsMeta: true));
        Assert.Equal(
            "\u21e7\u2318S", ShellShortcuts.Describe(ShellShortcut.SaveAs, commandIsMeta: true));
        Assert.Equal(
            "\u2318,", ShellShortcuts.Describe(ShellShortcut.Preferences, commandIsMeta: true));
        Assert.Equal(
            "\u2318\u232b", ShellShortcuts.Describe(ShellShortcut.Delete, commandIsMeta: true));
    }

    /// <summary>And everywhere else the modifiers are named and joined.</summary>
    [Fact]
    public void ElsewhereAChordIsWrittenInWords()
    {
        Assert.Equal("Ctrl+N", ShellShortcuts.Describe(ShellShortcut.New, commandIsMeta: false));
        Assert.Equal(
            "Ctrl+Shift+S", ShellShortcuts.Describe(ShellShortcut.SaveAs, commandIsMeta: false));
        Assert.Equal(
            "Ctrl+,", ShellShortcuts.Describe(ShellShortcut.Preferences, commandIsMeta: false));
        Assert.Equal(
            "Ctrl+Backspace",
            ShellShortcuts.Describe(ShellShortcut.Delete, commandIsMeta: false));
    }
}
