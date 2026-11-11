using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Preferences;

namespace VoxelCAD.Presenter;

/// <summary>
/// The read side of the application preferences, so an agent can report which
/// endpoint it is talking to and on what terms it holds control. Editor-only, like
/// the camera and control commands: a headless run has no persisted settings and no
/// listener. Read-only on purpose — an agent changing the port it is connected on
/// would be sawing off the branch it sits on, so writing preferences stays a
/// question for the GUI.
/// </summary>
internal static class PreferencesCommands
{
    public static IReadOnlyList<CommandDefinition> Create(IEditorPreferences preferences)
    {
        return new List<CommandDefinition>
        {
            Query("preferences", "Reports the agent endpoint and the handover's terms.",
                (_, _) => CommandResult.Success(new Dictionary<string, object?>
                {
                    ["endpoint"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = preferences.McpEnabled,
                        ["url"] = preferences.McpUrl,
                        ["port"] = preferences.McpPort,
                        ["clientConfigPath"] = preferences.ClientConfigPath,
                        // The token itself is a secret and stays off the surface — an
                        // agent that reached this command already authenticated with
                        // it. Reporting only that one is set answers "is the endpoint
                        // configured" without echoing it back.
                        ["tokenSet"] = preferences.McpToken.Length > 0
                    },
                    ["handover"] = new Dictionary<string, object?>
                    {
                        ["countdownEnabled"] = preferences.CountdownEnabled,
                        ["countdownSeconds"] = preferences.CountdownSeconds,
                        ["maximumWaitSeconds"] = preferences.MaximumWaitSeconds,
                        ["silenceWarningSeconds"] = preferences.SilenceWarningSeconds,
                        ["silenceGraceSeconds"] = preferences.SilenceGraceSeconds
                    },

                    // So an agent can ask whether the instrument is available
                    // rather than discovering it by being refused.
                    ["inputInjection"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = preferences.InputInjectionEnabled,
                        ["note"] = "The 'input-*' commands drive the interface for testing it. "
                            + "Build models with 'set-voxels', 'fill-box' and 'set-transform'."
                    },

                    // Reading earns its place here: it is the one thing that
                    // tells an agent whether a capture taken right after an
                    // injected gesture can be mid-transition. Writing does not —
                    // switching animation off changes what every later capture
                    // shows, for the user and for the next agent, and unlike a
                    // session's camera a preference does not come back.
                    ["animation"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = preferences.AnimationsEnabled,
                        ["speed"] = preferences.AnimationSpeed.ToString(),
                        ["note"] = "How the chrome moves, and how long it waits. With it off "
                            + "every transition lands at once and a tab opens the frame the "
                            + "pointer reaches it; otherwise a 'window' capture taken straight "
                            + "after an 'input-*' gesture may catch a panel in flight — a "
                            + "transition lasts 0.05 to 0.20 s at Fast and 2.25 times that at "
                            + "Slow, and a hover takes 0.2 s at Fast to be believed."
                    },

                    // Reading, on the same terms as the pace: a light theme
                    // changes what every 'window' capture looks like, so an
                    // agent reading a screenshot needs to know which way round
                    // the chrome is drawn. Writing is refused for the reason
                    // every other display preference is — it outlives the
                    // session, and nothing puts it back.
                    ["theme"] = new Dictionary<string, object?>
                    {
                        ["name"] = preferences.Theme.ToString(),
                        ["accent"] = preferences.AccentColor,
                        ["note"] = "Which way round the chrome is drawn, and the colour it "
                            + "picks things out in. A 'window' capture is light or dark "
                            + "accordingly; the 3D scene is not themed."
                    },

                    // What the render form is set to. Reading only, like the
                    // rest here — and worth reading, because a render an agent
                    // asks for takes its own arguments while the card takes
                    // these, and the two answering differently is exactly the
                    // kind of thing nobody could explain from a picture.
                    ["render"] = BuildRenderReport(preferences.RenderDials),

                    // What the viewport is spending its frame on, and whether the
                    // chrome is glass. Reading only, like every display preference
                    // — and the one an agent most needs, because a 'frame' capture
                    // with the occlusion or the factures switched off is a picture
                    // of a different editor and nothing else would say so.
                    ["display"] = new Dictionary<string, object?>
                    {
                        ["occlusion"] = preferences.ViewportOcclusion,
                        ["shadows"] = preferences.ViewportShadows,
                        ["factures"] = preferences.ViewportFactures,
                        ["glass"] = preferences.GlassPanels,
                        ["note"] = "What the viewport draws and whether the panels read the "
                            + "screen. Water and a mirror still read with everything off; "
                            + "the tracer ignores all four, being a different way of seeing."
                    },

                    ["viewCube"] = new Dictionary<string, object?>
                    {
                        ["corner"] = preferences.ViewCubeCorner.ToString(),
                        ["note"] = "Where the navigation cube sits in the viewport. Look from a "
                            + "standard direction with 'view', not by clicking it."
                    },

                    // How the material section is being worked in. Reading only,
                    // like every other display preference — and worth reading for
                    // the same reason the theme is: a 'window' capture shows the
                    // tuning rows or does not, and the colour row is written in
                    // whichever notation this says.
                    ["material"] = new Dictionary<string, object?>
                    {
                        ["advanced"] = preferences.MaterialAdvanced,
                        ["colorFormat"] = preferences.MaterialColorFormat.ToString(),
                        ["previewShown"] = preferences.MaterialPreviewShown,
                        ["note"] = "What the inspector's material section is showing. None of "
                            + "it is document data. Read a material's own values with "
                            + "'materials' and look at one with 'material-preview'."
                    },

                    // The recovery snapshot's terms. Reading only, like every
                    // preference here; what the running editor has actually
                    // written is 'autosave-status', and 'autosave-now' writes one.
                    ["autosave"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = preferences.AutosaveEnabled,
                        ["intervalSeconds"] = preferences.AutosaveIntervalSeconds,
                        ["directory"] = preferences.AutosaveDirectory,
                        ["note"] = "A recovery copy of the open document, kept beside the editor's "
                            + "own settings and never in the user's file. 'autosave-status' says "
                            + "what has actually been written, 'autosave-now' writes a snapshot "
                            + "now, 'autosave-recover' opens one, 'autosave-discard' removes one "
                            + "this session left. Snapshots name this machine's absolute paths "
                            + "and are local to it."
                    },

                    // Chrome layout is read-only here on the same terms as the
                    // corner: an agent reads the document through 'tree' and
                    // 'slice', not through the panels, so it never needs to move
                    // one — but it should never be surprised by where they stand.
                    ["panels"] = BuildPanelReport(preferences),

                    // One number per side, because a column's panels share
                    // their width; a panel's dragged height rides on its own
                    // entry above. Reading only, like every display preference.
                    ["panelWidths"] = new Dictionary<string, object?>
                    {
                        ["left"] = preferences.PanelWidths.Left,
                        ["right"] = preferences.PanelWidths.Right
                    }
                }))
        };
    }

