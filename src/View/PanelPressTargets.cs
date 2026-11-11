using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What one panel of a column shows to a press: the surface it occupies, and
/// the two things inside it a gesture can take hold of.
/// </summary>
/// <param name="Still">
/// Whether the surface has stopped moving. A panel caught mid-flight has no
/// settled place to be grabbed by — but it is drawn where it is drawn, so it
/// still stands in front of whatever is behind it.
/// </param>
/// <param name="Grip">Null where no grip is shown, never an empty rect.</param>
internal readonly record struct PanelPressInput(
    PanelKind Kind,
    bool Still,
    Rect2 Surface,
    float Notch,
    bool StubOnRight,
    Rect2 Header,
    Rect2? Grip,
    bool HeightResizable);

/// <summary>
/// Which panel a press belongs to, and what it took hold of. One answer for the
/// two polled gestures of the chrome — picking a panel up by its header, and
/// pulling its resize grip.
/// </summary>
/// <remarks>
/// <para>
/// The rule is one line: <b>a press belongs to the topmost panel covering that
/// point</b>, and a panel that covers a point hides everything behind it. Both
/// gestures used to walk the column in layout order and take the first panel
/// whose header or grip happened to contain the point, which is a hit test that
/// cannot see what is drawn over it — and panels in this chrome routinely
/// overlap: a temporary one grown out of a run, one still retracting while its
/// neighbour opens, one carried across the window.
/// </para>
/// <para>
/// What it shipped as: the Toolbar's tab is the last card of the left run, its
/// grown panel does not fit below the tab, so the column clamps it to the
/// bottom and it slides up over the pinned panel above by exactly one card —
/// putting the pinned panel's resize grip under the temporary panel's header,
/// at the end where the pin is. Sixty per cent of the pin, its centre included,
/// then answered as a resize of the panel underneath: the press was marked
/// answered and the pin's own click was refused, silently, with the grip
/// invisible under the glass. Aiming at the middle of the pin could not work.
/// </para>
/// <para>
/// The class, which the ledger already records one axis over: <b>a polled
/// gesture that hit-tests by rect containment must respect the same depth order
/// the drawing does</b>. The column solves that order for Godot already
/// (<see cref="PanelDepthOrder"/>); this asks it the same question rather than
/// keeping a second opinion.
/// </para>
/// </remarks>
internal static class PanelPressTargets
{
    /// <summary>
    /// Whether a panel's surface covers a point — its rect, less the notch cut
    /// out below the stub. The cut is a hole: what shows through it is whatever
    /// the surface was cut away to reveal, so a press there is not this panel's.
    /// </summary>
    /// <remarks>
    /// Asked by <see cref="ChromePanel._HasPoint"/> too, so what the engine
    /// routes an event to and what this layer calls the owner of a press cannot
    /// come to disagree.
    /// </remarks>
    public static bool Covers(Rect2 surface, float notch, bool stubOnRight, Vector2 point)
    {
        if (!surface.HasPoint(point))
            return false;

        if (notch <= 0f || point.Y - surface.Position.Y <= ChromePanel.CollapsedSide)
            return true;

        float local = point.X - surface.Position.X;

        return stubOnRight
            ? local < surface.Size.X - notch
            : local > notch;
    }

    /// <summary>
    /// The panel a press at this point lands on: the front-most one whose
    /// surface covers it, or none where the point is past the chrome.
    /// </summary>
    public static bool TryGetOwner(
        IReadOnlyList<PanelPressInput> panels,
        PanelKind? carried,
        Vector2 point,
        out PanelPressInput owner)
    {
        var kinds = new PanelKind[panels.Count];

        for (var index = 0; index < panels.Count; index++)
            kinds[index] = panels[index].Kind;

        IReadOnlyList<int> depths = PanelDepthOrder.Solve(kinds, carried);
        var byDepth = new int[panels.Count];

        for (var index = 0; index < panels.Count; index++)
            byDepth[depths[index]] = index;

        for (int rank = panels.Count - 1; rank >= 0; rank--)
        {
            PanelPressInput panel = panels[byDepth[rank]];

            if (!Covers(panel.Surface, panel.Notch, panel.StubOnRight, point))
                continue;

            owner = panel;

            return true;
        }

        owner = default;

        return false;
    }

    /// <summary>
    /// The panel a drag would pick up: the one a press belongs to, if the press
    /// is on that panel's own header and it has stopped moving.
    /// </summary>
    public static bool TryGetDraggable(
        IReadOnlyList<PanelPressInput> panels,
        PanelKind? carried,
        Vector2 point,
        out PanelKind kind)
    {
        kind = default;

        if (!TryGetOwner(panels, carried, point, out PanelPressInput owner))
            return false;

        if (!owner.Still || !owner.Header.HasPoint(point))
            return false;

        kind = owner.Kind;

        return true;
    }

    /// <summary>
    /// The panel a resize would take hold of, and whether its grip moves the
    /// height as well as the side's width.
    /// </summary>
    public static bool TryGetGrip(
        IReadOnlyList<PanelPressInput> panels,
        PanelKind? carried,
        Vector2 point,
        out PanelKind kind,
        out bool heightToo)
    {
        kind = default;
        heightToo = false;

        if (!TryGetOwner(panels, carried, point, out PanelPressInput owner))
            return false;

        if (!owner.Still || owner.Grip is not { } grip || !grip.HasPoint(point))
            return false;

        kind = owner.Kind;
        heightToo = owner.HeightResizable;

        return true;
    }
}
