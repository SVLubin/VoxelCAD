using System.IO;
using System.Text;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// Binary STL for 3D printing: an 80-byte header, a triangle count, then 50 bytes
/// a facet — a normal, three vertices, and a two-byte attribute field. No
/// materials and no shared vertices, which is all the format carries.
/// </summary>
/// <remarks>
/// Solid culling gives the watertight outer shell. The IR is Y-up, but a slicer
/// stands a model on the XY bed with Z as height, so the writer rotates Y-up to
/// Z-up — a proper rotation, so outward faces stay outward. Each facet's normal is
/// taken from its own rotated, scaled geometry, so it agrees with the winding
/// whichever a slicer trusts. <see cref="ExportOptions.Scale"/> is the millimetre
/// size of one voxel.
/// </remarks>
internal sealed class StlFormat : IExportFormat
{
    private const int HeaderBytes = 80;
    private const ushort NoAttributes = 0;

    public string Extension => ".stl";

    public string Label => "Binary STL";

    public ExportFacet Facet => ExportFacet.Mesh;

    public CullingPolicy DefaultCulling => CullingPolicy.Solid;

    public void Write(ExportModel model, IExportSink sink, ExportOptions options)
    {
        ExportMesh mesh = model.Mesh!;

        using var writer = new BinaryWriter(sink.Open(sink.BaseName + ".stl"));

        writer.Write(Header());
        writer.Write((uint)TriangleCount(mesh));

        foreach (ExportSurface surface in mesh.Surfaces)
        {
            for (var i = 0; i < surface.Indices.Count; i += 3)
            {
                Vector3 a = Place(surface.Positions[surface.Indices[i]], options.Scale);
                Vector3 b = Place(surface.Positions[surface.Indices[i + 1]], options.Scale);
                Vector3 c = Place(surface.Positions[surface.Indices[i + 2]], options.Scale);

                WriteFacet(writer, a, b, c);
            }
        }
    }

    private static int TriangleCount(ExportMesh mesh)
    {
        var count = 0;

        foreach (ExportSurface surface in mesh.Surfaces)
            count += surface.Indices.Count / 3;

        return count;
    }

    private static void WriteFacet(BinaryWriter writer, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = (b - a).Cross(c - a).Normalized();

        Write(writer, normal);
        Write(writer, a);
        Write(writer, b);
        Write(writer, c);
        writer.Write(NoAttributes);
    }

    private static void Write(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }

    /// <summary>Scale to millimetres, then rotate Y-up into the slicer's Z-up bed.</summary>
    private static Vector3 Place(Vector3 position, float scale)
    {
        Vector3 scaled = position * scale;

        return new Vector3(scaled.X, -scaled.Z, scaled.Y);
    }

    /// <summary>
    /// Eighty bytes that must not begin with the word "solid", or a reader takes
    /// the file for ASCII STL. A short label followed by zeros satisfies both.
    /// </summary>
    private static byte[] Header()
    {
        var header = new byte[HeaderBytes];
        byte[] label = Encoding.ASCII.GetBytes("VoxelCAD binary STL export");

        System.Array.Copy(label, header, label.Length);

        return header;
    }
}
