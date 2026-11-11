using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The hand-rolled ASCII FBX writer. Blender reads only binary FBX, so correctness
/// is proven by parsing the ASCII back: the sections are present and Y-up, the
/// polygons decode with FBX's bit-negated last index and wind outward, per-polygon
/// materials carry the right indices, and each element is its own model.
/// </summary>
public sealed class FbxExportTests
{
    private static readonly Vector3 CubeCentre = new(0.5f, 0.5f, 0.5f);

    [Fact]
    public void TheFileDeclaresVersionAndAYUpScene()
    {
        string fbx = WriteFbx(SingleCube("Model"), new ExportOptions());

        Assert.Contains("FBXVersion: 7400", fbx);
        Assert.Contains("Objects:", fbx);
        Assert.Contains("Connections:", fbx);
        Assert.Contains("P: \"UpAxis\", \"int\", \"Integer\", \"\",1", fbx);
    }

    [Fact]
    public void TheCubeGeometryHasTheExpectedArrayLengths()
    {
        string fbx = WriteFbx(SingleCube("Model"), new ExportOptions());

        // 24 welded corners = 72 vertex components; 12 triangles = 36 polygon
        // indices and, one normal per polygon-vertex, 108 normal components.
        Assert.Equal(72, Floats(fbx, "Vertices").Count);
        Assert.Equal(36, Ints(fbx, "PolygonVertexIndex").Count);
        Assert.Equal(108, Floats(fbx, "Normals").Count);
    }

    [Fact]
    public void EveryPolygonEndsOnABitNegatedIndexAndWindsOutward()
    {
        string fbx = WriteFbx(SingleCube("Model"), new ExportOptions());
        IReadOnlyList<float> vertices = Floats(fbx, "Vertices");
        IReadOnlyList<int> indices = Ints(fbx, "PolygonVertexIndex");

        var corners = new HashSet<Vector3>();

        for (var i = 0; i < indices.Count; i += 3)
        {
            // FBX marks a polygon's end by bit-negating its last index.
            Assert.True(indices[i] >= 0);
            Assert.True(indices[i + 1] >= 0);
            Assert.True(indices[i + 2] < 0);

            Vector3 a = Vertex(vertices, indices[i]);
            Vector3 b = Vertex(vertices, indices[i + 1]);
            Vector3 c = Vertex(vertices, -indices[i + 2] - 1);

            corners.Add(a);
            corners.Add(b);
            corners.Add(c);

            Vector3 geometricNormal = (b - a).Cross(c - a);
            Vector3 outward = (a + b + c) / 3f - CubeCentre;

            Assert.True(geometricNormal.Dot(outward) > 0f, "An FBX polygon winds inward.");
        }

        Assert.Equal(8, corners.Count);
    }

