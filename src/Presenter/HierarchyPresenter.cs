using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Keeps the project panel in step with the document: the scenes, the prefabs and
/// the materials, and inside the one scene being worked on, its element tree.
/// </summary>
/// <remarks>
/// The three category rows are the panel's own structure — nothing in the model
/// corresponds to them, which is why they are addressed by kind and carry no id.
/// Only the active scene shows its contents: the others are rows to activate, and
/// activating one rebuilds this presenter with that scene.
/// <para>
/// The constructor fills the view from scratch. That invariant is what lets panel
/// presenters be thrown away and rebuilt when the document — or the active scene —
/// changes, instead of being taught to re-bind.
/// </para>
/// </remarks>
internal sealed class HierarchyPresenter : PresenterBase
{
    private static readonly SelectionTarget Root = new(SelectionKind.Project, Guid.Empty);
    private static readonly SelectionTarget Scenes = new(SelectionKind.ScenesCategory, Guid.Empty);
    private static readonly SelectionTarget Prefabs = new(SelectionKind.PrefabsCategory, Guid.Empty);
    private static readonly SelectionTarget Materials = new(SelectionKind.MaterialsCategory, Guid.Empty);

    private readonly IProject _project;
    private readonly IScene _scene;
    private readonly IHierarchyView _view;
    private readonly EditorSession _session;
    private readonly IDialogService _dialogs;
    private readonly Action<Guid> _activateScene;

    public HierarchyPresenter(
        IProject project,
        IScene scene,
        IHierarchyView view,
        EditorSession session,
        IDialogService dialogs,
        Action<Guid> activateScene)
    {
        _project = project;
        _scene = scene;
        _view = view;
        _session = session;
        _dialogs = dialogs;
        _activateScene = activateScene;

        _view.Clear();
        _view.SetRoot(Root, "Project");
        _view.AddItem(Scenes, Root, "Scenes", 0, ComponentKind.None);
        _view.AddItem(Prefabs, Root, "Prefabs", 1, ComponentKind.None);
        _view.AddItem(Materials, Root, "Materials", 2, ComponentKind.None);

        for (var i = 0; i < project.Scenes.Count; i++)
            AddScene(project.Scenes[i], i);

        for (var i = 0; i < project.Prefabs.Count; i++)
            AddPrefab(project.Prefabs[i], i);

        for (var i = 0; i < project.Materials.Count; i++)
            AddMaterial(project.Materials[i], i);

        PushSelection();

        _project.Changed += OnProjectChanged;
        _scene.Changed += OnSceneChanged;
        _view.SelectionRequested += OnSelectionRequested;
        _view.ItemActivated += OnItemActivated;
        _view.ToolRequested += OnToolRequested;
        _view.ItemDropped += OnItemDropped;
        _view.VisibilityToggled += OnVisibilityToggled;
        _view.DeleteRequested += OnDeleteRequested;
        _session.SelectionChanged += OnSelectionChanged;

        // Questions rather than news, so they are answered rather than subscribed
        // to: the panel asks one before a drag starts, one while a row is in the
        // air, and one as the pointer arrives on a row carrying a bin.
        _view.CanDrag = CanDrag;
        _view.CanDrop = CanDrop;
        _view.CanDelete = CanDelete;
    }

    protected override void OnDispose()
    {
        _project.Changed -= OnProjectChanged;
        _scene.Changed -= OnSceneChanged;
        _view.SelectionRequested -= OnSelectionRequested;
        _view.ItemActivated -= OnItemActivated;
        _view.ToolRequested -= OnToolRequested;
        _view.ItemDropped -= OnItemDropped;
        _view.VisibilityToggled -= OnVisibilityToggled;
        _view.DeleteRequested -= OnDeleteRequested;
        _session.SelectionChanged -= OnSelectionChanged;

        // The view outlives this presenter; a stale answer from a disposed one
        // would be a drop accepted against a scene nobody is looking at, or a bin
        // offered over a document that is no longer open.
        _view.CanDrag = null;
        _view.CanDrop = null;
        _view.CanDelete = null;
    }

