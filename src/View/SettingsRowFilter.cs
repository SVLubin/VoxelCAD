using System;

namespace VoxelCAD.View;

/// <summary>
/// Which rows a settings form shows: the chosen section while nothing is being
/// searched for, and every match across every section once something is.
/// </summary>
/// <remarks>
/// Pure, and separate from the form, because it is the one decision in there
/// worth holding to a test — a search that quietly stayed inside the open
/// section would look like it worked and find nothing.
/// </remarks>
internal static class SettingsRowFilter
{
    public static bool ShouldShow(
        SettingsSection rowSection,
        string rowText,
        SettingsSection openSection,
        string query)
    {
        string trimmed = query.Trim();

        if (trimmed.Length == 0)
            return rowSection == openSection;

        return rowText.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the rail still decides what is shown. It is dimmed while a search
    /// is running, because what is on screen then answers to the query and a
    /// highlighted section would be claiming otherwise.
    /// </summary>
    public static bool IsSectionInEffect(string query) => query.Trim().Length == 0;
}
