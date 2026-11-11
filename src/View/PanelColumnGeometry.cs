using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>Where one slot of a column belongs, and what the cascade made of it.</summary>
internal readonly record struct PanelColumnPlace(
    bool Pinned,
    Rect2 Rect,
    bool BodyVisible,
    float BodyHeight,
    bool AutoFolded,
    bool RevealsTabs);

/// <summary>
/// Where everything in one column stands, as a pure function: the pinned stack
/// down from the top, then the run of tabs below it.
/// </summary>
/// <remarks>
/// Lifted out of <see cref="EdgeColumn"/> unchanged, for the reason every other
/// piece of this subsystem was: a decision inside a Godot node is a decision no
/// test can reach. This one had a second reason. The geometry is an input to the
/// drop target (<see cref="PanelDropTargets"/>), and the drop target is an input
/// to the placements the geometry is solved from — so the two close a loop that
/// runs once a frame, and whether that loop settles is a question about a pure
/// function or it is a question nobody can ask.
/// <para>
/// The cascade it calls is order-dependent by design — it shrinks and folds
/// bottom-up, so the topmost panel surrenders last — which is exactly what makes
/// the loop worth checking: moving a panel one place up the stack can change
/// every height below it.
/// </para>
/// </remarks>
internal static class PanelColumnGeometry
{
    /// <param name="surfaceWidth">
    /// The surface every pinned panel of this column wears — the side's body
    /// width plus the frame. A parameter rather than the constant it used to
    /// read, because the width is the user's now; the default is the shipped
    /// chrome.
    /// </param>
    public static IReadOnlyList<PanelColumnPlace> Solve(
        IReadOnlyList<PanelSlotInput> slots,
        float columnHeight,
        PanelSide side,
        float surfaceWidth = ChromePanel.SurfaceWidth)
    {
        var pinnedCount = 0;
        var tabCount = 0;

        foreach (PanelSlotInput slot in slots)
        {
            if (slot.Pinned)
                pinnedCount++;
            else
                tabCount++;
        }

        var described = new PanelSlotInput[pinnedCount];
        var index = 0;

        foreach (PanelSlotInput slot in slots)
        {
            if (slot.Pinned)
                described[index++] = slot;
        }

        float available =
            columnHeight - 2f * EdgeColumn.Inset
            - pinnedCount * EdgeColumn.Chrome
            - Math.Max(0, pinnedCount - 1) * EdgeColumn.Gap
            - tabCount * (EdgeColumn.TabCard + EdgeColumn.Gap);

        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(described, available);

        var places = new PanelColumnPlace[slots.Count];
        float y = EdgeColumn.Inset;
        var planIndex = 0;

        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (!slots[slotIndex].Pinned)
                continue;

            PanelSlotPlan plan = plans[planIndex++];

            // A panel showing only its header is exactly that header's square:
            // the separation below it belongs to a body, and a folded one has
            // none, so reserving it left a strip of empty glass under the band.
            float height = plan.BodyVisible
                ? EdgeColumn.Chrome + plan.BodyHeight
                : EdgeColumn.TabCard;

            places[slotIndex] = new PanelColumnPlace(
                Pinned: true,
                Rect: new Rect2(
                    EdgeColumn.Inset, y, surfaceWidth, height),
                BodyVisible: plan.BodyVisible,
                BodyHeight: plan.BodyHeight,
                AutoFolded: plan.AutoFolded,
                RevealsTabs: false);

            y += height + EdgeColumn.Gap;
        }

        // Against the window's edge, which on a wide right column is the far
        // side of it — a run inset from the column's own left sits at the edge
        // on the left and floats a column's width inside the window on the
        // right. The rail case is centred and reads the same either way.
        float tabX = pinnedCount > 0
            ? (side == PanelSide.Left
                ? EdgeColumn.Inset
                : surfaceWidth + EdgeColumn.Inset - EdgeColumn.TabCard)
            : (EdgeColumn.RailWidth - EdgeColumn.TabCard) / 2f;

