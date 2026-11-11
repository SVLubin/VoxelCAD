using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The pivot is the point the position channel names: the element's frame is
/// <c>T(pos) · R · S · T(−pivot)</c>, so the number in the inspector, the point
/// the handles sit on and the point a rotation turns about are one and the same.
/// It lives on the half-cell lattice, starts at the middle of what the first
/// voxel edit filled, and moves only when told to — never by itself.
/// </summary>
public sealed class PivotTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public PivotTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    private IHistory History => _project.History;

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void ANewElementPivotsOnItsOriginAndPositionReadsZero()
    {
        Assert.Equal(Vector3.Zero, Element().PivotPoint);
        Assert.Equal(Vector3.Zero, Element().Transform.Position);
    }

    /// <summary>
    /// The first voxel edit gives the pivot its default: the middle of what that
    /// edit filled. Cells 2..5 span [2, 6) and are centred on 4; a single-cell
    /// first edit is centred on the cell's own middle, a half.
    /// </summary>
    [Fact]
    public void TheFirstEditCentresThePivotOnWhatItFilled()
    {
        Fill(new Vector3I(2, 0, 0), new Vector3I(5, 0, 0));

        Assert.Equal(new Vector3(4f, 0.5f, 0.5f), Element().PivotPoint);
        Assert.Equal(new Vector3(4f, 0.5f, 0.5f), Element().Transform.Position);
    }

    /// <summary>
    /// Painting more does not chase the middle: the default is a one-time gift of
    /// the first edit, not a rule that keeps recomputing. A pivot that followed
    /// the cells could never be the point the position channel names.
    /// </summary>
    [Fact]
    public void LaterEditsLeaveThePivotWhereItStands()
    {
        Fill(new Vector3I(0, 0, 0), new Vector3I(1, 0, 0));
        Vector3 pivot = Element().PivotPoint;

        Fill(new Vector3I(20, 20, 20), new Vector3I(24, 24, 24));

        Assert.Equal(pivot, Element().PivotPoint);
    }

    /// <summary>
    /// The default is part of what the first edit did, so undoing that edit takes
    /// it back — a pivot pointing at cells that are no longer there would be a
    /// leftover, not a state.
    /// </summary>
    [Fact]
    public void UndoingTheFirstEditTakesTheDefaultBack()
    {
        Fill(new Vector3I(2, 0, 0), new Vector3I(5, 0, 0));

        History.Undo();

        Assert.Equal(Vector3.Zero, Element().PivotPoint);

        History.Redo();

        Assert.Equal(new Vector3(4f, 0.5f, 0.5f), Element().PivotPoint);
    }

    /// <summary>
    /// An explicitly set pivot — even one set to zero — is a decision, and the
    /// first edit's default must not overwrite it.
    /// </summary>
    [Fact]
    public void TheDefaultDoesNotOverwriteAnExplicitPivot()
    {
        _scene.SetPivot(_element, Vector3.Zero);

        Fill(new Vector3I(2, 0, 0), new Vector3I(5, 0, 0));

        Assert.Equal(Vector3.Zero, Element().PivotPoint);
    }

    [Fact]
    public void ThePointIsRoundedToTheHalfCellLattice()
    {
        _scene.SetPivot(_element, new Vector3(3.3f, -0.6f, 1.75f));

        Assert.Equal(new Vector3(3.5f, -0.5f, 2f), Element().PivotPoint);
    }

    /// <summary>
    /// Moving the pivot moves no geometry — the local origin is held — but the
    /// position channel names the pivot, so the reported position follows it.
    /// </summary>
    [Fact]
    public void MovingThePivotMovesTheNumberAndNotTheGeometry()
    {
        Fill(new Vector3I(0, 0, 0), new Vector3I(3, 3, 3));

        Transform3D before = Element().WorldTransform;

        SceneChange? observed = null;
        _scene.Changed += change => observed = change;

        _scene.SetPivot(_element, new Vector3(4f, 0f, 0f));

        Assert.Equal(SceneChangeKind.PivotChanged, observed?.Kind);
        Assert.Equal(before, Element().WorldTransform);
        Assert.Equal(new Vector3(4f, 0f, 0f), Element().Transform.Position);
    }

    [Fact]
    public void SettingThePivotRecordsOneUndoableAction()
    {
        int recordsBefore = History.Records.Count;

        _scene.SetPivot(_element, new Vector3(3f, 0f, 0f));

        Assert.Equal(recordsBefore + 1, History.Records.Count);
        Assert.Equal(ActionKind.ChangePivot, History.Records[^1].Kind);

        History.Undo();
        Assert.Equal(Vector3.Zero, Element().PivotPoint);

        History.Redo();
        Assert.Equal(new Vector3(3f, 0f, 0f), Element().PivotPoint);
    }

    [Fact]
    public void SettingTheCurrentValueRecordsNothing()
    {
        _scene.SetPivot(_element, new Vector3(3f, 0f, 0f));
        int recordsBefore = History.Records.Count;

        // Rounded to the same lattice point, so nothing changed even though the
        // argument differs.
        _scene.SetPivot(_element, new Vector3(3.1f, 0.2f, -0.2f));

        Assert.Equal(recordsBefore, History.Records.Count);
    }

    // --- position names the pivot ---

    /// <summary>
    /// The heart of the design. With the pivot at the middle of a 4-cube, setting
    /// the position to (10, 10, 10) puts the <em>middle</em> there — the cells
    /// land around it, not cornered on it.
    /// </summary>
    [Fact]
    public void PositionPlacesThePivotNotTheOrigin()
    {
        Fill(new Vector3I(0, 0, 0), new Vector3I(3, 3, 3));

        Assert.Equal(new Vector3(2f, 2f, 2f), Element().PivotPoint);

        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10f, 10f, 10f));

        Assert.Equal(new Vector3(10f, 10f, 10f), Element().Transform.Position);

        // The pivot's world location is what the number names.
        Assert.Equal(
            new Vector3(10f, 10f, 10f),
            Element().WorldTransform * Element().PivotPoint);
    }

    /// <summary>
    /// A rotation turns about the pivot and leaves the position exactly where it
    /// was — one record, no compensation for anyone to write. The pivot here is
    /// on whole cells, so the held position stays on the Integer lattice.
    /// </summary>
    [Fact]
    public void ARotationHoldsThePosition()
    {
        Fill(new Vector3I(0, 0, 0), new Vector3I(3, 3, 3));
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10f, 10f, 10f));

        int recordsBefore = History.Records.Count;

        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        Assert.Equal(recordsBefore + 1, History.Records.Count);
        Assert.Equal(new Vector3(0f, 90f, 0f), Element().Transform.RotationDegrees);
        Assert.Equal(new Vector3(10f, 10f, 10f), Element().Transform.Position);
        Assert.True(
            (Element().WorldTransform * Element().PivotPoint)
                .IsEqualApprox(new Vector3(10f, 10f, 10f)));
    }

    [Fact]
    public void AScaleGrowsAboutThePivotAndHoldsThePosition()
    {
        Fill(new Vector3I(0, 0, 0), new Vector3I(3, 3, 3));
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10f, 10f, 10f));

        _scene.SetTransform(_element, TransformChannel.Scale, new Vector3(2f, 2f, 2f));

        Assert.Equal(new Vector3(2f, 2f, 2f), Element().Transform.Scale);
        Assert.Equal(new Vector3(10f, 10f, 10f), Element().Transform.Position);
        Assert.True(
            (Element().WorldTransform * Element().PivotPoint)
                .IsEqualApprox(new Vector3(10f, 10f, 10f)));
    }

    [Fact]
    public void UndoRestoresBothHalvesOfARotation()
    {
        Fill(new Vector3I(0, 0, 0), new Vector3I(3, 3, 3));
        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(10f, 10f, 10f));

        Transform3D before = Element().WorldTransform;

        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));
        History.Undo();

        Assert.Equal(Vector3.Zero, Element().Transform.RotationDegrees);
        Assert.True(Element().WorldTransform.IsEqualApprox(before));
    }

    // --- anchoring never moves the pivot ---

    /// <summary>
    /// The bug that forced the rule: a pivot whose axes mix whole and half cells
    /// (the centre of a 3×4×3 model) puts the re-anchored origin off the Integer
    /// lattice on a pitch, and rounding it moved the pivot — worse, the next turn
    /// anchored to the moved pivot, so three quarter-turns left the model a full
    /// cell away at its starting rotation. The rule now: the origin is never
    /// rounded in anchoring; the mode degrades to Float instead, and the number
    /// does not move at all.
    /// </summary>
    [Fact]
    public void ARoundTripOfPitchesComesBackExactly()
    {
        Fill(new Vector3I(15, 0, 15), new Vector3I(17, 3, 17));

        Assert.Equal(new Vector3(16.5f, 2f, 16.5f), Element().Transform.Position);

        foreach (float degrees in new[] { 90f, 180f, 0f })
        {
            _scene.SetTransform(
                _element, TransformChannel.Rotation, new Vector3(degrees, 0f, 0f));

            Assert.Equal(new Vector3(16.5f, 2f, 16.5f), Element().Transform.Position);
        }

        Assert.Equal(Vector3.Zero, Element().Transform.RotationDegrees);
    }

    /// <summary>
    /// The degradation itself: mixed parity pitched off the lattice flips the
    /// position mode to Float — the reparent rule — and the geometry stands.
    /// A parity-clean yaw about the same pivot keeps Integer.
    /// </summary>
    [Fact]
    public void MixedParityDegradesThePositionModeInsteadOfMovingAnything()
    {
        Fill(new Vector3I(15, 0, 15), new Vector3I(17, 3, 17));

        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        // Yaw turns the two half axes into each other: still on the lattice.
        Assert.Equal(FieldMode.Integer, Element().TransformModes.Position);

        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(90f, 0f, 0f));

        Assert.Equal(FieldMode.Float, Element().TransformModes.Position);
        Assert.Equal(new Vector3(16.5f, 2f, 16.5f), Element().Transform.Position);
    }

    [Fact]
    public void UndoRestoresTheDegradedModeToo()
    {
        Fill(new Vector3I(15, 0, 15), new Vector3I(17, 3, 17));

        _scene.SetTransform(_element, TransformChannel.Rotation, new Vector3(90f, 0f, 0f));
        Assert.Equal(FieldMode.Float, Element().TransformModes.Position);

        History.Undo();

        Assert.Equal(FieldMode.Integer, Element().TransformModes.Position);
        Assert.Equal(Vector3.Zero, Element().Transform.RotationDegrees);
    }

    /// <summary>
    /// With the pivot on a half cell — the default of a single-voxel first edit —
    /// an Integer position lands on the lattice shifted by that half: the cells
    /// must stay on the world grid, and the grid is what Integer means.
    /// </summary>
    [Fact]
    public void AnIntegerPositionLandsOnTheLatticeThePivotShifts()
    {
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(16, 0, 16), _project.Materials[0].Id)
        });

        Assert.Equal(new Vector3(16.5f, 0.5f, 16.5f), Element().Transform.Position);

        _scene.SetTransform(_element, TransformChannel.Position, new Vector3(5.5f, 0.5f, 5.5f));

        Assert.Equal(new Vector3(5.5f, 0.5f, 5.5f), Element().Transform.Position);

        // The origin behind it stays whole, which is what keeps the cells on the
        // scene's own grid.
        Assert.Equal(new Vector3(-11f, 0f, -11f), Element().WorldTransform.Origin);
    }

    /// <summary>A group has a pivot too; centring it reads the subtree.</summary>
    [Fact]
    public void AGroupCentresOnWhatItContains()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");

        // Cells 0..1 centre the child's pivot on (1, 0.5, 0.5); the requested
        // position lands on the lattice that pivot shifts, exactly.
        _scene.SetVoxels(child, new[]
        {
            new VoxelEdit(new Vector3I(0, 0, 0), _project.Materials[0].Id),
            new VoxelEdit(new Vector3I(1, 0, 0), _project.Materials[0].Id)
        });
        _scene.SetTransform(child, TransformChannel.Position, new Vector3(11f, 1.5f, 0.5f));

        Assert.True(PivotCenter.TryCalculate(_scene.Element(group), out Vector3 center));
        Assert.True(center.IsEqualApprox(new Vector3(11f, 1.5f, 0.5f)));
    }

    [Fact]
    public void AnEmptyGroupHasNothingToCentreOn()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.False(PivotCenter.TryCalculate(_scene.Element(group), out _));
    }

    [Fact]
    public void ThePivotSurvivesTheRoundTrip()
    {
        Fill(new Vector3I(2, 0, 0), new Vector3I(5, 0, 0));
        _scene.SetPivot(_element, new Vector3(3.5f, -1f, 4f));

        IHierarchyElement loaded = LoadedElement();

        Assert.Equal(new Vector3(3.5f, -1f, 4f), loaded.PivotPoint);
        Assert.Equal(Element().Transform.Position, loaded.Transform.Position);
    }

    [Fact]
    public void ANeverSetPivotWritesNothingAndReadsBackAsNeverSet()
    {
        _project.Save(_path);

        Assert.DoesNotContain("pivot", File.ReadAllText(_path), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Vector3.Zero, LoadedElement().PivotPoint);
    }

    /// <summary>
    /// "Never set" survives the trip as itself: the first voxel edit after a load
    /// may still gift the default, exactly as it would have before the save.
    /// </summary>
    [Fact]
    public void AFreshElementStillEarnsItsDefaultAfterAReload()
    {
        _project.Save(_path);
        IProject loaded = ModelFactory.LoadProject(_path);
        IScene scene = loaded.Scenes[0];

        scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(4, 0, 4), loaded.Materials[0].Id)
        });

        Assert.Equal(new Vector3(4.5f, 0.5f, 4.5f), scene.Element(_element).PivotPoint);
    }

    private void Fill(Vector3I from, Vector3I to)
    {
        var editsList = new List<VoxelEdit>();

        for (int x = from.X; x <= to.X; x++)
        {
            for (int y = from.Y; y <= to.Y; y++)
            {
                for (int z = from.Z; z <= to.Z; z++)
                    editsList.Add(new VoxelEdit(new Vector3I(x, y, z), _project.Materials[0].Id));
            }
        }

        _scene.SetVoxels(_element, editsList);
    }

    private IHierarchyElement Element() => _scene.Element(_element);

    private IHierarchyElement LoadedElement()
    {
        _project.Save(_path);
        IProject loaded = ModelFactory.LoadProject(_path);

        return loaded.Scenes[0].Element(_element);
    }
}
