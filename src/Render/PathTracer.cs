using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// Runs the tracer: owns a rendering device of its own, holds the compiled
/// scene on it, and accumulates samples a pass at a time.
/// </summary>
/// <remarks>
/// A <em>local</em> device rather than the one the viewport draws with, because a
/// render that stalls the editor is not a render anybody would start. It is also
/// why the passes are counted rather than run to the end in one call: whoever
/// owns the clock decides how much of a frame to spend here.
/// <para>
/// Progressive by construction. Every pass adds one sample per pixel into a float
/// buffer and nothing is ever divided until it is read, so stopping early is a
/// noisier picture of the same render rather than a different one.
/// </para>
/// </remarks>
internal sealed class PathTracer : IPathTracer
{
    private const int ParamsFloats = 52;

    private readonly RenderingDevice _device;
    private readonly RenderSettings _settings;
    private readonly RenderTileGrid _grid;
    private readonly TileScheduler _scheduler;
    private readonly byte[] _tileSamplesBytes;
    private readonly bool[] _touchedList;

    private float _exposure;

    private readonly Rid _shader;
    private readonly Rid _pipeline;
    private readonly Rid _resolveShader;
    private readonly Rid _resolvePipeline;
    private readonly Rid _resolveSet;
    private readonly Rid _resolved;
    private readonly Rid _accumulation;
    private readonly Rid _elements;
    private readonly Rid _voxels;
    private readonly Rid _materials;
    private readonly Rid _specularAlbedo;
    private readonly Rid _parameters;
    private readonly Rid _finishes;
    private readonly Rid _lights;
    private readonly Rid _tileSamples;
    private readonly Rid _depth;
    private readonly Rid _moments;
    private readonly Rid _noise;
    private readonly Rid _noiseShader;
    private readonly Rid _noisePipeline;
    private readonly Rid _noiseSet;
    private readonly float[] _noiseByTile;

    private RenderTile _region;
    private readonly Rid _uniformSet;
    private readonly Rid _glareDownShader;
    private readonly Rid _glareDownPipeline;
    private readonly Rid _glareDownSet;
    private readonly Rid _glareBlurShader;
    private readonly Rid _glareBlurPipeline;
    private readonly Rid _glareAcross;
    private readonly Rid _glareDown;
    private readonly Rid _glareFirst;
    private readonly Rid _glareSecond;
    private readonly int _glareWidth;
    private readonly int _glareHeight;

    private bool _disposed;
    private bool _inFlight;

