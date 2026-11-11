using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What meshing one box of cells promises. The chunking itself lives in a Godot
/// node and cannot be built here; what can be held is the rule it rests on —
/// a box decides which faces are emitted and never what is looked at.
/// </summary>
public class VoxelChunkTests
{
    private static readonly Guid Stone = Guid.NewGuid();

    private static Dictionary<Guid, float> Opaque() => new() { [Stone] = 1f };

    private static Dictionary<Vector3I, Guid> Bar(int length)
    {
        var voxels = new Dictionary<Vector3I, Guid>();

        for (var x = 0; x < length; x++)
            voxels[new Vector3I(x, 0, 0)] = Stone;

        return voxels;
    }

    [Fact]
    public void EveryBoxTogetherEmitsWhatTheWholeModelDoes()
    {
        Dictionary<Vector3I, Guid> voxels = Bar(8);

        int whole = Count(VoxelMesher.CollectVisibleFaces(voxels, Opaque(), null));

        int inPieces =
            Count(VoxelMesher.CollectVisibleFaces(
                voxels, Opaque(), new CellRegion(new Vector3I(0, 0, 0), new Vector3I(3, 0, 0))))
            + Count(VoxelMesher.CollectVisibleFaces(
                voxels, Opaque(), new CellRegion(new Vector3I(4, 0, 0), new Vector3I(7, 0, 0))));

        Assert.Equal(whole, inPieces);
    }

    [Fact]
    public void ACellAtTheEdgeOfABoxStillSeesTheNeighbourBeyondIt()
    {
        Dictionary<Vector3I, Guid> voxels = Bar(2);

        // Cell 0 alone, with cell 1 outside the box: the face between them must
        // still be culled, or every chunk boundary would grow a wall.
        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces = VoxelMesher.CollectVisibleFaces(
            voxels, Opaque(), new CellRegion(Vector3I.Zero, Vector3I.Zero));

        Assert.Equal(5, Count(faces));
    }

    [Fact]
    public void ABoxWithNothingInItEmitsNothing()
    {
        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces = VoxelMesher.CollectVisibleFaces(
            Bar(2), Opaque(), new CellRegion(new Vector3I(40, 0, 0), new Vector3I(48, 0, 0)));

        Assert.Empty(faces);
    }

    private static int Count(Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces)
    {
        var total = 0;

        foreach (List<VoxelMesher.VoxelFace> list in faces.Values)
            total += list.Count;

        return total;
    }

    /// <summary>
    /// C# division rounds toward zero, so <c>-1 / 16</c> is 0 — under which the
    /// chunks either side of the origin are one chunk twice as wide, and an edit
    /// at -1 rebuilds the chunk holding 0..15 while leaving the cell it touched
    /// as it was. Floored, they are two chunks.
    /// </summary>
    [Fact]
    public void TheChunksEitherSideOfTheOriginAreDifferentChunks()
    {
        Assert.NotEqual(
            VoxelChunkGrid.KeyOf(new Vector3I(-1, 0, 0)),
            VoxelChunkGrid.KeyOf(new Vector3I(0, 0, 0)));
    }

    [Theory]
    [InlineData(-32, -2)]
    [InlineData(-17, -2)]
    [InlineData(-16, -1)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    public void ACellFallsInTheChunkThatHoldsIt(int cell, int chunk)
    {
        Assert.Equal(chunk, VoxelChunkGrid.KeyOf(new Vector3I(cell, cell, cell)).X);
    }

    /// <summary>
    /// Every chunk is exactly <see cref="VoxelChunkGrid.Size"/> cells a side and
    /// they meet without a gap or an overlap — a gap is a stripe of the model
    /// nothing rebuilds, an overlap is every face of it drawn twice.
    /// </summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void EachChunksBoxEndsWhereTheNextOneBegins(int chunk)
    {
        CellRegion here = VoxelChunkGrid.BoxOf(new Vector3I(chunk, 0, 0));
        CellRegion next = VoxelChunkGrid.BoxOf(new Vector3I(chunk + 1, 0, 0));

        Assert.Equal(VoxelChunkGrid.Size, here.Max.X - here.Min.X + 1);
        Assert.Equal(here.Max.X + 1, next.Min.X);
    }

    /// <summary>
    /// Every cell of a chunk's box is answered by that chunk, which is what makes
    /// the two functions one grid rather than two conventions.
    /// </summary>
    [Fact]
    public void AChunksOwnCellsAllAnswerWithThatChunk()
    {
        foreach (int chunk in new[] { -2, -1, 0, 1 })
        {
            CellRegion box = VoxelChunkGrid.BoxOf(new Vector3I(chunk, chunk, chunk));

            for (int x = box.Min.X; x <= box.Max.X; x++)
                Assert.Equal(chunk, VoxelChunkGrid.KeyOf(new Vector3I(x, x, x)).X);
        }
    }

    /// <summary>
    /// An edit against a chunk's own edge reaches the chunk beyond it: the cell
    /// appearing hides a face over there, and the occlusion its corner earns is
    /// read from over there too.
    /// </summary>
    [Fact]
    public void AnEditOnAChunksEdgeReachesTheChunkBeyondIt()
    {
        var edge = new Vector3I(VoxelChunkGrid.Size - 1, 0, 0);

        Assert.Contains(
            new Vector3I(1, 0, 0),
            new List<Vector3I>(VoxelChunkGrid.Touching(new CellRegion(edge, edge))));
    }

    /// <summary>
    /// And an edit at -1 reaches the chunk holding 0, which is the same rule
    /// across the origin, where the truncating version got it wrong.
    /// </summary>
    [Fact]
    public void AnEditJustBelowTheOriginReachesBothChunks()
    {
        var cell = new Vector3I(-1, 0, 0);

        var reached = new List<Vector3I>(VoxelChunkGrid.Touching(new CellRegion(cell, cell)));

        Assert.Contains(new Vector3I(-1, 0, 0), reached);
        Assert.Contains(new Vector3I(0, 0, 0), reached);
    }

    /// <summary>
    /// An edit well inside a chunk reaches that chunk alone: the grow is measured
    /// in cells and a chunk is sixteen of them.
    /// </summary>
    [Fact]
    public void AnEditInTheMiddleOfAChunkReachesThatChunkAlone()
    {
        var middle = new Vector3I(8, 8, 8);

        var reached = new List<Vector3I>(VoxelChunkGrid.Touching(new CellRegion(middle, middle)));

        Assert.Single(reached);
        Assert.Equal(new Vector3I(0, 0, 0), reached[0]);
    }

    /// <summary>
    /// A material that both wears the atlas and lets light through must not be in
    /// the atlas surface. The atlas is one surface appended to the opaque mesh,
    /// and Godot's alpha pass sorts instances rather than the surfaces inside one
    /// — so a see-through material sharing it is the exact defect per-material
    /// instances exist to prevent.
    /// </summary>
    /// <remarks>
    /// Held here because the consequence was worse than a sorting artefact: the
    /// mesher refused it the atlas branch for needing its own instance, and then
    /// found no material to give it, because taking an atlas layer is what throws
    /// the material away. It drew with none at all. Nothing ships transparent
    /// today, which is the only reason it was not on screen.
    /// </remarks>
    [Theory]
    [InlineData(1f, false)]
    [InlineData(0.999f, true)]
    [InlineData(0.5f, true)]
    [InlineData(0f, true)]
    public void OnlyAnOpaqueMaterialMayShareOneSurface(float opacity, bool ownInstance)
    {
        Assert.Equal(ownInstance, VoxelMeshSet.NeedsOwnInstance(opacity));
    }
}
