using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The mark that says an event was injected rather than typed or clicked, and the
/// device a synthesized key has to wear to be recognised at all.
/// </summary>
/// <remarks>
/// The mark is metadata, and that is a measured choice rather than a stylistic
/// one. <c>InputEvent.Device</c> would be the obvious carrier — it survives the
/// viewport re-making the event on the way in, which is more than can be said for
/// most of the object — but Godot 4 matches an InputMap action only when the
/// event's device equals the one the binding was recorded with. This project's
/// keymap is on device 16, and sweeping -8 to 32 found nothing else that matches:
/// tagging the device would silently stop <c>W</c> switching tools and the arrows
/// turning the camera, which are exactly the keys worth injecting. Metadata was
/// measured to survive the same trip, for keys and for mouse events alike, and it
/// costs the InputMap nothing.
/// <para>
/// The mark's failure mode is one-sided by construction: nothing physical ever
/// carries it, so a lost mark means an agent's gesture is ignored, never that a
/// user's click slips through a dimmed interface.
/// </para>
/// </remarks>
internal static class SyntheticInput
{
    /// <summary>
    /// Named for the project rather than something generic, because metadata is a
    /// shared namespace on an object anything may touch.
    /// </summary>
    private const string MetaKey = "voxelcad_synthetic";

    /// <summary>The device to fall back on when the keymap has nothing to say.</summary>
    public const int FallbackKeyboardDevice = 0;

    /// <summary>Marks an event as injected, and hands it back to be parsed.</summary>
    public static T Tag<T>(T @event) where T : InputEvent
    {
        @event.SetMeta(MetaKey, true);

        return @event;
    }

    public static bool IsSynthetic(InputEvent @event) => @event.HasMeta(MetaKey);

    /// <summary>
    /// The device the project's own key bindings were recorded with, so a
    /// synthesized key matches the actions a real one would.
    /// </summary>
    /// <remarks>
    /// Read off the keymap rather than written down, because the number is the
    /// machine's, not the project's — it came from whichever keyboard was plugged
    /// in when the bindings were captured, and hard-coding it here would be a
    /// magic number that works on one desk.
    /// </remarks>
    public static int ResolveKeyboardDevice()
    {
        var devicesList = new List<int>();

        foreach (StringName action in InputMap.Singleton.GetActions())
        {
            // The engine's own ui_* bindings come from elsewhere and would only
            // muddy the vote; what matters is what this project recorded.
            if (action.ToString().StartsWith("ui_", System.StringComparison.Ordinal))
                continue;

            foreach (InputEvent bound in InputMap.Singleton.ActionGetEvents(action))
            {
                if (bound is InputEventKey key)
                    devicesList.Add(key.Device);
            }
        }

        return ChooseKeyboardDevice(devicesList);
    }

    /// <summary>
    /// The one device the bindings agree on. Anything else — none at all, or
    /// several — has no single answer that would work, so it falls back rather
    /// than picking a winner nobody chose.
    /// </summary>
    public static int ChooseKeyboardDevice(IReadOnlyList<int> devicesList)
    {
        if (devicesList.Count == 0)
            return FallbackKeyboardDevice;

        int first = devicesList[0];

        foreach (int device in devicesList)
        {
            if (device != first)
                return FallbackKeyboardDevice;
        }

        return first;
    }
}