    private PathTracer(
        RenderingDevice device,
        RenderSettings settings,
        RenderScene scene,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes)
    {
        _device = device;
        _settings = settings;
        _exposure = settings.Exposure;

        _grid = new RenderTileGrid(settings.Width, settings.Height);
        _scheduler = new TileScheduler(_grid.Count, settings.Samples);
        _tileSamplesBytes = new byte[_grid.Count * sizeof(uint)];
        _touchedList = new bool[_grid.Count];

        RDShaderSpirV spirV = Compile(device, PathTraceShader.Source);

        _shader = device.ShaderCreateFromSpirV(spirV);
        _pipeline = device.ComputePipelineCreate(_shader);

        _resolveShader = device.ShaderCreateFromSpirV(Compile(device, ResolveShader.Source));
        _resolvePipeline = device.ComputePipelineCreate(_resolveShader);

        int pixels = settings.Width * settings.Height;

        _accumulation = CreateBuffer(device, new float[pixels * 4]);
        _elements = CreateBuffer(device, PackElements(scene));
        _voxels = CreateBuffer(device, PackVoxels(scene));
        (float[] atlas, Dictionary<string, int> offsets) = PackFinishes(scene, finishes);

        _finishes = CreateBuffer(device, atlas);
        _lights = CreateBuffer(device, PackLights(scene));
        _materials = CreateBuffer(device, PackMaterials(scene, finishes, offsets));

        // What the specular lobe returns when it scatters once, so the shader can
        // hand back what a second scattering would have carried. A table rather
        // than a fit, because a fit of a different integral is what put the energy
        // out by a third at roughness one.
        _specularAlbedo = CreateBuffer(device, SpecularAlbedoTable.Values);
        _parameters = CreateBuffer(device, PackParameters(scene, settings, camera));

        _resolved = device.StorageBufferCreate((uint)(pixels * sizeof(uint)));
        _depth = CreateBuffer(device, new float[pixels]);
        _moments = CreateBuffer(device, new float[pixels]);
        _noiseByTile = new float[_grid.Count];
        _noise = CreateBuffer(device, new float[_grid.Count]);

        // Before anything that names it: a uniform set built from a buffer that
        // does not exist yet is refused by the device with nothing a caller can
        // see, and the render then starts and traces nothing.
        _tileSamples = device.StorageBufferCreate((uint)_tileSamplesBytes.Length, _tileSamplesBytes);

        _noiseShader = device.ShaderCreateFromSpirV(Compile(device, NoiseShader.Source));
        _noisePipeline = device.ComputePipelineCreate(_noiseShader);

        _noiseSet = device.UniformSetCreate(
            new Godot.Collections.Array<RDUniform>
            {
                Bind(0, _accumulation),
                Bind(1, _moments),
                Bind(2, _tileSamples),
                Bind(3, _noise)
            },
            _noiseShader,
            0);

        // The glare is computed on a reduced copy, so a nine-tap blur reaches
        // across a good part of the picture for a sixty-fourth of the work.
        _glareWidth = Math.Max(1, (settings.Width + BloomShader.Reduction - 1) / BloomShader.Reduction);
        _glareHeight = Math.Max(1, (settings.Height + BloomShader.Reduction - 1) / BloomShader.Reduction);

        _glareAcross = CreateBuffer(device, new float[_glareWidth * _glareHeight * 4]);
        _glareDown = CreateBuffer(device, new float[_glareWidth * _glareHeight * 4]);

        _glareDownShader = device.ShaderCreateFromSpirV(Compile(device, BloomShader.DownSource));
        _glareDownPipeline = device.ComputePipelineCreate(_glareDownShader);

        _glareBlurShader = device.ShaderCreateFromSpirV(Compile(device, BloomShader.BlurSource));
        _glareBlurPipeline = device.ComputePipelineCreate(_glareBlurShader);

        _glareDownSet = device.UniformSetCreate(
            new Godot.Collections.Array<RDUniform>
            {
                Bind(0, _accumulation),
                Bind(1, _glareAcross),
                Bind(2, _tileSamples)
            },
            _glareDownShader,
            0);

        // Across, then down, so the second pass reads what the first wrote.
        _glareFirst = device.UniformSetCreate(
            new Godot.Collections.Array<RDUniform> { Bind(0, _glareAcross), Bind(1, _glareDown) },
            _glareBlurShader,
            0);

        _glareSecond = device.UniformSetCreate(
            new Godot.Collections.Array<RDUniform> { Bind(0, _glareDown), Bind(1, _glareAcross) },
            _glareBlurShader,
            0);

        _resolveSet = device.UniformSetCreate(
            new Godot.Collections.Array<RDUniform>
            {
                Bind(0, _accumulation),
                Bind(1, _resolved),
                Bind(2, _tileSamples),
                Bind(3, _glareAcross)
            },
            _resolveShader,
            0);

        _uniformSet = device.UniformSetCreate(
            new Godot.Collections.Array<RDUniform>
            {
                Bind(0, _accumulation),
                Bind(1, _elements),
                Bind(2, _voxels),
                Bind(3, _materials),
                Bind(4, _parameters),
                Bind(5, _finishes),
                Bind(6, _lights),
                Bind(7, _depth),
                Bind(8, _moments),
                Bind(9, _specularAlbedo)
            },
            _shader,
            0);
    }

    /// <summary>
    /// What the <em>least</em> converged part of the picture has had. The whole
    /// render is worth what its worst corner is worth, so this is the number that
    /// answers "how far along is it" — <see cref="SamplesPeak"/> says how far the
    /// part being looked at has got.
    /// </summary>
    public int SamplesTaken => _scheduler.Minimum;

    public int SamplesPeak => _scheduler.Peak;

    public int SamplesWanted => _scheduler.Wanted;

    /// <summary>
    /// How many steps make one sample over the whole picture — one per tile. A
    /// caller that wants to think in samples rather than in tiles multiplies by
    /// this rather than working the grid out for itself.
    /// </summary>
    public int StepsPerSample => _grid.Count;

    public float Progress => _scheduler.Fraction;

    /// <summary>
    /// How far from settled the worst part of the picture is: the relative
    /// standard error of its pixels' means, so a tenth is "the average pixel there
    /// is about ten per cent out". Nought where nothing has been measured yet.
    /// </summary>
    /// <remarks>
    /// The worst rather than the average, because a render is done when its worst
    /// part is quiet — the same reason <see cref="SamplesTaken"/> reports the least
    /// converged tile rather than the mean.
    /// </remarks>
    public float Noise
    {
        get
        {
            float worst = 0f;

            foreach (float value in _noiseByTile)
                worst = Math.Max(worst, value);

            return worst;
        }
    }

