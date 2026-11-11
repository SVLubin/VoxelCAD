using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Names the icon a row wears, given what the row is. Pure — the panels ask it
/// which icon to fetch from the theme, and a test can ask the same question
/// without an engine.
/// </summary>
/// <remarks>
/// The names resolve against the icons <see cref="EditorIconFactory"/> registers
/// under <see cref="EditorThemeFactory.SemanticType"/>; a test holds the two
/// sides together. A null answer means the row carries no catalogue icon — a
/// material row's chip is its own colour swatch, not a shared glyph.
/// </remarks>
internal static class EditorIconCatalog
{
    public const string Project = "icon_project";
    public const string Folder = "icon_folder";
    public const string Scene = "icon_scene";
    public const string Group = "icon_group";
    public const string VoxelModel = "icon_voxel_model";
    public const string Prefab = "icon_prefab";

    /// <summary>
    /// A camera: a body with a lens. Deliberately not the render glyph, which is
    /// a picture in a frame — a camera is the thing that takes one, and a scene
    /// can hold several of them while there is only ever the one picture.
    /// </summary>
    public const string Camera = "icon_camera";

    /// <summary>
    /// A picture standing in the scene: a plane seen at an angle, with a horizon on
    /// it. Deliberately not a dashed rectangle, which is the group's, and not a
    /// framed picture, which is the render's — what this says is "a drawing hung in
    /// space", and neither of those does.
    /// </summary>
    public const string ReferenceImage = "icon_reference_image";

    /// <summary>
    /// Somebody else's model: a sphere with its equator, which is what every
    /// application means by "a 3D object". A cube would have been the obvious
    /// drawing and is taken twice over — the voxel model's is filled and the
    /// prefab's is banded.
    /// </summary>
    public const string ReferenceModel = "icon_reference_model";

    public const string HistoryOrigin = "icon_history_origin";
    public const string AgentSession = "icon_agent_session";

    /// <summary>
    /// The same robot the history rows wear, tinted for the plaque: full text
    /// colour rather than the receded structure tone, because on the plaque it
    /// is the subject, not the furniture.
    /// </summary>
    public const string AgentPlaque = "icon_agent_plaque";

    /// <summary>
    /// The plaque's robot, wearing what it is doing. One head, four sets of
    /// eyes: open and square while it works, small while it waits its turn,
    /// crossed once it has stopped answering, and shut for the blink that is the
    /// only thing it does when nothing is happening.
    /// </summary>
    /// <remarks>
    /// Four drawings of one face rather than four faces, so the head cannot
    /// drift between them — the body is written once and the eyes are what the
    /// state changes, which is also why a cross-fade between any two of them
    /// looks like an expression changing rather than an icon being swapped.
    /// </remarks>
    public const string AgentFaceWorking = "icon_agent_face_working";
    public const string AgentFaceWaiting = "icon_agent_face_waiting";
    public const string AgentFaceSilent = "icon_agent_face_silent";
    public const string AgentFaceBlink = "icon_agent_face_blink";
    /// <summary>The cursor: the tool that points at things and edits nothing.</summary>
    public const string ToolSelect = "icon_tool_select";

    /// <summary>
    /// The paint tool's own glyph — a pencil over a cell, not the bare pencil
    /// <see cref="Rename"/> wears: one draws, the other relabels.
    /// </summary>
    public const string ToolPaint = "icon_tool_paint";

    public const string Eyedropper = "icon_eyedropper";

    /// <summary>The extrude tool: a face, and an arrow pushing it off the surface.</summary>
    public const string ToolExtrude = "icon_tool_extrude";

    /// <summary>The fill tool: a tilted bucket pouring into the region it recolours.</summary>
    public const string ToolFill = "icon_tool_fill";

    /// <summary>
    /// The open eye a project-tree row wears while its element is shown. Drawn
    /// only under the pointer: it is an affordance, not a state — what a row
    /// looks like when nothing is wrong with it is nothing at all.
    /// </summary>
    public const string EyeOpen = "icon_eye_open";

    /// <summary>
    /// The crossed eye of a hidden element. The counterpart of
    /// <see cref="EyeOpen"/> and the half that is always drawn, because it is the
    /// state rather than the offer — a row nobody is pointing at still has to say
    /// that what it names is out of the viewport and out of the file.
    /// </summary>
    public const string EyeClosed = "icon_eye_closed";

