using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// ASCII FBX 7.4 (7400), hand-rolled in pure C# — no third-party library, so the
/// output is portable and auditable. One <c>Geometry</c> and one <c>Model</c> per
/// element, a Lambert <c>Material</c> per palette entry (Diffuse + Opacity), and a
/// <c>Connections</c> block wiring them to the root.
/// </summary>
/// <remarks>
/// The IR is already FBX's frame — right-handed, Y-up — so geometry is emitted
/// unchanged and <c>GlobalSettings</c> declares Y-up. FBX marks the end of a
/// polygon by bit-negating its last index, so a triangle (a, b, c) is written
/// a, b, -(c+1). Normals are <c>ByPolygonVertex</c>/<c>Direct</c> and materials are
/// <c>ByPolygon</c>/<c>IndexToDirect</c>; every palette material is connected to
/// every model in palette order, so a polygon's material index is simply its
/// palette index. Geometry is world-baked, so each model sits at the origin under
/// the root — the element hierarchy is flattened.
/// </remarks>
internal sealed class FbxFormat : IExportFormat
{
    private const long RootNodeId = 0;
    private const long MaterialIdBase = 1_000_000;
    private const long GeometryIdBase = 2_000_000;
    private const long ModelIdBase = 3_000_000;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Extension => ".fbx";

    public string Label => "FBX (ASCII 7.4)";

    public ExportFacet Facet => ExportFacet.Mesh;

    public CullingPolicy DefaultCulling => CullingPolicy.Visual;

    public void Write(ExportModel model, IExportSink sink, ExportOptions options)
    {
        ExportMesh mesh = model.Mesh!;
        IReadOnlyList<ElementGeometry> elements = GroupByElement(mesh.Surfaces, options.Scale);

        var builder = new FbxBuilder();

        WriteHeader(builder);
        WriteGlobalSettings(builder);
        WriteDocuments(builder);
        WriteDefinitions(builder, elements.Count, mesh.Palette.Materials.Count);
        WriteObjects(builder, elements, mesh.Palette);
        WriteConnections(builder, elements, mesh.Palette.Materials.Count);

        using var writer = new StreamWriter(sink.Open(sink.BaseName + ".fbx"), Utf8WithoutBom) { NewLine = "\n" };
        writer.Write(builder.ToString());
    }

    private static void WriteHeader(FbxBuilder builder)
    {
        builder.Line("; FBX 7.4.0 project file");
        builder.Line("; Written by VoxelCAD");
        builder.Blank();

        builder.Open("FBXHeaderExtension");
        builder.Line("FBXHeaderVersion: 1003");
        builder.Line("FBXVersion: 7400");
        builder.Line("Creator: \"VoxelCAD\"");
        builder.Close();
    }