    public bool IsFinished => _scheduler.IsFinished;

    public void SetSamplesWanted(int samples) =>
        _scheduler.SetWanted(Math.Clamp(samples, 1, RenderSettings.MaximumSamples));

    // The renderer's own bounds rather than the same two numbers written down: this
    // is the live path, since exposure re-grades instead of restarting, so a
    // widened range that missed it would be the one that silently did not take.
    public void SetExposure(float exposure) =>
        _exposure = Math.Clamp(
            exposure, RenderLimits.MinimumExposure, RenderLimits.MaximumExposure);

    /// <summary>
    /// Where in the picture the samples should go first, in image pixels. What has
    /// already been traced is kept — this changes the order and never the total,
    /// and the finished picture is the same picture whatever was looked at on the
    /// way.
    /// </summary>
    public void SetPriorityRegion(int x, int y, int width, int height)
    {
        _region = new RenderTile(x, y, width, height);

        Reweigh();
    }

    /// <summary>
    /// Measures how far from settled each tile still is and lets that decide, with
    /// where the viewer is looking, what the next samples are spent on.
    /// </summary>
    /// <remarks>
    /// Run occasionally rather than per sample: it walks the picture once, which is
    /// about what a resolve costs, and the answer moves slowly. What it changes is
    /// the <em>order</em> — every tile is still asked for the same number of
    /// samples, so the finished picture is the same picture.
    /// </remarks>
    public void MeasureNoise()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (SamplesPeak < 2)
            return;

        Wait();

        for (int i = 0; i < _grid.Count; i++)
            BitConverter.GetBytes((uint)_scheduler.TakenAt(i)).CopyTo(_tileSamplesBytes, i * sizeof(uint));

        _device.BufferUpdate(_tileSamples, 0, (uint)_tileSamplesBytes.Length, _tileSamplesBytes);

        byte[] push = new byte[32];
        BitConverter.GetBytes(_settings.Width).CopyTo(push, 0);
        BitConverter.GetBytes(_settings.Height).CopyTo(push, 4);
        BitConverter.GetBytes(_grid.Columns).CopyTo(push, 8);
        BitConverter.GetBytes(_grid.Rows).CopyTo(push, 12);
        BitConverter.GetBytes(RenderTileGrid.TileSize).CopyTo(push, 16);
        BitConverter.GetBytes(_grid.Count).CopyTo(push, 20);

        long list = _device.ComputeListBegin();

        _device.ComputeListBindComputePipeline(list, _noisePipeline);
        _device.ComputeListBindUniformSet(list, _noiseSet, 0);
        _device.ComputeListSetPushConstant(list, push, (uint)push.Length);
        _device.ComputeListDispatch(list, (uint)((_grid.Count + 63) / 64), 1, 1);

        _device.ComputeListEnd();
        _device.Submit();
        _device.Sync();

        byte[] measured = _device.BufferGetData(_noise);

        for (int i = 0; i < _noiseByTile.Length && (i + 1) * sizeof(float) <= measured.Length; i++)
            _noiseByTile[i] = BitConverter.ToSingle(measured, i * sizeof(float));

