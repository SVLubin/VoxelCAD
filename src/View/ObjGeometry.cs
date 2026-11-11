using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// An OBJ file as surfaces, read without an engine.
/// </summary>
/// <remarks>
/// <para>
/// Three conventions differ from Godot's and all three are corrected here, because
/// each of them fails silently. Faces are wound counter-clockwise, so every
/// triangle is reversed. The texture's V runs up from the bottom, so it is flipped.
/// And indices are one-based, with negative ones counting back from the end of
/// what has been read so far.
/// </para>
/// <para>
/// Polygons are triangulated as a fan, which is exact for a convex face and is what
/// every reader does; a concave polygon comes out with a sliver, and no exporter
/// this would meet writes one.
/// </para>
/// </remarks>
internal static class ObjGeometry
{
    /// <summary>
    /// The surfaces, one per material named, in the order the file first names
    /// them — and the libraries it asks for, which whoever knows where the file
    /// sits can then find.
    /// </summary>
    public static bool TryRead(
        string text,
        out IReadOnlyList<ObjSurface> surfacesList,
        out IReadOnlyList<string> librariesList)
    {
        var positionsList = new List<Vector3>();
        var uvsList = new List<Vector2>();
        var normalsList = new List<Vector3>();
        var libraries = new List<string>();
        var buildersList = new List<SurfaceBuilder>();
        var buildersByMaterialDictionary = new Dictionary<string, SurfaceBuilder>();

        SurfaceBuilder current = Builder(string.Empty);

        foreach (string line in text.Split('\n'))
        {
            string[] words = Split(line);

            if (words.Length == 0)
                continue;

            switch (words[0])
            {
                case "v" when words.Length >= 4:
                    positionsList.Add(new Vector3(
                        Number(words[1]),
                        Number(words[2]),
                        Number(words[3])));
                    break;

                case "vt" when words.Length >= 3:
                    // Flipped: the file's V runs up from the bottom of the picture
                    // and a texture's runs down from the top.
                    uvsList.Add(new Vector2(Number(words[1]), 1f - Number(words[2])));
                    break;

                case "vn" when words.Length >= 4:
                    normalsList.Add(new Vector3(
                        Number(words[1]),
                        Number(words[2]),
                        Number(words[3])));
                    break;

                case "usemtl":
                    current = Builder(words.Length > 1 ? words[1] : string.Empty);
                    break;

                case "mtllib" when words.Length > 1:
                    for (var index = 1; index < words.Length; index++)
                        libraries.Add(words[index]);
                    break;

                case "f" when words.Length >= 4:
                    AddFace(current, words);
                    break;
            }
        }

        var built = new List<ObjSurface>();

        foreach (SurfaceBuilder builder in buildersList)
        {
            if (builder.Indices.Count > 0)
                built.Add(builder.Build());
        }

        surfacesList = built;
        librariesList = libraries;

        return built.Count > 0;

        SurfaceBuilder Builder(string material)
        {
            if (buildersByMaterialDictionary.TryGetValue(material, out SurfaceBuilder? held))
                return held;

            var fresh = new SurfaceBuilder(material, positionsList, uvsList, normalsList);

            buildersByMaterialDictionary[material] = fresh;
            buildersList.Add(fresh);

            return fresh;
        }
    }

    /// <summary>
    /// A fan from the first corner, with each triangle's last two corners swapped:
    /// counter-clockwise to clockwise.
    /// </summary>
    private static void AddFace(SurfaceBuilder builder, string[] words)
    {
        for (var corner = 2; corner + 1 < words.Length; corner++)
        {
            int a = builder.Vertex(words[1]);
            int b = builder.Vertex(words[corner]);
            int c = builder.Vertex(words[corner + 1]);

            if (a < 0 || b < 0 || c < 0)
                continue;

            builder.Indices.Add(a);
            builder.Indices.Add(c);
            builder.Indices.Add(b);
        }
    }

