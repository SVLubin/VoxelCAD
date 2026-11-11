using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Every decision about how a reference is drawn, and the quad a picture is drawn
/// on. These exist because neither the material nor the mesh can be built without
/// an engine, so until they were pulled out here the only way to check any of them
/// was to take a photograph.
/// </summary>
public sealed class ReferenceDrawingTests
{
    [Fact]
    public void APictureIsUnlitUntilItIsToldOtherwise()
    {
        Assert.True(ReferenceLook.From(ReferenceImageSettings.Default).Unshaded);

        Assert.False(
            ReferenceLook.From(ReferenceImageSettings.Default with { LitByScene = true })
                .Unshaded);
    }

    /// <summary>
    /// Godot's per-instance fade runs the other way from opacity, and getting that
    /// backwards is a reference that disappears the moment somebody makes it solid.
    /// </summary>
    [Fact]
    public void OpacityIsTheOppositeOfWhatTheEngineTakes()
    {
        Assert.Equal(0f, ReferenceLook.From(ReferenceImageSettings.Default).Transparency);

        Assert.Equal(
            0.75f,
            ReferenceLook.From(ReferenceImageSettings.Default with { Opacity = 0.25f })
                .Transparency);
    }

    /// <summary>
    /// Nought is "keep it and stop drawing it", which is a different sentence from
    /// hiding the element: the row stays lit and the handles stay on it.
    /// </summary>
    [Fact]
    public void NoughtOpacityIsNotDrawnAtAll()
    {
        Assert.False(
            ReferenceLook.From(ReferenceImageSettings.Default with { Opacity = 0f }).Drawn);

        Assert.True(ReferenceLook.From(ReferenceImageSettings.Default).Drawn);

        Assert.False(
            ReferenceLook.From(ReferenceModelSettings.Default with { Opacity = 0f }).Drawn);
    }

    [Fact]
    public void PixelArtIsSampledWithoutSmoothing()
    {
        Assert.True(ReferenceLook.From(ReferenceImageSettings.Default).Nearest);

        Assert.False(
            ReferenceLook.From(ReferenceImageSettings.Default with { Filtered = true }).Nearest);
    }

    /// <summary>
    /// A reference is furniture, on <c>SceneBox</c>'s own terms. Stated as a sweep
    /// rather than as a line in a node so that nobody can fold a shadow into it by
    /// adding a switch — which is exactly how a fourth setting would arrive.
    /// </summary>
    [Fact]
    public void NoSettingAnywhereMakesAReferenceCastAShadow()
    {
        foreach (bool lit in new[] { false, true })
        {
            foreach (float opacity in new[] { 0f, 0.5f, 1f })
            {
                Assert.False(
                    ReferenceLook.From(ReferenceImageSettings.Default with
                    {
                        LitByScene = lit,
                        Opacity = opacity
                    }).CastsShadow);

                Assert.False(
                    ReferenceLook.From(ReferenceModelSettings.Default with
                    {
                        LitByScene = lit,
                        Opacity = opacity
                    }).CastsShadow);
            }
        }
    }

    /// <summary>
    /// A plane has no inside, so a reference that vanished when the camera crossed
    /// it would be one you had to orbit back to find.
    /// </summary>
    [Fact]
    public void APictureIsVisibleFromBothSides() =>
        Assert.True(ReferenceLook.From(ReferenceImageSettings.Default).DoubleSided);

    /// <summary>
    /// The load-bearing one: quantisation acts on the stored origin, so a corner at
    /// local zero is a corner on the cell lattice — which is the whole of what makes
    /// one pixel of the picture land on one voxel.
    /// </summary>
    [Fact]
    public void TheQuadSpansFromLocalZero()
    {
        Vector3[] corners = ReferencePlaneGeometry.Corners(new Vector2(64f, 32f));

        Assert.Equal(Vector3.Zero, corners[0]);
        Assert.Equal(new Vector3(64f, 0f, 0f), corners[1]);
        Assert.Equal(new Vector3(64f, 32f, 0f), corners[2]);
        Assert.Equal(new Vector3(0f, 32f, 0f), corners[3]);
    }

    /// <summary>
    /// A texture's V runs down from its top row while the plane's Y runs up, so the
    /// two are opposite — the sort of thing that ships upside down and is noticed by
    /// nobody until a drawing with writing on it turns up.
    /// </summary>
    [Fact]
    public void ThePicturesTopRowLandsOnThePlanesTopEdge()
    {
        Vector3[] corners = ReferencePlaneGeometry.Corners(new Vector2(10f, 4f));
        Vector2[] uvs = ReferencePlaneGeometry.Uvs();

        for (var index = 0; index < corners.Length; index++)
        {
            bool top = corners[index].Y > 0f;
            bool right = corners[index].X > 0f;

            Assert.Equal(top ? 0f : 1f, uvs[index].Y);
            Assert.Equal(right ? 1f : 0f, uvs[index].X);
        }
    }

    /// <summary>
    /// Godot's front faces are wound clockwise, the opposite of the OpenGL
    /// convention — so the right-hand normal of each triangle points <em>away</em>
    /// from the side the picture is meant to be seen from, while the shading normal
    /// points towards it. Both are correct and they look like a contradiction, which
    /// is exactly why it is pinned here.
    /// </summary>
    [Fact]
    public void TheQuadIsWoundClockwiseSeenFromTheFront()
    {
        Vector3[] corners = ReferencePlaneGeometry.Corners(new Vector2(6f, 3f));
        int[] indices = ReferencePlaneGeometry.Indices();

        Assert.Equal(6, indices.Length);

        for (var triangle = 0; triangle < 2; triangle++)
        {
            Vector3 a = corners[indices[triangle * 3]];
            Vector3 b = corners[indices[(triangle * 3) + 1]];
            Vector3 c = corners[indices[(triangle * 3) + 2]];

            Vector3 handed = (b - a).Cross(c - a).Normalized();

            Assert.Equal(Vector3.Forward, handed);
        }

        Assert.Equal(Vector3.Back, ReferencePlaneGeometry.Normal);
    }

    /// <summary>
    /// Both triangles together cover the rectangle exactly: no gap along the shared
    /// diagonal, and nothing folded over itself.
    /// </summary>
    [Fact]
    public void TheTwoTrianglesCoverTheWholeRectangle()
    {
        var size = new Vector2(6f, 3f);
        Vector3[] corners = ReferencePlaneGeometry.Corners(size);
        int[] indices = ReferencePlaneGeometry.Indices();
        var area = 0f;

        for (var triangle = 0; triangle < 2; triangle++)
        {
            Vector3 a = corners[indices[triangle * 3]];
            Vector3 b = corners[indices[(triangle * 3) + 1]];
            Vector3 c = corners[indices[(triangle * 3) + 2]];

            area += (b - a).Cross(c - a).Length() / 2f;
        }

        Assert.Equal(size.X * size.Y, area, 3);
    }
}