        Reweigh();
    }

    private void Reweigh()
    {
        float[] weights = TilePriority.Weigh(_grid, _region, _noiseByTile);

        if (TilePriority.Differ(_scheduler.Weights, weights))
            _scheduler.SetWeights(weights);
    }

    /// <summary>
    /// Builds a tracer, or says why it could not. A device that cannot run compute
    /// is a machine's answer rather than a mistake, so it is reported rather than
    /// thrown.
    /// </summary>
    internal static bool TryCreate(
        RenderScene scene,
        RenderSettings settings,
        CameraView camera,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        out PathTracer? tracer,
        out string error)
    {
        tracer = null;
        error = string.Empty;

        RenderingDevice? device = RenderingServer.CreateLocalRenderingDevice();

        if (device is null)
        {
            error = "This machine has no rendering device that can run compute shaders.";

            return false;
        }

        try
        {
            tracer = new PathTracer(device, settings.Clamped(), scene, camera, finishes);

            return true;
        }
        catch (Exception exception)
        {
            device.Free();
            error = exception.Message;

            return false;
        }
    }

    /// <summary>
    /// Traces up to <paramref name="steps"/> more tiles, each one sample, and
    /// answers how many it actually managed. Which tiles is the scheduler's
    /// answer, so the region being looked at is served first without this having
    /// to know that a viewer exists.
    /// </summary>
    /// <remarks>
    /// One command list for the whole batch, so it costs one round trip to the
    /// device rather than one each. Two tiles do not overlap, so nothing has to
    /// be ordered between them; a barrier goes in only where the same tile comes
    /// round again inside one batch, which is the one case where a pass would
    /// read what the pass before it is still writing.
    /// </remarks>
    public int RunSteps(int steps)
    {
        int taken = SubmitSteps(steps);

        Wait();

        return taken;
    }

    /// <summary>
    /// Builds a batch and hands it to the device <em>without waiting for it</em>,
    /// answering how many tiles it holds.
    /// </summary>
    /// <remarks>
    /// This is what makes a render cost the editor almost nothing. Submitting is
    /// cheap; what used to cost a frame was the <c>Sync</c> straight after it,
    /// where the processor stood and watched the card work. Split in two, the
    /// caller submits at the end of one frame and waits at the start of the next
    /// — by which time the batch is usually already done, so the wait returns at
    /// once and the whole of that frame's own drawing happened alongside the
    /// tracing.
    /// <para>
    /// It is not a thread, and it cannot be one: a rendering device may only be
    /// driven from the thread the engine renders on, which is measured rather than
    /// assumed — every call from a thread of our own was refused outright, while
    /// the scheduler went on counting samples nothing had traced.
    /// </para>
    /// </remarks>
    public int SubmitSteps(int steps)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (steps <= 0 || _scheduler.IsFinished)
            return 0;

        // One batch at a time: a device refuses a second submission before the
        // first has been waited for.
        Wait();

        long list = _device.ComputeListBegin();

        _device.ComputeListBindComputePipeline(list, _pipeline);
        _device.ComputeListBindUniformSet(list, _uniformSet, 0);

        Array.Clear(_touchedList);

        byte[] push = new byte[32];
        int taken = 0;

        for (int step = 0; step < steps; step++)
        {
            if (!_scheduler.TryNext(out int index))
                break;

            if (_touchedList[index])
            {
                _device.ComputeListAddBarrier(list);
                Array.Clear(_touchedList);
            }

            _touchedList[index] = true;

            RenderTile tile = _grid[index];

            BitConverter.GetBytes(_scheduler.TakenAt(index)).CopyTo(push, 0);
            BitConverter.GetBytes(tile.X).CopyTo(push, 16);
            BitConverter.GetBytes(tile.Y).CopyTo(push, 20);
            BitConverter.GetBytes(tile.Width).CopyTo(push, 24);
            BitConverter.GetBytes(tile.Height).CopyTo(push, 28);

            _device.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _device.ComputeListDispatch(
                list,
                (uint)((tile.Width + 7) / 8),
                (uint)((tile.Height + 7) / 8),
                1);

            _scheduler.Record(index);
            taken++;
        }

        _device.ComputeListEnd();
        _device.Submit();

        _inFlight = true;

        return taken;
    }

    /// <summary>
    /// Waits for whatever was submitted. Doing nothing when there is nothing in
    /// flight is what lets every caller ask without keeping track.
    /// </summary>
    public void Wait()
    {
        if (!_inFlight)
            return;

        _inFlight = false;

        _device.Sync();
    }

    /// <summary>
    /// The accumulated radiance, exposed, tone mapped and encoded — what a person
    /// or a file is actually shown.
    /// </summary>
    public Image ReadImage()
    {
        byte[] pixels = ReadPixels(out int width, out int height);

        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
    }

    /// <summary>
    /// The same picture as bytes. Separate from <see cref="ReadImage"/> because the
    /// thread that owns the device is not the one that owns the textures.
    /// </summary>
    public byte[] ReadPixels(out int width, out int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        width = _settings.Width;
        height = _settings.Height;

        // The counts about to cross say what the accumulation holds, so whatever
        // is still on its way has to have landed first.
        Wait();

        // What each tile has had goes across first: the resolve divides every
        // pixel by its own tile's count, which is what lets one part of the
        // picture be further along than another and still read at the right
        // brightness.
        for (int i = 0; i < _grid.Count; i++)
            BitConverter.GetBytes((uint)_scheduler.TakenAt(i)).CopyTo(_tileSamplesBytes, i * sizeof(uint));

        _device.BufferUpdate(_tileSamples, 0, (uint)_tileSamplesBytes.Length, _tileSamplesBytes);

        if (_settings.Glare > 0f)
            Glare();

        // Divided, exposed, tone mapped and encoded on the device, so what comes
        // back is four bytes a pixel and the processor does nothing to them. The
        // preview is read many times during one render, and doing this here was
        // measured to cost more than the tracing it was showing.
        byte[] push = new byte[48];
        BitConverter.GetBytes(_settings.Width).CopyTo(push, 0);
        BitConverter.GetBytes(_settings.Height).CopyTo(push, 4);
        BitConverter.GetBytes(_grid.Columns).CopyTo(push, 8);
        BitConverter.GetBytes(_grid.Rows).CopyTo(push, 12);
        BitConverter.GetBytes(RenderTileGrid.TileSize).CopyTo(push, 16);
        BitConverter.GetBytes(_exposure).CopyTo(push, 20);

        // Nought says the picture is opaque, which is every backdrop but one.
        BitConverter.GetBytes(_settings.Backdrop == RenderBackdrop.Transparent ? 1 : 0).CopyTo(push, 24);
        BitConverter.GetBytes(_settings.Glare).CopyTo(push, 28);
        BitConverter.GetBytes(_glareWidth).CopyTo(push, 32);
        BitConverter.GetBytes(_glareHeight).CopyTo(push, 36);
        BitConverter.GetBytes(BloomShader.Reduction).CopyTo(push, 40);

        long list = _device.ComputeListBegin();

        _device.ComputeListBindComputePipeline(list, _resolvePipeline);
        _device.ComputeListBindUniformSet(list, _resolveSet, 0);
        _device.ComputeListSetPushConstant(list, push, (uint)push.Length);
        _device.ComputeListDispatch(
            list,
            (uint)((_settings.Width + 7) / 8),
            (uint)((_settings.Height + 7) / 8),
            1);

        _device.ComputeListEnd();
        _device.Submit();
        _device.Sync();

        return _device.BufferGetData(_resolved);
    }

    /// <summary>
    /// The lens's scatter, as three passes over a reduced copy: average the
    /// exposed picture down, blur it across, blur it down. The resolve then mixes
    /// a little of it back.
    /// </summary>
    /// <remarks>
    /// Run on every read rather than once, because it is computed from the
    /// <em>exposed</em> picture and exposure is a dial that moves without the
    /// render starting again. Three passes over a sixty-fourth of the pixels is
    /// not what a render spends its time on.
    /// </remarks>
    private void Glare()
    {
        uint groupsX = (uint)((_glareWidth + 7) / 8);
        uint groupsY = (uint)((_glareHeight + 7) / 8);

        byte[] down = new byte[32];
        BitConverter.GetBytes(_settings.Width).CopyTo(down, 0);
        BitConverter.GetBytes(_settings.Height).CopyTo(down, 4);
        BitConverter.GetBytes(_glareWidth).CopyTo(down, 8);
        BitConverter.GetBytes(_glareHeight).CopyTo(down, 12);
        BitConverter.GetBytes(_grid.Columns).CopyTo(down, 16);
        BitConverter.GetBytes(_grid.Rows).CopyTo(down, 20);
        BitConverter.GetBytes(RenderTileGrid.TileSize).CopyTo(down, 24);
        BitConverter.GetBytes(_exposure).CopyTo(down, 28);

        long list = _device.ComputeListBegin();

        _device.ComputeListBindComputePipeline(list, _glareDownPipeline);
        _device.ComputeListBindUniformSet(list, _glareDownSet, 0);
        _device.ComputeListSetPushConstant(list, down, (uint)down.Length);
        _device.ComputeListDispatch(list, groupsX, groupsY, 1);
        _device.ComputeListAddBarrier(list);

        _device.ComputeListBindComputePipeline(list, _glareBlurPipeline);

        for (int axis = 0; axis < 2; axis++)
        {
            byte[] blur = new byte[16];
            BitConverter.GetBytes(_glareWidth).CopyTo(blur, 0);
            BitConverter.GetBytes(_glareHeight).CopyTo(blur, 4);
            BitConverter.GetBytes(axis == 0 ? 1 : 0).CopyTo(blur, 8);
            BitConverter.GetBytes(axis == 0 ? 0 : 1).CopyTo(blur, 12);

            _device.ComputeListBindUniformSet(list, axis == 0 ? _glareFirst : _glareSecond, 0);
            _device.ComputeListSetPushConstant(list, blur, (uint)blur.Length);
            _device.ComputeListDispatch(list, groupsX, groupsY, 1);
            _device.ComputeListAddBarrier(list);
        }

        _device.ComputeListEnd();
        _device.Submit();
        _device.Sync();
    }

    /// <summary>
    /// The mean radiance over a rectangle of the picture, in the units the tracer
    /// works in — before exposure, before the film curve, before glare.
    /// </summary>
    /// <remarks>
    /// A picture is what a render is <em>for</em>, and a number is what a check
    /// needs: read off the encoded image, an energy error comes back about a third
    /// smaller than it is, because the film curve compresses. This is the same
    /// buffer the resolve divides, divided the same way — each pixel by its own
    /// tile's count, so a rectangle that straddles two tiles at different depths
    /// still averages what each of them is actually worth.
    /// </remarks>
    public Vector3 ReadRadiance(int x, int y, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int left = Math.Clamp(x, 0, _settings.Width);
        int top = Math.Clamp(y, 0, _settings.Height);
        int right = Math.Clamp(x + width, left, _settings.Width);
        int bottom = Math.Clamp(y + height, top, _settings.Height);

        if (right <= left || bottom <= top)
            return Vector3.Zero;

        Wait();

        var total = Vector3.Zero;
        int counted = 0;

        // The rows the rectangle covers, in one read. The accumulation holds four
        // floats for every pixel of the whole picture — 7.9 MiB at 960 by 540, and
        // the furnace check was pulling all of it seven times over for seven small
        // patches. Not a read per row, which would be right about the bytes and
        // wrong about the cost: each one is a round trip to the device, and a tall
        // patch would buy a few kilobytes with three hundred of them.
        const int stride = 4 * sizeof(float);

        int rowBytes = _settings.Width * stride;

        byte[] bytes = _device.BufferGetData(
            _accumulation,
            (uint)(top * rowBytes),
            (uint)((bottom - top) * rowBytes));

        for (int row = top; row < bottom; row++)
        {
            for (int column = left; column < right; column++)
            {
                int taken = _scheduler.TakenAt(_grid.TileAt(column, row));

                if (taken <= 0)
                    continue;

                int at = (row - top) * rowBytes + column * stride;

                if (at + 3 * sizeof(float) > bytes.Length)
                    break;

                total += new Vector3(
                    BitConverter.ToSingle(bytes, at),
                    BitConverter.ToSingle(bytes, at + sizeof(float)),
                    BitConverter.ToSingle(bytes, at + 2 * sizeof(float))) / taken;

                counted++;
            }
        }

        return counted == 0 ? Vector3.Zero : total / counted;
    }

    /// <summary>
    /// How far the camera ray at this pixel reached, or false where it reached
    /// nothing — which is what the sky is, and what focusing on it would mean
    /// nothing by.
    /// </summary>
    /// <remarks>
    /// Read from the buffer every sample writes rather than traced again: a second
    /// trace would be a second answer, and one taken through a different jitter of
    /// the same pixel at that.
    /// </remarks>
    public bool TryReadDepth(int x, int y, out float distance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        distance = 0f;

        if (x < 0 || y < 0 || x >= _settings.Width || y >= _settings.Height)
            return false;

        Wait();

        int index = y * _settings.Width + x;
        byte[] bytes = _device.BufferGetData(_depth, (uint)(index * sizeof(float)), sizeof(float));

        if (bytes.Length < sizeof(float))
            return false;

        distance = BitConverter.ToSingle(bytes, 0);

        return distance > 0f;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Freeing a buffer a batch in flight is still reading is the one way this
        // could take the editor with it.
        Wait();

        _disposed = true;

        // Order matters, and getting it wrong is quiet: a uniform set is freed by
        // the device the moment any buffer it names is freed, so a set released
        // after its buffers is an invalid id and says so in the engine's log
        // rather than in anything a caller sees. Sets, then what they were built
        // from, then the buffers.
        _device.FreeRid(_resolveSet);
        _device.FreeRid(_uniformSet);
        _device.FreeRid(_noiseSet);
        _device.FreeRid(_noisePipeline);
        _device.FreeRid(_noiseShader);
        _device.FreeRid(_glareDownSet);
        _device.FreeRid(_glareFirst);
        _device.FreeRid(_glareSecond);
        _device.FreeRid(_glareDownPipeline);
        _device.FreeRid(_glareDownShader);
        _device.FreeRid(_glareBlurPipeline);
        _device.FreeRid(_glareBlurShader);
        _device.FreeRid(_resolvePipeline);
        _device.FreeRid(_resolveShader);
        _device.FreeRid(_pipeline);
        _device.FreeRid(_shader);

        _device.FreeRid(_tileSamples);
        _device.FreeRid(_depth);
        _device.FreeRid(_moments);
        _device.FreeRid(_noise);
        _device.FreeRid(_glareAcross);
        _device.FreeRid(_glareDown);
        _device.FreeRid(_resolved);
        _device.FreeRid(_lights);
        _device.FreeRid(_finishes);
        _device.FreeRid(_parameters);
        _device.FreeRid(_materials);
        _device.FreeRid(_specularAlbedo);
        _device.FreeRid(_voxels);
        _device.FreeRid(_elements);
        _device.FreeRid(_accumulation);

        _device.Free();
    }

    /// <summary>
    /// GLSL to SPIR-V, refusing loudly. A shader that did not compile is a
    /// programming error rather than a machine's answer, unlike a device that
    /// cannot run compute at all.
    /// </summary>
    private static RDShaderSpirV Compile(RenderingDevice device, string glsl)
    {
        var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl };
        source.SetStageSource(RenderingDevice.ShaderStage.Compute, glsl);

        RDShaderSpirV spirV = device.ShaderCompileSpirVFromSource(source);
        string error = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);

        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"A shader did not compile: {error}");

        return spirV;
    }

    private static RDUniform Bind(int binding, Rid buffer)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = binding
        };

        uniform.AddId(buffer);

        return uniform;
    }

    /// <summary>
    /// A buffer holding these floats. Never empty: a device refuses a buffer of
    /// nothing, and a scene with no elements at all is a legitimate thing to
    /// render — it is the sky.
    /// </summary>
    private static Rid CreateBuffer(RenderingDevice device, float[] values)
    {
        if (values.Length == 0)
            values = new float[4];

        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);

        return device.StorageBufferCreate((uint)bytes.Length, bytes);
    }

    private static float[] PackElements(RenderScene scene)
    {
        float[] buffer = new float[Math.Max(scene.Elements.Count, 1) * RenderElement.Floats];

        for (int i = 0; i < scene.Elements.Count; i++)
            scene.Elements[i].Write(buffer, i * RenderElement.Floats);

        return buffer;
    }

    private static float[] PackMaterials(
        RenderScene scene,
        IReadOnlyDictionary<string, FinishHeightField> finishes,
        IReadOnlyDictionary<string, int> offsets)
    {
        float[] buffer = new float[Math.Max(scene.Materials.Count, 1) * RenderMaterial.Floats];

        for (int i = 0; i < scene.Materials.Count; i++)
        {
            RenderMaterial material = scene.Materials[i];

            // A facture nobody supplied a field for is a material with none: the
            // caller decides what it can draw, and a missing one is flat rather
            // than a refusal.
            FinishHeightField field = finishes.TryGetValue(material.Name, out FinishHeightField found)
                ? found
                : FinishHeightField.None;

            offsets.TryGetValue(material.Name, out int offset);

            material.Write(buffer, i * RenderMaterial.Floats, field, offset);
        }

        return buffer;
    }

    /// <summary>
    /// Every supplied tile, one after another, and where each one starts. One
    /// buffer rather than a texture array because the tracer wants a value at a
    /// point and does its own filtering — there is no sampler on this path.
    /// </summary>
    private static (float[] Atlas, Dictionary<string, int> Offsets) PackFinishes(
        RenderScene scene,
        IReadOnlyDictionary<string, FinishHeightField> finishes)
    {
        var offsets = new Dictionary<string, int>();

        // Keyed by the heights *instance*, so every material handed the same drawn
        // field shares one copy in the atlas. A drawn tile is 256 square and up to
        // six floats a texel, so a scene where twenty materials wear one facture
        // would otherwise upload it twenty times.
        var placedByHeights = new Dictionary<float[], int>(ReferenceEqualityComparer.Instance);
        var atlasList = new List<float>();

        foreach (RenderFacture facture in scene.Factures)
        {
            if (!finishes.TryGetValue(facture.Name, out FinishHeightField field) || !field.HasField)
                continue;

            if (placedByHeights.TryGetValue(field.Heights, out int already))
            {
                offsets[facture.Name] = already;
                continue;
            }

            offsets[facture.Name] = atlasList.Count;
            placedByHeights[field.Heights] = atlasList.Count;

            // Interleaved by texel rather than laid out as separate planes, so one
            // bilinear tap reads a texel's height, colour and gloss out of
            // neighbouring words instead of three places a tile apart.
            for (int texel = 0; texel < field.Heights.Length; texel++)
            {
                atlasList.Add(field.Heights[texel]);

                if (!field.HasColor)
                    continue;

                atlasList.Add(field.Colors![texel * 3 + 0]);
                atlasList.Add(field.Colors[texel * 3 + 1]);
                atlasList.Add(field.Colors[texel * 3 + 2]);

                if (!field.HasRoughness)
                    continue;

                atlasList.Add(field.Roughness![texel]);

                if (!field.HasOcclusion)
                    continue;

                atlasList.Add(field.Occlusion![texel]);

                if (field.HasMetal)
                    atlasList.Add(field.Metal![texel]);
            }
        }

        return (atlasList.ToArray(), offsets);
    }

    private static float[] PackLights(RenderScene scene)
    {
        float[] buffer = new float[Math.Max(scene.Lights.Count, 1) * RenderLight.Floats];

        for (int i = 0; i < scene.Lights.Count; i++)
            scene.Lights[i].Write(buffer, i * RenderLight.Floats);

        return buffer;
    }

    private static float[] PackVoxels(RenderScene scene)
    {
        float[] buffer = new float[Math.Max(scene.Voxels.Length, 1)];

        for (int i = 0; i < scene.Voxels.Length; i++)
            buffer[i] = BitConverter.Int32BitsToSingle((int)scene.Voxels[i]);

        return buffer;
    }

    private static float[] PackParameters(RenderScene scene, RenderSettings settings, CameraView camera)
    {
        float[] buffer = new float[ParamsFloats];

        Basis basis = camera.Transform.Basis;

        // A Godot camera looks down its own -Z, and its field of view is the
        // vertical one — the same convention the viewport's own camera is set up
        // with, which is what makes a render frame what the editor was showing.
        Vector3 forward = -basis.Column2;
        Vector3 right = basis.Column0;
        Vector3 up = basis.Column1;

        float tangent = Mathf.Tan(Mathf.DegToRad(camera.FovDegrees) * 0.5f);
        float aspect = (float)settings.Width / settings.Height;

        Write(buffer, 0, camera.Transform.Origin, tangent);
        Write(buffer, 4, right, aspect);
        Write(buffer, 8, up, settings.Aperture);

        // Nought means "on whatever is being looked at", which is the middle of
        // what the scene holds — the point the camera was framed on.
        float focus = settings.FocusDistance > 0f
            ? settings.FocusDistance
            : Mathf.Max(camera.Transform.Origin.DistanceTo(scene.Centre), 0.01f);

        Write(buffer, 12, forward, focus);

        Write(
            buffer,
            16,
            settings.SunDirection,
            Mathf.Cos(Mathf.DegToRad(settings.SunAngularRadiusDegrees)));

        // A light aimed at must not also be counted when a path lands on it; one
        // past the cap is not aimed at, so it must. The whole scene follows the
        // stricter of the two, which is the one that cannot count a lamp twice.
        Write(buffer, 20, settings.SunRadiance, scene.LightsTruncated ? 0f : 1f);

        Write(buffer, 24, Linear(settings.SkyZenith), settings.SkyIntensity);

        // The ground sits on the buildable region's floor, which is where the room
        // stands: the region spans nought to its size on every axis.
        Write(buffer, 28, Linear(settings.SkyHorizon), 0f);
        Write(buffer, 32, Linear(settings.SkyGround), settings.GroundEnabled ? 1f : 0f);
        Write(buffer, 36, Linear(settings.GroundColor), scene.Lights.Count);

        buffer[40] = BitConverter.Int32BitsToSingle(settings.Width);
        buffer[41] = BitConverter.Int32BitsToSingle(settings.Height);
        buffer[42] = BitConverter.Int32BitsToSingle(scene.Elements.Count);
        buffer[43] = BitConverter.Int32BitsToSingle(settings.Bounces);

        // Nought keeps the shot the perspective camera was framing: the window is
        // as tall as the cone was where the subject stands, so switching
        // projection changes the drawing rather than what is in it.
        float height = settings.Projection == RenderProjection.Orthographic
            ? (settings.OrthographicHeight > 0f
                ? settings.OrthographicHeight
                : Mathf.Max(2f * focus * tangent, 0.01f))
            : 0f;

        Write(buffer, 44, new Vector3(height, (float)settings.Backdrop, 0f), 0f);
        Write(buffer, 48, Linear(settings.BackdropColor), 0f);

        return buffer;
    }

    private static Vector3 Linear(Color color)
    {
        Color linear = color.SrgbToLinear();

        return new Vector3(linear.R, linear.G, linear.B);
    }

    private static void Write(float[] buffer, int offset, Vector3 value, float w)
    {
        buffer[offset + 0] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
        buffer[offset + 3] = w;
    }
}
