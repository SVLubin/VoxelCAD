using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The binary STL writer. The file is parsed back and measured: the header is
/// well-formed, the count is honest, Solid culling ignores materials, every facet
/// normal points outward, and the model is stood up Z-up the way a slicer expects.
/// </summary>
public sealed class StlExportTests
{
    private const int HeaderBytes = 80;
    private const int FacetBytes = 50;

    [Fact]
    public void TheHeaderIsEightyBytesAndDoesNotClaimToBeAscii()
    {
        byte[] stl = WriteStl(SingleCube("Model"), new ExportOptions());

        Assert.Equal(HeaderBytes + 4 + FacetBytes * 12, stl.Length);
        Assert.NotEqual("solid", Encoding.ASCII.GetString(stl, 0, 5).ToLowerInvariant());
    }

    [Fact]
    public void TheCountMatchesTheFacetsWritten()
    {
        ParsedStl parsed = ParsedStl.Parse(WriteStl(SingleCube("Model"), new ExportOptions()));

        Assert.Equal(12, parsed.DeclaredCount);
        Assert.Equal(12, parsed.Facets.Count);
    }

    [Fact]
    public void SolidCullingDropsAnInteriorSeamWhateverTheMaterials()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project);
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        ModelBuilder.Fill(scene, element, glass, new Vector3I(1, 0, 0));

        ParsedStl parsed = ParsedStl.Parse(WriteStl(project, scene, new ExportOptions()));

        // Ten quads: two cubes less the two faces that meet, opacity notwithstanding.
        Assert.Equal(10 * 2, parsed.Facets.Count);
    }

    [Fact]
    public void EveryFacetNormalPointsOutward()
    {
        ParsedStl parsed = ParsedStl.Parse(WriteStl(SingleCube("Model"), new ExportOptions()));

        // The cube's centre after the Y-up to Z-up rotation of (x, -z, y).
        var centre = new Vector3(0.5f, -0.5f, 0.5f);

        foreach (StlFacet facet in parsed.Facets)
        {
            Vector3 centroid = (facet.A + facet.B + facet.C) / 3f;

            Assert.True(facet.Normal.Dot(centroid - centre) > 0f, "A facet normal points inward.");

            // The stored normal must also match the winding a slicer would derive.
            Vector3 geometric = (facet.B - facet.A).Cross(facet.C - facet.A).Normalized();
            Assert.True(facet.Normal.Dot(geometric) > 0.99f, "The stored normal disagrees with the winding.");
        }
    }

    [Fact]
    public void HeightBecomesTheZAxis()
    {
        // A column two cells tall along the model's Y (up).
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Column");
        ModelBuilder.Fill(
            scene, element, ModelBuilder.DefaultMaterial(project),
            Vector3I.Zero, new Vector3I(0, 1, 0));

        ParsedStl parsed = ParsedStl.Parse(WriteStl(project, scene, new ExportOptions()));
        IReadOnlyList<Vector3> corners = parsed.Facets
            .SelectMany(facet => new[] { facet.A, facet.B, facet.C })
            .ToList();

        // Two cells of height in Y become two units of Z; the footprint stays one.
        Assert.Equal(2f, corners.Max(corner => corner.Z) - corners.Min(corner => corner.Z));
        Assert.Equal(1f, corners.Max(corner => corner.X) - corners.Min(corner => corner.X));
    }

    [Fact]
    public void ScaleSizesTheModelInMillimetres()
    {
        ParsedStl parsed = ParsedStl.Parse(WriteStl(SingleCube("Model"), new ExportOptions(Scale: 2f)));
        IReadOnlyList<Vector3> corners = parsed.Facets
            .SelectMany(facet => new[] { facet.A, facet.B, facet.C })
            .ToList();

        Assert.Equal(2f, corners.Max(corner => corner.X) - corners.Min(corner => corner.X));
    }

    private static (IProject Project, IScene Scene) SingleCube(string name)
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, name);
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        return (project, scene);
    }

    private static byte[] WriteStl((IProject Project, IScene Scene) built, ExportOptions options) =>
        WriteStl(built.Project, built.Scene, options);

    private static byte[] WriteStl(IProject project, IScene scene, ExportOptions options)
    {
        ExportModel model = ExportFactory.CreateExporter()
            .BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Solid);

        var sink = new BufferingExportSink("part");
        new StlFormat().Write(model, sink, options);

        return sink.Collect().Single().Bytes;
    }

    private readonly record struct StlFacet(Vector3 Normal, Vector3 A, Vector3 B, Vector3 C);

    private sealed class ParsedStl
    {
        private ParsedStl(int declaredCount, IReadOnlyList<StlFacet> facetsList)
        {
            DeclaredCount = declaredCount;
            Facets = facetsList;
        }

        public int DeclaredCount { get; }

        public IReadOnlyList<StlFacet> Facets { get; }

        public static ParsedStl Parse(byte[] stl)
        {
            using var reader = new BinaryReader(new MemoryStream(stl));

            reader.ReadBytes(HeaderBytes);
            var count = (int)reader.ReadUInt32();

            var facetsList = new List<StlFacet>(count);

            for (var i = 0; i < count; i++)
            {
                Vector3 normal = ReadVector(reader);
                Vector3 a = ReadVector(reader);
                Vector3 b = ReadVector(reader);
                Vector3 c = ReadVector(reader);
                reader.ReadUInt16();

                facetsList.Add(new StlFacet(normal, a, b, c));
            }

            return new ParsedStl(count, facetsList);
        }

        private static Vector3 ReadVector(BinaryReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
