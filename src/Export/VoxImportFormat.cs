using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// Reads MagicaVoxel <c>.vox</c>: the models, the palette entries anybody wears,
/// and the scene graph that groups and places them.
/// </summary>
/// <remarks>
/// The placement is done entirely in <see cref="CellTransform"/>, which is what
/// makes it exact. MagicaVoxel puts a model's voxel <c>v</c> at world cell
/// <c>t + R·(v − c)</c>, where <c>c</c> is the middle of the model's grid by
/// integer division — a rule confirmed against MagicaVoxel's own files rather
/// than taken from the specification, which does not state it: in <c>cars.vox</c>
/// both cars land with their grids exactly on the ground, at an odd height and at
/// an even one.
/// <para>
/// Because every step is a signed permutation and an integer offset, an imported
/// element lands on <see cref="CommonTypes.FieldMode.Integer"/> position and
/// <see cref="CommonTypes.FieldMode.Snap90"/> rotation exactly — there is no
/// placement this format can state that would degrade a field mode.
/// </para>
/// <para>
/// The graph's own shape is kept: a group becomes a grouping element holding its
/// parts, transform and all. Composing it per node and composing the whole chain
/// down to each shape give the same cells — checked over MagicaVoxel's own files
/// and the awkward fixtures beside them, 768,000 cells at the largest, including
/// nested groups that carry transforms and shape nodes reached by several paths.
/// </para>
/// </remarks>
internal sealed class VoxImportFormat : IImportFormat
{
    public string Extension => ".vox";

    public string Label => "MagicaVoxel";

    public ImportedFile Read(byte[] bytes, string name)
    {
        VoxDocument document = VoxChunkReader.Read(bytes);
        var palette = new VoxPaletteMap(document);
        var notesList = new List<string>();
        var builder = new Builder(document, palette, notesList, name);

        IReadOnlyList<ImportedNode> rootsList = builder.BuildRoots();

        return new ImportedFile(rootsList, palette.Materials, notesList);
    }

    /// <summary>
    /// Walks the graph once, turning each transform node into one element. Its own
    /// class so the walk can carry what it needs without threading five arguments
    /// through every step.
    /// </summary>
    private sealed class Builder
    {
        /// <summary>
        /// A cell index is one corner of its cell, so flipping an axis sends the
        /// cell at <c>k</c> to the one at <c>-k-1</c>. That <c>-1</c> is the whole
        /// difference between the index map and the direction map, and it has to
        /// be taken back out whenever a rotation is carried across the seam.
        /// </summary>
        private static readonly Vector3I IndexShift = new(0, 0, -1);

        /// <summary>
        /// How deep the scene graph may nest. MagicaVoxel's own files are four
        /// deep and its deepest fixture is six, so this is generous by two orders
        /// of magnitude — and it is a hard bound rather than the node count,
        /// because a file can name fifty thousand nodes in a straight line and
        /// the walk is recursive. Without it that is a stack overflow, which is
        /// not a refusal.
        /// </summary>
        private const int MaximumDepth = 256;

        private readonly VoxDocument _document;
        private readonly VoxPaletteMap _palette;
        private readonly List<string> _notesList;
        private readonly string _fileName;

        public Builder(
            VoxDocument document,
            VoxPaletteMap palette,
            List<string> notesList,
            string fileName)
        {
            _document = document;
            _palette = palette;
            _notesList = notesList;
            _fileName = fileName;
        }

        public IReadOnlyList<ImportedNode> BuildRoots()
        {
            // A file with no scene graph places its models at the origin, which is
            // what every reader of the format does and what this editor's own
            // writer assumes when it writes one.
            if (_document.Nodes.Count == 0)
            {
                var modelsList = new List<ImportedNode>(_document.Models.Count);

                for (var i = 0; i < _document.Models.Count; i++)
                    modelsList.Add(Shape(i, Basis.Identity, Vector3I.Zero, name: null));

                return modelsList;
            }

            IReadOnlyList<ImportedNode> rootsList = Transform(0, depth: 0);

            // MagicaVoxel always opens with an identity transform over an unnamed
            // group. Kept as an element it would be a wrapper nobody asked for, so
            // where it says nothing it is dropped and what it held becomes the
            // roots.
            return rootsList.Count == 1
                && rootsList[0].Volume == null
                && rootsList[0].Placement == CellTransform.Identity
                && rootsList[0].Name == _fileName
                    ? rootsList[0].Children
                    : rootsList;
        }

        /// <summary>
        /// One transform node as one element. What is under it decides whether the
        /// element holds cells or children, and which of the two placements it
        /// takes: a group's frame is its own, while a shape's also carries the
        /// model's centring and the normalising of its cells to a zero-based box.
        /// </summary>
        private IReadOnlyList<ImportedNode> Transform(int nodeId, int depth)
        {
            VoxDocument.VoxNode node = Node(nodeId, depth);

            if (node.Kind != VoxNodeKind.Transform)
            {
                throw Damaged(
                    $"node {nodeId} stands where a transform belongs and is a {node.Kind}");
            }

            if (node.Frames > 1)
                Note("This file animates a transform; its first frame was read.");

            VoxDocument.VoxNode child = Node(node.Child, depth);

            if (child.Kind == VoxNodeKind.Shape)
                return new[] { Shape(ModelOf(child), node.Rotation, node.Translation, node.Name) };

            if (VoxRotation.IsMirrored(node.Rotation))
            {
                throw Damaged(
                    $"the group '{node.Name ?? "(unnamed)"}' is mirrored, and a mirrored group "
                    + "cannot be expressed as a rotation of its own");
            }

            var childrenList = new List<ImportedNode>();

            foreach (int grandChild in child.Children)
                childrenList.AddRange(Transform(grandChild, depth + 1));

            Basis rotation = VoxAxis.TurnToWorld(node.Rotation);
            var placement = new CellTransform(
                rotation,
                VoxAxis.ToWorld(node.Translation) - CellTransform.Apply(rotation, IndexShift));

            return new[] { new ImportedNode(Name(node.Name), placement, volume: null, childrenList) };
        }

