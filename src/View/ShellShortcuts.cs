using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The shell's chords, as a function rather than as a switch buried in an input
/// handler: which key with the command modifier means what.
/// </summary>
/// <remarks>
/// It is a function because the modifier itself turned out to be a decision. The
/// handler used to test <c>CtrlPressed</c> literally, which on macOS is not the
/// key anybody presses — the platform spells a command with Cmd, so Cmd+Z did
/// nothing while Ctrl+Z worked, and that reads as "undo is broken, except
/// sometimes". <see cref="IsCommand"/> is the answer to "does this chord carry
/// the command modifier", and it deliberately accepts both: Godot's own
/// <c>IsCommandOrControlPressed</c> for the platform's spelling, plus plain Ctrl
/// everywhere, because a desktop CAD user arriving from Windows presses Ctrl on a
/// Mac and nothing else in this editor binds Ctrl to a letter.
/// </remarks>
internal static class ShellShortcuts
{
    /// <summary>
    /// Whether a chord carries the command modifier. <paramref name="commandOrControl"/>
    /// is the engine's platform answer — Cmd on macOS, Ctrl elsewhere — and
    /// <paramref name="control"/> is the physical Ctrl key, accepted on top of it.
    /// </summary>
    public static bool IsCommand(bool commandOrControl, bool control) =>
        commandOrControl || control;

    /// <summary>
    /// What the chord asks for, or <see cref="ShellShortcut.None"/> when it asks
    /// for nothing the shell answers. Nothing without the command modifier is a
    /// shell chord: a bare letter belongs to whichever panel holds the keyboard.
    /// </summary>
    public static ShellShortcut Resolve(Key keycode, bool command, bool shift)
    {
        if (!command)
            return ShellShortcut.None;

        return keycode switch
        {
            // Shift+Z is the redo half of the same chord, so it has to be tested
            // before the plain one or it would be swallowed as an undo.
            Key.Z when shift => ShellShortcut.Redo,
            Key.Z => ShellShortcut.Undo,
            Key.Y => ShellShortcut.Redo,
            Key.N => ShellShortcut.New,
            Key.O => ShellShortcut.Open,
            Key.S when shift => ShellShortcut.SaveAs,
            Key.S => ShellShortcut.Save,

            // The clipboard chords are the shell's only while nothing else wanted
            // them: a focused text field consumes its own copy and paste before an
            // unhandled key ever reaches here, which is why typing in a spin box
            // still copies text rather than the selected element.
            Key.C => ShellShortcut.Copy,
            Key.X => ShellShortcut.Cut,
            Key.V => ShellShortcut.Paste,
            Key.D => ShellShortcut.Duplicate,

            // Cmd+Backspace is what a macOS user presses to throw something away,
            // and the forward-delete key is the same chord on a full keyboard. A
            // bare Backspace is deliberately not bound: it belongs to whichever
            // panel holds the keyboard, and the project tree's type-to-search and
            // every text field would lose it.
            Key.Backspace or Key.Delete => ShellShortcut.Delete,
            Key.Comma => ShellShortcut.Preferences,
            Key.R => ShellShortcut.Render,
            _ => ShellShortcut.None
        };
    }

    /// <summary>
    /// The chord a shortcut is spelled with, or false where it has none. The
    /// inverse of <see cref="Resolve"/>, and it lives here for that reason: a
    /// menu that printed its accelerators from a second table would be one
    /// edit away from promising a chord nothing answers.
    /// </summary>
    /// <remarks>
    /// Where <see cref="Resolve"/> accepts more than one spelling — redo is both
    /// Shift+Z and Y, delete is both Backspace and the forward key — this gives
    /// the one to print. Held by a test that walks every member and asks
    /// <see cref="Resolve"/> to agree.
    /// </remarks>
    public static bool TryGetChord(ShellShortcut shortcut, out Key key, out bool shift)
    {
        (key, shift) = shortcut switch
        {
            ShellShortcut.Undo => (Key.Z, false),
            ShellShortcut.Redo => (Key.Z, true),
            ShellShortcut.New => (Key.N, false),
            ShellShortcut.Open => (Key.O, false),
            ShellShortcut.Save => (Key.S, false),
            ShellShortcut.SaveAs => (Key.S, true),
            ShellShortcut.Copy => (Key.C, false),
            ShellShortcut.Cut => (Key.X, false),
            ShellShortcut.Paste => (Key.V, false),
            ShellShortcut.Duplicate => (Key.D, false),
            ShellShortcut.Delete => (Key.Backspace, false),
            ShellShortcut.Preferences => (Key.Comma, false),
            ShellShortcut.Render => (Key.R, false),
            _ => (Key.None, false)
        };

        return key != Key.None;
    }

    /// <summary>
    /// How a chord is written where a menu shows it. Two spellings, because the
    /// platforms genuinely differ: macOS writes the modifiers as symbols with no
    /// separator, everything else names them and joins them with a plus.
    /// </summary>
    /// <param name="commandIsMeta">
    /// Whether the command modifier is the Mac's own — which is also what decides
    /// the spelling, the two being the same question about the platform.
    /// </param>
    public static string Describe(ShellShortcut shortcut, bool commandIsMeta)
    {
        if (!TryGetChord(shortcut, out Key key, out bool shift))
            return string.Empty;

        string name = KeyName(key, commandIsMeta);

        if (commandIsMeta)
            return shift ? $"\u21e7\u2318{name}" : $"\u2318{name}";

        return shift ? $"Ctrl+Shift+{name}" : $"Ctrl+{name}";
    }

    /// <summary>
    /// The key's own name. Spelled from the enum rather than through
    /// <c>OS.GetKeycodeString</c>, which is an engine singleton: this whole
    /// table is a decision about words, and one that needs a running engine to
    /// be asked cannot be held by a test. It crashed the test host outright,
    /// which is the loudest available version of that lesson.
    /// </summary>
    private static string KeyName(Key key, bool commandIsMeta) => key switch
    {
        Key.Comma => ",",
        Key.Backspace => commandIsMeta ? "\u232b" : "Backspace",
        _ => key.ToString()
    };
}