    [Fact]
    public void PolygonsCarryTheMaterialOfTheirSurface()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project);
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        ModelBuilder.Fill(scene, element, glass, new Vector3I(1, 0, 0));

        string fbx = WriteFbx(project, scene, new ExportOptions());
        IReadOnlyList<int> materials = Ints(fbx, "Materials");
        IReadOnlyList<int> indices = Ints(fbx, "PolygonVertexIndex");

        // One material index a polygon, and both palette indices are used.
        Assert.Equal(indices.Count / 3, materials.Count);
        Assert.Contains(0, materials);
        Assert.Contains(1, materials);
    }

    [Fact]
    public void EachElementBecomesItsOwnModelAndGeometry()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid material = ModelBuilder.DefaultMaterial(project);
        ModelBuilder.Fill(scene, ModelBuilder.VoxelElement(scene, "Alpha"), material, Vector3I.Zero);
        ModelBuilder.Fill(scene, ModelBuilder.VoxelElement(scene, "Beta"), material, new Vector3I(4, 0, 0));

        string fbx = WriteFbx(project, scene, new ExportOptions());

        Assert.Equal(2, Count(fbx, "Geometry: "));
        Assert.Equal(2, Count(fbx, "Model: "));
        Assert.Contains("\"Model::Alpha\"", fbx);
        Assert.Contains("\"Model::Beta\"", fbx);
    }

    [Fact]
    public void AMaterialCarriesItsDiffuseColourAndOpacity()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project);
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, glass, Vector3I.Zero);

        string fbx = WriteFbx(project, scene, new ExportOptions());

        Assert.Contains("\"Material::Glass\"", fbx);
        Assert.Contains("P: \"DiffuseColor\", \"Color\", \"\", \"A\",0.4,0.7,1", fbx);
        Assert.Contains("P: \"Opacity\", \"double\", \"Number\", \"\",0.45", fbx);
    }

    [Fact]
    public void EveryMaterialIsConnectedToEveryModel()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        project.CreateMaterial("Extra", new Color(0.1f, 0.2f, 0.3f));
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        string fbx = WriteFbx(project, scene, new ExportOptions());

        // Geometry -> Model, Model -> RootNode(0), and both materials -> Model.
        Assert.Contains("C: \"OO\",3000000,0", fbx);
        Assert.Contains("C: \"OO\",2000000,3000000", fbx);
        Assert.Contains("C: \"OO\",1000000,3000000", fbx);
        Assert.Contains("C: \"OO\",1000001,3000000", fbx);
    }

    [Fact]
    public void ScaleMultipliesEveryVertex()
    {
        string fbx = WriteFbx(SingleCube("Model"), new ExportOptions(Scale: 10f));
        IReadOnlyList<float> vertices = Floats(fbx, "Vertices");

        Assert.All(vertices, component => Assert.True(component is 0f or 10f));
    }

    [Fact]
    public void ExportWritesOneFileToDisk()
    {
        (IProject project, IScene scene) = SingleCube("Model");
        string directory = Path.Combine(Path.GetTempPath(), "voxelcad-fbx-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "part.fbx");
            IReadOnlyList<ExportArtifact> artifacts =
                ExportFactory.CreateExporter().Export(project, scene, path, new ExportOptions());

            ExportArtifact only = Assert.Single(artifacts);
            Assert.True(File.Exists(only.Path));
            Assert.True(only.Bytes > 0);
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

    private static string WriteFbx((IProject Project, IScene Scene) built, ExportOptions options) =>
        WriteFbx(built.Project, built.Scene, options);

    private static string WriteFbx(IProject project, IScene scene, ExportOptions options)
    {
        ExportModel model = ExportFactory.CreateExporter()
            .BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        var sink = new BufferingExportSink("part");
        new FbxFormat().Write(model, sink, options);

        return Encoding.UTF8.GetString(sink.Collect().Single().Bytes);
    }

    private static Vector3 Vertex(IReadOnlyList<float> vertices, int index) =>
        new(vertices[index * 3], vertices[index * 3 + 1], vertices[index * 3 + 2]);

    private static IReadOnlyList<float> Floats(string fbx, string arrayName) =>
        Values(fbx, arrayName).Select(token => float.Parse(token, CultureInfo.InvariantCulture)).ToList();

    private static IReadOnlyList<int> Ints(string fbx, string arrayName) =>
        Values(fbx, arrayName).Select(token => int.Parse(token, CultureInfo.InvariantCulture)).ToList();

    private static int Count(string fbx, string prefix) =>
        fbx.Split('\n').Count(line => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// The comma-separated values of the first array named <paramref name="arrayName"/>:
    /// the <c>a:</c> line inside its <c>Name: *N { ... }</c> block.
    /// </summary>
    private static IReadOnlyList<string> Values(string fbx, string arrayName)
    {
        string[] lines = fbx.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith(arrayName + ": *", StringComparison.Ordinal))
                continue;

            for (var j = i + 1; j < lines.Length; j++)
            {
                string trimmed = lines[j].TrimStart();

                if (trimmed.StartsWith("a:", StringComparison.Ordinal))
                {
                    string body = trimmed.Substring("a:".Length).Trim();

                    return body.Length == 0
                        ? Array.Empty<string>()
                        : body.Split(',', StringSplitOptions.TrimEntries);
                }
            }
        }

        throw new InvalidOperationException($"No '{arrayName}' array in the FBX.");
    }
}
