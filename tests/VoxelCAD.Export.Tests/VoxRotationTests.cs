using System.Collections.Generic;
using Godot;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// MagicaVoxel's packed rotation byte, and the crossing of a rotation from its
/// axes to ours.
/// </summary>
/// <remarks>
/// The one that matters is <see cref="EveryRotationSurvivesTheCrossingAndTheEulerAngles"/>:
/// it walks all twenty-four and asserts the <em>basis</em> comes back, not the
/// angles. Which decomposition Godot picks is its business — asserting the angles
/// would pin an answer nobody needs and would fail on a different but equally
/// correct one.
/// </remarks>
public sealed class VoxRotationTests
{
    /// <summary>
    /// First row's non-zero in column 0, second row's in column 1, every sign
    /// positive — which is what an absent <c>_r</c> means.
    /// </summary>
    public const byte IdentityByte = 4;

    [Fact]
    public void TheIdentityByteDecodesToTheIdentity()
    {
        Assert.True(VoxRotation.TryDecode(IdentityByte, out Basis rotation));
        Assert.Equal(Basis.Identity, rotation);
    }

    /// <summary>
    /// The reader falls back on <see cref="VoxRotation.Identity"/> where a frame
    /// carries no <c>_r</c>, and that is decoded from the byte rather than written
    /// down twice — so this holds the byte and the fallback to each other, not a
    /// constant nothing reads.
    /// </summary>
    [Fact]
    public void AnAbsentRotationIsTheIdentityByte()
    {
        Assert.True(VoxRotation.TryDecode(VoxRotation.IdentityByte, out Basis rotation));
        Assert.Equal(Basis.Identity, rotation);
        Assert.Equal(rotation, VoxRotation.Identity);
    }

    [Fact]
    public void ExactlyTwentyFourEncodingsBelowTheDuplicateBitAreRotations()
    {
        var rotations = 0;
        var mirrors = 0;

        // Bit 7 is unused, so every matrix has two encodings; below it each
        // appears once. Half of what the bits can say is a reflection.
        for (var value = 0; value < 128; value++)
        {
            if (!VoxRotation.TryDecode((byte)value, out Basis rotation))
                continue;

            if (VoxRotation.IsMirrored(rotation))
                mirrors++;
            else
                rotations++;
        }

        Assert.Equal(24, rotations);
        Assert.Equal(24, mirrors);
    }

    [Fact]
    public void TwoRowsClaimingOneColumnNameNoRotation()
    {
        // Both rows point at column 0, which no permutation can mean.
        Assert.False(VoxRotation.TryDecode(0b0000_0000, out _));

        // A column index of three, which does not exist.
        Assert.False(VoxRotation.TryDecode(0b0000_0011, out _));
    }

    [Fact]
    public void TheUnusedTopBitChangesNothing()
    {
        for (var value = 0; value < 128; value++)
        {
            bool low = VoxRotation.TryDecode((byte)value, out Basis first);
            bool high = VoxRotation.TryDecode((byte)(value | 0b1000_0000), out Basis second);

            Assert.Equal(low, high);
            Assert.Equal(first, second);
        }
    }

    /// <summary>
    /// The whole chain a rotation goes through on import: MagicaVoxel's byte, the
    /// turn into this document's axes, and back out as the quarter turns the
    /// document stores. All twenty-four, with the basis compared at the end.
    /// </summary>
    [Fact]
    public void EveryRotationSurvivesTheCrossingAndTheEulerAngles()
    {
        var checkedRotations = new List<Basis>();

        for (var value = 0; value < 128; value++)
        {
            if (!VoxRotation.TryDecode((byte)value, out Basis magica)
                || VoxRotation.IsMirrored(magica))
            {
                continue;
            }

            Basis ours = VoxAxis.TurnToWorld(magica);

            Assert.True(ours.Determinant() > 0f, $"_r {value} turned into a reflection");

            Vector3 degrees = ImportPlacement.RotationDegrees(ours);

            foreach (float angle in new[] { degrees.X, degrees.Y, degrees.Z })
                Assert.Equal(0f, angle % 90f, precision: 4);

            // The document keeps degrees and composes them Godot's own way, so the
            // proof that nothing was lost is that the same basis comes back.
            Basis rebuilt = Basis.FromEuler(new Vector3(
                Mathf.DegToRad(degrees.X),
                Mathf.DegToRad(degrees.Y),
                Mathf.DegToRad(degrees.Z)));

            AssertSameBasis(ours, rebuilt);

            checkedRotations.Add(ours);
        }

        Assert.Equal(24, checkedRotations.Count);
        Assert.Equal(24, new HashSet<Basis>(checkedRotations).Count);
    }