        // The last card in the run has nothing below it to reveal, so it cuts
        // no notch — and that is a fact about where it stands, which is what
        // this function knows and nobody else should have to be told.
        var lastTabIndex = -1;

        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (!slots[slotIndex].Pinned)
                lastTabIndex = slotIndex;
        }

        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (slots[slotIndex].Pinned)
                continue;

            places[slotIndex] = new PanelColumnPlace(
                Pinned: false,
                Rect: new Rect2(
                    tabX, y, EdgeColumn.TabCard, EdgeColumn.TabCard),
                BodyVisible: false,
                BodyHeight: 0f,
                AutoFolded: false,
                RevealsTabs: slotIndex != lastTabIndex);

            y += EdgeColumn.TabCard + EdgeColumn.Gap;
        }

        return places;
    }

    /// <summary>
    /// Where a shown panel stands: out of its tab's own corner, keeping the
    /// stub the tab occupied, and lifted when it would not fit below.
    /// </summary>
    /// <remarks>
    /// Lifted out of the node for the reason the rest of this file was — the
    /// surface's width is the number a body's width is solved backwards from,
    /// and it lived where no test could read it.
    /// </remarks>
    public static PanelShape SolveGrown(
        Vector2 tabCorner,
        bool revealsTabs,
        float naturalBody,
        float minBody,
        float columnHeight,
        PanelSide side,
        float surfaceWidth = ChromePanel.SurfaceWidth)
    {
        OverlayPlan plan = PanelColumnLayout.SolveOverlay(
            naturalBody, minBody, tabCorner.Y, columnHeight,
            EdgeColumn.Inset, EdgeColumn.Chrome);

        // The surface a body needs, plus the notch it gives away — so what
        // stands past the cut is the same surface a pinned panel gets. On the
        // right it is hung so the surface's outer edge is the card's, because
        // the stub must land on the very tab it grew from.
        // One call, so the width and the cut cannot be answered separately.
        float cut = PanelNotch.Reach(pinned: false, revealsTabs);
        float width = surfaceWidth + cut;

        float x = side == PanelSide.Left
            ? tabCorner.X
            : tabCorner.X + EdgeColumn.TabCard - width;

        // The solver's own top, not the tab's corner: lifting a panel whose tab
        // sits too low is half of what SolveOverlay is for.
        return PanelShape.For(
            new Rect2(x, plan.Top, width, EdgeColumn.Chrome + plan.BodyHeight), cut, side);
    }

    /// <summary>
    /// What the panel in hand will occupy once it is put down, in the column's
    /// own coordinates — the very hole the solve is already holding open. Null
    /// when this column is carrying nothing.
    /// </summary>
    /// <remarks>
    /// Three answers, in the order the column already branches in. Pinned, and
    /// it is the cascade's plan for it, a folded header's square included. A
    /// tab, and it is the card in the run. Open and dropped in the run, and it
    /// is the surface it will grow back into — a panel travels as the thing it
    /// is and lands as the thing it was, so a tab card there would be a lie.
    /// </remarks>
    public static PanelShape? SolveGhost(
        IReadOnlyList<PanelColumnPlace> places,
        int carried,
        bool grown,
        float naturalBody,
        float minBody,
        float columnHeight,
        PanelSide side,
        float surfaceWidth = ChromePanel.SurfaceWidth)
    {
        if (carried < 0 || carried >= places.Count)
            return null;

        PanelColumnPlace place = places[carried];

        // Pinned it is the cascade's plan, a folded header's square included,
        // and a pinned panel cuts nothing. A tab is the card in the run, and a
        // card with no body has nothing to cut a notch past — cutting one would
        // take a bite out of the tab's own underside. Neither is a Γ.
        if (place.Pinned || !grown)
            return PanelShape.For(place.Rect, cut: 0f, side);

        // Open and dropped in the run: the surface it grows back into, wearing
        // the very notch it will wear.
        return SolveGrown(
            place.Rect.Position,
            place.RevealsTabs,
            naturalBody,
            minBody,
            columnHeight,
            side,
            surfaceWidth);
    }

    /// <summary>
    /// What a column offers a drop, derived from where everything stands —
    /// optionally without one panel, which is the one being carried and has no
    /// business being measured against itself.
    /// </summary>
    public static PanelColumnDrop Describe(
        IReadOnlyList<PanelColumnPlace> places, float columnTop, int exclude = -1)
    {
        var pinnedCentres = new List<float>();
        var tabCentres = new List<float>();
        float top = float.MaxValue;
        float bottom = float.MinValue;

        for (var index = 0; index < places.Count; index++)
        {
            PanelColumnPlace place = places[index];
            float y = columnTop + place.Rect.Position.Y;

            // The span is the stack as it stands on screen, and the carried
            // panel's own place is part of it: the column goes on reserving it
            // and the ghost is drawn in it, so leaving it out measures the
            // pointer against a stack a panel shorter than the one it is over.
            if (place.Pinned)
            {
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + place.Rect.Size.Y);
            }

            // The centres are a different question — how many panels the
            // pointer has passed — and there the carried one must not count
            // itself, or the index it lands on depends on where it was just
            // put.
            if (index == exclude)
                continue;

            if (place.Pinned)
                pinnedCentres.Add(y + (place.Rect.Size.Y * 0.5f));
            else
                tabCentres.Add(y + (place.Rect.Size.Y * 0.5f));
        }

        return new PanelColumnDrop(top, bottom, pinnedCentres, tabCentres);
    }
}
