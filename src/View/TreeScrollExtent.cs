using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Where a <see cref="Tree"/>'s own scrolling stands, read off the scrollbar it
/// cranks rather than worked out from its rows.
/// </summary>
/// <remarks>
/// A <see cref="Tree"/> scrolls itself and publishes only half the answer:
/// <see cref="Tree.GetScroll"/> says where it is and nothing says how far it may
/// go. The bar is there — as an internal child, which is why this reaches for it
/// through <c>GetChildren(true)</c> — and it is the authority, where
/// <see cref="TreeContentHeight"/> is deliberately not: that measure adds a row
/// of headroom so the last row breathes, and a maximum overstated by that
/// headroom is a body still dissolving at a bottom edge it has already reached.
/// <para>
/// A tree with nothing to scroll hides its bar, and a hidden bar is read as
/// nothing to scroll rather than as whatever values it was left holding.
/// </para>
/// </remarks>
internal static class TreeScrollExtent
{
    public static ScrollExtent Measure(Tree tree)
    {
        foreach (Node child in tree.GetChildren(true))
        {
            if (child is not VScrollBar bar || !bar.Visible)
                continue;

            return new ScrollExtent(
                (float)bar.Value, (float)Mathf.Max(0d, bar.MaxValue - bar.Page));
        }

        return ScrollExtent.None;
    }
}
