using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The OBJ writer, the reference exporter. The heavy invariants — winding and
/// welding — are checked by parsing the file back and measuring the geometry,
/// which catches a wrong winding a hand-written golden never could; the small,
/// stable pieces (the material file, the header) are pinned as text.
/// </summary>
public sealed class ObjExportTests
{
    private static readonly Vector3 CubeCentre = new(0.5f, 0.5f, 0.5f);

    [Fact]
    public void TheMaterialFileListsTheWholePaletteWithColourAndOpacity()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        ModelBuilder.Glass(project);
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        string mtl = WriteObj(project, scene, new ExportOptions())["part.mtl"];

        Assert.Equal(
            "# VoxelCAD MTL export\n"
            + "newmtl Default\n"
            + "Kd 0.7 0.7 0.72\n"
            + "d 1\n"
            + "illum 2\n"
            + "newmtl Glass\n"
            + "Kd 0.4 0.7 1\n"
            + "d 0.45\n"
            + "illum 2\n",
            mtl);
    }

    [Fact]
    public void TheGeometryFileOpensWithItsHeaderAndFullCounts()
    {
        string obj = WriteObj(SingleCube("Model"), new ExportOptions())["part.obj"];
        string[] lines = obj.Split('\n');

        Assert.Equal("# VoxelCAD OBJ export", lines[0]);
        Assert.Equal("mtllib part.mtl", lines[1]);
        Assert.Equal("o Model", lines[2]);
        Assert.Equal("usemtl Default", lines[3]);

        // A lone cube: six faces, four welded corners a face, two triangles a face.
        Assert.Equal(24, lines.Count(line => line.StartsWith("v ")));
        Assert.Equal(24, lines.Count(line => line.StartsWith("vn ")));
        Assert.Equal(12, lines.Count(line => line.StartsWith("f ")));
    }

    [Fact]
    public void EveryTriangleIsWoundSoItsNormalPointsOutward()
    {
        string obj = WriteObj(SingleCube("Model"), new ExportOptions())["part.obj"];
        ParsedObj parsed = ParsedObj.Parse(obj);

        Assert.Equal(12, parsed.Triangles.Count);

        foreach ((Vector3 a, Vector3 b, Vector3 c) in parsed.Triangles)
        {
            Vector3 geometricNormal = (b - a).Cross(c - a);
            Vector3 outward = (a + b + c) / 3f - CubeCentre;

            Assert.True(
                geometricNormal.Dot(outward) > 0f,
                $"Triangle ({a}, {b}, {c}) is wound inward.");
        }
    }

    [Fact]
    public void TheWeldedCornersAreExactlyTheEightCubeVertices()
    {
        string obj = WriteObj(SingleCube("Model"), new ExportOptions())["part.obj"];
        ParsedObj parsed = ParsedObj.Parse(obj);

        HashSet<Vector3> corners = parsed.Positions.ToHashSet();

        Assert.Equal(8, corners.Count);
        Assert.All(corners, corner =>
        {
            Assert.True(corner.X is 0f or 1f);
            Assert.True(corner.Y is 0f or 1f);
            Assert.True(corner.Z is 0f or 1f);
        });
    }

    [Fact]
    public void EachElementBecomesItsOwnObjectGroup()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid material = ModelBuilder.DefaultMaterial(project);
        ModelBuilder.Fill(scene, ModelBuilder.VoxelElement(scene, "Alpha"), material, Vector3I.Zero);
        ModelBuilder.Fill(scene, ModelBuilder.VoxelElement(scene, "Beta"), material, new Vector3I(4, 0, 0));

        string obj = WriteObj(project, scene, new ExportOptions())["part.obj"];

        Assert.Contains("o Alpha", obj.Split('\n'));
        Assert.Contains("o Beta", obj.Split('\n'));
    }

    [Fact]
    public void ScaleMultipliesEveryCoordinate()
    {
        string obj = WriteObj(SingleCube("Model"), new ExportOptions(Scale: 10f))["part.obj"];
        ParsedObj parsed = ParsedObj.Parse(obj);

        HashSet<Vector3> corners = parsed.Positions.ToHashSet();

        Assert.All(corners, corner =>
        {
            Assert.True(corner.X is 0f or 10f);
            Assert.True(corner.Y is 0f or 10f);
            Assert.True(corner.Z is 0f or 10f);
        });
    }

    [Fact]
    public void ExportWritesBothFilesToDisk()
    {
        (IProject project, IScene scene) = SingleCube("Model");
        string directory = Path.Combine(Path.GetTempPath(), "voxelcad-obj-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "part.obj");
            IReadOnlyList<ExportArtifact> artifacts =
                ExportFactory.CreateExporter().Export(project, scene, path, new ExportOptions());

            Assert.Equal(2, artifacts.Count);
            Assert.True(File.Exists(Path.Combine(directory, "part.obj")));
            Assert.True(File.Exists(Path.Combine(directory, "part.mtl")));
            Assert.All(artifacts, artifact => Assert.True(artifact.Bytes > 0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (IProject Project, IScene Scene) SingleCube(string name)
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, name);
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        return (project, scene);
    }

    private static IReadOnlyDictionary<string, string> WriteObj(
        (IProject Project, IScene Scene) built,
        ExportOptions options) =>
        WriteObj(built.Project, built.Scene, options);

    private static IReadOnlyDictionary<string, string> WriteObj(
        IProject project,
        IScene scene,
        ExportOptions options)
    {
        ExportModel model = ExportFactory.CreateExporter()
            .BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        var sink = new BufferingExportSink("part");
        new ObjFormat().Write(model, sink, options);

        return sink.Collect().ToDictionary(
            file => file.FileName,
            file => Encoding.UTF8.GetString(file.Bytes));
    }

    /// <summary>The parts of an OBJ these tests reason about: positions and triangles.</summary>
    private sealed class ParsedObj
    {
        private ParsedObj(IReadOnlyList<Vector3> positionsList, IReadOnlyList<(Vector3, Vector3, Vector3)> trianglesList)
        {
            Positions = positionsList;
            Triangles = trianglesList;
        }

        public IReadOnlyList<Vector3> Positions { get; }

        public IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> Triangles { get; }

        public static ParsedObj Parse(string obj)
        {
            var positionsList = new List<Vector3>();
            var trianglesList = new List<(Vector3, Vector3, Vector3)>();

            foreach (string line in obj.Split('\n'))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                    continue;

                if (parts[0] == "v")
                {
                    positionsList.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                }
                else if (parts[0] == "f")
                {
                    // One-based indices; the vertex is the token before the first slash.
                    int a = VertexIndex(parts[1]);
                    int b = VertexIndex(parts[2]);
                    int c = VertexIndex(parts[3]);

                    trianglesList.Add((positionsList[a], positionsList[b], positionsList[c]));
                }
            }

            return new ParsedObj(positionsList, trianglesList);
        }

        private static int VertexIndex(string token) =>
            int.Parse(token.Split('/')[0], CultureInfo.InvariantCulture) - 1;

        private static float ParseFloat(string token) =>
            float.Parse(token, CultureInfo.InvariantCulture);
    }
}
