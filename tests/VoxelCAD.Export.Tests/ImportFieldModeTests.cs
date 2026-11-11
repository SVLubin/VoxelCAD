using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The claim the placement arithmetic is built around, asserted against the model
/// rather than against the arithmetic: an imported element lands on
/// <see cref="FieldMode.Integer"/> position and <see cref="FieldMode.Snap90"/>
/// rotation exactly, for every rotation the format can state.
/// </summary>
/// <remarks>
/// A field mode degrades when the model cannot represent what it is given, and
/// nothing announces it — the element simply becomes Float and its cells stop
/// sitting on the world's grid. So the test is end to end: a file is built with
/// the rotation in it, imported into a real document, and the document is asked
/// what it made of it.
/// </remarks>
public sealed class ImportFieldModeTests
{
    [Fact]
    public void EveryRotationTheFormatCanStateLandsOnTheGridWithoutDegrading()
    {
        var checkedRotations = 0;

        for (var packed = 0; packed < 128; packed++)
        {
            if (!VoxRotation.TryDecode((byte)packed, out Basis magica)
                || VoxRotation.IsMirrored(magica))
            {
                continue;
            }

            // Deliberately lopsided, and mixing odd and even sides: the grid's
            // middle is taken by integer division, so an even side puts it one
            // cell past halfway and that is where a half could leak in.
            AssertLandsCleanly((byte)packed, new Vector3I(3, 4, 5), new Vector3I(7, -3, 2));
            AssertLandsCleanly((byte)packed, new Vector3I(2, 2, 2), Vector3I.Zero);
            AssertLandsCleanly((byte)packed, new Vector3I(1, 6, 1), new Vector3I(-11, 5, 9));

            // Big enough that a half-cell pivot has real magnitude beside it, and
            // odd against even on every axis. A review claimed the invariant fails
            // past about forty cells; this is where that would show.
            AssertLandsCleanly((byte)packed, new Vector3I(44, 45, 46), new Vector3I(0, 0, 0));
            AssertLandsCleanly((byte)packed, new Vector3I(97, 128, 63), new Vector3I(31, -17, 5));

            // And at the format's own ceiling, where the pivot is furthest from
            // the origin and a float error in the basis has the most room to grow.
            AssertLandsCleanly((byte)packed, new Vector3I(255, 256, 253), new Vector3I(-99, 120, -7));

            checkedRotations++;
        }

        Assert.Equal(24, checkedRotations);
    }

    private static void AssertLandsCleanly(byte rotation, Vector3I size, Vector3I translation)
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject(new Vector3I(256, 256, 256));

        byte[] bytes = VoxBuilder.File()
            .Size(size.X, size.Y, size.Z)
            .Voxels((0, 0, 0, 1), (size.X - 1, size.Y - 1, size.Z - 1, 1))
            .Palette((1, 255, 255, 255, 255))
            .Transform(0, 1, Vector3I.Zero)
            .Group(1, 2)
            .Transform(2, 3, translation, rotation)
            .Shape(3, 0)
            .Build();

        var plan = new ImportPlan(new VoxImportFormat().Read(bytes, "part"), "part", ".vox");

        ImportProjection.Apply(project, scene, plan, scene.Id);

        IHierarchyElement element = Assert.Single(scene.Children);
        var what = $"_r {rotation}, size {size}, _t {translation}";

        Assert.Equal(FieldMode.Integer, element.TransformModes.Position);
        Assert.Equal(FieldMode.Snap90, element.TransformModes.Rotation);

        // The mode surviving is not enough on its own: what it is for is the cells
        // sitting on the world's own grid, so that is what is asserted.
        Transform3D world = element.WorldTransform;

        foreach (Vector3I cell in ((IVoxelModel)element.Component!).Voxels.Keys)
        {
            Vector3 corner = world * new Vector3(cell.X, cell.Y, cell.Z);

            foreach (float axis in new[] { corner.X, corner.Y, corner.Z })
            {
                Assert.True(
                    Mathf.Abs(axis - Mathf.Round(axis)) < 0.001f,
                    $"{what}: a cell corner landed at {corner}, off the grid");
            }
        }

        // And the rotation really is the one the file asked for, not a mode that
        // survived by the rotation being thrown away. Compared approximately
        // because the two sides reach the same turn by different routes — one
        // built from the byte, one composed through Euler angles — and the last
        // bits of a float do not survive that.
        Quaternion asked = VoxAxis.TurnToWorld(Decode(rotation)).GetRotationQuaternion();
        Quaternion got = world.Basis.GetRotationQuaternion();

        Assert.True(
            asked.IsEqualApprox(got) || asked.IsEqualApprox(-got),
            $"{what}: asked for {asked} but the element turned {got}");
    }

    private static Basis Decode(byte packed)
    {
        VoxRotation.TryDecode(packed, out Basis rotation);

        return rotation;
    }
}
