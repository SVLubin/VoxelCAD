using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which rows the settings form shows. The one decision in there worth a test:
/// a search that quietly stayed inside the open section would look like it
/// worked and find nothing.
/// </summary>
public sealed class SettingsRowFilterTests
{
    [Fact]
    public void WithNoQueryTheOpenSectionDecides()
    {
        Assert.True(SettingsRowFilter.ShouldShow(
            SettingsSection.Endpoint, "port", SettingsSection.Endpoint, string.Empty));

        Assert.False(SettingsRowFilter.ShouldShow(
            SettingsSection.Handover, "countdown", SettingsSection.Endpoint, string.Empty));
    }

    [Fact]
    public void AQueryReachesAcrossEverySection()
    {
        Assert.True(SettingsRowFilter.ShouldShow(
            SettingsSection.Handover, "countdown seconds", SettingsSection.Endpoint, "count"));
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        Assert.True(SettingsRowFilter.ShouldShow(
            SettingsSection.Debug, "Let an agent drive", SettingsSection.Endpoint, "AGENT"));
        Assert.True(SettingsRowFilter.ShouldShow(
            SettingsSection.Files, "autosave recovery snapshot", SettingsSection.Display, "recovery"));
    }

    [Fact]
    public void AQueryThatMatchesNothingShowsNothing()
    {
        Assert.False(SettingsRowFilter.ShouldShow(
            SettingsSection.Endpoint, "port", SettingsSection.Endpoint, "zzz"));
    }

    /// <summary>Whitespace is not a search — the rail keeps deciding.</summary>
    [Fact]
    public void BlankSpaceIsNotAQuery()
    {
        Assert.True(SettingsRowFilter.ShouldShow(
            SettingsSection.Endpoint, "port", SettingsSection.Endpoint, "   "));

        Assert.True(SettingsRowFilter.IsSectionInEffect("   "));
        Assert.False(SettingsRowFilter.IsSectionInEffect("po"));
    }
}
