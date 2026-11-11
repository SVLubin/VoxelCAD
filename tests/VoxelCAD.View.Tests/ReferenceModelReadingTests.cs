using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The two model formats this editor reads itself. Both invert conventions Godot
/// does not share, and every one of those fails silently: a model comes out inside
/// out, or upside down, and nothing anywhere says so.
/// </summary>
public sealed class ReferenceModelReadingTests
{
    /// <summary>
    /// The file's faces are counter-clockwise by the right-hand rule and Godot's
    /// front faces are clockwise, so a reader that trusted the order would draw
    /// every reference inside out.
    /// </summary>
    [Fact]
    public void AnObjTriangleComesOutWoundTheOtherWay()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out _));

        ObjSurface surface = Assert.Single(surfacesList);

        Assert.Equal(new[] { 0, 2, 1 }, surface.Indices);
        Assert.Equal(Vector3.Forward, Handed(surface, 0));
    }

    /// <summary>
    /// One-based, and a negative index counts back from the end of what has been
    /// read so far — which is why they are resolved as the file is walked rather
    /// than afterwards.
    /// </summary>
    [Fact]
    public void NegativeIndicesCountBackFromWhatHasBeenReadSoFar()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f -3 -2 -1
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out _));

        Assert.Equal(3, Assert.Single(surfacesList).Positions.Length);
        Assert.Equal(new Vector3(0f, 1f, 0f), surfacesList[0].Positions[2]);
    }

    [Fact]
    public void APolygonIsTriangulatedAsAFan()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out _));

        ObjSurface surface = Assert.Single(surfacesList);

        Assert.Equal(6, surface.Indices.Length);
        Assert.Equal(Vector3.Forward, Handed(surface, 0));
        Assert.Equal(Vector3.Forward, Handed(surface, 1));
    }

    /// <summary>
    /// A texture's V runs down from the top and the file's runs up from the
    /// bottom, so a picture read straight comes out upside down.
    /// </summary>
    [Fact]
    public void TheTextureCoordinateIsFlipped()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0.25 0.75
            vt 0 0
            vt 1 1
            f 1/1 2/2 3/3
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out _));

        Assert.Equal(new Vector2(0.25f, 0.25f), Assert.Single(surfacesList).Uvs[0]);
    }

    /// <summary>
    /// A surface is what a material is worn by, so the file's own runs decide the
    /// split — and a surface nothing was ever added to is not written at all.
    /// </summary>
    [Fact]
    public void EachMaterialGetsItsOwnSurface()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 0 0 1
            mtllib parts.mtl
            usemtl steel
            f 1 2 3
            usemtl glass
            f 1 2 4
            usemtl unused
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out IReadOnlyList<string> librariesList));

        Assert.Equal(2, surfacesList.Count);
        Assert.Equal("steel", surfacesList[0].MaterialName);
        Assert.Equal("glass", surfacesList[1].MaterialName);
        Assert.Equal(new[] { "parts.mtl" }, librariesList);
    }

    /// <summary>
    /// Faces meeting at one position with different texture coordinates get a
    /// vertex each — what the format means, and what stops a seam being smeared.
    /// </summary>
    [Fact]
    public void OnePositionWithTwoTextureCoordinatesIsTwoVertices()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 1
            f 1/1 2/1 3/1
            f 1/2 2/1 3/1
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out _));

        Assert.Equal(4, Assert.Single(surfacesList).Positions.Length);
    }

    /// <summary>
    /// A file with no normals still has to be lit if somebody switches the lights
    /// on, and every corner facing nowhere would render black.
    /// </summary>
    [Fact]
    public void NormalsAreMadeForAFileThatCarriesNone()
    {
        Assert.True(ObjGeometry.TryRead(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """,
            out IReadOnlyList<ObjSurface> surfacesList,
            out _));

        foreach (Vector3 normal in Assert.Single(surfacesList).Normals)
            Assert.Equal(1f, normal.Length(), 3);
    }

    [Fact]
    public void AFileWithNoFacesIsNotAModel() =>
        Assert.False(ObjGeometry.TryRead("v 0 0 0\nv 1 0 0\n", out _, out _));

    [Fact]
    public void AMaterialLibraryCarriesColourOpacityAndPicture()
    {
        Dictionary<string, ObjMaterial> read = ObjMaterialLibrary.Read(
            """
            newmtl steel
            Kd 0.2 0.4 0.6
            map_Kd -s 1 1 1 plate.png

            newmtl glass
            Tr 0.75
            """);

        Assert.Equal(new Color(0.2f, 0.4f, 0.6f), read["steel"].Diffuse);
        Assert.Equal("plate.png", read["steel"].DiffuseMap);

        // Tr is the same number said the other way round, and both are legal.
        Assert.Equal(0.25f, read["glass"].Opacity, 3);
    }

    /// <summary>
    /// Plenty of exporters begin a binary file with the word "solid", so what tells
    /// the two apart is arithmetic: a binary file is exactly its header plus fifty
    /// bytes a triangle.
    /// </summary>
    [Fact]
    public void ABinaryStlIsToldFromATextOneByItsLength()
    {
        Assert.True(StlGeometry.TryRead(BinaryStl(2), out Vector3[] positions));
        Assert.Equal(6, positions.Length);
    }

    [Fact]
    public void ATextStlIsReadAndWoundTheOtherWay()
    {
        Assert.True(StlGeometry.TryRead(
            Encoding.UTF8.GetBytes(
                """
                solid part
                  facet normal 0 0 1
                    outer loop
                      vertex 0 0 0
                      vertex 1 0 0
                      vertex 0 1 0
                    endloop
                  endfacet
                endsolid part
                """),
            out Vector3[] positions));

        Assert.Equal(3, positions.Length);
        Assert.Equal(new Vector3(0f, 1f, 0f), positions[1]);
        Assert.Equal(new Vector3(1f, 0f, 0f), positions[2]);

        Vector3 handed = (positions[1] - positions[0]).Cross(positions[2] - positions[0]);

        Assert.Equal(Vector3.Forward, handed.Normalized());
    }

    [Fact]
    public void RubbishIsNotAModel()
    {
        Assert.False(StlGeometry.TryRead(new byte[32], out _));
        Assert.False(StlGeometry.TryRead(Encoding.UTF8.GetBytes("solid empty\nendsolid"), out _));
    }

    /// <summary>
    /// An interior edge belongs to two triangles, and drawn twice it reads as a
    /// darker line down the middle of every flat face.
    /// </summary>
    [Fact]
    public void ASharedEdgeIsDrawnOnce()
    {
        int[] edges = WireframeEdges.From(new[] { 0, 1, 2, 0, 2, 3 });

        Assert.Equal(10, edges.Length);
        Assert.Equal(1, CountEdge(edges, 0, 2));
    }

    [Fact]
    public void AnEdgeThatGoesNowhereIsNotDrawn() =>
        Assert.Empty(WireframeEdges.From(new[] { 4, 4, 4 }));

    private static int CountEdge(int[] edges, int from, int to)
    {
        var found = 0;

        for (var index = 0; index + 1 < edges.Length; index += 2)
        {
            if ((edges[index] == from && edges[index + 1] == to)
                || (edges[index] == to && edges[index + 1] == from))
            {
                found++;
            }
        }

        return found;
    }

    private static Vector3 Handed(ObjSurface surface, int triangle)
    {
        Vector3 a = surface.Positions[surface.Indices[triangle * 3]];
        Vector3 b = surface.Positions[surface.Indices[(triangle * 3) + 1]];
        Vector3 c = surface.Positions[surface.Indices[(triangle * 3) + 2]];

        return (b - a).Cross(c - a).Normalized();
    }

    private static byte[] BinaryStl(int triangles)
    {
        var bytes = new byte[84 + (50 * triangles)];

        BitConverter.GetBytes(triangles).CopyTo(bytes, 80);

        for (var triangle = 0; triangle < triangles; triangle++)
        {
            int at = 84 + (triangle * 50) + 12;

            BitConverter.GetBytes(1f).CopyTo(bytes, at + 4);
            BitConverter.GetBytes(1f).CopyTo(bytes, at + 12 + 8);
        }

        return bytes;
    }
}