    /// <summary>
    /// A quarter turn about MagicaVoxel's up axis is a quarter turn about ours,
    /// which is the whole point of the crossing being a conjugation.
    /// </summary>
    [Fact]
    public void MagicaVoxelsUpAxisBecomesOurs()
    {
        // _r = 17: first row's non-zero in column 1 and negative, second row's in
        // column 0 — a quarter turn about MagicaVoxel's Z, its up axis. This is
        // the byte one of cars.vox's two cars carries.
        Assert.True(VoxRotation.TryDecode(17, out Basis magica));

        Basis ours = VoxAxis.TurnToWorld(magica);

        // Our up is Y, and a turn about it leaves Y alone while sweeping the two
        // axes on the ground into each other.
        Assert.Equal(Vector3.Up, ours * Vector3.Up);
        Assert.Equal(Vector3.Forward, ours * Vector3.Right);
        Assert.Equal(Vector3.Right, ours * Vector3.Back);
    }

    /// <summary>
    /// The axis convention itself, stated rather than inferred from a round trip.
    /// A round trip through our own writer cannot see a sign error here, because
    /// both halves go through the same function and a mirrored convention is its
    /// own inverse — so the crossing has to be pinned against the values it is
    /// meant to produce.
    /// </summary>
    [Fact]
    public void TheAxisCrossingIsPinnedToItsOwnAnswers()
    {
        // Height is Y here and Z there; depth is Z here and Y there, running the
        // other way — a cell at Z = k occupies the box whose far corner faces
        // MagicaVoxel's -Y, which is the whole reason the index map carries a -1.
        Assert.Equal(new Vector3I(0, -1, 0), VoxAxis.ToMagica(Vector3I.Zero));
        Assert.Equal(new Vector3I(1, -1, 0), VoxAxis.ToMagica(new Vector3I(1, 0, 0)));
        Assert.Equal(new Vector3I(0, -1, 1), VoxAxis.ToMagica(new Vector3I(0, 1, 0)));
        Assert.Equal(new Vector3I(0, -2, 0), VoxAxis.ToMagica(new Vector3I(0, 0, 1)));

        Assert.Equal(Vector3I.Zero, VoxAxis.ToWorld(new Vector3I(0, -1, 0)));
        Assert.Equal(new Vector3I(0, 1, 0), VoxAxis.ToWorld(new Vector3I(0, -1, 1)));
        Assert.Equal(new Vector3I(0, 0, 1), VoxAxis.ToWorld(new Vector3I(0, -2, 0)));
    }

    /// <summary>
    /// And the two directions are exact inverses, which is what a writer and a
    /// reader sharing one convention actually means.
    /// </summary>
    [Fact]
    public void TheTwoDirectionsAreExactInverses()
    {
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                for (var z = -3; z <= 3; z++)
                {
                    var cell = new Vector3I(x, y, z);

                    Assert.Equal(cell, VoxAxis.ToWorld(VoxAxis.ToMagica(cell)));
                    Assert.Equal(cell, VoxAxis.ToMagica(VoxAxis.ToWorld(cell)));
                }
            }
        }
    }

    /// <summary>
    /// A direction is not a cell: the linear half carries no offset, and the
    /// difference between the two is exactly what the placement arithmetic has to
    /// keep straight.
    /// </summary>
    [Fact]
    public void ADirectionCrossesWithoutTheCellsOffset()
    {
        Assert.Equal(Vector3I.Zero, VoxAxis.TurnToWorld(Vector3I.Zero));
        Assert.Equal(new Vector3I(0, 1, 0), VoxAxis.TurnToWorld(new Vector3I(0, 0, 1)));
        Assert.Equal(new Vector3I(0, 0, -1), VoxAxis.TurnToWorld(new Vector3I(0, 1, 0)));

        // And it is the cell map without its -1, on every input.
        for (var y = -3; y <= 3; y++)
        {
            var cell = new Vector3I(1, y, 2);

            Assert.Equal(VoxAxis.ToWorld(cell) + new Vector3I(0, 0, 1), VoxAxis.TurnToWorld(cell));
        }
    }

    private static void AssertSameBasis(Basis expected, Basis actual)
    {
        Assert.True(
            expected.Column0.IsEqualApprox(actual.Column0)
            && expected.Column1.IsEqualApprox(actual.Column1)
            && expected.Column2.IsEqualApprox(actual.Column2),
            $"expected {expected} but got {actual}");
    }
}
