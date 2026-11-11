using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>Structure of the hierarchy and the integrity rules the scene enforces.</summary>
public sealed class HierarchyTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public HierarchyTests()
    {
        _scene = _project.Scenes[0];
    }

    [Fact]
    public void NewSceneIsEmpty()
    {
        Assert.Empty(_scene.Children);
    }

    [Fact]
    public void CreatedElementIsAppendedToItsParent()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        Guid second = _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");

        Assert.Equal(new[] { first, second }, new[] { _scene.Children[0].Id, _scene.Children[1].Id });
    }

    [Fact]
    public void TopLevelElementParentsToTheScene()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        Assert.Same(_scene, _scene.Element(element).Parent);
    }

    [Fact]
    public void NestedElementParentsToItsElement()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.None, "Child");

        Assert.Equal(parent, _scene.Element(child).Parent.Id);
        Assert.Single(_scene.Element(parent).Children);
    }

    [Fact]
    public void CreatingUnderAnUnknownParentIsRefused()
    {
        Assert.Throws<ArgumentException>(() => _scene.CreateElement(Guid.NewGuid(), ComponentKind.None, "Orphan"));
    }

    [Fact]
    public void RenameChangesTheName()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Before");
        _scene.RenameElement(element, "After");

        Assert.Equal("After", _scene.Element(element).Name);
    }

    [Fact]
    public void DuplicateNamesAreAllowedBecauseIdentityIsTheGuid()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "Same");
        Guid second = _scene.CreateElement(_scene.Id, ComponentKind.None, "Same");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeleteDetachesFromTheParent()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        _scene.DeleteElement(element);

        Assert.Empty(_scene.Children);
    }

    [Fact]
    public void DeleteUnregistersTheWholeSubtree()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.None, "Child");
        Guid grandchild = _scene.CreateElement(child, ComponentKind.None, "Grandchild");

        _scene.DeleteElement(parent);

        Assert.False(_scene.TryGetElement(child, out _));
        Assert.False(_scene.TryGetElement(grandchild, out _));
    }

    [Fact]
    public void OperationsOnAnUnknownElementAreRefused()
    {
        Assert.Throws<ArgumentException>(() => _scene.RenameElement(Guid.NewGuid(), "Ghost"));
    }

    [Fact]
    public void ReparentMovesTheElementToTheGivenIndex()
    {
        Guid target = _scene.CreateElement(_scene.Id, ComponentKind.None, "Target");
        Guid first = _scene.CreateElement(target, ComponentKind.None, "First");
        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");

        _scene.ReparentElement(moving, target, 0);

        Assert.Equal(
            new[] { moving, first },
            new[] { _scene.Element(target).Children[0].Id, _scene.Element(target).Children[1].Id });
        Assert.Single(_scene.Children);
    }

    [Fact]
    public void ReparentUnderSelfIsRefused()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");

        Assert.Throws<InvalidOperationException>(() =>
            _scene.ReparentElement(element, element, 0));
    }

    [Fact]
    public void ReparentUnderOwnDescendantIsRefused()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.None, "Child");
        Guid grandchild = _scene.CreateElement(child, ComponentKind.None, "Grandchild");

        Assert.Throws<InvalidOperationException>(() =>
            _scene.ReparentElement(parent, grandchild, 0));
    }

    [Fact]
    public void ReparentBackToTheSceneIsAllowed()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.None, "Child");

        _scene.ReparentElement(child, _scene.Id, 0);

        Assert.Equal(_scene.Id, _scene.Element(child).Parent.Id);
        Assert.Empty(_scene.Element(parent).Children);
    }

    /// <summary>
    /// The index is clamped like every other out-of-range request the model is
    /// handed. It used to throw out through <c>reparent-element</c> at whoever
    /// typed a number past the end.
    /// </summary>
    [Fact]
    public void ReparentPastTheEndLandsAtTheEnd()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        Guid second = _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");
        Guid target = _scene.CreateElement(_scene.Id, ComponentKind.None, "Target");

        _scene.ReparentElement(first, target, 99);
        _scene.ReparentElement(second, target, 99);

        Assert.Equal(
            new[] { first, second },
            new[] { _scene.Element(target).Children[0].Id, _scene.Element(target).Children[1].Id });
    }

    [Fact]
    public void ReparentBeforeTheStartLandsAtTheStart()
    {
        Guid target = _scene.CreateElement(_scene.Id, ComponentKind.None, "Target");
        Guid first = _scene.CreateElement(target, ComponentKind.None, "First");
        Guid moving = _scene.CreateElement(_scene.Id, ComponentKind.None, "Moving");

        _scene.ReparentElement(moving, target, -5);

        Assert.Equal(
            new[] { moving, first },
            new[] { _scene.Element(target).Children[0].Id, _scene.Element(target).Children[1].Id });
    }

    /// <summary>
    /// A move that lands exactly where the element already stands records
    /// nothing — the drag that wanders and comes home must not leave a record
    /// whose undo does nothing visible.
    /// </summary>
    [Fact]
    public void ReparentToTheSamePlaceRecordsNothing()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");
        int recordsBefore = _project.History.Records.Count;

        _scene.ReparentElement(first, _scene.Id, 0);

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    /// <summary>An index past the end clamps to the place the element already holds.</summary>
    [Fact]
    public void ReparentClampedToTheSamePlaceRecordsNothing()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        Guid last = _scene.CreateElement(_scene.Id, ComponentKind.None, "Last");
        int recordsBefore = _project.History.Records.Count;

        _scene.ReparentElement(last, _scene.Id, 99);

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    /// <summary>
    /// The index names a place in the list the element ends up in, so moving one
    /// down inside its own parent counts from the list it has already left.
    /// </summary>
    [Fact]
    public void ReorderingInsideOneParentCountsAfterTheMove()
    {
        Guid first = _scene.CreateElement(_scene.Id, ComponentKind.None, "First");
        Guid second = _scene.CreateElement(_scene.Id, ComponentKind.None, "Second");
        Guid third = _scene.CreateElement(_scene.Id, ComponentKind.None, "Third");

        _scene.ReparentElement(first, _scene.Id, 2);

        Assert.Equal(
            new[] { second, third, first },
            new[]
            {
                _scene.Children[0].Id,
                _scene.Children[1].Id,
                _scene.Children[2].Id
            });
    }
}