    private static SelectionTarget SceneTarget(Guid sceneId) => new(SelectionKind.Scene, sceneId);

    private static SelectionTarget ElementTarget(Guid elementId) =>
        new(SelectionKind.Element, elementId);

    private static SelectionTarget MaterialTarget(Guid materialId) =>
        new(SelectionKind.Material, materialId);

    private static SelectionTarget PrefabTarget(Guid prefabId) =>
        new(SelectionKind.Prefab, prefabId);

    /// <summary>
    /// The row that stands for what is being worked on: a scene's, or — while a
    /// prefab is open for editing — the library entry's. A prefab's contents are a
    /// scene, so everything below this line treats the two the same; only the row
    /// they hang under differs.
    /// </summary>
    private SelectionTarget ContextTarget =>
        _project.TryGetPrefab(_scene.Id, out _) ? PrefabTarget(_scene.Id) : SceneTarget(_scene.Id);

    private void OnProjectChanged(ProjectChange change)
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (!IsAlive)
            return;

        switch (change.Kind)
        {
            case ProjectChangeKind.SceneAdded:
                if (_project.TryGetScene(change.TargetId, out IScene added))
                {
                    AddScene(added, change.Index);
                    _view.ScrollTo(SceneTarget(change.TargetId));
                }

                break;

            case ProjectChangeKind.SceneRemoved:
                _view.RemoveItem(SceneTarget(change.TargetId));
                break;

            case ProjectChangeKind.SceneChanged:
                if (_project.TryGetScene(change.TargetId, out IScene renamed))
                    _view.RenameItem(SceneTarget(renamed.Id), renamed.Name);
                break;

            case ProjectChangeKind.PrefabAdded:
                if (_project.TryGetPrefab(change.TargetId, out IPrefab newPrefab))
                {
                    AddPrefab(newPrefab, change.Index);
                    _view.ScrollTo(PrefabTarget(change.TargetId));
                }

                break;

            case ProjectChangeKind.PrefabRemoved:
                _view.RemoveItem(PrefabTarget(change.TargetId));
                break;

            case ProjectChangeKind.PrefabChanged:
                if (_project.TryGetPrefab(change.TargetId, out IPrefab renamedPrefab))
                    _view.RenameItem(PrefabTarget(renamedPrefab.Id), renamedPrefab.Name);
                break;

            case ProjectChangeKind.MaterialAdded:
                if (_project.TryGetMaterial(change.TargetId, out IMaterial newMaterial))
                {
                    AddMaterial(newMaterial, change.Index);
                    _view.ScrollTo(MaterialTarget(change.TargetId));
                }

                break;

            case ProjectChangeKind.MaterialRemoved:
                _view.RemoveItem(MaterialTarget(change.TargetId));
                break;

            case ProjectChangeKind.MaterialChanged:
                if (_project.TryGetMaterial(change.TargetId, out IMaterial material))
                {
                    _view.RenameItem(MaterialTarget(material.Id), material.Name);
                    _view.SetItemSwatch(MaterialTarget(material.Id), material.Color);
                }

                break;
        }
    }

    private void OnSceneChanged(SceneChange change)
    {
        if (!IsAlive)
            return;

        switch (change.Kind)
        {
            case SceneChangeKind.ElementAdded:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement added))
                {
                    AddSubtree(added, ParentTarget(change.ParentId), change.Index);

                    // A live add, not the constructor's bulk fill, so the newest
                    // row is worth bringing into view.
                    _view.ScrollTo(ElementTarget(change.ElementId));
                }

                break;

            case SceneChangeKind.ElementRemoved:
                _view.RemoveItem(ElementTarget(change.ElementId));
                break;

            case SceneChangeKind.ElementMoved:
                // Godot's tree items cannot be reparented, so the subtree is
                // rebuilt in its new place.
                _view.RemoveItem(ElementTarget(change.ElementId));

                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement moved))
                    AddSubtree(moved, ParentTarget(change.ParentId), change.Index);

                PushSelection();
                break;

            case SceneChangeKind.ElementRenamed:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement element))
                    _view.RenameItem(ElementTarget(element.Id), element.Name);
                break;

            // The whole subtree, not only the row that changed: what is under a
            // hidden element reaches nothing either, and its rows have to recede
            // with it. Their own eyes are untouched by that — the model wrote
            // nothing to them, and neither does this.
            case SceneChangeKind.VisibilityChanged:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement toggled))
                    PushVisibility(toggled, IsAncestryShown(toggled));
                break;
        }
    }

    /// <summary>
    /// A scene shows its contents only while it is the one being worked on; the
    /// rest are rows waiting to be activated, which is also how the panel says
    /// which is which without a second visual language.
    /// </summary>
    private void AddScene(IScene scene, int index)
    {
        SelectionTarget target = SceneTarget(scene.Id);

        _view.AddItem(target, Scenes, scene.Name, index, ComponentKind.None);
        _view.SetItemDeletable(target, true);

        if (scene.Id != _scene.Id)
            return;

        _view.SetItemEmphasized(target, true);

        foreach (IHierarchyElement child in scene.Children)
            AddSubtree(child, target);
    }

    /// <summary>
    /// A library entry, shown on the same terms as a scene: its contents only while
    /// it is the one being worked on, which is what "open the prefab" comes to.
    /// </summary>
    private void AddPrefab(IPrefab prefab, int index)
    {
        SelectionTarget target = PrefabTarget(prefab.Id);

        _view.AddItem(target, Prefabs, prefab.Name, index, ComponentKind.None);
        _view.SetItemDeletable(target, true);

        if (prefab.Id != _scene.Id)
            return;

        _view.SetItemEmphasized(target, true);

        foreach (IHierarchyElement child in prefab.Content.Children)
            AddSubtree(child, target);
    }

    private void AddMaterial(IMaterial material, int index)
    {
        SelectionTarget target = MaterialTarget(material.Id);

        _view.AddItem(target, Materials, material.Name, index, ComponentKind.None);
        _view.SetItemSwatch(target, material.Color);
        _view.SetItemDeletable(target, true);
    }

    private void AddSubtree(IHierarchyElement element, SelectionTarget parent, int index = -1) =>
        AddSubtree(element, parent, index, IsAncestryShown(element));

    /// <summary>
    /// Adds the row and everything under it, dressing each as it goes. The line
    /// above the subtree is walked once by the caller and carried down from there,
    /// which is the same bargain <see cref="PushVisibility"/> strikes.
    /// </summary>
    private void AddSubtree(
        IHierarchyElement element,
        SelectionTarget parent,
        int index,
        bool ancestryShown)
    {
        _view.AddItem(
            ElementTarget(element.Id),
            parent,
            element.Name,
            index < 0 ? element.Parent.Children.Count - 1 : index,
            element.ComponentKind);

        _view.SetItemVisibility(
            ElementTarget(element.Id), Resolve(element.Visible, ancestryShown));

        // An element row carries a bin — except a prefab instance's contents, which
        // stand for nothing that could go: they are derived from the source, and
        // what would remove them is deleting the instance or unpacking it. That
        // absence is also how a derived row is told from an ordinary one, the panel
        // having no colour left to say it with (see the tech-debt ledger).
        _view.SetItemDeletable(ElementTarget(element.Id), element.PrefabInstanceId == null);

        bool childrenShown = ancestryShown && element.Visible;

        foreach (IHierarchyElement child in element.Children)
            AddSubtree(child, ElementTarget(element.Id), -1, childrenShown);
    }

    /// <summary>
    /// Re-dresses the row and every row under it. Both halves of what a row says
    /// about visibility need this: the eye is the element's own flag, but whether
    /// the row has receded depends on the whole line above it, so a subtree is the
    /// unit — hiding a group re-dresses everything it holds.
    /// </summary>
    /// <remarks>
    /// <paramref name="ancestryShown"/> is carried down rather than re-derived per
    /// row, which is what keeps a walk over the tree linear instead of quadratic
    /// in its depth. Only element rows are dressed at all: a scene, a material and
    /// the three category rows are not things that can be hidden.
    /// </remarks>
    private void PushVisibility(IHierarchyElement element, bool ancestryShown)
    {
        _view.SetItemVisibility(
            ElementTarget(element.Id), Resolve(element.Visible, ancestryShown));

        bool childrenShown = ancestryShown && element.Visible;

        foreach (IHierarchyElement child in element.Children)
            PushVisibility(child, childrenShown);
    }

    /// <summary>
    /// The element's own flag first: a hidden thing says so with its own eye even
    /// when it also stands under something hidden, because that flag is the one
    /// its eye can change.
    /// </summary>
    private static RowVisibility Resolve(bool visible, bool ancestryShown) =>
        !visible ? RowVisibility.Hidden
        : ancestryShown ? RowVisibility.Shown
        : RowVisibility.HiddenByAncestor;

    /// <summary>
    /// Whether every element above this one is shown. Walked rather than carried
    /// where a subtree is entered from the outside — a fresh row, a reparent, an
    /// element whose flag just changed.
    /// </summary>
    private static bool IsAncestryShown(IHierarchyElement element)
    {
        for (IHierarchyContainer current = element.Parent;
            current is IHierarchyElement ancestor;
            current = ancestor.Parent)
        {
            if (!ancestor.Visible)
                return false;
        }

        return true;
    }

    /// <summary>The scene is a container like any other, and its row is the tree's.</summary>
    private SelectionTarget ParentTarget(Guid parentId) =>
        parentId == _scene.Id ? ContextTarget : ElementTarget(parentId);

    /// <summary>
    /// Rows were picked out. The panel says which and what it was asking for; what
    /// the selection becomes goes through <see cref="SelectionEdit"/>, the same
    /// function the viewport's own clicks go through — so a modifier cannot come to
    /// mean one thing in the list and another in the scene.
    /// </summary>
    /// <remarks>
    /// The homogeneity rule bites here rather than in the viewport, because only
    /// here can a gesture cross kinds: a Shift range dragged from a scene row down
    /// past the materials names both, and the row actually clicked is what says
    /// which kind was meant. <see cref="SelectionEdit"/> keeps that one and drops
    /// the rest.
    /// </remarks>
    private void OnSelectionRequested(HierarchySelection selection)
    {
        // Reported a frame later, so this presenter may have been rebuilt since —
        // by a scene activation the previous click asked for, among other things.
        if (!IsAlive)
            return;

        _session.SetSelection(SelectionEdit.Apply(
            _session.Selection, selection.Targets, selection.Active, selection.Intent));

        // The session may have made something else of it — a kind dropped, a
        // toggle handing the active role on — and the rows have to say what the
        // session holds rather than what the click asked for. A no-op when the two
        // already agree.
        PushSelection();
    }

    /// <summary>
    /// Activating is one gesture across the whole tree: it makes the row the
    /// current one of its kind. A scene becomes the scene being worked on; a
    /// material becomes what the brush paints with.
    /// </summary>
    private void OnItemActivated(SelectionTarget target)
    {
        switch (target.Kind)
        {
            case SelectionKind.Scene:
                _activateScene(target.Id);
                break;

            // Opening a prefab is the same gesture as opening a scene, and it is
            // the same call: a library entry's id names its contents, which become
            // the context every scene-bound presenter is rebuilt around.
            case SelectionKind.Prefab:
                _activateScene(target.Id);
                break;

            case SelectionKind.Material:
                _session.CurrentMaterialId = target.Id;
                break;
        }
    }

    /// <summary>
    /// A tool key pressed on the panel means the same thing it means in the
    /// viewport, and it goes to the same place — with one condition the viewport
    /// does not have: the tool must have something to act on, so a scene, a
    /// material or a category row leaves the key meaning nothing, and so does a
    /// group under a tool that aims at cells. Sending the keyboard away from the
    /// panel a user is browsing is worse than a key that does not answer.
    /// </summary>
    /// <remarks>
    /// The same question <see cref="ToolFallbackPresenter"/> asks, through the same
    /// <see cref="ToolSubjects"/>, and deliberately: the fallback would put a tool
    /// asked for here straight back to the cursor anyway, but not before the
    /// keyboard had already left this panel. What is refused here is the keyboard,
    /// not the tool.
    /// <para>
    /// The cursor is the exception, and it is the exception <em>because</em> of that
    /// rule rather than despite it: it takes hold of nothing, so there is nothing
    /// for it to need selected, and what it is for is finding an element rather than
    /// acting on one. Leaving whatever mode you are in must also work from a row
    /// that no other tool would answer on.
    /// </para>
    /// </remarks>
    private void OnToolRequested(ToolKind tool)
    {
        if (!IsAlive
            || !ToolSubjects.CanAct(tool, ToolSubjects.FindSubject(_scene, _session.ActiveTarget)))
        {
            return;
        }

        _session.RequestTool(tool);
    }

    /// <summary>
    /// Whether a row may be picked up at all: an element of this scene, and not
    /// a prefab instance's contents, which are read-only wherever they appear.
    /// The tree also holds scenes, materials and three category rows that
    /// correspond to nothing in the document — none of them travels.
    /// </summary>
    /// <remarks>
    /// A prefab row travels too, and means something different at the other end:
    /// dropping an element moves it, dropping a library entry places an instance
    /// of it. Both are "put this here", which is why one gesture carries both.
    /// </remarks>
    private bool CanDrag(SelectionTarget target)
    {
        if (!IsAlive)
            return false;

        if (target.Kind == SelectionKind.Prefab)
            return _project.TryGetPrefab(target.Id, out _);

        return target.Kind == SelectionKind.Element
            && _scene.TryGetElement(target.Id, out IHierarchyElement element)
            && element.PrefabInstanceId == null;
    }

    /// <summary>
    /// Whether the row's bin would do anything. Asked as the pointer arrives, so
    /// the answer is drawn rather than discovered by pressing it — and asked again
    /// when the press comes back, because a frame has passed since.
    /// </summary>
    /// <remarks>
    /// Three rules, and only two of them are the model's. An element goes unless
    /// it is prefab content, which is read-only wherever it appears; a material
    /// goes unless the project would be left with none or a voxel still wears it,
    /// which is <see cref="IProject.IsMaterialInUse"/> — the model's own refusal,
    /// asked rather than restated. **The scene rule is this panel's alone**: the
    /// document is perfectly able to delete the scene being worked on, and
    /// <c>delete-scene</c> still does, but a bin that pulls the room out from
    /// under the user is not a thing to offer in one click. That also subsumes
    /// the model's "a project keeps at least one scene": the last scene is always
    /// the active one.
    /// </remarks>
    private bool CanDelete(SelectionTarget target)
    {
        if (!IsAlive)
            return false;

        return target.Kind switch
        {
            SelectionKind.Element =>
                _scene.TryGetElement(target.Id, out IHierarchyElement element)
                && element.PrefabInstanceId == null,

            SelectionKind.Scene =>
                target.Id != _scene.Id && _project.TryGetScene(target.Id, out _),

            // Always live, unlike a material in use: deleting a prefab unpacks its
            // instances rather than being refused by them, so there is nothing to
            // grey out. What it costs is asked in a dialog instead.
            SelectionKind.Prefab => _project.TryGetPrefab(target.Id, out _),

            SelectionKind.Material =>
                _project.Materials.Count > 1
                && _project.TryGetMaterial(target.Id, out _)
                && !_project.IsMaterialInUse(target.Id),

            _ => false
        };
    }

    /// <summary>
    /// The bin was pressed. It acts at once and asks nothing — undo is the answer
    /// to a misclick, and a dialog in front of every deletion would cost more than
    /// the mistake does.
    /// </summary>
    /// <remarks>
    /// Nothing is caught here. <see cref="CanDelete"/> is exact rather than
    /// approximate — it asks the model the same questions the model would refuse
    /// on — so a deletion that got this far is one the document accepts, and
    /// swallowing an exception would hide the day that stops being true.
    /// </remarks>
    private void OnDeleteRequested(SelectionTarget target)
    {
        // Reported a frame later, so the row may name something already gone.
        if (!CanDelete(target))
            return;

        _session.EndGesture();

        switch (target.Kind)
        {
            case SelectionKind.Element:
                _scene.DeleteElement(target.Id);
                break;

            case SelectionKind.Scene:
                _project.DeleteScene(target.Id);
                break;

            case SelectionKind.Material:
                _project.DeleteMaterial(target.Id);
                break;

            case SelectionKind.Prefab:
                _ = DeletePrefabAsync(target.Id);
                break;
        }
    }

    /// <summary>
    /// The one deletion in this panel that asks first, and the reason is not
    /// caution about the prefab: it is that the press reaches every scene of the
    /// project at once, unpacking instances the user cannot see from where they are
    /// standing. Undo covers it, but "undo what exactly" is a worse question than
    /// one dialog.
    /// </summary>
    private async Task DeletePrefabAsync(Guid prefabId)
    {
        if (!_project.TryGetPrefab(prefabId, out IPrefab prefab))
            return;

        var message = _project.IsPrefabInUse(prefabId)
            ? $"Delete the prefab '{prefab.Name}'?\n"
                + "Every instance of it, in every scene, becomes ordinary elements."
            : $"Delete the prefab '{prefab.Name}'?";

        ConfirmResult answer = await _dialogs.AskConfirmAsync(
            new ConfirmRequest("Delete prefab", message, "Delete", string.Empty, "Cancel"));

        // A dialog outlives the panel that raised it: the document may have been
        // closed, the scene switched, or the prefab deleted by an agent meanwhile.
        if (!IsAlive
            || answer != ConfirmResult.Confirm
            || !_project.TryGetPrefab(prefabId, out _))
        {
            return;
        }

        _session.EndGesture();
        _project.DeletePrefab(prefabId);
    }

    /// <summary>
    /// Whether a dragged row may land where the pointer is: something that may be
    /// picked up, over a place an element can live, without folding a subtree
    /// into itself.
    /// </summary>
    /// <remarks>
    /// The cycle rule is checked here as well as in the model, and not
    /// redundantly: the model refuses by throwing, which is right for a
    /// programming error and useless for a drag — a drop indicator has to say no
    /// before the button comes up, not after.
    /// </remarks>
    private bool CanDrop(HierarchyDrop drop)
    {
        if (!CanDrag(drop.Target) || !TryResolveParent(drop.Parent, out IHierarchyContainer parent))
            return false;

        // Whether a prefab may stand here is the model's own rule, asked rather
        // than restated: the model refuses by throwing, which is right for a
        // programming error and useless for a drag, where the answer has to be
        // drawn before the button comes up.
        if (drop.Target.Kind == SelectionKind.Prefab)
            return _project.CanContainPrefab(_scene.Id, drop.Target.Id);

        return _scene.TryGetElement(drop.Target.Id, out IHierarchyElement moving)
            && !IsSelfOrDescendant(parent, moving);
    }

    /// <summary>
    /// The container a row stands for: an element of this scene, or the scene's own
    /// row, which is the tree's way of saying "top level". Anything else — another
    /// scene, a material, a category — is not a place an element can live.
    /// </summary>
    private bool TryResolveParent(SelectionTarget target, out IHierarchyContainer parent)
    {
        parent = _scene;

        // The row that stands for the top level is the scene's — or the prefab's,
        // while one is open. Either way it is the same container.
        if (target.Kind == SelectionKind.Scene || target.Kind == SelectionKind.Prefab)
            return target.Id == _scene.Id;

        if (target.Kind != SelectionKind.Element
            || !_scene.TryGetElement(target.Id, out IHierarchyElement element)
            || element.PrefabInstanceId != null)
        {
            return false;
        }

        parent = element;

        return true;
    }

    /// <summary>
    /// Whether the prospective parent is the element itself or something under it —
    /// the drop that would make a subtree its own ancestor.
    /// </summary>
    private static bool IsSelfOrDescendant(IHierarchyContainer parent, IHierarchyElement moving)
    {
        IHierarchyContainer? walk = parent;

        while (walk != null)
        {
            if (walk.Id == moving.Id)
                return true;

            walk = walk is IHierarchyElement element ? element.Parent : null;
        }

        return false;
    }

    /// <summary>
    /// A row was dropped. World position is the model's promise, not this one's:
    /// <see cref="IScene.ReparentElement"/> keeps the element where it stands and
    /// degrades a field mode rather than moving the geometry, which is exactly the
    /// Unity-shaped behaviour a drag in a hierarchy is expected to have.
    /// </summary>
    private void OnItemDropped(HierarchyDrop drop)
    {
        // Reported a frame later, so the document may have moved on since — and a
        // dropped row is a new action like any other, which ends whatever gesture
        // was in flight first.
        if (!CanDrop(drop) || !TryResolveParent(drop.Parent, out IHierarchyContainer parent))
            return;

        if (drop.Target.Kind == SelectionKind.Prefab)
        {
            _session.EndGesture();
            _session.SetSelection(new SelectionTarget(
                SelectionKind.Element, _scene.CreateInstance(parent.Id, drop.Target.Id)));

            return;
        }

        if (!_scene.TryGetElement(drop.Target.Id, out IHierarchyElement moving))
            return;

        _session.EndGesture();

        _scene.ReparentElement(
            drop.Target.Id,
            parent.Id,
            ReparentIndexHelper.Calculate(
                drop.Index,
                sameParent: moving.Parent.Id == parent.Id,
                currentIndex: IndexOf(parent, moving)));
    }

    private static int IndexOf(IHierarchyContainer parent, IHierarchyElement element)
    {
        for (var index = 0; index < parent.Children.Count; index++)
        {
            if (parent.Children[index].Id == element.Id)
                return index;
        }

        return -1;
    }

    /// <summary>
    /// The eye was clicked. The panel reports which row it happened on and nothing
    /// else, so the new state is read off the document here — the row's own picture
    /// could be a frame behind, and the model is never a frame behind itself.
    /// </summary>
    /// <remarks>
    /// Reported a frame later like every row event, so the element may be gone by
    /// now; and hiding something is a new action, which ends whatever gesture was
    /// in flight first.
    /// </remarks>
    private void OnVisibilityToggled(SelectionTarget target)
    {
        if (!IsAlive
            || target.Kind != SelectionKind.Element
            || !_scene.TryGetElement(target.Id, out IHierarchyElement element)
            // Derived content has no flag of its own to change: what it shows is
            // the source's. The eye is still drawn on the row so that a hidden
            // instance's contents recede with it, and this is what keeps pressing
            // it from reaching a model that would refuse.
            || element.PrefabInstanceId != null)
        {
            return;
        }

        _session.EndGesture();

        _scene.SetVisible(target.Id, !element.Visible);
    }

    /// <summary>
    /// The rows follow the selection, and the panel follows the active one: a
    /// model picked in the viewport, or one an undo brought back, may name a row
    /// scrolled out of sight, and a panel that says nothing about what was just
    /// picked is a panel the user has to go looking in.
    /// </summary>
    /// <remarks>
    /// Here rather than in <see cref="PushSelection"/>, because the two are
    /// different questions: the rows are re-pushed whenever they may have gone
    /// stale — a reparent rebuilding a subtree, the constructor's bulk fill — and
    /// none of those is somebody choosing something. Scrolling on a bulk fill would
    /// also break the rule that a freshly opened project shows its top.
    /// <para>
    /// A no-op when the row is already on screen, which is the common case of a
    /// click on the row itself, and when nothing is selected at all.
    /// </para>
    /// </remarks>
    private void OnSelectionChanged()
    {
        PushSelection();

        if (_session.ActiveTarget.Kind != SelectionKind.None)
            _view.ScrollTo(_session.ActiveTarget);
    }

    private void PushSelection() =>
        _view.SetSelection(_session.Selection.Targets, _session.ActiveTarget);
}
