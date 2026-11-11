using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// A cube of one material, on its own, lit and turnable. Both the panel's preview
/// and the <c>material-preview</c> command are this same viewport: a material's
/// whole point is how it looks, so seeing one has to mean seeing a surface rather
/// than reading six numbers.
/// </summary>
/// <remarks>
/// It is a voxel block rather than a plain box, meshed by the very mesher the
/// scene uses, so the ambient occlusion in its corners is the real thing rather
/// than a preview's approximation of it.
/// <para>
/// How many cells to a side is the facture's own answer (<see cref="FinishPeriod"/>)
/// and not a constant: one cell more than the pattern's period, so the pattern is
/// shown whole <b>and</b> shown starting again — never fewer than three, which is
/// what the corners need. A fixed three was what shipped, and it showed three
/// quarters of a four-cell plank pattern: the placed model then wore the quarter
/// the preview had cropped, which reads as the preview being of a different
/// material.
/// </para>
/// <para>
/// What no size can promise is the <b>phase</b>. The facture is a field over the
/// element's own cell lattice — that is what makes it ride with the model instead
/// of sliding across it as the model is moved — so a model shows the window of the
/// pattern its cells happen to sit at, and a cube at cells 8..10 wears a different
/// part of the plank than one at 0..2. Measured: moving an element changes its
/// facture not at all, pixel for pixel, while painting the same shape three cells
/// over changes it plainly. The preview now contains everything a model can show;
/// which part of it a given model shows is the model's own position in the timber.
/// </para>
/// <para>
/// A <see cref="SubViewport"/> itself rather than a node holding one, because a
/// <see cref="SubViewportContainer"/> can only show a viewport that is its own
/// child — and the panel needs exactly that while the command needs the same
/// viewport with nobody watching.
/// </para>
/// </remarks>
internal sealed partial class MaterialPreviewStage : SubViewport
{


    private static readonly Guid PreviewMaterialId = Guid.Parse("00000000-0000-0000-0000-0000000000FE");

    private readonly Node3D _block = new();

    private readonly List<MeshInstance3D> _blockMeshesList = new();

    private Camera3D _camera = null!;
    private string _materialName = string.Empty;
    private Color _color = Colors.White;
    private MaterialAppearance _appearance = MaterialAppearance.Default;
    private MaterialPreviewTurn _turn = MaterialPreviewTurn.Default;

    public override void _Ready()
    {
        TransparentBg = true;
        OwnWorld3D = true;

        // Multisampling on top of the supersample the panel already asks for, so
        // an edge at 45° to the pixel grid — which is most of a cube's silhouette —
        // is smooth at the size this is actually looked at.
        Msaa3D = Viewport.Msaa.Msaa4X;

        // The scene's own light, aimed the same way, so a preview and the model in
        // the viewport are the same material seen twice rather than two opinions
        // about it.
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0f, 0f, 0f, 0f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.55f, 0.6f),
                AmbientLightEnergy = 0.35f,
                Sky = SceneView.CreateReflectionSky(),
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky
            }
        });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-70f, 120f, 0f),
            LightEnergy = 1.15f
        });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-15f, 55f, 0f),
            LightEnergy = 0.25f
        });

        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,

            // Set here so the camera is valid the moment it exists; Aim() sizes it
            // to the block, which changes with the facture.
            Size = FinishPeriod.MinimumPreviewCells * 1.9f,

            // Said out loud: a viewport with its own world has no camera until one
            // claims it, and a viewport with no camera renders nothing at all —
            // which is a blank square rather than an error anywhere.
            Current = true
        };

        AddChild(_camera);
        AddChild(_block);

        Aim();
        Rebuild();
    }

    /// <summary>The material shown. Rebuilds the block, which is how a facture reaches it.</summary>
    public void SetMaterial(string name, Color color, MaterialAppearance appearance)
    {
        _materialName = name;
        _color = color;
        _appearance = appearance;

        if (_camera is null)
            return;

        // Aim after rebuilding, not instead of it: a facture with a longer period
        // makes a bigger block, and the framing is measured from that.
        Rebuild();
        Aim();
    }

    /// <summary>
    /// Turns the block, through <see cref="MaterialPreviewTurns.Clamp"/> so the
    /// command and the panel's buttons meet the same limits.
    /// </summary>
    public void Turn(MaterialPreviewTurn turn)
    {
        _turn = MaterialPreviewTurns.Clamp(turn);

        if (_camera is not null)
            Aim();
    }

    public MaterialPreviewTurn Pose => _turn;

    /// <summary>
    /// Renders once, right now, and answers the pixels. For the command, which has
    /// no frames to wait for — and which is why the stage is built with the view
    /// rather than when a preview is first asked for: a viewport added inside the
    /// call that photographs it has not been through a frame, and no number of
    /// forced draws inside that same frame will fill it.
    /// </summary>
    public Image Capture(int size)
    {
        Vector2I was = Size;
        SubViewport.UpdateMode mode = RenderTargetUpdateMode;

        Size = new Vector2I(size, size);
        RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        RenderingServer.ForceDraw();

        Image image = GetTexture().GetImage();

        Size = was;
        RenderTargetUpdateMode = mode;

        return image;
    }

    private void Aim()
    {
        int cells = FinishPeriod.GetPreviewCells(_appearance);
        var middle = new Vector3(cells * 0.5f, cells * 0.5f, cells * 0.5f);

        // A shade wider than the block's diagonal, so the corners breathe. Re-said
        // on every aim, because the block grows and shrinks with the facture's
        // period and a camera framing the old size would crop or shrink it.
        _camera.Size = cells * 1.9f;

        float yaw = Mathf.DegToRad(_turn.YawDegrees);
        float pitch = Mathf.DegToRad(_turn.PitchDegrees);

        var offset = new Vector3(
            Mathf.Cos(pitch) * Mathf.Sin(yaw),
            -Mathf.Sin(pitch),
            Mathf.Cos(pitch) * Mathf.Cos(yaw));

        _camera.Position = middle + offset * (cells * 3f);
        _camera.LookAt(middle, Vector3.Up);

        // A transform reaches the rendering server on a notification the engine
        // delivers between frames, and a capture forces a draw without returning to
        // it — so an aim made inside the call that photographs it would render the
        // new block through the *old* camera. Measured before this line existed: the
        // first capture after the facture's period changed filled 1.6% of the square
        // with the cube half out of frame, and the next one 47.3% centred. A second
        // forced draw does not fix it; only pushing the transform does.
        _camera.ForceUpdateTransform();
    }

    private void Rebuild()
    {
        var materials = new Dictionary<Guid, Material>
        {
            [PreviewMaterialId] = VoxelMaterialFactory.Create(_materialName, _color, _appearance, ViewportEffects.Full)
        };

        VoxelMeshInstances.Apply(
            _block,
            _blockMeshesList,
            VoxelMesher.Build(
                CreateBlock(FinishPeriod.GetPreviewCells(_appearance)),
                materials,
                new Dictionary<Guid, float> { [PreviewMaterialId] = _appearance.OpacityOf(_color.A) }),
            GeometryInstance3D.ShadowCastingSetting.On,
            RenderLayers.Voxels);
    }

    private static Dictionary<Vector3I, Guid> CreateBlock(int cells)
    {
        var voxels = new Dictionary<Vector3I, Guid>();

        for (var z = 0; z < cells; z++)
        {
            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                    voxels[new Vector3I(x, y, z)] = PreviewMaterialId;
            }
        }

        return voxels;
    }
}