        /// <summary>
        /// A model's cells in this document's axes, normalised into a box at the
        /// origin, with the transform that puts them back where the file had them.
        /// </summary>
        private ImportedNode Shape(int modelIndex, Basis magicaRotation, Vector3I translation, string? name)
        {
            VoxDocument.VoxModel model = _document.Models[modelIndex];
            Vector3I size = model.Size;
            var centre = new Vector3I(size.X / 2, size.Y / 2, size.Z / 2);
            Basis rotation = VoxAxis.TurnToWorld(magicaRotation);

            // The node's translation with the model's own centring folded in, so
            // what is left is a plain "cell index times a rotation plus an offset".
            Vector3I magicaOffset = translation - CellTransform.Apply(magicaRotation, centre);

            // Normalising the cells to a zero-based box is itself a translation,
            // and it has to come back out of the placement or the model would sit
            // a grid away from where it belongs.
            var normalise = new Vector3I(0, 0, size.Y - 1);
            var placement = new CellTransform(
                rotation,
                VoxAxis.ToWorld(magicaOffset) - CellTransform.Apply(rotation, normalise));

            var cellsList = new List<ImportedCell>(model.Cells.Count);

            foreach ((Vector3I cell, byte colorIndex) in model.Cells)
            {
                cellsList.Add(new ImportedCell(
                    new Vector3I(cell.X, cell.Z, size.Y - 1 - cell.Y),
                    _palette.IndexOf(colorIndex)));
            }

            var volume = new ImportedVolume(
                new Vector3I(size.X, size.Z, size.Y),
                cellsList,
                new Vector3(centre.X + 0.5f, centre.Z + 0.5f, size.Y - centre.Y - 0.5f));

            if (VoxRotation.IsMirrored(rotation))
                (volume, placement) = Unmirror(volume, placement);

            return new ImportedNode(Name(name), placement, volume, Array.Empty<ImportedNode>());
        }

        /// <summary>
        /// Turns a reflection into cells. The <c>_r</c> byte can encode a mirroring
        /// as well as a rotation; this document's transform holds rotations only,
        /// so the flip is applied to the model's own lattice and the transform
        /// keeps what is left, which is a proper rotation.
        /// </summary>
        /// <remarks>
        /// MagicaVoxel does not write one, so this path exists for files from
        /// elsewhere. Folding the flip into the cells rather than refusing costs
        /// the user nothing they can see — the model lands exactly where the file
        /// asks — and costs them the ability to un-mirror it by editing the
        /// transform, which is the honest trade and is recorded in the ledger.
        /// </remarks>
        private static (ImportedVolume Volume, CellTransform Placement) Unmirror(
            ImportedVolume volume,
            CellTransform placement)
        {
            var flip = new CellTransform(
                new Basis(
                    column0: new Vector3(-1f, 0f, 0f),
                    column1: new Vector3(0f, 1f, 0f),
                    column2: new Vector3(0f, 0f, 1f)),
                new Vector3I(volume.Size.X - 1, 0, 0));

            var cellsList = new List<ImportedCell>(volume.Cells.Count);

            foreach (ImportedCell cell in volume.Cells)
                cellsList.Add(cell with { Cell = flip.Apply(cell.Cell) });

            // A point at local x lands at size − x, where a cell index lands at
            // size − 1 − x: the pivot is a point, so it takes the point's rule.
            var pivot = new Vector3(volume.Size.X - volume.Pivot.X, volume.Pivot.Y, volume.Pivot.Z);

            // The flip is its own inverse, so composing it back into the placement
            // leaves the cells where they were and the rotation proper.
            return (new ImportedVolume(volume.Size, cellsList, pivot), placement.Compose(flip));
        }

        private int ModelOf(VoxDocument.VoxNode shape)
        {
            if (shape.Models.Count == 0)
                throw Damaged("a shape node names no model");

            if (shape.Models.Count > 1)
                Note("This file animates a model; its first frame was read.");

            int modelIndex = shape.Models[0];

            if (modelIndex < 0 || modelIndex >= _document.Models.Count)
            {
                throw Damaged(
                    $"a shape names model {modelIndex}, but the file holds {_document.Models.Count}");
            }

            return modelIndex;
        }

        /// <summary>
        /// A node by id, refusing a graph that names what it never defined or
        /// refers back to itself. Depth is bounded by the node count, which no
        /// acyclic walk can exceed.
        /// </summary>
        private VoxDocument.VoxNode Node(int nodeId, int depth)
        {
            if (depth > MaximumDepth)
                throw Damaged($"its scene graph nests more than {MaximumDepth} deep");

            if (depth > _document.Nodes.Count)
                throw Damaged("its scene graph refers back to itself");

            return _document.Nodes.TryGetValue(nodeId, out VoxDocument.VoxNode? node)
                ? node
                : throw Damaged($"it places a node {nodeId} that it never defines");
        }

        /// <summary>
        /// What the file calls this part, or the file's own name where it says
        /// nothing. Names are left as the file gives them, duplicates included:
        /// what a name has to be unambiguous against is the scene it lands in,
        /// which only the projection knows, and composing here as well would leave
        /// a part called "Wheel 2 2".
        /// </summary>
        private string Name(string? name) => name ?? _fileName;

        private void Note(string note)
        {
            if (!_notesList.Contains(note))
                _notesList.Add(note);
        }

        private static InvalidOperationException Damaged(string reason) =>
            new($"This .vox file cannot be read: {reason}.");
    }
}
