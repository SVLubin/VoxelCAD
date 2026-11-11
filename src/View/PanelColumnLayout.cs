using System;
using System.Collections.Generic;

namespace VoxelCAD.View;

/// <summary>
/// One column slot as the solver sees it, ordered top to bottom as it stands in
/// the column: pinned panels by their order, then the unpinned tabs.
/// </summary>
/// <param name="NaturalBody">
/// What the content asks for, measured live — a tree with five rows asks for
/// five rows. Never less than <paramref name="MinBody"/> once inside the solve.
/// </param>
internal readonly record struct PanelSlotInput(
    bool Pinned,
    bool Folded,
    bool OpenTemp,
    float NaturalBody,
    float MinBody);

/// <summary>What one slot gets: its body height, or the reason it has none.</summary>
internal readonly record struct PanelSlotPlan(
    bool BodyVisible,
    float BodyHeight,
    bool AutoFolded,
    bool Scrolls);

/// <summary>Where a rail overlay stands and how tall its body is.</summary>
internal readonly record struct OverlayPlan(float Top, float BodyHeight, bool Scrolls);

/// <summary>
/// The height cascade of <c>docs/panels.md</c>, as a pure function: slots in,
/// per-slot plans out. Recomputed rather than remembered — auto-folds are
/// derived from the same inputs every time, which is what makes them transient:
/// close the temporary panel and the stack restores by mere recomputation.
/// </summary>
/// <remarks>
/// The cascade, in order: a temporary panel takes <c>clamp(available, min,
/// natural)</c>; only when its minimum does not fit do the pinned bodies give —
/// first shrinking continuously toward their minimums, then folding to their
/// headers, both bottom-up so the topmost panel surrenders last; and when
/// everything folded still leaves less than the minimum, the opening panel takes
/// what is there and scrolls inside. Opening never refuses. The same shrink and
/// fold steps serve a window too short for the pinned stack alone.
/// </remarks>
internal static class PanelColumnLayout
{
    /// <summary>
    /// The least body a scrolling panel is granted, so the last resort still
    /// shows a couple of rows rather than a sliver.
    /// </summary>
    public const float ScrollFloor = 44f;

    public static IReadOnlyList<PanelSlotPlan> Solve(
        IReadOnlyList<PanelSlotInput> slots,
        float availableForBodies)
    {
        var wanted = new float[slots.Count];
        var minimum = new float[slots.Count];
        var autoFolded = new bool[slots.Count];
        int tempIndex = -1;

        for (var index = 0; index < slots.Count; index++)
        {
            PanelSlotInput slot = slots[index];
            minimum[index] = Math.Max(0f, slot.MinBody);

            if (slot.Pinned && !slot.Folded)
                wanted[index] = Math.Max(minimum[index], slot.NaturalBody);

            if (!slot.Pinned && slot.OpenTemp && tempIndex < 0)
                tempIndex = index;
        }

        float available = Math.Max(0f, availableForBodies);
        float pinnedSum = SumPinned(slots, wanted);

        float tempMin = 0f;
        if (tempIndex >= 0)
            tempMin = Math.Max(ScrollFloor, minimum[tempIndex]);

        // Shrink, then fold, bottom-up — exactly as far as the deficit demands.
        float deficit = pinnedSum + tempMin - available;

        if (deficit > 0f)
            deficit = ShrinkBottomUp(slots, wanted, minimum, deficit);

        if (deficit > 0f)
            FoldBottomUp(slots, wanted, autoFolded, deficit);

        return Plan(slots, wanted, autoFolded, tempIndex, available);
    }

