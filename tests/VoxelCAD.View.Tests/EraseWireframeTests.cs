using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which faces of the erase marker's cube face the camera. The engine-free
/// decision on its own, independent of the line mesh it feeds.
/// </summary>
public sealed class EraseWireframeTests
{
    private const int PlusX = 0;
    private const int MinusX = 1;
    private const int PlusY = 2;
    private const int MinusY = 3;
    private const int PlusZ = 4;
    private const int MinusZ = 5;

    [Fact]
    public void ACameraInTheOctantAboveSeesExactlyThreeFaces()
    {
        IReadOnlyList<int> faces = EraseWireframe.VisibleFaces(new Vector3(1f, 1f, 1f));

        Assert.Equal(3, faces.Count);
        Assert.Contains(PlusX, faces);
        Assert.Contains(PlusY, faces);
        Assert.Contains(PlusZ, faces);
    }

    [Fact]
    public void TheOppositeOctantSeesTheOtherThreeFaces()
    {
        IReadOnlyList<int> faces = EraseWireframe.VisibleFaces(new Vector3(-1f, -1f, -1f));

        Assert.Equal(3, faces.Count);
        Assert.Contains(MinusX, faces);
        Assert.Contains(MinusY, faces);
        Assert.Contains(MinusZ, faces);
    }

    [Fact]
    public void ACameraStraightAboveNeverIncludesTheFloor()
    {
        // Looking straight down: the four side faces are edge-on (zero dot), only
        // the top qualifies. Never the opposite face regardless of viewing angle.
        IReadOnlyList<int> faces = EraseWireframe.VisibleFaces(Vector3.Up);

        Assert.DoesNotContain(MinusY, faces);
        Assert.Contains(PlusY, faces);
    }

    [Fact]
    public void EachVisibleFaceContributesOneBorderAndOneXOfSixLineSegments()
    {
        var points = new List<Vector3>();

        EraseWireframe.AppendFaceLines(points, PlusX);

        // 4 border edges + 2 diagonals = 6 segments = 12 endpoints.
        Assert.Equal(12, points.Count);
    }

    [Fact]
    public void AGenericViewingAngleProducesThreeFacesWorthOfLinePoints()
    {
        // EraseWireframe.Build wraps this same list in an ArrayMesh, which needs
        // a running engine to construct — untestable here, same as
        // VoxelMesher.Build. The point list it is built from is not.
        var points = new List<Vector3>();

        foreach (int face in EraseWireframe.VisibleFaces(new Vector3(1f, 1f, 1f)))
            EraseWireframe.AppendFaceLines(points, face);

        // 3 faces * 6 segments * 2 endpoints.
        Assert.Equal(36, points.Count);
    }
}
