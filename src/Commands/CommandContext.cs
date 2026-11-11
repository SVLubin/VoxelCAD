using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Commands;

/// <summary>
/// What the commands act on. The host decides how it is seeded: the console
/// starts empty and opens a file, while an in-editor transport hands over the
/// document the user already has.
/// </summary>
public sealed class CommandContext
{
    private IProject? _project;
    private Guid _activeSceneId;

    public CommandContext(IProject? project = null)
    {
        if (project != null)
            Bind(project);
    }

    /// <summary>
    /// A different document is now open. The console has nobody to tell; the
    /// editor rebuilds its panel presenters around the new one, which is how an
    /// agent's <c>open</c> reaches the window instead of quietly leaving it
    /// showing the previous project.
    /// </summary>
    public event Action? ProjectChanged;

    /// <summary>The open document was written to disk, wherever the write came from.</summary>
    public event Action? ProjectSaved;

    /// <summary>
    /// A different scene of the same document is now the one being worked on.
    /// Which scene that is lives here for the same reason the open document does:
    /// the user activating one and an agent's <c>activate-scene</c> are the same
    /// event arriving from two directions, and the two must not drift apart.
    /// </summary>
    public event Action? ActiveSceneChanged;

    /// <summary>Throws rather than returning null, so no command has to check.</summary>
    public IProject Project =>
        _project ?? throw new CommandException("No project is open. Use 'new' or 'open' first.");

    /// <summary>Where the document lives, or null when it has never been written.</summary>
    public string? Path { get; private set; }

    /// <summary>
    /// The scene commands address: the active one. A project always has at least
    /// one, so the fallback here is a safety net for an id that has gone rather
    /// than an ordinary case.
    /// </summary>
    public IScene Scene =>
        TryResolveActiveScene(_activeSceneId, out IScene scene) ? scene : Project.Scenes[0];

    /// <summary>
    /// The library entry being edited, or null when the active context is an
    /// ordinary scene. A prefab's contents are a scene, so everything that acts on
    /// <see cref="Scene"/> works inside one unchanged; this is how a caller — and
    /// the editor's own shell — can tell which of the two it is looking at.
    /// </summary>
    public IPrefab? ActivePrefab =>
        Project.TryGetPrefab(_activeSceneId, out IPrefab prefab) ? prefab : null;

    public void SetProject(IProject project, string? path)
    {
        Unbind();
        Bind(project);
        Path = Normalize(path);

        ProjectChanged?.Invoke();
    }

    /// <summary>
    /// A path the document is known by is absolute or nothing. A relative one
    /// from a script or an agent is resolved against the working directory
    /// <em>now</em>, because it is written down — a recovery snapshot names it,
    /// and a later launch from another directory would resolve it elsewhere.
    /// </summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or System.IO.PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>
    /// Makes <paramref name="sceneId"/> the scene being worked on. The id must
    /// exist: callers resolve names first, so an unknown one is a programming
    /// error rather than user input. A prefab's id names its contents, which is
    /// the whole of how "open the prefab" works: the same call, the same event,
    /// and every scene-bound presenter rebuilt around a scene that happens to be
    /// a library entry.
    /// </summary>
    public void SetActiveScene(Guid sceneId)
    {
        if (!TryResolveActiveScene(sceneId, out _))
        {
            throw new ArgumentException(
                $"No scene or prefab with id {sceneId} in this project.", nameof(sceneId));
        }

        if (_activeSceneId == sceneId)
            return;

        _activeSceneId = sceneId;

        ActiveSceneChanged?.Invoke();
    }

    /// <summary>Records where the document was just written. It does not write it.</summary>
    public void MarkSaved(string path)
    {
        Path = Normalize(path);

        ProjectSaved?.Invoke();
    }

    /// <summary>
    /// Accepts an id or a name. Names are far easier for a model to keep in its
    /// head than a Guid, so an unambiguous one is treated as an address; an
    /// ambiguous one is refused with the candidates listed rather than guessed.
    /// </summary>
    public Guid ResolveElement(string text) => ResolveElement(Scene, text);

    /// <summary>
    /// The same address read in a scene of the caller's choosing. Copying into
    /// another scene needs it: the parent named there is that scene's element, and
    /// resolving it against the active one would find the wrong tree or none at all.
    /// </summary>
    public Guid ResolveElement(IScene scene, string text)
    {
        if (Guid.TryParse(text, out Guid id))
        {
            if (!scene.TryGetElement(id, out _))
                throw new CommandException($"No element with id {id}.");

            return id;
        }

        var matchesList = new List<IHierarchyElement>();
        Collect(scene.Children, text, matchesList);
        matchesList = PreferEditable(matchesList);

        return matchesList.Count switch
        {
            1 => matchesList[0].Id,
            0 => throw new CommandException($"No element called '{text}'."),
            _ => throw new CommandException(
                $"'{text}' matches {matchesList.Count} elements; use an id: "
                + string.Join(", ", matchesList.Select(element => element.Id)))
        };
    }

    /// <summary>
    /// The scene an id names. Ids reach this from <see cref="ResolveScene"/>, so an
    /// unknown one is a programming error rather than something a caller typed.
    /// </summary>
    public IScene GetScene(Guid id) =>
        Project.TryGetScene(id, out IScene scene)
            ? scene
            : throw new ArgumentException($"No scene with id {id} in this project.", nameof(id));