    private static string[] Split(string line)
    {
        int comment = line.IndexOf('#');
        string body = comment >= 0 ? line[..comment] : line;

        return body.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static float Number(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;

    /// <summary>
    /// One surface being assembled. A corner is identified by the whole
    /// <c>v/vt/vn</c> triple, so two faces meeting at one position with different
    /// texture coordinates get a vertex each — which is what the format means and
    /// what stops a seam being smeared.
    /// </summary>
    private sealed class SurfaceBuilder
    {
        private readonly IReadOnlyList<Vector3> _positionsList;
        private readonly IReadOnlyList<Vector2> _uvsList;
        private readonly IReadOnlyList<Vector3> _normalsList;
        private readonly Dictionary<string, int> _indicesByCornerDictionary = new();
        private readonly List<Vector3> _verticesList = new();
        private readonly List<Vector2> _texturesList = new();
        private readonly List<Vector3> _shadingList = new();

        private bool _carriesUvs;
        private bool _carriesNormals;

        public SurfaceBuilder(
            string materialName,
            IReadOnlyList<Vector3> positionsList,
            IReadOnlyList<Vector2> uvsList,
            IReadOnlyList<Vector3> normalsList)
        {
            MaterialName = materialName;
            _positionsList = positionsList;
            _uvsList = uvsList;
            _normalsList = normalsList;
        }

        public string MaterialName { get; }

        public List<int> Indices { get; } = new();

        public int Vertex(string corner)
        {
            if (_indicesByCornerDictionary.TryGetValue(corner, out int held))
                return held;

            string[] parts = corner.Split('/');

            if (!TryResolve(parts, 0, _positionsList.Count, out int position))
                return -1;

            _verticesList.Add(_positionsList[position]);

            _texturesList.Add(TryResolve(parts, 1, _uvsList.Count, out int uv)
                ? Carry(_uvsList[uv], ref _carriesUvs)
                : Vector2.Zero);

            _shadingList.Add(TryResolve(parts, 2, _normalsList.Count, out int normal)
                ? Carry(_normalsList[normal], ref _carriesNormals)
                : Vector3.Zero);

            int index = _verticesList.Count - 1;

            _indicesByCornerDictionary[corner] = index;

            return index;
        }

        public ObjSurface Build()
        {
            Vector3[] positions = _verticesList.ToArray();

            return new ObjSurface(
                MaterialName,
                positions,
                _carriesNormals ? _shadingList.ToArray() : Smooth(positions, Indices),
                _carriesUvs ? _texturesList.ToArray() : Array.Empty<Vector2>(),
                Indices.ToArray());
        }

        private static T Carry<T>(T value, ref bool carried)
        {
            carried = true;

            return value;
        }

        /// <summary>
        /// Normals for a file that carries none: every face's own, accumulated at
        /// the corners it touches. Smooth rather than flat, because flat shading
        /// needs a vertex per face and that would undo the sharing the format is
        /// built on — and because a reference is drawn unshaded by default, under
        /// which neither is visible at all.
        /// </summary>
        private static Vector3[] Smooth(Vector3[] positions, List<int> indicesList)
        {
            var normals = new Vector3[positions.Length];

            for (var index = 0; index + 2 < indicesList.Count; index += 3)
            {
                int a = indicesList[index];
                int b = indicesList[index + 1];
                int c = indicesList[index + 2];

                // Not normalized: a triangle's cross product is twice its area, so
                // accumulating them weights each face by how much of the corner it
                // actually occupies.
                Vector3 face = (positions[b] - positions[a]).Cross(positions[c] - positions[a]);

                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }

            for (var index = 0; index < normals.Length; index++)
            {
                normals[index] = normals[index].LengthSquared() > 0f
                    ? normals[index].Normalized()
                    : Vector3.Up;
            }

            return normals;
        }

        /// <summary>
        /// One-based, and negative counts back from the end of what has been read
        /// so far — which is why this is asked as the file is walked rather than
        /// afterwards.
        /// </summary>
        private static bool TryResolve(string[] parts, int slot, int count, out int index)
        {
            index = -1;

            if (slot >= parts.Length || string.IsNullOrEmpty(parts[slot]))
                return false;

            if (!int.TryParse(parts[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int named))
                return false;

            index = named > 0 ? named - 1 : count + named;

            return index >= 0 && index < count;
        }
    }
}