    private static void WriteGlobalSettings(FbxBuilder builder)
    {
        builder.Open("GlobalSettings");
        builder.Line("Version: 1000");
        builder.Open("Properties70");

        // Y-up, right-handed: up is +Y, front is +Z, the coordinate axis is +X.
        builder.Line("P: \"UpAxis\", \"int\", \"Integer\", \"\",1");
        builder.Line("P: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
        builder.Line("P: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
        builder.Line("P: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
        builder.Line("P: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
        builder.Line("P: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
        builder.Line("P: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");

        builder.Close();
        builder.Close();
    }

    private static void WriteDocuments(FbxBuilder builder)
    {
        builder.Open("Documents");
        builder.Line("Count: 1");
        builder.Open("Document: 1, \"\", \"Scene\"");
        builder.Line("RootNode: 0");
        builder.Close();
        builder.Close();
    }

    private static void WriteDefinitions(FbxBuilder builder, int elementCount, int materialCount)
    {
        builder.Open("Definitions");
        builder.Line("Version: 100");
        builder.Line($"Count: {elementCount * 2 + materialCount + 1}");

        builder.Open("ObjectType: \"GlobalSettings\"");
        builder.Line("Count: 1");
        builder.Close();

        builder.Open("ObjectType: \"Geometry\"");
        builder.Line($"Count: {elementCount}");
        builder.Close();

        builder.Open("ObjectType: \"Model\"");
        builder.Line($"Count: {elementCount}");
        builder.Close();

        builder.Open("ObjectType: \"Material\"");
        builder.Line($"Count: {materialCount}");
        builder.Close();

        builder.Close();
    }

    private static void WriteObjects(FbxBuilder builder, IReadOnlyList<ElementGeometry> elements, ExportPalette palette)
    {
        builder.Open("Objects");

        for (var i = 0; i < elements.Count; i++)
        {
            WriteGeometry(builder, elements[i], GeometryIdBase + i);
            WriteModel(builder, elements[i].Name, ModelIdBase + i);
        }

        for (var i = 0; i < palette.Materials.Count; i++)
            WriteMaterial(builder, palette.Materials[i], MaterialIdBase + i);

        builder.Close();
    }

    private static void WriteGeometry(FbxBuilder builder, ElementGeometry element, long id)
    {
        builder.Open($"Geometry: {id}, \"Geometry::{element.Name}\", \"Mesh\"");

        builder.Array("Vertices", element.VertexComponents());
        builder.Array("PolygonVertexIndex", element.PolygonIndices());
        builder.Line("GeometryVersion: 124");

        builder.Open("LayerElementNormal: 0");
        builder.Line("Version: 101");
        builder.Line("Name: \"\"");
        builder.Line("MappingInformationType: \"ByPolygonVertex\"");
        builder.Line("ReferenceInformationType: \"Direct\"");
        builder.Array("Normals", element.NormalComponents());
        builder.Close();

        builder.Open("LayerElementMaterial: 0");
        builder.Line("Version: 101");
        builder.Line("Name: \"\"");
        builder.Line("MappingInformationType: \"ByPolygon\"");
        builder.Line("ReferenceInformationType: \"IndexToDirect\"");
        builder.Array("Materials", element.PolygonMaterials());
        builder.Close();

        builder.Open("Layer: 0");
        builder.Line("Version: 100");
        builder.Open("LayerElement");
        builder.Line("Type: \"LayerElementNormal\"");
        builder.Line("TypedIndex: 0");
        builder.Close();
        builder.Open("LayerElement");
        builder.Line("Type: \"LayerElementMaterial\"");
        builder.Line("TypedIndex: 0");
        builder.Close();
        builder.Close();

        builder.Close();
    }

    private static void WriteModel(FbxBuilder builder, string name, long id)
    {
        builder.Open($"Model: {id}, \"Model::{name}\", \"Mesh\"");
        builder.Line("Version: 232");
        builder.Open("Properties70");
        builder.Line("P: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
        builder.Line("P: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
        builder.Line("P: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
        builder.Close();
        builder.Line("Shading: Y");
        builder.Line("Culling: \"CullingOff\"");
        builder.Close();
    }

    private static void WriteMaterial(FbxBuilder builder, ExportMaterial material, long id)
    {
        Color colour = material.Rgba;

        builder.Open($"Material: {id}, \"Material::{material.Name}\", \"\"");
        builder.Line("Version: 102");
        builder.Line("ShadingModel: \"Lambert\"");
        builder.Line("MultiLayer: 0");
        builder.Open("Properties70");
        builder.Line(
            $"P: \"DiffuseColor\", \"Color\", \"\", \"A\",{Format(colour.R)},{Format(colour.G)},{Format(colour.B)}");
        builder.Line($"P: \"Opacity\", \"double\", \"Number\", \"\",{Format(colour.A)}");
        builder.Close();
        builder.Close();
    }

    private static void WriteConnections(FbxBuilder builder, IReadOnlyList<ElementGeometry> elements, int materialCount)
    {
        builder.Open("Connections");

        for (var i = 0; i < elements.Count; i++)
        {
            long modelId = ModelIdBase + i;

            builder.Line($"C: \"OO\",{modelId},{RootNodeId}");
            builder.Line($"C: \"OO\",{GeometryIdBase + i},{modelId}");

            // Every material to every model, in palette order, so a polygon's
            // material index is its palette index with nothing to remap.
            for (var m = 0; m < materialCount; m++)
                builder.Line($"C: \"OO\",{MaterialIdBase + m},{modelId}");
        }

        builder.Close();
    }

    /// <summary>
    /// Combines an element's per-material surfaces into one geometry, remapping each
    /// surface's indices past the vertices already added. Surfaces are contiguous
    /// per element in the mesh, so a run of one name is one element.
    /// </summary>
    private static IReadOnlyList<ElementGeometry> GroupByElement(IReadOnlyList<ExportSurface> surfaces, float scale)
    {
        var elementsList = new List<ElementGeometry>();
        ElementGeometry? current = null;

        foreach (ExportSurface surface in surfaces)
        {
            if (current == null || current.Name != surface.ElementName)
            {
                current = new ElementGeometry(surface.ElementName, scale);
                elementsList.Add(current);
            }

            current.Add(surface);
        }

        return elementsList;
    }

    private static string Format(float value) =>
        value == 0f ? "0" : value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>One element's combined mesh, ready to serialise as FBX arrays.</summary>
    private sealed class ElementGeometry
    {
        private readonly float _scale;
        private readonly List<Vector3> _positionsList = new();
        private readonly List<Vector3> _normalsList = new();
        private readonly List<(int A, int B, int C, int Material)> _trianglesList = new();

        public ElementGeometry(string name, float scale)
        {
            Name = name;
            _scale = scale;
        }

        public string Name { get; }

        public void Add(ExportSurface surface)
        {
            var offset = _positionsList.Count;

            _positionsList.AddRange(surface.Positions);
            _normalsList.AddRange(surface.Normals);

            for (var i = 0; i < surface.Indices.Count; i += 3)
            {
                _trianglesList.Add((
                    offset + surface.Indices[i],
                    offset + surface.Indices[i + 1],
                    offset + surface.Indices[i + 2],
                    surface.MaterialIndex));
            }
        }

        public IEnumerable<string> VertexComponents()
        {
            foreach (Vector3 position in _positionsList)
            {
                Vector3 scaled = position * _scale;

                yield return Format(scaled.X);
                yield return Format(scaled.Y);
                yield return Format(scaled.Z);
            }
        }

        /// <summary>Triangle indices with FBX's bit-negated last index per polygon.</summary>
        public IEnumerable<string> PolygonIndices()
        {
            foreach ((int a, int b, int c, int _) in _trianglesList)
            {
                yield return a.ToString(CultureInfo.InvariantCulture);
                yield return b.ToString(CultureInfo.InvariantCulture);
                yield return (-(c + 1)).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Per polygon-vertex normals, in the order the indices are written.</summary>
        public IEnumerable<string> NormalComponents()
        {
            var componentsList = new List<string>(_trianglesList.Count * 9);

            foreach ((int a, int b, int c, int _) in _trianglesList)
            {
                AppendNormal(componentsList, a);
                AppendNormal(componentsList, b);
                AppendNormal(componentsList, c);
            }

            return componentsList;
        }

        public IEnumerable<string> PolygonMaterials()
        {
            foreach ((int _, int _, int _, int material) in _trianglesList)
                yield return material.ToString(CultureInfo.InvariantCulture);
        }

        private void AppendNormal(List<string> componentsList, int vertex)
        {
            Vector3 normal = _normalsList[vertex];

            componentsList.Add(Format(normal.X));
            componentsList.Add(Format(normal.Y));
            componentsList.Add(Format(normal.Z));
        }
    }

    /// <summary>An indented-text emitter for the FBX node tree.</summary>
    private sealed class FbxBuilder
    {
        private readonly StringBuilder _builder = new();
        private int _depth;

        public void Blank() => _builder.Append('\n');

        public void Line(string text)
        {
            Indent();
            _builder.Append(text).Append('\n');
        }

        public void Open(string header)
        {
            Indent();
            _builder.Append(header);

            // Every FBX node ends its name with a colon; a header carrying
            // properties already has one, a bare node name does not.
            if (!header.Contains(':'))
                _builder.Append(':');

            _builder.Append(" {\n");
            _depth++;
        }

        public void Close()
        {
            _depth--;
            Indent();
            _builder.Append("}\n");
        }

        public void Array(string name, IEnumerable<string> values)
        {
            List<string> valuesList = values.ToList();

            Open($"{name}: *{valuesList.Count}");
            Line("a: " + string.Join(",", valuesList));
            Close();
        }

        public override string ToString() => _builder.ToString();

        private void Indent() => _builder.Append('\t', _depth);
    }
}