    public Guid ResolveScene(string text)
    {
        if (Guid.TryParse(text, out Guid id))
        {
            if (!Project.TryGetScene(id, out _))
                throw new CommandException($"No scene with id {id}.");

            return id;
        }

        List<IScene> matchesList = Project.Scenes
            .Where(scene => scene.Name == text)
            .ToList();

        return matchesList.Count switch
        {
            1 => matchesList[0].Id,
            0 => throw new CommandException($"No scene called '{text}'."),
            _ => throw new CommandException(
                $"'{text}' matches {matchesList.Count} scenes; use an id: "
                + string.Join(", ", matchesList.Select(scene => scene.Id)))
        };
    }

    public Guid ResolvePrefab(string text)
    {
        if (Guid.TryParse(text, out Guid id))
        {
            if (!Project.TryGetPrefab(id, out _))
                throw new CommandException($"No prefab with id {id}.");

            return id;
        }

        List<IPrefab> matchesList = Project.Prefabs
            .Where(prefab => prefab.Name == text)
            .ToList();

        return matchesList.Count switch
        {
            1 => matchesList[0].Id,
            0 => throw new CommandException($"No prefab called '{text}'."),
            _ => throw new CommandException(
                $"'{text}' matches {matchesList.Count} prefabs; use an id: "
                + string.Join(", ", matchesList.Select(prefab => prefab.Id)))
        };
    }

    public Guid ResolveMaterial(string text)
    {
        if (Guid.TryParse(text, out Guid id))
        {
            if (!Project.TryGetMaterial(id, out _))
                throw new CommandException($"No material with id {id}.");

            return id;
        }

        // The project's own first, then the library the editor ships with: a
        // document that defines a material called "Brick" means its own.
        //
        // The model refuses a new or renamed material a shipped name and composes
        // a duplicate away from one, so a document made by this build cannot put
        // two materials under one name — but a document is not always made by this
        // build. One saved before that guard existed can carry a project material
        // named exactly after a shipped one, and reading it must not fail; what it
        // must not do either is silently mean the copy, which is how the defect
        // read from the outside. So a collision is reported as the ambiguity it
        // is, with both ids, and the caller picks.
        List<IMaterial> matchesList = Project.Materials
            .Where(material => material.Name == text)
            .ToList();

        List<IMaterial> shippedList = Project.StandardMaterials
            .Where(material => material.Name == text)
            .ToList();

        matchesList = matchesList.Count == 0
            ? shippedList
            : matchesList.Concat(shippedList).ToList();

        return matchesList.Count switch
        {
            1 => matchesList[0].Id,
            0 => throw new CommandException($"No material called '{text}'."),
            _ => throw new CommandException(
                $"'{text}' matches {matchesList.Count} materials; use an id: "
                + string.Join(", ", matchesList.Select(material => material.Id)))
        };
    }

    private void Bind(IProject project)
    {
        _project = project;
        _activeSceneId = project.Scenes[0].Id;

        project.Changed += OnProjectChanged;
    }

    private void Unbind()
    {
        if (_project != null)
            _project.Changed -= OnProjectChanged;
    }

    /// <summary>
    /// The active scene can go without anyone asking for it to — deleted from a
    /// panel, or undone away — and something has to be active afterwards.
    /// </summary>
    private void OnProjectChanged(ProjectChange change)
    {
        if (change.TargetId != _activeSceneId)
            return;

        // A prefab being edited can go the same way an active scene can — deleted
        // from a panel, or undone away — and the fallback is the same one.
        if (change.Kind != ProjectChangeKind.SceneRemoved
            && change.Kind != ProjectChangeKind.PrefabRemoved)
        {
            return;
        }

        _activeSceneId = Project.Scenes[0].Id;

        ActiveSceneChanged?.Invoke();
    }

    private bool TryResolveActiveScene(Guid id, out IScene scene)
    {
        if (Project.TryGetScene(id, out scene))
            return true;

        if (Project.TryGetPrefab(id, out IPrefab prefab))
        {
            scene = prefab.Content;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A name means the element a caller can act on. An instance's contents keep
    /// the names of the source they were derived from — that is what makes the
    /// inside of a copied assembly read like the original — so every instance would
    /// otherwise make its own name ambiguous the moment it was created, and
    /// <c>element=Wheel</c> would stop working for the very element it names.
    /// Derived content stays reachable by id, and by <c>tree</c>, which is where a
    /// reader meets it anyway.
    /// </summary>
    private static List<IHierarchyElement> PreferEditable(List<IHierarchyElement> matchesList)
    {
        var editableList = new List<IHierarchyElement>(matchesList.Count);

        foreach (IHierarchyElement element in matchesList)
        {
            if (element.PrefabInstanceId == null)
                editableList.Add(element);
        }

        return editableList.Count > 0 ? editableList : matchesList;
    }

    private static void Collect(
        IReadOnlyList<IHierarchyElement> children,
        string name,
        List<IHierarchyElement> matchesList)
    {
        foreach (IHierarchyElement child in children)
        {
            if (child.Name == name)
                matchesList.Add(child);

            Collect(child.Children, name, matchesList);
        }
    }
}
