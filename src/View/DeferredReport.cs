using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Re-raises a view event on the next frame, outside the engine dispatch that
/// delivered the signal it came from.
/// </summary>
/// <remarks>
/// For a row event this is load-bearing, not politeness. A presenter may answer
/// a click by rebuilding the very panel the click landed in — undo from the
/// history list, a scene activation from the project tree — and Godot's
/// <see cref="Tree"/> refuses <c>Clear</c> and <c>CreateItem</c> while it is
/// delivering a mouse event. Inline, the rebuild silently fails, the panel is
/// left lying about the document, and its half-cleared lookups swallow every
/// later click; caught live exactly that way. Same bargain the dialogs make:
/// an answer is delivered one frame later, never inline.
/// </remarks>
internal static class DeferredReport
{
    public static void Raise(Action report) => Callable.From(report).CallDeferred();
}