    /// <summary>
    /// The bin a project-tree row offers beside its eye. Not <see cref="Delete"/>,
    /// which is the cross the toolbar button and the history rows wear: a row's
    /// bin acts the instant it is pressed, and a glyph that says "throw this away"
    /// is worth more there than one that says "remove".
    /// </summary>
    public const string Trash = "icon_trash";

    /// <summary>
    /// The pin a panel header carries: pressed, the panel stands in its column;
    /// released, it collapses to a tab.
    /// </summary>
    public const string PanelPin = "icon_panel_pin";

    /// <summary>
    /// A panel header's chevron. One mark for both readings: a folded panel
    /// wears it as drawn, pointing down at the body a press would bring back,
    /// and an open one wears it mirrored. The right-pointing one it replaced
    /// went with the state-picture reading and could not be turned into, only
    /// swapped for.
    /// </summary>
    public const string ChevronDown = "icon_chevron_down";

    /// <summary>
    /// A panel's resize grip: a double-headed arrow along the pull. Drawn for
    /// the left column's corner and mirrored in code for the right, the
    /// chevron's own one-drawing rule.
    /// </summary>
    public const string ResizeDiagonal = "icon_resize_diagonal";

    /// <summary>The same grip where only the width can move.</summary>
    public const string ResizeHorizontal = "icon_resize_horizontal";

    /// <summary>The inspector panel's glyph: three sliders.</summary>
    public const string Inspector = "icon_inspector";

    /// <summary>The toolbar panel's glyph: the three-line menu.</summary>
    public const string Toolbar = "icon_toolbar";

    /// <summary>
    /// The history panel's glyph: a clock swept backwards. Not
    /// <see cref="HistoryOrigin"/>, which is the origin row's own mark — a bare
    /// circle says "start", not "history".
    /// </summary>
    public const string History = "icon_history";

    /// <summary>The File menu button: a page with a folded corner.</summary>
    public const string FileMenu = "icon_file";

    /// <summary>A fresh sheet, with the plus every "new" in this editor wears.</summary>
    public const string NewFile = "icon_new_file";

    public const string Open = "icon_open";
    public const string Save = "icon_save";
    public const string Undo = "icon_undo";
    public const string Redo = "icon_redo";

    /// <summary>The settings gear — the one glyph that always means preferences.</summary>
    /// <summary>Out of the document and into another format: a box with an arrow leaving it.</summary>
    public const string Export = "icon_export";

    /// <summary>
    /// A picture in its frame — what a render produces, and the one thing in this
    /// editor that is one. Deliberately not a sun: Preferences is a circle with
    /// eight spokes, and at the size a menu row draws them the two were the same
    /// glyph.
    /// </summary>
    public const string Render = "icon_render";

    /// <summary>Something went wrong. Worn by an error card and nothing else.</summary>
    public const string Warning = "icon_warning";

    public const string Preferences = "icon_preferences";

    /// <summary>
    /// The settings form's own section glyphs. A rail of four words is read by
    /// reading all four; a rail of four pictures is read by recognising one.
    /// </summary>
    public const string SectionEndpoint = "icon_section_endpoint";
    public const string SectionHandover = "icon_section_handover";
    public const string SectionDisplay = "icon_section_display";
    public const string SectionFiles = "icon_section_files";
    public const string SectionDebug = "icon_section_debug";

    /// <summary>
    /// The magnifier a search field carries. Without it a search box is a text
    /// box with grey words in it, which is what its placeholder already is.
    /// </summary>
    public const string Search = "icon_search";

    /// <summary>
    /// The create row's glyphs: each kind's own icon with a plus badge in the
    /// corner, composed from the kind's artwork so the two cannot drift apart.
    /// </summary>
    public const string CreateGroup = "icon_create_group";
    public const string CreateCamera = "icon_create_camera";

    /// <summary>
    /// Badged from the picture rather than the model, the two being one menu row:
    /// a picture is what a reference usually is, and the row says "Import
    /// reference…" in words beside it.
    /// </summary>
    public const string CreateReference = "icon_create_reference";

    public const string CreateModel = "icon_create_model";
    public const string CreateScene = "icon_create_scene";
    public const string CreateMaterial = "icon_create_material";
    public const string CreateInstance = "icon_create_instance";

