namespace VoxelCAD.Preferences;

/// <summary>The preferences assembly's only public entry point.</summary>
/// <remarks>
/// The directory is passed in rather than discovered. Where "beside the
/// application" is on any given platform is the engine's answer — <c>user://</c>
/// — and knowing it here would drag Godot into a layer that is otherwise files
/// and numbers, and out of reach of a test.
/// </remarks>
public static class PreferencesFactory
{
    public static IEditorPreferences Load(string directory) => new EditorPreferences(directory);
}
