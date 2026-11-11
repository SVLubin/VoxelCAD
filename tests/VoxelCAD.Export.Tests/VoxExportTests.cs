using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The MagicaVoxel writer. The file is parsed back to check the chunk skeleton,
/// the palette mapping, and the Y-up to Z-up turn; the refusal path is checked
/// through the public exporter, which must name the offending element and leave no
/// file behind.
/// </summary>
public sealed class VoxExportTests
{
    [Fact]
    public void TheFileOpensWithTheVoxMagicAndVersion()
    {
        byte[] vox = WriteVox(SingleVoxel());

        Assert.Equal("VOX ", Encoding.ASCII.GetString(vox, 0, 4));
        Assert.Equal(150, BitConverter.ToInt32(vox, 4));
    }

    [Fact]
    public void EachFilledCellBecomesOneVoxelWithItsPaletteIndex()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid red = project.CreateMaterial("Red", new Color(1f, 0f, 0f));
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        ModelBuilder.Fill(scene, element, red, new Vector3I(1, 0, 0));

        ParsedVox parsed = ParsedVox.Parse(WriteVox(project, scene));

        Assert.Equal(2, parsed.Voxels.Count);

        // Default is palette index 0, Red is index 1, so their colour indices are
        // 1 and 2 — MagicaVoxel reserves 0 for empty.
        Assert.Contains(parsed.Voxels, voxel => voxel.ColorIndex == 1);
        Assert.Contains(parsed.Voxels, voxel => voxel.ColorIndex == 2);
    }

    [Fact]
    public void ThePaletteHoldsEachMaterialWhereItsColourIndexPoints()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid red = project.CreateMaterial("Red", new Color(1f, 0f, 0f));
        Guid blue = project.CreateMaterial("Blue", new Color(0f, 0f, 1f));
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, red, Vector3I.Zero);
        ModelBuilder.Fill(scene, element, blue, new Vector3I(1, 0, 0));

        ParsedVox parsed = ParsedVox.Parse(WriteVox(project, scene));

        // Red is palette index 1, Blue index 2, so colour index k reads RGBA[k-1]:
        // Red's colour sits at entry 1, Blue's at entry 2.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Entry(parsed.Palette, 1));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Entry(parsed.Palette, 2));
    }

    [Fact]
    public void HeightAlongYComesOutAlongZWithoutMirroring()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid red = project.CreateMaterial("Red", new Color(1f, 0f, 0f));
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero, new Vector3I(1, 0, 0));
        ModelBuilder.Fill(scene, element, red, new Vector3I(0, 3, 0));

        ParsedVox parsed = ParsedVox.Parse(WriteVox(project, scene));

        // Footprint two wide in X, one deep, four tall: the Y=3 cell rose to Z=3.
        Assert.Equal(new Vector3I(2, 1, 4), parsed.Size);

        ParsedVoxel top = Assert.Single(parsed.Voxels, voxel => voxel.ColorIndex == 2);
        Assert.Equal((0, 0, 3), (top.X, top.Y, top.Z));
    }

    [Fact]
    public void ARotatedElementIsRefusedByName()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Tilted");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.SetFieldMode(element, TransformChannel.Rotation, FieldMode.Float);
        scene.SetTransform(element, TransformChannel.Rotation, new Vector3(0f, 45f, 0f));

        string directory = TempDirectory();

        try
        {
            string path = Path.Combine(directory, "part.vox");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ExportFactory.CreateExporter().Export(project, scene, path, new ExportOptions()));

            Assert.Contains("Tilted", error.Message);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ASceneLargerThanTheModelLimitIsRefused()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject(new Vector3I(300, 4, 4));
        Guid element = ModelBuilder.VoxelElement(scene, "Wide");
        ModelBuilder.Fill(
            scene, element, ModelBuilder.DefaultMaterial(project),
            Vector3I.Zero, new Vector3I(299, 0, 0));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => WriteVox(project, scene));

        Assert.Contains("256", error.Message);
    }

    private static (IProject Project, IScene Scene) SingleVoxel()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        return (project, scene);
    }

    private static byte[] WriteVox((IProject Project, IScene Scene) built) =>
        WriteVox(built.Project, built.Scene);

    private static byte[] WriteVox(IProject project, IScene scene)
    {
        ExportModel model = ExportFactory.CreateExporter()
            .BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid);

        var sink = new BufferingExportSink("part");
        new VoxFormat().Write(model, sink, new ExportOptions());

        return sink.Collect().Single().Bytes;
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "voxelcad-vox-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>The four RGBA bytes of palette entry <paramref name="index"/>.</summary>
    private static byte[] Entry(byte[] palette, int index) =>
        palette.Skip(index * 4).Take(4).ToArray();

    private readonly record struct ParsedVoxel(byte X, byte Y, byte Z, byte ColorIndex);

    private sealed class ParsedVox
    {
        private ParsedVox(Vector3I size, IReadOnlyList<ParsedVoxel> voxelsList, byte[] palette)
        {
            Size = size;
            Voxels = voxelsList;
            Palette = palette;
        }

        public Vector3I Size { get; }

        public IReadOnlyList<ParsedVoxel> Voxels { get; }

        /// <summary>The raw 1024-byte RGBA block; entry <c>i</c> is at <c>i * 4</c>.</summary>
        public byte[] Palette { get; }

        public static ParsedVox Parse(byte[] vox)
        {
            using var reader = new BinaryReader(new MemoryStream(vox));

            reader.ReadBytes(4);
            reader.ReadInt32();

            var size = Vector3I.Zero;
            var voxelsList = new List<ParsedVoxel>();
            var palette = new byte[256 * 4];

            // MAIN header, then walk its children by their content lengths.
            reader.ReadBytes(4);
            reader.ReadInt32();
            reader.ReadInt32();

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                int contentBytes = reader.ReadInt32();
                reader.ReadInt32();
                byte[] content = reader.ReadBytes(contentBytes);

                switch (id)
                {
                    case "SIZE":
                        size = new Vector3I(
                            BitConverter.ToInt32(content, 0),
                            BitConverter.ToInt32(content, 4),
                            BitConverter.ToInt32(content, 8));
                        break;

                    case "XYZI":
                        int count = BitConverter.ToInt32(content, 0);
                        for (var i = 0; i < count; i++)
                        {
                            var offset = 4 + i * 4;
                            voxelsList.Add(new ParsedVoxel(
                                content[offset], content[offset + 1], content[offset + 2], content[offset + 3]));
                        }
                        break;

                    case "RGBA":
                        Array.Copy(content, palette, palette.Length);
                        break;
                }
            }

            return new ParsedVox(size, voxelsList, palette);
        }
    }
}
