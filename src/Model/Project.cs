using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IProject"/>
internal sealed class Project : IProject
{
    internal const string DefaultSceneName = "Scene";

    internal const string DefaultPrefabName = "Prefab";

    private static readonly Color DefaultMaterialColor = new(0.7f, 0.7f, 0.72f);

    // What a prefab's contents stand at: nowhere, unturned, unscaled, and on the
    // grid. The instance carries where the thing actually is.
    private static readonly TransformValues IdentityValues =
        new(Vector3.Zero, Vector3.Zero, Vector3.One);

    private static readonly TransformModes GridModes =
        new(FieldMode.Integer, FieldMode.Snap90, FieldMode.Integer);

    private readonly History _history = new();
    private readonly List<Material> _materialsList = new();
    private readonly Dictionary<Guid, Material> _materialsByIdDictionary = new();
    private readonly List<Scene> _scenesList = new();
    private readonly Dictionary<Guid, Scene> _scenesByIdDictionary = new();
    private readonly List<Prefab> _prefabsList = new();
    private readonly Dictionary<Guid, Prefab> _prefabsByIdDictionary = new();

    // Sources whose instances are out of date. A source change arrives as a stream
    // of element events, and rebuilding every instance on each of them would be
    // quadratic in the size of the edit; collecting them and draining once is what
    // keeps one keystroke inside a prefab to one rebuild.
    private readonly HashSet<Guid> _dirtyPrefabsHashSet = new();

    private bool _rebuilding;

    /// <summary>
    /// The starting material and scene are attached directly rather than through
    /// commands: "new project" is not something the user can undo their way out of.
    /// </summary>
    public Project(Vector3I sceneSize)
    {
        AttachMaterial(
            new Material(Guid.NewGuid(), new MaterialSnapshot("Default", DefaultMaterialColor)),
            _materialsList.Count);

        AttachScene(new Scene(this, _history, Guid.NewGuid(), DefaultSceneName, sceneSize), 0);
    }

    /// <summary>
    /// Nothing of the document in it — the reader fills that — but the shipped
    /// materials are here before a single byte is read, because that is exactly
    /// what a file does not carry: its cells name them by id and expect them to
    /// be waiting.
    /// </summary>
    /// <summary>Nothing in it — the reader fills it, and a project on disk always has both.</summary>
    private Project()
    {
    }

    public event Action<ProjectChange>? Changed;

    public IHistory History => _history;

    public IReadOnlyList<IMaterial> Materials => _materialsList;

    public IReadOnlyList<IMaterial> StandardMaterials => Model.StandardMaterials.Shared;

    public IEnumerable<IMaterial> AllMaterials =>
        System.Linq.Enumerable.Concat(_materialsList, Model.StandardMaterials.Shared);

    public IReadOnlyList<IScene> Scenes => _scenesList;

    public IReadOnlyList<IPrefab> Prefabs => _prefabsList;

    public bool TryGetScene(Guid id, out IScene scene)
    {
        if (_scenesByIdDictionary.TryGetValue(id, out Scene? found))
        {
            scene = found;
            return true;
        }

        scene = null!;
        return false;
    }

    public bool TryGetPrefab(Guid id, out IPrefab prefab)
    {
        if (_prefabsByIdDictionary.TryGetValue(id, out Prefab? found))
        {
            prefab = found;
            return true;
        }

        prefab = null!;
        return false;
    }

    public Guid CreateScene(string name, Vector3I size)
    {
        var scene = new Scene(this, _history, Guid.NewGuid(), name, size);

        // Stamped with the scene itself: undoing the deletion of a scene, or
        // redoing its creation, is exactly the case where the editor should be
        // looking at that scene.
        _history.Execute(new CreateSceneCommand(this, scene, _scenesList.Count), scene.Id);

        return scene.Id;
    }

    public void DeleteScene(Guid id)
    {
        Scene scene = GetScene(id);

        if (_scenesList.Count == 1)
            throw new InvalidOperationException("A project must keep at least one scene.");

        _history.Execute(new DeleteSceneCommand(this, scene, _scenesList.IndexOf(scene)), scene.Id);
    }