    /// <summary>
    /// The bare rail's overlay, solved on the same terms as the column: the
    /// panel takes its natural height clamped into the window, top aligned to
    /// its tab and pushed up when the bottom would not fit. Pure for the same
    /// reason the cascade is — the boundary cases (a natural below the floor, a
    /// tab at the bottom edge, a window shorter than the minimum) are exactly
    /// what a node cannot hold a test to.
    /// </summary>
    public static OverlayPlan SolveOverlay(
        float naturalBody,
        float minBody,
        float anchorY,
        float columnHeight,
        float inset,
        float headerHeight)
    {
        // The minimum never exceeds what the content actually asks for — a
        // near-empty panel needs no 140-unit floor — but always covers the
        // scroll floor, so the clamp below cannot invert.
        float effectiveMin = Math.Max(ScrollFloor, Math.Min(minBody, Math.Max(0f, naturalBody)));
        float natural = Math.Max(effectiveMin, naturalBody);

        float availableBelow = columnHeight - inset - anchorY - headerHeight;
        float body = Math.Clamp(availableBelow, effectiveMin, natural);

        // A window shorter than even the minimum: shrink to what the column
        // has, floored, rather than hanging out of the bottom.
        float windowBody = columnHeight - 2f * inset - headerHeight;

        if (windowBody < body)
            body = Math.Max(ScrollFloor, windowBody);

        float height = headerHeight + body;
        float top = Math.Max(inset, Math.Min(anchorY, columnHeight - inset - height));

        return new OverlayPlan(top, body, Scrolls: naturalBody > body);
    }

    private static float SumPinned(IReadOnlyList<PanelSlotInput> slots, float[] wanted)
    {
        var sum = 0f;

        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].Pinned)
                sum += wanted[index];
        }

        return sum;
    }

    private static float ShrinkBottomUp(
        IReadOnlyList<PanelSlotInput> slots,
        float[] wanted,
        float[] minimum,
        float deficit)
    {
        for (int index = slots.Count - 1; index >= 0 && deficit > 0f; index--)
        {
            if (!slots[index].Pinned || slots[index].Folded)
                continue;

            float give = Math.Min(deficit, wanted[index] - minimum[index]);

            if (give <= 0f)
                continue;

            wanted[index] -= give;
            deficit -= give;
        }

        return deficit;
    }

    private static void FoldBottomUp(
        IReadOnlyList<PanelSlotInput> slots,
        float[] wanted,
        bool[] autoFolded,
        float deficit)
    {
        for (int index = slots.Count - 1; index >= 0 && deficit > 0f; index--)
        {
            if (!slots[index].Pinned || slots[index].Folded || wanted[index] <= 0f)
                continue;

            deficit -= wanted[index];
            wanted[index] = 0f;
            autoFolded[index] = true;
        }
    }

    private static IReadOnlyList<PanelSlotPlan> Plan(
        IReadOnlyList<PanelSlotInput> slots,
        float[] wanted,
        bool[] autoFolded,
        int tempIndex,
        float available)
    {
        var plans = new PanelSlotPlan[slots.Count];
        float granted = SumPinned(slots, wanted);

        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].Pinned)
            {
                plans[index] = new PanelSlotPlan(
                    BodyVisible: wanted[index] > 0f,
                    BodyHeight: wanted[index],
                    AutoFolded: autoFolded[index],
                    Scrolls: false);
                continue;
            }

            if (index != tempIndex)
                continue;

            // The temporary panel takes what the cascade left. Less than its own
            // minimum means everything above already gave all it had — it opens
            // on the floor and scrolls, because opening never refuses.
            PanelSlotInput slot = slots[index];
            float remaining = available - granted;
            float effectiveMin = Math.Max(ScrollFloor, slot.MinBody);
            float natural = Math.Max(effectiveMin, slot.NaturalBody);
            bool scrolls;
            float body;

            if (remaining >= effectiveMin)
            {
                body = Math.Clamp(remaining, effectiveMin, natural);
                scrolls = body < natural;
            }
            else
            {
                // "Takes what is left and scrolls" — what is left, not the bare
                // floor: granting 44 with 80 free would waste a visible strip.
                body = Math.Max(ScrollFloor, remaining);
                scrolls = true;
            }

            plans[index] = new PanelSlotPlan(
                BodyVisible: true,
                BodyHeight: body,
                AutoFolded: false,
                Scrolls: scrolls);
        }

        return plans;
    }
}
