using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Draws the editor's icon set — small single-colour SVG glyphs tinted from the
/// <see cref="EditorPalette"/> — and registers each under
/// <see cref="EditorThemeFactory.SemanticType"/>, where the panels fetch them by
/// the names <see cref="EditorIconCatalog"/> hands out.
/// </summary>
/// <remarks>
/// The sources are kept as data so the set is enumerable without an engine:
/// a test walks <see cref="Names"/> against the catalogue's answers, and only
/// <see cref="Register"/> touches Godot. Glyphs are authored at 16 units and
/// rasterised oversampled, so a scaled window draws them from spare pixels
/// rather than blurring a native-size bitmap up.
/// </remarks>
internal static class EditorIconFactory
{
    /// <summary>Side of the artwork, in canvas units — what a glyph is drawn at.</summary>
    private const int ArtworkSide = 16;

    /// <summary>Rasterisation scale over the 16-unit artwork; see the class remarks.</summary>
    private const float Oversample = 4f;

    private const string ColorMark = "{c}";

    /// <summary>
    /// The artwork, one 16×16 SVG body per catalogue name. <see cref="ColorMark"/>
    /// stands where the tint goes — always a six-digit hex colour, because the
    /// SVG rasteriser is not owed eight-digit support; translucency is done with
    /// explicit opacity attributes instead.
    /// </summary>
    private static readonly Dictionary<string, string> SourcesByName = new()
    {
        // A closed wireframe box: the project, everything packed in one place.
        [EditorIconCatalog.Project] =
            """
            <path d="M8 1.5 14 4.75v6.5L8 14.5 2 11.25v-6.5Z M2 4.75 8 8l6-3.25 M8 8v6.5"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.Folder] =
            """
            <path d="M1.75 3.5h4.25l1.5 1.75h6.75V13H1.75Z"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            """,

        // The room: the same box as the project glyph with its lid off.
        [EditorIconCatalog.Scene] =
            """
            <path d="M2 4.5v6.75L8 14.5l6-3.25V4.5 M2 4.5 8 7.75l6-3.25 M8 7.75v6.75"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            """,

        // A marquee with parts inside — a grouping element owns, it is not seen.
        [EditorIconCatalog.Group] =
            """
            <rect x="2.25" y="2.25" width="11.5" height="11.5" rx="1"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-dasharray="2.6 2.1"/>
            <rect x="5" y="5" width="2.6" height="2.6" fill="{c}"/>
            <rect x="8.4" y="8.4" width="2.6" height="2.6" fill="{c}"/>
            """,

        [EditorIconCatalog.VoxelModel] =
            """
            <path d="M8 1.5 14 4.75v6.5L8 14.5 2 11.25v-6.5Z" fill="{c}"/>
            <path d="M2 4.75 8 8l6-3.25 M8 8v6.5"
                fill="none" stroke="#000000" stroke-opacity="0.4" stroke-width="1.2"/>
            """,

        // The project box again, banded: packed for reuse.
        [EditorIconCatalog.Prefab] =
            """
            <path d="M8 1.5 14 4.75v6.5L8 14.5 2 11.25v-6.5Z M2 4.75 8 8l6-3.25 M8 8v6.5"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            <path d="M5 3.1 11 6.35" fill="none" stroke="{c}" stroke-width="1.4"/>
            """,

        [EditorIconCatalog.HistoryOrigin] =
            """
            <circle cx="8" cy="8" r="3.25" fill="none" stroke="{c}" stroke-width="1.5"/>
            """,

        // A head with an aerial: the run of records an agent filed while it held
        // the editor, folded under one row.
        [EditorIconCatalog.AgentSession] =
            """
            <path d="M8 1.5v2.25" fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
            <rect x="2.75" y="4.25" width="10.5" height="8.25" rx="2.25"
                fill="none" stroke="{c}" stroke-width="1.3"/>
            <rect x="5.4" y="7.4" width="1.9" height="1.9" fill="{c}"/>
            <rect x="8.7" y="7.4" width="1.9" height="1.9" fill="{c}"/>
            """,

        // The arrow every interface points with, leaning the way the hand tools
        // lean: tip at the top left, tail towards the bottom right.
        [EditorIconCatalog.ToolSelect] =
            """
            <path d="M3.4 1.9 12.6 8.6 8.2 9.2 6.1 13.4Z"
                fill="{c}" stroke="{c}" stroke-width="1.1" stroke-linejoin="round"/>
            """,

        // The rename pencil's sibling, drawing rather than relabelling: the
        // same pencil, over the cell it is about to fill.
        [EditorIconCatalog.ToolPaint] =
            """
            <path d="M5.5 12.5 6 10.25 12.5 3.75l1.75 1.75L7.75 12 5.5 12.5Z M11.25 5 13 6.75"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <rect x="2" y="11.4" width="2.6" height="2.6" fill="{c}"/>
            """,

        // A dropper aimed the way the pencils lean: bulb high, tip at a cell.
        [EditorIconCatalog.Eyedropper] =
            """
            <circle cx="12.3" cy="3.7" r="2" fill="{c}"/>
            <path d="M9.3 4.3 11.7 6.7 5.2 13.2l-2.7.3.3-2.7Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            """,

        // A face and the arrow that pushes it off the surface — the square is what
        // the click grabs, the arrow where the layer goes.
        [EditorIconCatalog.ToolExtrude] =
            """
            <rect x="2.2" y="7.6" width="6.2" height="6.2"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M9.4 6.6 13.6 2.4 M10.2 2.4h3.4v3.4"
                fill="none" stroke="{c}" stroke-width="1.4"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // A bucket tipped the way the hand tools lean, and the drop that has
        // left it: what pours is the colour, what it lands in is the region.
        [EditorIconCatalog.ToolFill] =
            """
            <path d="M2.4 8.6 7.6 3.4l5.2 5.2-5.2 5.2Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M4.2 6.8 9.8 6.8"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
            <path d="M13.4 8.6c0 1-1 2-1 2.9a1 1 0 0 0 2 0c0-.9-1-1.9-1-2.9Z" fill="{c}"/>
            """,

        // The almond and its pupil: an element that is shown.
        [EditorIconCatalog.EyeOpen] =
            """
            <path d="M1.4 8S4 3.9 8 3.9 14.6 8 14.6 8 12 12.1 8 12.1 1.4 8 1.4 8Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <circle cx="8" cy="8" r="1.85" fill="{c}"/>
            """,

        // The same eye struck through — and struck through hard, with the almond
        // receding and the pupil gone: two lines of equal weight crossing at the
        // middle read as a muddle at 16 units, which is the size this is read at.
        [EditorIconCatalog.EyeClosed] =
            """
            <path d="M1.4 8S4 3.9 8 3.9 14.6 8 14.6 8 12 12.1 8 12.1 1.4 8 1.4 8Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"
                opacity="0.5"/>
            <path d="M2.9 13.1 13.1 2.9"
                fill="none" stroke="{c}" stroke-width="1.7" stroke-linecap="round"/>
            """,

        // A bin with its lid off: the row's own delete, which acts at once.
        [EditorIconCatalog.Trash] =
            """
            <path d="M2.5 4.25h11 M6.25 4.25V2.5h3.5v1.75 M4 4.25l.7 9.25h6.6l.7-9.25"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M6.6 6.75v4.25 M9.4 6.75v4.25"
                fill="none" stroke="{c}" stroke-width="1.1" stroke-linecap="round"/>
            """,

        // A pin leaning the way the hand tools lean: head high, needle at the
        // spot it holds. The panel headers' pin.
        [EditorIconCatalog.PanelPin] =
            """
            <path d="M9.2 2.2 13.8 6.8 12.6 8 11.4 7.8 8.6 10.6 8.8 12.6 7.6 13.8 5.2 11.4 2.2 14.4 1.6 13.8 4.6 10.8 2.2 8.4 3.4 7.2 5.4 7.4 8.2 4.6 8 3.4Z"
                fill="{c}"/>
            """,

        [EditorIconCatalog.ChevronDown] =
            """
            <path d="M3.5 5.75 8 10.25 12.5 5.75"
                fill="none" stroke="{c}" stroke-width="1.7"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // A double-headed arrow along the diagonal a corner grip pulls.
        [EditorIconCatalog.ResizeDiagonal] =
            """
            <path d="M5.2 5.2 10.8 10.8"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <path d="M4.2 7.6V4.2h3.4z" fill="{c}"/>
            <path d="M11.8 8.4v3.4H8.4z" fill="{c}"/>
            """,

        // The same arrow lying flat, for a panel whose height is its own.
        [EditorIconCatalog.ResizeHorizontal] =
            """
            <path d="M4.6 8h6.8"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <path d="M5.4 5.2 2.6 8l2.8 2.8z" fill="{c}"/>
            <path d="M10.6 5.2 13.4 8l-2.8 2.8z" fill="{c}"/>
            """,


        // Three sliders, knobs staggered: the panel where values are set.
        [EditorIconCatalog.Inspector] =
            """
            <path d="M2 4.4h12 M2 8h12 M2 11.6h12"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
            <circle cx="10.4" cy="4.4" r="1.8" fill="{c}"/>
            <circle cx="5.4" cy="8" r="1.8" fill="{c}"/>
            <circle cx="8.6" cy="11.6" r="1.8" fill="{c}"/>
            """,

        [EditorIconCatalog.Toolbar] =
            """
            <path d="M2.5 4.4h11 M2.5 8h11 M2.5 11.6h11"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            """,

        // A clock swept backwards: the arc runs counterclockwise into an
        // arrowhead, with the hands still on the dial.
        [EditorIconCatalog.History] =
            """
            <path d="M3.4 5A5.4 5.4 0 1 1 3.1 8.9"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linecap="round"/>
            <path d="M1.3 5.9 3.6 5 3.9 7.4Z" fill="{c}"/>
            <path d="M8.5 5.6v3l2 1.4"
                fill="none" stroke="{c}" stroke-width="1.3"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.FileMenu] =
            """
            <path d="M4 1.75h5.5L12 4.25V14.25H4Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M9.5 1.75V4.25H12" fill="none" stroke="{c}" stroke-width="1.1"/>
            """,

        // The File glyph's own sheet, with the create badge on it: the same plus
        // the tree's create rows wear, so "new" is one picture wherever it is.
        [EditorIconCatalog.NewFile] =
            """
            <path d="M3.2 1.75h5.1L10.6 4.05V10.2H3.2Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M8.3 1.75V4.05H10.6" fill="none" stroke="{c}" stroke-width="1.1"/>
            <path d="M11.1 9.4v5 M8.6 11.9h5"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            """,

        // Out of the box and away: the same sheet, with the arrow leaving it.
        // Deliberately the mirror of Open's arrow, which comes in.
        [EditorIconCatalog.Export] =
            """
            <path d="M9.6 2.2H3.4v11.6h9.2V8"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M7.4 8.6 13.8 2.2 M9.8 2.2h4v4"
                fill="none" stroke="{c}" stroke-width="1.4"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // A body with a lens, seen from the front, with the finder above it.
        // Deliberately not the render glyph beside it: that is a picture in a
        // frame, and a camera is what takes one — a scene holds several cameras
        // and produces one picture.
        [EditorIconCatalog.Camera] =
            """
            <path d="M1.9 5.4h3l1-1.6h4.2l1 1.6h3v7.2H1.9Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <circle cx="8" cy="9" r="2.4" fill="none" stroke="{c}" stroke-width="1.3"/>
            """,

        // A plane seen at an angle with a horizon on it: a drawing hung in space.
        // Not the group's dashed square and not the render's framed picture — the
        // first says "several things together" and the second says "the output".
        [EditorIconCatalog.ReferenceImage] =
            """
            <path d="M3.4 2.4 13.1 4.3v9.3L3.4 11.7Z"
                fill="none" stroke="{c}" stroke-width="1.35" stroke-linejoin="round"/>
            <path d="M3.4 9.6 6.9 7.2 9.4 9.4 13.1 6.6"
                fill="none" stroke="{c}" stroke-width="1.25"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // A sphere with its equator, which is what every application means by "a
        // 3D object". A cube is taken twice over in this set already.
        [EditorIconCatalog.ReferenceModel] =
            """
            <circle cx="8" cy="8" r="6" fill="none" stroke="{c}" stroke-width="1.35"/>
            <path d="M2 8a6 2.6 0 0 0 12 0a6 2.6 0 0 0-12 0"
                fill="none" stroke="{c}" stroke-width="1.2" stroke-opacity="0.75"/>
            """,

        // A picture in its frame. The first cut was a sun, and Preferences is a
        // circle with eight spokes — at the size a menu row draws them the two
        // were the same glyph. What a render produces is a picture, and nothing
        // else in this editor is one.
        [EditorIconCatalog.Render] =
            """
            <rect x="1.7" y="2.9" width="12.6" height="10.2" rx="1.3"
                fill="none" stroke="{c}" stroke-width="1.4"/>
            <circle cx="5.4" cy="6.3" r="1.2" fill="{c}"/>
            <path d="M2.4 12.3 6.3 8.4 8.5 10.6 10.7 8.3 13.6 11.2"
                fill="none" stroke="{c}" stroke-width="1.35"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // The triangle every desktop means by it, with the bar and the dot.
        [EditorIconCatalog.Warning] =
            """
            <path d="M8 1.9 15 13.9H1Z"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            <path d="M8 6.1v3.6"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <circle cx="8" cy="11.9" r="0.85" fill="{c}"/>
            """,

        // The folder again, with what comes out of it.
        [EditorIconCatalog.Open] =
            """
            <path d="M1.75 3.5h4.25l1.5 1.75h6.75V13H1.75Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M8 11.4V7.4 M6.2 9 8 7.2 9.8 9"
                fill="none" stroke="{c}" stroke-width="1.3"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.Save] =
            """
            <path d="M2.5 2.5h9L13.5 4.5v9h-11Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M5.2 2.5v3.2h5.2V2.5" fill="none" stroke="{c}" stroke-width="1.1"/>
            <rect x="4.7" y="8.7" width="6.6" height="4.8"
                fill="none" stroke="{c}" stroke-width="1.1"/>
            """,

        [EditorIconCatalog.Undo] =
            """
            <path d="M3.2 6.2H9a4 4 0 0 1 0 8H6.5"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <path d="M6.2 3.2 3.2 6.2l3 3"
                fill="none" stroke="{c}" stroke-width="1.5"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.Redo] =
            """
            <path d="M12.8 6.2H7a4 4 0 0 0 0 8h2.5"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <path d="M9.8 3.2 12.8 6.2l-3 3"
                fill="none" stroke="{c}" stroke-width="1.5"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // A ring with eight spokes: the gear every application means by it.
        [EditorIconCatalog.Preferences] =
            """
            <circle cx="8" cy="8" r="3.1" fill="none" stroke="{c}" stroke-width="1.5"/>
            <path d="M8 1.4v2.3 M8 12.3v2.3 M1.4 8h2.3 M12.3 8h2.3
                M3.3 3.3l1.65 1.65 M11.05 11.05l1.65 1.65 M12.7 3.3l-1.65 1.65 M4.95 11.05 3.3 12.7"
                fill="none" stroke="{c}" stroke-width="1.6" stroke-linecap="round"/>
            """,

        // A plug on its lead: the socket an agent connects to. Prongs up, so it
        // reads as the plug rather than as the wall.
        [EditorIconCatalog.SectionEndpoint] =
            """
            <path d="M6 1.6v3.1 M10 1.6v3.1"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <path d="M3.6 4.7h8.8v3.1a4.4 4.4 0 0 1-8.8 0Z"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            <path d="M8 12.2v2.2"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            """,

        // Passed from one to the other: a filled card, an arrow, an empty one.
        // The editor changes hands; what it is does not.
        [EditorIconCatalog.SectionHandover] =
            """
            <rect x="1.3" y="4.4" width="4.3" height="7.2" rx="0.9" fill="{c}"/>
            <rect x="10.4" y="4.4" width="4.3" height="7.2" rx="0.9"
                fill="none" stroke="{c}" stroke-width="1.3"/>
            <path d="M6.6 8h2.8 M8.2 6.7 9.5 8 8.2 9.3"
                fill="none" stroke="{c}" stroke-width="1.4"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        // A screen on its stand: what the page is about is what you look at.
        [EditorIconCatalog.SectionDisplay] =
            """
            <rect x="1.6" y="2.7" width="12.8" height="8.4" rx="1.2"
                fill="none" stroke="{c}" stroke-width="1.4"/>
            <path d="M8 11.1v2.2 M5.2 13.3h5.6"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linecap="round"/>
            """,

