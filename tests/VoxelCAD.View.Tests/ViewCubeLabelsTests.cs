using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Where the cube's axis letters stand. Held by a test rather than by looking,
/// because at the pose the editor opens at the top face is thirty degrees off
/// the eye: a letter lying on its side there is a smudge, and so is a letter the
/// right way up.
/// </summary>
public sealed class ViewCubeLabelsTests
{
    private static readonly Vector3[] Normals =
    {
        Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward
    };

    [Fact]
    public void EachFaceReadsOutwards()
    {
        foreach (Vector3 normal in Normals)
            Assert.True(ViewCubeLabels.FaceBasis(normal).Z.IsEqualApprox(normal));
    }

    /// <summary>
    /// A left-handed frame is a mirrored letter. Nothing else in the widget can
    /// produce one, so the determinant is the whole of the check.
    /// </summary>
    [Fact]
    public void NoFaceMirrorsItsLetter()
    {
        foreach (Vector3 normal in Normals)
            Assert.Equal(1f, ViewCubeLabels.FaceBasis(normal).Determinant(), 3);
    }

    /// <summary>
    /// The front face is the one everybody can check by looking, so it is the one
    /// the others are reasoned from: text to the right, tops of the letters up.
    /// </summary>
    [Fact]
    public void TheFrontFaceReadsLeftToRightAndUpright()
    {
        Basis front = ViewCubeLabels.FaceBasis(Vector3.Back);

        Assert.True(front.X.IsEqualApprox(Vector3.Right));
        Assert.True(front.Y.IsEqualApprox(Vector3.Up));
    }

    /// <summary>
    /// The one that was wrong. Seen from above, the letter has to read along +X
    /// with its top pointing away from the viewer — which is world -Z, the
    /// direction a camera at the default yaw is looking along. Built with
    /// <see cref="Vector3.Back"/> as the up vector instead, the text ran along
    /// -X: a Y on its side.
    /// </summary>
    [Fact]
    public void TheTopFaceReadsAlongXWithItsTopAwayFromTheEye()
    {
        Basis top = ViewCubeLabels.FaceBasis(Vector3.Up);

        Assert.True(top.X.IsEqualApprox(Vector3.Right));
        Assert.True(top.Y.IsEqualApprox(Vector3.Forward));
    }

    [Fact]
    public void ThePositiveEndTakesTheBareLetterAndTheNegativeASign()
    {
        Assert.Equal("X", ViewCubeLabels.TextFor(0, 1));
        Assert.Equal("-X", ViewCubeLabels.TextFor(0, -1));
        Assert.Equal("Y", ViewCubeLabels.TextFor(1, 1));
        Assert.Equal("-Z", ViewCubeLabels.TextFor(2, -1));
    }
}
