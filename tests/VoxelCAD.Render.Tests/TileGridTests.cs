using System;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// The image cut into tiles. Two properties matter and both are arithmetic
/// nobody can check by looking at a render: the tiles cover the picture exactly,
/// and a tile half inside a region is worth half of one wholly inside it.
/// </summary>
public sealed class TileGridTests
{
    [Theory]
    [InlineData(64, 64)]
    [InlineData(960, 540)]
    [InlineData(1, 1)]
    [InlineData(4096, 4096)]
    [InlineData(65, 129)]
    public void TheTilesCoverThePictureExactlyOnce(int width, int height)
    {
        var grid = new RenderTileGrid(width, height);
        var covered = new bool[width, height];

        for (int i = 0; i < grid.Count; i++)
        {
            RenderTile tile = grid[i];

            Assert.True(tile.Width > 0 && tile.Height > 0);

            for (int x = tile.X; x < tile.X + tile.Width; x++)
            {
                for (int y = tile.Y; y < tile.Y + tile.Height; y++)
                {
                    Assert.False(covered[x, y], $"Pixel {x},{y} is in two tiles.");
                    covered[x, y] = true;
                }
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                Assert.True(covered[x, y], $"Pixel {x},{y} is in no tile.");
        }
    }

    /// <summary>
    /// The resolve shader finds a pixel's tile by dividing, which only agrees with
    /// the grid because the tiles are laid out in reading order at a fixed pitch.
    /// Stated here because the two are in different languages and nothing else
    /// would notice them parting.
    /// </summary>
    [Fact]
    public void APixelsTileIsWhereDividingSaysItIs()
    {
        var grid = new RenderTileGrid(300, 200);

        for (int x = 0; x < 300; x += 7)
        {
            for (int y = 0; y < 200; y += 5)
            {
                int index = y / RenderTileGrid.TileSize * grid.Columns + x / RenderTileGrid.TileSize;
                RenderTile tile = grid[index];

                Assert.InRange(x, tile.X, tile.X + tile.Width - 1);
                Assert.InRange(y, tile.Y, tile.Y + tile.Height - 1);
            }
        }
    }

    [Fact]
    public void ATileWhollyInsideARegionOverlapsItCompletely()
    {
        var grid = new RenderTileGrid(256, 256);

        Assert.Equal(1f, grid.Overlap(0, new RenderTile(0, 0, 256, 256)), 3);
    }

    [Fact]
    public void ATileOutsideARegionDoesNotOverlapIt()
    {
        var grid = new RenderTileGrid(256, 256);

        Assert.Equal(0f, grid.Overlap(grid.Count - 1, new RenderTile(0, 0, 64, 64)), 3);
    }

    /// <summary>
    /// Half a tile is half the overlap, which is what keeps a priority region's
    /// own boundary from being drawn in the noise.
    /// </summary>
    [Fact]
    public void HalfATileIsHalfTheOverlap()
    {
        var grid = new RenderTileGrid(256, 256);

        Assert.Equal(0.5f, grid.Overlap(0, new RenderTile(0, 0, 32, 64)), 3);
        Assert.Equal(0.25f, grid.Overlap(0, new RenderTile(0, 0, 32, 32)), 3);
    }

    [Fact]
    public void APictureWithNoSizeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTileGrid(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTileGrid(10, -1));
    }
}