        // A sheet with its corner folded down: the page about files — what is
        // kept, where, and what comes back.
        [EditorIconCatalog.SectionFiles] =
            """
            <path d="M4.2 1.9h5l3.6 3.6v7.7a.9.9 0 0 1-.9.9H4.2a.9.9 0 0 1-.9-.9V2.8a.9.9 0 0 1 .9-.9Z"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            <path d="M9.2 1.9v3.6h3.6"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            <path d="M5.6 8.6h4.8 M5.6 11h3.2"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
            """,

        // A spanner laid across the diagonal: the page of things you reach for
        // only when something is being taken apart.
        [EditorIconCatalog.SectionDebug] =
            """
            <path d="M10.4 1.9a3.6 3.6 0 0 0-3.1 5.4L2.4 12.2a1.4 1.4 0 0 0 2 2l4.9-4.9
                a3.6 3.6 0 0 0 4.5-4.7l-2.1 2.1-2-.5-.5-2Z"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            """,

        // The magnifier, tilted the way every magnifier is so the handle clears
        // the glass.
        [EditorIconCatalog.Search] =
            """
            <circle cx="6.9" cy="6.9" r="4" fill="none" stroke="{c}" stroke-width="1.5"/>
            <path d="M9.9 9.9 13.6 13.6"
                fill="none" stroke="{c}" stroke-width="1.6" stroke-linecap="round"/>
            """,

