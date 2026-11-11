using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// Wavefront OBJ, the reference mesh exporter: text geometry with a companion
/// <c>.mtl</c> for colour and opacity. The IR is already OBJ's frame — right-handed,
/// Y-up, CCW front faces — so the writer only formats numbers and applies scale.
/// </summary>
/// <remarks>
/// One <c>o</c> per element, one <c>usemtl</c> per surface, 1-based indices global
/// across the file. Positions and normals are emitted together per surface, so a
/// vertex and its normal always share an index and a face reads <c>v//vn</c> with
/// the two equal.
/// </remarks>
internal sealed class ObjFormat : IExportFormat
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Extension => ".obj";

    public string Label => "Wavefront OBJ";

    public ExportFacet Facet => ExportFacet.Mesh;

    public CullingPolicy DefaultCulling => CullingPolicy.Visual;

    public void Write(ExportModel model, IExportSink sink, ExportOptions options)
    {
        ExportMesh mesh = model.Mesh!;
        string materialFileName = sink.BaseName + ".mtl";

        WriteGeometry(mesh, sink, materialFileName, options.Scale);
        WriteMaterials(mesh.Palette, sink.Open(materialFileName));
    }

    private static void WriteGeometry(ExportMesh mesh, IExportSink sink, string materialFileName, float scale)
    {
        using var writer = NewWriter(sink.Open(sink.BaseName + ".obj"));

        writer.WriteLine("# VoxelCAD OBJ export");
        writer.WriteLine($"mtllib {materialFileName}");

        var vertexBase = 0;
        string? currentElement = null;

        foreach (ExportSurface surface in mesh.Surfaces)
        {
            if (surface.ElementName != currentElement)
            {
                writer.WriteLine($"o {surface.ElementName}");
                currentElement = surface.ElementName;
            }

            writer.WriteLine($"usemtl {MaterialName(mesh.Palette, surface.MaterialIndex)}");

            foreach (Vector3 position in surface.Positions)
            {
                Vector3 scaled = position * scale;
                writer.WriteLine($"v {Format(scaled.X)} {Format(scaled.Y)} {Format(scaled.Z)}");
            }

            foreach (Vector3 normal in surface.Normals)
                writer.WriteLine($"vn {Format(normal.X)} {Format(normal.Y)} {Format(normal.Z)}");

            for (var i = 0; i < surface.Indices.Count; i += 3)
            {
                int a = vertexBase + surface.Indices[i] + 1;
                int b = vertexBase + surface.Indices[i + 1] + 1;
                int c = vertexBase + surface.Indices[i + 2] + 1;

                writer.WriteLine($"f {a}//{a} {b}//{b} {c}//{c}");
            }

            vertexBase += surface.Positions.Count;
        }
    }

    private static void WriteMaterials(ExportPalette palette, Stream output)
    {
        using StreamWriter writer = NewWriter(output);

        writer.WriteLine("# VoxelCAD MTL export");

        for (var index = 0; index < palette.Materials.Count; index++)
        {
            ExportMaterial material = palette.Materials[index];
            Color colour = material.Rgba;

            writer.WriteLine($"newmtl {MaterialName(palette, index)}");
            writer.WriteLine($"Kd {Format(colour.R)} {Format(colour.G)} {Format(colour.B)}");
            writer.WriteLine($"d {Format(colour.A)}");
            writer.WriteLine("illum 2");
        }
    }

    private static StreamWriter NewWriter(Stream output) =>
        new(output, Utf8WithoutBom) { NewLine = "\n" };

    /// <summary>The material's name, or a stable fallback when it is blank.</summary>
    private static string MaterialName(ExportPalette palette, int index)
    {
        string name = palette.Materials[index].Name;

        return string.IsNullOrWhiteSpace(name) ? $"material_{index}" : name;
    }

    /// <summary>
    /// Trims trailing zeros and normalises negative zero, so an integer voxel
    /// coordinate reads as <c>5</c> and a unit normal as <c>1</c> — clean, and
    /// stable enough for a golden file.
    /// </summary>
    private static string Format(float value) =>
        value == 0f ? "0" : value.ToString("0.######", CultureInfo.InvariantCulture);
}
