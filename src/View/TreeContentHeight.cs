using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What a <see cref="Tree"/>'s rows actually stand at — the "natural" height a
/// tree panel reports to the column solver, so a five-row project asks for five
/// rows rather than half the window.
/// </summary>
/// <remarks>
/// Walked over the visible items per frame. Linear in visible rows, which is
/// fine at panel sizes; the day a ten-thousand-row tree turns up, this is the
/// walk to cache behind the tree's own change signals.
/// </remarks>
internal static class TreeContentHeight
{
    /// <summary>Panel stylebox insets plus a row of headroom, so the last row breathes.</summary>
    private const float Chrome = 12f;

    public static float Measure(Tree tree)
    {
        TreeItem? root = tree.GetRoot();

        if (root == null)
            return Chrome;

        int separation = tree.GetThemeConstant("v_separation", "Tree");
        var height = 0f;

        TreeItem? item = tree.HideRoot ? root.GetNextVisible() : root;

        while (item != null)
        {
            height += tree.GetItemAreaRect(item).Size.Y + separation;
            item = item.GetNextVisible();
        }

        return height + Chrome;
    }
}
