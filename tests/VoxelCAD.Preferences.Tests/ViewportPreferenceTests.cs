using System;
using System.IO;
using VoxelCAD.Preferences;
using Xunit;

namespace VoxelCAD.Preferences.Tests;

/// <summary>
/// What the display switches promise: they are off only when somebody said so,
/// and what they say survives the window.
/// </summary>
public class ViewportPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"voxelcad-display-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private IEditorPreferences Open() => PreferencesFactory.Load(_directory);

    /// <summary>
    /// A file that has never heard of them reads them as on, which is what the
    /// editor has always looked like — the alternative is somebody's editor going
    /// flat and shadowless the day they update.
    /// </summary>
    [Fact]
    public void TheyAreAllOnBeforeAnybodyTouchesThem()
    {
        IEditorPreferences preferences = Open();

        Assert.True(preferences.ViewportOcclusion);
        Assert.True(preferences.ViewportShadows);
        Assert.True(preferences.ViewportFactures);
        Assert.True(preferences.GlassPanels);
    }

    [Fact]
    public void EachOneSurvivesBeingClosedAndOpened()
    {
        IEditorPreferences preferences = Open();

        preferences.ApplyViewportOcclusion(false);
        preferences.ApplyViewportShadows(false);
        preferences.ApplyViewportFactures(false);
        preferences.ApplyGlassPanels(false);

        IEditorPreferences reopened = Open();

        Assert.False(reopened.ViewportOcclusion);
        Assert.False(reopened.ViewportShadows);
        Assert.False(reopened.ViewportFactures);
        Assert.False(reopened.GlassPanels);
    }

    [Fact]
    public void TurningOneBackOnStores()
    {
        IEditorPreferences preferences = Open();

        preferences.ApplyGlassPanels(false);
        preferences.ApplyGlassPanels(true);

        Assert.True(Open().GlassPanels);
    }

    [Fact]
    public void EachOneIsIndependentOfTheOthers()
    {
        IEditorPreferences preferences = Open();

        preferences.ApplyViewportShadows(false);

        IEditorPreferences reopened = Open();

        Assert.False(reopened.ViewportShadows);
        Assert.True(reopened.ViewportOcclusion);
        Assert.True(reopened.ViewportFactures);
        Assert.True(reopened.GlassPanels);
    }
}