    public SceneClip CaptureScene(Guid id)
    {
        Scene scene = GetScene(id);
        IReadOnlyList<ElementNode> nodesList = scene.CaptureNodes();

        return new SceneClip(
            scene.Name,
            scene.Size,
            nodesList,
            CaptureClipMaterials(nodesList),
            CaptureClipPrefabs(nodesList));
    }

    /// <summary>
    /// The scene is filled before it is attached, so the whole thing arrives as one
    /// <see cref="ProjectChangeKind.SceneAdded"/> rather than as a stream of
    /// elements landing in a scene nobody has been told about yet.
    /// </summary>
    public Guid PasteScene(SceneClip clip, string? name = null)
    {
        var materialsList = new List<Material>();
        var prefabsList = new List<Prefab>();
        ClipResolution resolution =
            ResolveClip(clip.Materials, clip.Prefabs, materialsList, prefabsList);

        var scene = new Scene(
            this,
            _history,
            Guid.NewGuid(),
            name ?? CopyNameHelper.Compose(clip.Name, CollectSceneNames()),
            clip.Size);

        foreach (ElementNode node in clip.Elements)
            scene.AddClipElement(ElementClipFactory.Create(node, resolution, node.Name));

        // Stamped with the scene itself, like a created one: undoing a paste is
        // exactly the case where the editor should be looking at what it removed.
        _history.Execute(
            new PasteSceneCommand(this, scene, _scenesList.Count, materialsList, prefabsList),
            scene.Id);

        return scene.Id;
    }

    public void RenameScene(Guid id, string name)
    {
        Scene scene = GetScene(id);

        if (scene.Name == name)
            return;

        _history.Execute(new RenameSceneCommand(this, scene, name), scene.Id);
    }

