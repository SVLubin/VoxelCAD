using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What each tool needs to have anything to act on. One answer, asked by the
/// fallback that watches the selection and by the panel keys that decide whether
/// a tool may be asked for at all.
/// </summary>
public sealed class ToolSubjectsTests
{
    /// <summary>
    /// The guard the whole class exists for: every tool must have an arm in the
    /// switch, so a tool added tomorrow fails here instead of silently inheriting
    /// somebody else's answer. It is the lesson of a real bug — a list that named
    /// what it refused rotted as the enum grew.
    /// </summary>
    [Fact]
    public void EveryToolAnswersForEverySubject()
    {
        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
        {
            foreach (ToolSubject subject in Enum.GetValues<ToolSubject>())
            {
                bool canAct = ToolSubjects.CanAct(tool, subject);

                Assert.Equal(canAct ? tool : ToolKind.Select, ToolSubjects.Resolve(tool, subject));
            }
        }
    }

    /// <summary>
    /// The cursor is the way out of every other tool, so it must never be the one
    /// that falls away — including from a selection that offers nothing at all.
    /// </summary>
    [Fact]
    public void TheCursorActsOnAnything()
    {
        foreach (ToolSubject subject in Enum.GetValues<ToolSubject>())
            Assert.True(ToolSubjects.CanAct(ToolKind.Select, subject));
    }

    /// <summary>
    /// The handles want an element, but the tool does not need one to be worth
    /// being in: a click that misses the handles picks what is under it, so
    /// reaching for Move before choosing a part is how one is chosen.
    /// </summary>
    [Theory]
    [InlineData(ToolKind.Move)]
    [InlineData(ToolKind.Rotate)]
    [InlineData(ToolKind.Scale)]
    public void ATransformToolActsOnAnythingBecauseItSelects(ToolKind tool)
    {
        foreach (ToolSubject subject in Enum.GetValues<ToolSubject>())
            Assert.True(ToolSubjects.CanAct(tool, subject));
    }

    /// <summary>A group has a transform and no cells; the cell tools aim at cells.</summary>
    [Theory]
    [InlineData(ToolKind.Paint)]
    [InlineData(ToolKind.Eyedropper)]
    [InlineData(ToolKind.Extrude)]
    [InlineData(ToolKind.Fill)]
    public void ACellToolWantsAVoxelModel(ToolKind tool)
    {
        Assert.False(ToolSubjects.CanAct(tool, ToolSubject.Nothing));
        Assert.False(ToolSubjects.CanAct(tool, ToolSubject.Element));
        Assert.True(ToolSubjects.CanAct(tool, ToolSubject.VoxelModel));
    }

    /// <summary>
    /// A prefab instance's contents hold cells and are still nothing a tool can
    /// act on: every write to one is refused by the model, so a tool held over it
    /// would throw out of its own handler on every click. The fallback puts the
    /// editor in the cursor instead, which is what the row is for — selectable
    /// and inspectable, not editable.
    /// </summary>
    [Fact]
    public void PrefabInstanceContentIsNothingToActOn()
    {
        IProject project = ModelFactory.CreateProject();
        IScene scene = project.Scenes[0];
        Guid model = scene.CreateElement(scene.Id, ComponentKind.VoxelModel, "Wheel");

        scene.SetVoxels(model, new[]
        {
            new VoxelEdit(Godot.Vector3I.Zero, project.Materials[0].Id)
        });

        scene.CreatePrefab(model);

        // A transfer, not a copy: an instance stands where the element did.
        IHierarchyElement instance = scene.Children[0];

        Assert.Equal(ComponentKind.PrefabInstance, instance.ComponentKind);

        var content = new SelectionTarget(SelectionKind.Element, instance.Children[0].Id);

        Assert.Equal(ToolSubject.Nothing, ToolSubjects.FindSubject(scene, content));
        Assert.Equal(ToolKind.Select, ToolSubjects.Resolve(ToolKind.Paint, ToolSubject.Nothing));
    }

    [Fact]
    public void WhatCannotActResolvesToTheCursor()
    {
        Assert.Equal(ToolKind.Select, ToolSubjects.Resolve(ToolKind.Paint, ToolSubject.Nothing));
        Assert.Equal(ToolKind.Select, ToolSubjects.Resolve(ToolKind.Extrude, ToolSubject.Element));
    }

    [Fact]
    public void WhatCanActIsLeftAlone()
    {
        Assert.Equal(ToolKind.Move, ToolSubjects.Resolve(ToolKind.Move, ToolSubject.Nothing));
        Assert.Equal(ToolKind.Paint, ToolSubjects.Resolve(ToolKind.Paint, ToolSubject.VoxelModel));
    }
}
