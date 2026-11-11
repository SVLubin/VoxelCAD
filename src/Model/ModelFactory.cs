using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// The model assembly's only public entry point. It creates roots and nothing
/// else — everything below a root is created through the interfaces a root hands
/// out, so this never grows into a catalogue of the layer.
/// </summary>
public static class ModelFactory
{
    public static IProject CreateProject() => new Project(Scene.DefaultSize);

    public static IProject CreateProject(Vector3I sceneSize) => new Project(sceneSize);

    /// <summary>
    /// Opens a project from disk. The result has an empty history: opening a file
    /// is not something the user undoes their way out of.
    /// </summary>
    public static IProject LoadProject(string path) =>
        ProjectReader.Read(ProjectFileStorage.Load(path));

    /// <summary>The sample projects the editor ships with, by name.</summary>
    public static IReadOnlyList<string> SampleNames => Samples.Names;

    /// <summary>
    /// Opens a shipped sample as a new project with an empty history and no file of
    /// its own, so saving it asks where. Throws <see cref="KeyNotFoundException"/>
    /// for a name that is not in <see cref="SampleNames"/>.
    /// </summary>
    public static IProject OpenSample(string name) => Samples.Open(name);
}