    private static Dictionary<string, object?> BuildRenderReport(RenderDials dials) =>
        new()
        {
            ["width"] = dials.Width,
            ["height"] = dials.Height,
            ["samples"] = dials.Samples,
            ["exposure"] = Math.Round(dials.Exposure, 3),
            ["sunElevation"] = Math.Round(dials.SunElevationDegrees, 2),
            ["sunAzimuth"] = Math.Round(dials.SunAzimuthDegrees, 2),
            ["sunSize"] = Math.Round(dials.SunSizeDegrees, 3),
            ["backdrop"] = dials.Backdrop.ToString(),
            ["ground"] = dials.Ground,
            ["note"] = "The film and the light the render form was last set to. The lens — the "
                + "projection, the blur and the focus — belongs to the camera the picture is "
                + "taken through; ask 'cameras' for that. A 'render' command takes its own "
                + "arguments and defaults rather than these, so the two can differ."
        };

    private static List<Dictionary<string, object?>> BuildPanelReport(
        IEditorPreferences preferences)
    {
        var panels = new List<Dictionary<string, object?>>();

        foreach (VoxelCAD.CommonTypes.PanelPlacement placement in preferences.PanelPlacements)
        {
            panels.Add(new Dictionary<string, object?>
            {
                ["panel"] = placement.Kind.ToString(),
                ["side"] = placement.Side.ToString(),
                ["order"] = placement.Order,
                ["pinned"] = placement.Pinned,
                ["folded"] = placement.Folded,

                // Null while the user has never dragged one: the body is as
                // tall as its content asks.
                ["height"] = placement.Height > 0.0 ? placement.Height : null
            });
        }

        return panels;
    }

    private static CommandDefinition Query(
        string name,
        string summary,
        System.Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters, mutates: false), handler);
}
