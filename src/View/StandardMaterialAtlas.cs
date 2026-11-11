using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Every shipped material as layers of two texture arrays, drawn by one shader —
/// so a model wearing three hundred of them is one surface and one draw call per
/// chunk instead of three hundred.
/// </summary>
/// <remarks>
/// An <b>array</b> rather than an atlas, and the reason is that these textures
/// tile: a material repeats along a surface, and hardware wrapping works on a
/// whole texture, never on a rectangle inside one. Packed side by side we would
/// have to wrap by hand, and every mip level would then blend across the joins —
/// on a repeating pattern that is a seam per tile rather than a rounding error. A
/// layer is a whole texture with its own wrap and its own mip chain.
/// <para>
/// Two arrays because five maps fit in eight channels: colour is three and gloss
/// is one, and the normal needs only two of its three (the third is recovered, a
/// unit vector's z being what is left) leaving room for the occlusion and the
/// metal. 300 layers of 64 square is 4.9 MB apiece — the same pixels the separate
/// materials held, in two objects instead of fifteen hundred.
/// </para>
/// <para>
/// What makes this simple is that every shipped material has the <em>same</em>
/// appearance — white, rough one, metal governed by its map — and cannot be
/// changed. So what differs between them is which layer to read, which is what a
/// number on a vertex carries. <b>How it tiles is the second such number</b>, and
/// it stopped being the same for all of them the moment a project material could
/// wear a pack name at a scale of its own: the period is per material, in the
/// custom channel beside the layer (<see cref="AtlasPlacement"/>). Anything else
/// an appearance holds — the colour, the roughness, the metal, the emission, the
/// relief — the atlas still cannot say per material, and a material differing in
/// one of them is drawn as though it did not.
/// </para>
/// </remarks>
internal sealed class StandardMaterialAtlas
{
    /// <summary>
    /// Layers reserved before anything is used. A texture array's depth is fixed
    /// when it is created, so a project that wears a handful of shipped materials
    /// would otherwise pay for the whole library — measured, three materials in an
    /// empty document held the same memory as three hundred. Sixteen because that
    /// is more than most models wear and small enough to be free.
    /// </summary>
    private const int InitialDepth = 16;

    private readonly Dictionary<string, int> _layerByNameDictionary = new();
    private readonly List<Image> _surfaceLayersList = new();
    private readonly List<Image> _reliefLayersList = new();
    private readonly Texture2DArray _surface = new();
    private readonly Texture2DArray _relief = new();

    private ShaderMaterial? _material;
    private ViewportEffects _effects = ViewportEffects.Full;
    private int _depth;

    /// <summary>
    /// The one material every shipped material is drawn with. Null until a layer
    /// has been asked for, because a project that uses none of them should pay
    /// nothing at all.
    /// </summary>
    public ShaderMaterial? Material => _material;

    /// <summary>
    /// Applies the viewport's optional effects to every shipped material at once —
    /// they share one material, so there is one place to write them.
    /// </summary>
    public void SetEffects(ViewportEffects effects)
    {
        _effects = effects;

        if (_material is null)
            return;

        _material.SetShaderParameter("occlusion", effects.Occlusion ? 1f : 0f);
        _material.SetShaderParameter("relief", effects.Factures ? 1f : 0f);
    }

    /// <summary>
    /// Which layer a shipped material reads, filling it the first time it is
    /// asked for. Minus one when the pack has no such material, which is how a
    /// caller knows to fall back to a material of its own.
    /// </summary>
    public int LayerOf(string name)
    {
        if (_layerByNameDictionary.TryGetValue(name, out int layer))
            return layer;

        if (!StandardMaterialPack.Has(name))
            return -1;

        layer = _layerByNameDictionary.Count;

        if (layer >= StandardMaterialPack.Names.Count)
            return -1;

        StandardMaterialPack.WriteLayers(name, out Image surface, out Image relief);

        // Kept, so growing the arrays is a re-upload rather than a re-read of the
        // pack: the images are what the depth costs anyway, and decoding five
        // more each time somebody crossed a power of two would be the one moment
        // a project stutters.
        _surfaceLayersList.Add(surface);
        _reliefLayersList.Add(relief);

        Reserve(layer + 1);

        _surface.UpdateLayer(surface, layer);
        _relief.UpdateLayer(relief, layer);

        _layerByNameDictionary[name] = layer;

        return layer;
    }

    /// <summary>
    /// Room for at least <paramref name="wanted"/> layers, doubling rather than
    /// growing by one so a project that keeps reaching for another material pays
    /// for the rebuild a handful of times rather than once per material.
    /// </summary>
    /// <remarks>
    /// The arrays are re-created in place rather than replaced, so the shader goes
    /// on pointing at the same two objects and nothing has to be re-bound. What it
    /// costs is re-uploading the layers already filled, which is why they are kept.
    /// </remarks>
    private void Reserve(int wanted)
    {
        if (wanted <= _depth)
            return;

        int depth = Math.Max(_depth == 0 ? InitialDepth : _depth * 2, wanted);
        depth = Math.Min(depth, StandardMaterialPack.Names.Count);

        int size = StandardMaterialPack.TexelsPerSide;

        var surfaces = new Godot.Collections.Array<Image>();
        var reliefs = new Godot.Collections.Array<Image>();

        for (var i = 0; i < depth; i++)
        {
            surfaces.Add(i < _surfaceLayersList.Count
                ? _surfaceLayersList[i]
                : Image.CreateEmpty(size, size, true, Image.Format.Rgba8));

            reliefs.Add(i < _reliefLayersList.Count
                ? _reliefLayersList[i]
                : Image.CreateEmpty(size, size, true, Image.Format.Rgba8));
        }

        _surface.CreateFromImages(surfaces);
        _relief.CreateFromImages(reliefs);
        _depth = depth;

        if (_material is not null)
            return;

        var shader = new Shader { Code = StandardMaterialShader.Source };

        _material = new ShaderMaterial { Shader = shader };
        _material.SetShaderParameter("surface_maps", _surface);
        _material.SetShaderParameter("relief_maps", _relief);

        // Whatever was asked for before the material existed — the effects are
        // pushed as the document is bound and the first layer is filled later.
        SetEffects(_effects);
    }
}
