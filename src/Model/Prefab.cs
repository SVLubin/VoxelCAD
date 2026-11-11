using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IPrefab"/>
internal sealed class Prefab : IPrefab
{
    private readonly Project _project;
    private readonly Scene _content;

    public Prefab(Project project, Scene content)
    {
        _project = project;
        _content = content;

        // Subscribed once and never dropped, deliberately. A prefab outlives its
        // removal from the library — a delete is undoable, and the command holds
        // this very instance — so unsubscribing on detach would mean resubscribing
        // on undo, one more pair of steps to forget. A detached prefab has no
        // instances left to rebuild, so what arrives here meanwhile costs a lookup.
        _content.Changed += OnContentChanged;
    }

    public Guid Id => _content.Id;

    public string Name => _content.Name;

    public IScene Content => _content;

    internal Scene ContentScene => _content;

    /// <summary>
    /// Any edit to the source is an edit to every instance of it. Marked rather
    /// than rebuilt on the spot: one source change raises a stream of element
    /// events, and each of them would otherwise rebuild every instance again.
    /// </summary>
    private void OnContentChanged(SceneChange change) => _project.MarkPrefabDirty(Id);
}