    /// <summary>
    /// Whether anything would have to be unpacked to delete this prefab. Published
    /// for the same reason <see cref="IsMaterialInUse"/> is: a surface offering the
    /// deletion has to be able to say what it will cost before it is pressed.
    /// </summary>
    /// <remarks>
    /// Instances that are themselves derived — one prefab's instance sitting inside
    /// another's contents — are not counted. They are not things anybody deletes or
    /// unpacks on their own; they go when the source holding them is rebuilt.
    /// </remarks>
    public bool IsPrefabInUse(Guid id)
    {
        foreach (Scene scene in CollectAllScenes())
        {
            if (scene.CollectWritableInstancesOf(id).Count > 0)
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool CanContainPrefab(Guid hostSceneId, Guid prefabId) =>
        !_prefabsByIdDictionary.ContainsKey(hostSceneId) || !ContainsPrefab(prefabId, hostSceneId);

    /// <summary>
    /// Removes a prefab, unpacking every instance of it first — in every scene and
    /// inside every other prefab. Not refused the way a material in use is: nothing
    /// is lost by it, since an unpacked instance keeps the geometry it was showing.
    /// </summary>
    public void DeletePrefab(Guid id)
    {
        Prefab prefab = GetPrefab(id);
        var unpacksList = new List<InstanceUnpack>();

        foreach (Scene scene in CollectAllScenes())
        {
            foreach (HierarchyElement instance in scene.CollectWritableInstancesOf(id))
            {
                unpacksList.Add(new InstanceUnpack(
                    scene,
                    instance,
                    (PrefabInstance)instance.Component!,
                    scene.PrepareUnpacked(instance)));
            }
        }

        // Project-level, like a material edit: it belongs to no one scene, and it
        // reaches as many of them as have an instance in.
        _history.Execute(
            new DeletePrefabCommand(this, prefab, _prefabsList.IndexOf(prefab), unpacksList));
    }

    /// <summary>
    /// Refused when another entry already has the name: the library is addressed by
    /// name, so two prefabs sharing one would make every instance of either
    /// ambiguous. Creation composes a free name instead — there the name is a
    /// default, here it is the user's explicit choice of words, and quietly changing
    /// an explicit choice is worse than refusing it.
    /// </summary>
    public void RenamePrefab(Guid id, string name)
    {
        Prefab prefab = GetPrefab(id);

        if (prefab.Name == name)
            return;

        if (CollectPrefabNames().Contains(name))
        {
            throw new InvalidOperationException(
                $"A prefab called '{name}' already exists; prefab names are unique.");
        }

        _history.Execute(new RenamePrefabCommand(this, prefab, name));
    }

    public bool TryGetMaterial(Guid id, out IMaterial material)
    {
        if (_materialsByIdDictionary.TryGetValue(id, out Material? found))
        {
            material = found;
            return true;
        }

        // Then the shipped library, which every project resolves against and no
        // project holds a copy of.
        if (Model.StandardMaterials.TryGet(id, out Material shipped))
        {
            material = shipped;
            return true;
        }

        material = null!;
        return false;
    }

    public Guid CreateMaterial(string name, Color color)
    {
        RefuseIfShippedName(name);

        var material = new Material(Guid.NewGuid(), new MaterialSnapshot(name, color));

        _history.Execute(new CreateMaterialCommand(this, material, _materialsList.Count));

        return material.Id;
    }

    /// <summary>
    /// The prefab library is walked as well as the scenes, and it has to be: a
    /// material worn only by cells inside a prefab that nothing instances yet is
    /// still in use, and calling it free would delete it out from under the next
    /// instance of that prefab.
    /// </summary>
    public bool IsMaterialInUse(Guid id)
    {
        foreach (Scene scene in CollectAllScenes())
        {
            if (scene.UsesMaterial(id))
                return true;
        }

        return false;
    }

    public void DeleteMaterial(Guid id)
    {
        Material material = GetMaterial(id);

        // A cell wearing a shipped material has nothing but its id to find it by,
        // so deleting one would make every document that ever used it unreadable
        // rather than merely poorer.
        RefuseIfStandard(id, "deleted");

        if (_materialsList.Count == 1)
            throw new InvalidOperationException("A project must keep at least one material.");

        // The same question a surface asks before it offers the deletion at all;
        // one rule, so a live button and a refusal cannot disagree. Named rather
        // than identified: the shell shows this sentence to a person as it stands.
        if (IsMaterialInUse(id))
        {
            throw new InvalidOperationException(
                $"Material '{material.Name}' is still in use; replace it on those voxels first.");
        }

        _history.Execute(new DeleteMaterialCommand(this, material, _materialsList.IndexOf(material)));
    }

    public MaterialClip CaptureMaterial(Guid id) => new(GetMaterial(id).Capture());

    public Guid PasteMaterial(MaterialClip clip, string? name = null)
    {
        var material = new Material(
            Guid.NewGuid(),
            clip.State with
            {
                Name = name ?? CopyNameHelper.Compose(clip.Name, CollectMaterialNames())
            });

        _history.Execute(new CreateMaterialCommand(this, material, _materialsList.Count));

        return material.Id;
    }

    /// <summary>
    /// Its name is what its id was derived from, so renaming a shipped material
    /// would leave every file's cells naming one that no longer exists.
    /// </summary>
    public void RenameMaterial(Guid id, string name)
    {
        RefuseIfShippedName(name);

        ChangeMaterial(id, state => state with { Name = name });
    }

    public void SetMaterialColor(Guid id, Color color) =>
        ChangeMaterial(id, state => state with { Color = color });

    public void SetMaterialAppearance(Guid id, MaterialAppearance appearance) =>
        ChangeMaterial(
            id,
            state => state with { Appearance = MaterialAppearanceClamp.Apply(appearance) });

    public void SetMaterialPreset(Guid id, MaterialPreset preset)
    {
        MaterialPresetEntry entry = MaterialPresetTable.Get(preset);

        // One record for both, because applying a preset is one act: undoing it
        // must not leave a material wearing Brick's colour and Steel's shading.
        ChangeMaterial(
            id,
            state => state with
            {
                Color = entry.Color,
                Appearance = MaterialAppearanceClamp.Apply(entry.Appearance)
            });
    }

    public void Save(string path) => Capture().Write(path);

    public IProjectSnapshot Capture() => ProjectWriter.Capture(this);

    internal static Project CreateEmpty() => new();

    /// <summary>
    /// Loading bypasses the command layer on purpose: opening a file is not
    /// something the user undoes their way out of, so nothing is recorded.
    /// </summary>
    internal Scene AddLoadedScene(Guid id, string name, Vector3I size)
    {
        var scene = new Scene(this, _history, id, name, size);
        AttachScene(scene, _scenesList.Count);

        return scene;
    }

    internal void AddLoadedMaterial(Guid id, MaterialSnapshot state) =>
        AttachMaterial(new Material(id, state), _materialsList.Count);

    /// <summary>
    /// Whether a cell may wear this id — the project's own, or one the editor
    /// ships with. Both, because a voxel names a material and does not care which
    /// list it came out of.
    /// </summary>
    internal bool ContainsMaterial(Guid id) =>
        _materialsByIdDictionary.ContainsKey(id) || Model.StandardMaterials.Contains(id);

    /// <summary>
    /// What a record about a material calls it: the name where one resolves, the
    /// id otherwise — a record may outlive the material it names.
    /// </summary>
    internal string DescribeMaterial(Guid id) =>
        TryGetMaterial(id, out IMaterial material) ? material.Name : id.ToString();

    public void ReplaceMaterial(Guid fromId, Guid toId)
    {
        if (!ContainsMaterial(toId))
            throw new ArgumentException($"No material with id {toId} in this project.", nameof(toId));

        if (fromId == toId)
            return;

        // Project-level, like a material edit: it belongs to no one scene and
        // reaches as many of them, prefab contents included, as wear the material.
        using (_history.CreateBatch(
                   ActionKind.ReplaceMaterial, Guid.Empty, fromId, DescribeMaterial(fromId)))
        {
            foreach (Scene scene in CollectAllScenes())
                scene.ReplaceMaterialInBatch(fromId, toId, null);
        }
    }

    /// <summary>
    /// The materials a set of clip nodes wear, under the ids they wear them by, so
    /// a paste can put back one this project has lost since.
    /// </summary>
    internal IReadOnlyList<ClipMaterial> CaptureClipMaterials(IReadOnlyList<ElementNode> nodesList)
    {
        var idsHashSet = new HashSet<Guid>();

        foreach (ElementNode node in nodesList)
            ElementClipFactory.CollectMaterialIds(node, idsHashSet);

        var materialsList = new List<ClipMaterial>(idsHashSet.Count);

        foreach (Guid id in idsHashSet)
        {
            if (_materialsByIdDictionary.TryGetValue(id, out Material? material))
                materialsList.Add(new ClipMaterial(id, material.Capture()));
        }

        return materialsList;
    }

    /// <summary>
    /// Which material each id of a clip means here. One the registry still has means
    /// itself — a copy shares the originals' materials, which is the whole point of
    /// a registry — and one that has gone is rebuilt from what the clip carried,
    /// under a new id, and collected in <paramref name="createdList"/> for the paste
    /// command to attach and to take away again on undo.
    /// </summary>
    /// <remarks>
    /// A new id rather than the one it had: the old id may still be named by the
    /// history record that deleted it, and undoing that far would otherwise try to
    /// put a second material with the same id back into the registry.
    /// </remarks>
    internal IReadOnlyDictionary<Guid, Guid> ResolveClipMaterials(
        IReadOnlyList<ClipMaterial> materialsList,
        List<Material> createdList)
    {
        var mapByIdDictionary = new Dictionary<Guid, Guid>(materialsList.Count);
        HashSet<string> takenHashSet = CollectMaterialNames();

        foreach (ClipMaterial clip in materialsList)
        {
            if (_materialsByIdDictionary.ContainsKey(clip.Id))
            {
                mapByIdDictionary[clip.Id] = clip.Id;
                continue;
            }

            var name = CopyNameHelper.Compose(clip.State.Name, takenHashSet);
            var material = new Material(Guid.NewGuid(), clip.State with { Name = name });

            takenHashSet.Add(name);
            createdList.Add(material);
            mapByIdDictionary[clip.Id] = material.Id;
        }

        return mapByIdDictionary;
    }

    /// <summary>
    /// The prefabs a set of clip nodes refer to, closure included: a prefab whose
    /// own contents hold an instance of another brings that one along, or a paste
    /// into a document that has neither would restore half a library.
    /// </summary>
    internal IReadOnlyList<ClipPrefab> CaptureClipPrefabs(IReadOnlyList<ElementNode> nodesList)
    {
        var seenHashSet = new HashSet<Guid>();
        var pendingQueue = new Queue<Guid>();

        foreach (ElementNode node in nodesList)
            EnqueueSourceIds(node, seenHashSet, pendingQueue);

        var prefabsList = new List<ClipPrefab>();

        while (pendingQueue.Count > 0)
        {
            Guid id = pendingQueue.Dequeue();

            if (!_prefabsByIdDictionary.TryGetValue(id, out Prefab? prefab))
                continue;

            IReadOnlyList<ElementNode> elementsList = prefab.ContentScene.CaptureNodes();

            prefabsList.Add(new ClipPrefab(
                id, prefab.Name, prefab.ContentScene.Size, elementsList));

            foreach (ElementNode node in elementsList)
                EnqueueSourceIds(node, seenHashSet, pendingQueue);
        }

        return prefabsList;
    }

    /// <summary>
    /// What the ids inside a clip mean here. Materials follow
    /// <see cref="ResolveClipMaterials"/>; prefabs follow the same rule one level
    /// up — one the library still has means itself, and one that has gone is rebuilt
    /// under a new id and collected for the paste command to attach.
    /// </summary>
    /// <remarks>
    /// Two passes, and it has to be two: a rebuilt prefab's contents may hold
    /// instances of other rebuilt prefabs, so every new id has to exist before any
    /// content is built against them.
    /// </remarks>
    internal ClipResolution ResolveClip(
        IReadOnlyList<ClipMaterial> materialsList,
        IReadOnlyList<ClipPrefab> prefabsList,
        List<Material> createdMaterialsList,
        List<Prefab> createdPrefabsList)
    {
        var mapByIdDictionary = new Dictionary<Guid, Guid>(prefabsList.Count);
        var missingList = new List<ClipPrefab>();

        foreach (ClipPrefab clip in prefabsList)
        {
            if (_prefabsByIdDictionary.ContainsKey(clip.Id))
            {
                mapByIdDictionary[clip.Id] = clip.Id;
                continue;
            }

            mapByIdDictionary[clip.Id] = Guid.NewGuid();
            missingList.Add(clip);
        }

        var resolution = new ClipResolution(
            ResolveClipMaterials(materialsList, createdMaterialsList), mapByIdDictionary);

        HashSet<string> takenHashSet = CollectPrefabNames();

        foreach (ClipPrefab clip in missingList)
        {
            var name = CopyNameHelper.Compose(clip.Name, takenHashSet);
            var content = new Scene(this, _history, mapByIdDictionary[clip.Id], name, clip.Size);

            takenHashSet.Add(name);

            foreach (ElementNode node in clip.Elements)
                content.AddClipElement(ElementClipFactory.Create(node, resolution, node.Name));

            createdPrefabsList.Add(new Prefab(this, content));
        }

        return resolution;
    }

    /// <summary>
    /// Builds a library entry from a captured subtree, detached — attaching it is
    /// the command's job. The contents go in at identity and on the grid: the
    /// instance left behind in the scene carries the element's own transform, and a
    /// prefab whose cells sat off the lattice could not be painted against the walls
    /// of its own room.
    /// </summary>
    internal Prefab CreateDetachedPrefab(string name, Vector3I size, ElementNode content)
    {
        var scene = new Scene(
            this,
            _history,
            Guid.NewGuid(),
            CopyNameHelper.Compose(name, CollectPrefabNames()),
            size);

        HierarchyElement root =
            ElementClipFactory.Create(content, ClipResolution.Identity, content.Name);

        root.ElementTransform.Restore(IdentityValues, GridModes);
        scene.AddClipElement(root);

        return new Prefab(this, scene);
    }

    internal bool TryGetPrefabContent(Guid id, out Scene content)
    {
        if (_prefabsByIdDictionary.TryGetValue(id, out Prefab? prefab))
        {
            content = prefab.ContentScene;
            return true;
        }

        content = null!;
        return false;
    }

    /// <summary>
    /// Refuses an instance that would put a prefab inside itself. Only asked of a
    /// prefab's own contents — an ordinary scene can hold anything, since nothing
    /// expands a scene into something else.
    /// </summary>
    internal void EnsureInstanceAllowed(Guid hostSceneId, Guid prefabId)
    {
        if (CanContainPrefab(hostSceneId, prefabId))
            return;

        throw new InvalidOperationException(
            $"Prefab {prefabId} cannot be placed inside prefab {hostSceneId}: "
            + "that would make it contain itself.");
    }

    /// <summary>The same question asked of every instance a clip carries.</summary>
    internal void EnsureInstancesAllowed(
        Guid hostSceneId,
        ElementNode root,
        ClipResolution resolution)
    {
        if (!_prefabsByIdDictionary.ContainsKey(hostSceneId))
            return;

        var idsHashSet = new HashSet<Guid>();
        ElementClipFactory.CollectSourceIds(root, idsHashSet);

        foreach (Guid id in idsHashSet)
            EnsureInstanceAllowed(hostSceneId, resolution.Prefab(id));
    }

    internal void AttachPrefab(Prefab prefab, int index)
    {
        _prefabsList.Insert(index, prefab);
        _prefabsByIdDictionary.Add(prefab.Id, prefab);

        Raise(ProjectChangeKind.PrefabAdded, prefab.Id, index);

        // An instance may have been standing over nothing while it was away.
        MarkPrefabDirty(prefab.Id);
    }

    internal void DetachPrefab(Prefab prefab)
    {
        _prefabsList.Remove(prefab);
        _prefabsByIdDictionary.Remove(prefab.Id);

        Raise(ProjectChangeKind.PrefabRemoved, prefab.Id, -1);
    }

    /// <summary>
    /// Attaches a closure of prefabs a paste brought with it, then expands their
    /// contents — which cannot happen one at a time, because they may hold
    /// instances of each other and half a library expands into half a model.
    /// </summary>
    internal void AttachClipPrefabs(IReadOnlyList<Prefab> prefabsList)
    {
        foreach (Prefab prefab in prefabsList)
            AttachPrefab(prefab, _prefabsList.Count);

        foreach (Prefab prefab in prefabsList)
            prefab.ContentScene.RebuildEveryInstance();
    }

    internal void DetachClipPrefabs(IReadOnlyList<Prefab> prefabsList)
    {
        for (var i = prefabsList.Count - 1; i >= 0; i--)
            DetachPrefab(prefabsList[i]);
    }

    internal void ApplyPrefabName(Prefab prefab, string name)
    {
        prefab.ContentScene.SetName(name);

        Raise(ProjectChangeKind.PrefabChanged, prefab.Id, -1);
    }

    /// <summary>
    /// A source changed, so every instance of it is out of date. Collected rather
    /// than rebuilt on the spot, and drained by whoever is not already draining:
    /// rebuilding an instance raises element events of its own, and one of those
    /// may be inside another prefab's contents, which marks that one too.
    /// </summary>
    /// <remarks>
    /// The walk terminates because the library is acyclic — that is what
    /// <see cref="EnsureInstanceAllowed"/> is for, and the reason it is a rule
    /// rather than a nicety.
    /// </remarks>
    internal void MarkPrefabDirty(Guid prefabId)
    {
        _dirtyPrefabsHashSet.Add(prefabId);

        if (_rebuilding)
            return;

        _rebuilding = true;

        try
        {
            while (TryTakeDirtyPrefab(out Guid id))
            {
                foreach (Scene scene in CollectAllScenes())
                    scene.RebuildInstancesOf(id);
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>
    /// Loading bypasses the command layer like every other part of it. The prefab
    /// is attached directly rather than through <see cref="AttachPrefab"/>, whose
    /// rebuild would run once per prefab over a document still being read.
    /// </summary>
    internal Scene AddLoadedPrefab(Guid id, string name, Vector3I size)
    {
        var prefab = new Prefab(this, new Scene(this, _history, id, name, size));

        _prefabsList.Add(prefab);
        _prefabsByIdDictionary.Add(id, prefab);

        return prefab.ContentScene;
    }

    /// <summary>
    /// Fills in every instance in the document once, after a file has been read:
    /// contents are derived, so nothing on disk carries them.
    /// </summary>
    internal void ExpandLoadedInstances()
    {
        foreach (Prefab prefab in _prefabsList)
            MarkPrefabDirty(prefab.Id);
    }

    internal void AttachMaterial(Material material, int index)
    {
        _materialsList.Insert(index, material);
        _materialsByIdDictionary.Add(material.Id, material);
        Raise(ProjectChangeKind.MaterialAdded, material.Id, index);
    }

    internal void DetachMaterial(Material material)
    {
        _materialsList.Remove(material);
        _materialsByIdDictionary.Remove(material.Id);
        Raise(ProjectChangeKind.MaterialRemoved, material.Id, -1);
    }

    /// <summary>
    /// Every write to a material goes through here, and a shipped one is refused:
    /// the library is shared by every project in the process and written to no
    /// file, so a change to it would show up in documents nobody edited and
    /// survive in none of them.
    /// </summary>
    private void RefuseIfStandard(Guid id, string verb)
    {
        if (Model.StandardMaterials.Contains(id))
        {
            throw new InvalidOperationException(
                $"Material {id} is one the editor ships with and cannot be {verb}. "
                + "Duplicate it to get one of your own.");
        }
    }

    internal void ApplyMaterial(Material material, MaterialSnapshot state)
    {
        material.Apply(state);

        Raise(ProjectChangeKind.MaterialChanged, material.Id, -1);
    }

    internal void AttachScene(Scene scene, int index)
    {
        _scenesList.Insert(index, scene);
        _scenesByIdDictionary.Add(scene.Id, scene);

        Raise(ProjectChangeKind.SceneAdded, scene.Id, index);
    }

    internal void DetachScene(Scene scene)
    {
        _scenesList.Remove(scene);
        _scenesByIdDictionary.Remove(scene.Id);

        Raise(ProjectChangeKind.SceneRemoved, scene.Id, -1);
    }

    internal void ApplySceneName(Scene scene, string name)
    {
        scene.SetName(name);

        Raise(ProjectChangeKind.SceneChanged, scene.Id, -1);
    }

    private void ChangeMaterial(Guid id, Func<MaterialSnapshot, MaterialSnapshot> change)
    {
        RefuseIfStandard(id, "changed");

        Material material = GetMaterial(id);
        MaterialSnapshot before = material.Capture();
        MaterialSnapshot after = change(before);

        if (before == after)
            return;

        _history.Execute(new MaterialCommand(this, material, before, after));
    }

    /// <summary>
    /// Every tree in the document: the scenes, and the contents of every prefab.
    /// Anything that walks the whole document walks this — an instance, a material
    /// and a voxel are the same things inside a library entry as inside a scene.
    /// </summary>
    private IReadOnlyList<Scene> CollectAllScenes()
    {
        var scenesList = new List<Scene>(_scenesList.Count + _prefabsList.Count);

        scenesList.AddRange(_scenesList);

        foreach (Prefab prefab in _prefabsList)
            scenesList.Add(prefab.ContentScene);

        return scenesList;
    }

    /// <summary>Whether <paramref name="prefabId"/> is, or transitively contains, <paramref name="soughtId"/>.</summary>
    private bool ContainsPrefab(Guid prefabId, Guid soughtId)
    {
        if (prefabId == soughtId)
            return true;

        if (!_prefabsByIdDictionary.TryGetValue(prefabId, out Prefab? prefab))
            return false;

        foreach (HierarchyElement element
            in prefab.ContentScene.CollectInstances(null, writableOnly: false))
        {
            if (element.Component is PrefabInstance instance
                && ContainsPrefab(instance.SourceId, soughtId))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryTakeDirtyPrefab(out Guid prefabId)
    {
        prefabId = Guid.Empty;

        var found = false;

        foreach (Guid id in _dirtyPrefabsHashSet)
        {
            prefabId = id;
            found = true;

            break;
        }

        if (found)
            _dirtyPrefabsHashSet.Remove(prefabId);

        return found;
    }

    private static void EnqueueSourceIds(
        ElementNode node,
        HashSet<Guid> seenHashSet,
        Queue<Guid> pendingQueue)
    {
        var idsHashSet = new HashSet<Guid>();

        ElementClipFactory.CollectSourceIds(node, idsHashSet);

        foreach (Guid id in idsHashSet)
        {
            if (seenHashSet.Add(id))
                pendingQueue.Enqueue(id);
        }
    }

    private HashSet<string> CollectPrefabNames()
    {
        var namesHashSet = new HashSet<string>();

        foreach (Prefab prefab in _prefabsList)
            namesHashSet.Add(prefab.Name);

        return namesHashSet;
    }

    private Prefab GetPrefab(Guid id)
    {
        if (!_prefabsByIdDictionary.TryGetValue(id, out Prefab? prefab))
            throw new ArgumentException($"No prefab with id {id} in this project.", nameof(id));

        return prefab;
    }

    private HashSet<string> CollectSceneNames()
    {
        var namesHashSet = new HashSet<string>();

        foreach (Scene scene in _scenesList)
            namesHashSet.Add(scene.Name);

        return namesHashSet;
    }

    /// <summary>
    /// Every material name a project cannot compose onto — its own and the
    /// library's both.
    /// </summary>
    /// <remarks>
    /// The library has to be in it, and leaving it out was not a small omission:
    /// duplicating a shipped material is the escape hatch <see cref="RefuseIfStandard"/>
    /// prints in its own refusal ("Duplicate it to get one of your own"), and the
    /// copy took the shipped name <em>verbatim</em> — after which every command
    /// naming a material by string meant the copy, and the shipped material was
    /// unreachable by name from the whole surface. Measured on the headless CLI:
    /// 'set-material-color material=brushed_iron_sharp' was refused before the
    /// duplicate and accepted after it.
    /// </remarks>
    private HashSet<string> CollectMaterialNames()
    {
        var namesHashSet = new HashSet<string>();

        foreach (Material material in _materialsList)
            namesHashSet.Add(material.Name);

        foreach (Material shipped in Model.StandardMaterials.Shared)
            namesHashSet.Add(shipped.Name);

        return namesHashSet;
    }

    /// <summary>
    /// Refuses a name the library already uses. Composed away where the name is a
    /// <em>default</em> — a paste with nothing said about it — and refused where
    /// the caller typed it, which is the rule <see cref="RenamePrefab"/> already
    /// states: quietly changing an explicit choice is worse than refusing it.
    /// </summary>
    private static void RefuseIfShippedName(string name)
    {
        foreach (Material shipped in Model.StandardMaterials.Shared)
        {
            if (!string.Equals(shipped.Name, name, StringComparison.Ordinal))
                continue;

            throw new InvalidOperationException(
                $"'{name}' is the name of a material the editor ships with, and two "
                + "materials of one name cannot both be reached by it. Choose another.");
        }
    }

    private Scene GetScene(Guid id)
    {
        if (!_scenesByIdDictionary.TryGetValue(id, out Scene? scene))
            throw new ArgumentException($"No scene with id {id} in this project.", nameof(id));

        return scene;
    }

    private Material GetMaterial(Guid id)
    {
        if (_materialsByIdDictionary.TryGetValue(id, out Material? material))
            return material;

        if (Model.StandardMaterials.TryGet(id, out Material shipped))
            return shipped;

        throw new ArgumentException($"No material with id {id} in this project.", nameof(id));
    }

    private void Raise(ProjectChangeKind kind, Guid targetId, int index) =>
        Changed?.Invoke(new ProjectChange(kind, targetId, index));
}