        [EditorIconCatalog.Create] =
            """
            <path d="M8 3v10 M3 8h10"
                fill="none" stroke="{c}" stroke-width="1.6" stroke-linecap="round"/>
            """,

        [EditorIconCatalog.Delete] =
            """
            <path d="m4 4 8 8 M12 4l-8 8"
                fill="none" stroke="{c}" stroke-width="1.6" stroke-linecap="round"/>
            """,

        [EditorIconCatalog.Rename] =
            """
            <path d="M3 13l.5-2.5L11 3l2 2-7.5 7.5Z M9.5 4.5 11.5 6.5"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.Reparent] =
            """
            <path d="M4 2.5V8a2.5 2.5 0 0 0 2.5 2.5H12 M9.5 7.5l3 3-3 3"
                fill="none" stroke="{c}" stroke-width="1.5"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.Move] =
            """
            <path d="M8 3.5v9 M3.5 8h9"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linecap="round"/>
            <path d="M8 1 6.2 3.4h3.6Z M8 15l-1.8-2.4h3.6Z
                M1 8l2.4-1.8v3.6Z M15 8l-2.4-1.8v3.6Z" fill="{c}"/>
            """,

        [EditorIconCatalog.Rotate] =
            """
            <path d="M13.25 8A5.25 5.25 0 1 1 10.9 3.63"
                fill="none" stroke="{c}" stroke-width="1.5" stroke-linecap="round"/>
            <path d="M9.9 5.2 14.1 4.7 12.2 1.4Z" fill="{c}"/>
            """,

        [EditorIconCatalog.Scale] =
            """
            <rect x="2.5" y="7.5" width="6" height="6"
                fill="none" stroke="{c}" stroke-width="1.4"/>
            <path d="M9.5 6.5 13 3 M13.5 6.5v-4h-4"
                fill="none" stroke="{c}" stroke-width="1.4"
                stroke-linecap="round" stroke-linejoin="round"/>
            """,

        [EditorIconCatalog.FieldMode] =
            """
            <path d="M5.5 2v12 M10.5 2v12 M2 5.5h12 M2 10.5h12"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
            """,

        // Three cells of a four-cell grid: a voxel just placed, or just erased.
        [EditorIconCatalog.VoxelEdit] =
            """
            <rect x="2.5" y="2.5" width="5.1" height="5.1" fill="{c}"/>
            <rect x="8.4" y="2.5" width="5.1" height="5.1" fill="{c}" opacity="0.55"/>
            <rect x="2.5" y="8.4" width="5.1" height="5.1" fill="{c}" opacity="0.55"/>
            """,

        [EditorIconCatalog.Material] =
            """
            <path d="M8 1.75C8 1.75 12.5 7 12.5 10a4.5 4.5 0 1 1-9 0C3.5 7 8 1.75 8 1.75Z"
                fill="none" stroke="{c}" stroke-width="1.4" stroke-linejoin="round"/>
            """
    };

    /// <summary>
    /// The create glyphs are the kind glyphs with a plus badge, composed from
    /// the same artwork so the two can never drift apart.
    /// </summary>
    static EditorIconFactory()
    {
        AddCreateBadge(EditorIconCatalog.CreateGroup, EditorIconCatalog.Group);
        AddCreateBadge(EditorIconCatalog.CreateModel, EditorIconCatalog.VoxelModel);
        AddCreateBadge(EditorIconCatalog.CreateCamera, EditorIconCatalog.Camera);
        AddCreateBadge(EditorIconCatalog.CreateScene, EditorIconCatalog.Scene);
        AddCreateBadge(EditorIconCatalog.CreateMaterial, EditorIconCatalog.Material);
        AddCreateBadge(EditorIconCatalog.CreateInstance, EditorIconCatalog.Prefab);
        AddCreateBadge(EditorIconCatalog.CreateReference, EditorIconCatalog.ReferenceImage);

        AddAgentFace(EditorIconCatalog.AgentFaceWorking, Eyes.Open);
        AddAgentFace(EditorIconCatalog.AgentFaceWaiting, Eyes.Small);
        AddAgentFace(EditorIconCatalog.AgentFaceSilent, Eyes.Crossed);
        AddAgentFace(EditorIconCatalog.AgentFaceBlink, Eyes.Shut);
    }

    /// <summary>
    /// The head every agent face shares — antenna and jaw, no eyes. Written once
    /// so the four expressions cannot drift apart, which is also what makes a
    /// cross-fade between any two of them read as a face changing rather than as
    /// an icon being swapped.
    /// </summary>
    private const string AgentHead =
        """
        <path d="M8 1.5v2.25" fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
        <rect x="2.75" y="4.25" width="10.5" height="8.25" rx="2.25"
            fill="none" stroke="{c}" stroke-width="1.3"/>
        """;

    /// <summary>What the head is wearing, and the whole of what a state changes.</summary>
    private static class Eyes
    {
        /// <summary>Working: square and lit, the pair the session rows already wear.</summary>
        public const string Open =
            """
            <rect x="5.4" y="7.4" width="1.9" height="1.9" fill="{c}"/>
            <rect x="8.7" y="7.4" width="1.9" height="1.9" fill="{c}"/>
            """;

        /// <summary>Waiting its turn: the same eyes, down to a couple of points.</summary>
        public const string Small =
            """
            <rect x="5.75" y="7.75" width="1.2" height="1.2" rx="0.3" fill="{c}"/>
            <rect x="9.05" y="7.75" width="1.2" height="1.2" rx="0.3" fill="{c}"/>
            """;

        /// <summary>Stopped answering. The one thing everybody reads without being told.</summary>
        public const string Crossed =
            """
            <path d="M5.4 7.4 7.3 9.3 M7.3 7.4 5.4 9.3 M8.7 7.4 10.6 9.3 M10.6 7.4 8.7 9.3"
                fill="none" stroke="{c}" stroke-width="1.2" stroke-linecap="round"/>
            """;

        /// <summary>Shut, for the blink — the only thing it does while nothing happens.</summary>
        public const string Shut =
            """
            <path d="M5.4 8.35h1.9 M8.7 8.35h1.9"
                fill="none" stroke="{c}" stroke-width="1.3" stroke-linecap="round"/>
            """;
    }

    private static void AddAgentFace(string name, string eyes) =>
        SourcesByName[name] = AgentHead + "\n" + eyes;

    /// <summary>Every name an icon is registered under, for the catalogue's test.</summary>
    public static IReadOnlyCollection<string> Names => SourcesByName.Keys;

    /// <summary>
    /// Every texture ever handed out, by the name it was registered under. A
    /// theme change re-renders into these rather than making new ones, and that
    /// is what makes a switch reach the icons already assigned to buttons and
    /// tree rows: a <see cref="Texture2D"/> is held by whoever was given it, so
    /// replacing the object leaves every holder showing the old picture, while
    /// replacing its pixels reaches all of them at once.
    /// </summary>
    private static readonly Dictionary<string, ImageTexture> TexturesByName = new();

    public static void Register(Theme theme, EditorPalette palette)
    {
        // Structure rows recede, element rows carry the one accent tint, and
        // everything else — the history glyphs above all — stays plain text
        // colour, so the panels read as quiet as they did without icons. How far
        // "recede" goes is the palette's, because it is measured against the
        // panel it lands on and the two themes' panels are not equally forgiving.
        Color structure = palette.IconStructure;

        foreach ((string name, string source) in SourcesByName)
            theme.SetIcon(name, EditorThemeFactory.SemanticType, Draw(name, source, TintOf(name, palette, structure)));

        // The plaque's robot: the session glyph again, at full text colour. A
        // second registration rather than a second drawing, so the two cannot
        // drift apart — one is literally the other, retinted.
        theme.SetIcon(
            EditorIconCatalog.AgentPlaque,
            EditorThemeFactory.SemanticType,
            Draw(
                EditorIconCatalog.AgentPlaque,
                SourcesByName[EditorIconCatalog.AgentSession],
                palette.Text));

        // The four expressions, at the plaque's own strength for the same
        // reason: on the plaque the robot is the subject, not the furniture.
        // The loop above has already registered them at the row tint, which is
        // what a history row would want if it ever asked for one.
        foreach (string face in AgentFaces)
        {
            theme.SetIcon(
                PlaqueFace(face),
                EditorThemeFactory.SemanticType,
                Draw(PlaqueFace(face), SourcesByName[face], palette.Text));
        }
    }

    /// <summary>The plaque's registration of a face, told apart from the row tint's.</summary>
    public static string PlaqueFace(string face) => $"{face}_plaque";

    /// <summary>The four expressions, so a caller can walk them without listing them.</summary>
    public static IReadOnlyList<string> AgentFaces { get; } = new[]
    {
        EditorIconCatalog.AgentFaceWorking,
        EditorIconCatalog.AgentFaceWaiting,
        EditorIconCatalog.AgentFaceSilent,
        EditorIconCatalog.AgentFaceBlink
    };

    private static void AddCreateBadge(string name, string baseName) =>
        SourcesByName[name] =
            $"""
            <g transform="translate(0.2,0.2) scale(0.7)">{SourcesByName[baseName]}</g>
            <path d="M12.2 9.4v5 M9.7 11.9h5"
                fill="none" stroke="{ColorMark}" stroke-width="1.7" stroke-linecap="round"/>
            """;

    private static Color TintOf(string name, EditorPalette palette, Color structure) => name switch
    {
        EditorIconCatalog.Project => structure,
        EditorIconCatalog.Folder => structure,
        EditorIconCatalog.HistoryOrigin => structure,
        EditorIconCatalog.AgentSession => structure,
        EditorIconCatalog.Group => palette.IconElement,
        EditorIconCatalog.VoxelModel => palette.IconElement,
        EditorIconCatalog.Prefab => palette.IconElement,
        _ => palette.Text
    };

    /// <summary>
    /// Rasterises one glyph, and gives the texture back its <b>logical</b> size:
    /// the artwork is 16 units, the bitmap is <see cref="Oversample"/> times that
    /// so a scaled window has spare pixels to draw from, and everything that asks
    /// a texture how big it is should be told 16.
    /// </summary>
    /// <remarks>
    /// Without the override a consumer draws the bitmap at its pixel size — four
    /// times too large — and the answer has to be a cap at every point of use.
    /// Three of those exist (the tree and history rows' <c>SetIconMaxWidth</c>,
    /// and <c>ToolButton</c>'s <c>icon_max_width</c>), and the fourth was the one
    /// that was missed: a <see cref="TreeItem"/> cell button, which Godot draws at
    /// the texture's own size and offers no cap for at all. So the size is stated
    /// once, here, where the artwork's units are known.
    /// </remarks>
    /// <summary>
    /// The glyph, in the one texture registered under this name — created on the
    /// first call and repainted on every later one. See
    /// <see cref="TexturesByName"/> for why the object has to be the same one.
    /// </summary>
    private static ImageTexture Draw(string name, string source, Color tint)
    {
        Image image = Rasterise(source, tint);

        if (TexturesByName.TryGetValue(name, out ImageTexture? texture))
        {
            texture.SetImage(image);

            // Re-stated after the image: setting one resets the override to the
            // bitmap's own size, which is the whole thing the override exists to
            // correct.
            texture.SetSizeOverride(new Vector2I(ArtworkSide, ArtworkSide));

            return texture;
        }

        texture = ImageTexture.CreateFromImage(image);
        texture.SetSizeOverride(new Vector2I(ArtworkSide, ArtworkSide));

        TexturesByName[name] = texture;

        return texture;
    }

    private static Image Rasterise(string source, Color tint)
    {
        var svg =
            $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="16" height="16">
            {source.Replace(ColorMark, $"#{tint.ToHtml(includeAlpha: false)}")}
            </svg>
            """;

        var image = new Image();
        image.LoadSvgFromString(svg, Oversample);

        return image;
    }
}
