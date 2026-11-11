using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// The presenter assembly's only public entry point: it wires a shell to its view
/// and hands back something the composition root can dispose.
/// </summary>
/// <remarks>
/// No document is passed in. Opening, creating and closing one is the shell's own
/// lifecycle work, and a composition root that handed it a first project would be
/// the one place that did it differently.
/// <para>
/// Preferences are passed in, because they are not the shell's to create: they
/// outlive it, the composition root needs the same instance to run the endpoint
/// from, and where they are stored is a question only the engine can answer.
/// </para>
/// </remarks>
public static class PresenterFactory
{
    public static IEditorShell CreateShell(IShellView view, IEditorPreferences preferences) =>
        new ShellPresenter(view, preferences);
}