    public const string Create = "icon_create";
    public const string Delete = "icon_delete";
    public const string Rename = "icon_rename";
    public const string Reparent = "icon_reparent";
    public const string Move = "icon_move";
    public const string Rotate = "icon_rotate";
    public const string Scale = "icon_scale";
    public const string FieldMode = "icon_field_mode";
    public const string VoxelEdit = "icon_voxel_edit";
    public const string Material = "icon_material";

    /// <summary>The glyph a chrome panel's header and rail tab wear.</summary>
    public static string ForPanel(PanelKind kind) => kind switch
    {
        PanelKind.Toolbar => Toolbar,
        PanelKind.Project => Project,
        PanelKind.History => History,
        PanelKind.Inspector => Inspector,
        _ => Project
    };

    /// <summary>The plus-badged glyph a create button wears for its kind.</summary>
    public static string ForCreate(CreateKind kind) => kind switch
    {
        CreateKind.Group => CreateGroup,
        CreateKind.VoxelModel => CreateModel,
        CreateKind.Camera => CreateCamera,
        CreateKind.Scene => CreateScene,
        CreateKind.Material => CreateMaterial,
        CreateKind.PrefabInstance => CreateInstance,
        CreateKind.Reference => CreateReference,
        _ => CreateGroup
    };

    public static string? ForRow(SelectionKind kind, ComponentKind component) => kind switch
    {
        SelectionKind.Project => Project,
        SelectionKind.ScenesCategory => Folder,
        SelectionKind.PrefabsCategory => Folder,
        SelectionKind.MaterialsCategory => Folder,
        SelectionKind.Scene => Scene,
        SelectionKind.Prefab => Prefab,
        SelectionKind.Element => ForElement(component),

        // A material row's chip is its swatch; anything unknown stays bare.
        _ => null
    };

    /// <summary>
    /// The glyph a tool button wears. The transform tools share the history's
    /// own glyphs on purpose: the button and the record it will leave should
    /// look like the same thing.
    /// </summary>
    public static string ForTool(ToolKind tool) => tool switch
    {
        ToolKind.Select => ToolSelect,
        ToolKind.Paint => ToolPaint,
        ToolKind.Move => Move,
        ToolKind.Rotate => Rotate,
        ToolKind.Scale => Scale,
        ToolKind.Eyedropper => Eyedropper,
        ToolKind.Extrude => ToolExtrude,
        ToolKind.Fill => ToolFill,
        _ => ToolPaint
    };

    public static string ForAction(ActionKind kind) => kind switch
    {
        ActionKind.CreateElement => Create,
        ActionKind.CreateMaterial => Create,
        ActionKind.CreateScene => Create,
        ActionKind.DeleteElement => Delete,
        ActionKind.DeleteMaterial => Delete,
        ActionKind.DeleteScene => Delete,
        ActionKind.RenameElement => Rename,
        ActionKind.RenameScene => Rename,
        ActionKind.ReparentElement => Reparent,
        ActionKind.MoveElement => Move,
        ActionKind.RotateElement => Rotate,
        ActionKind.ScaleElement => Scale,
        ActionKind.ChangeFieldMode => FieldMode,
        ActionKind.ChangeVisibility => EyeOpen,
        ActionKind.EditVoxels => VoxelEdit,
        ActionKind.ChangeMaterial => Material,
        ActionKind.ReplaceMaterial => Material,
        _ => Create
    };

    /// <summary>The glyph the settings rail wears for one of its sections.</summary>
    public static string ForSettingsSection(SettingsSection section) => section switch
    {
        SettingsSection.Endpoint => SectionEndpoint,
        SettingsSection.Handover => SectionHandover,
        SettingsSection.Display => SectionDisplay,
        SettingsSection.Files => SectionFiles,
        SettingsSection.Debug => SectionDebug,

        // A section added tomorrow gets the gear rather than nothing, and a
        // test over the enum says so before anybody meets it.
        _ => Preferences
    };

    private static string ForElement(ComponentKind component) => component switch
    {
        ComponentKind.VoxelModel => VoxelModel,
        ComponentKind.Camera => Camera,
        ComponentKind.ReferenceImage => ReferenceImage,
        ComponentKind.ReferenceModel => ReferenceModel,
        ComponentKind.PrefabInstance => Prefab,
        _ => Group
    };
}
