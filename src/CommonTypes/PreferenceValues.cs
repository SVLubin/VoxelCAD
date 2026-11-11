using System.Collections.Generic;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// Every application preference a settings form can set, in one piece. Applied as
/// a unit so half a form never reaches disk.
/// </summary>
/// <remarks>
/// It carries no token: that one is generated rather than typed, and a form that
/// could edit it would be a form that could mistype it.
/// <para>
/// It lives here rather than with the preferences because it crosses the view
/// seam — the settings dialog reports what the user filled in, and a view knows
/// nothing about where preferences are kept.
/// </para>
/// </remarks>
/// <param name="MaximumWaitSeconds">
/// How long a pending request waits before the countdown runs whether or not the
/// user is still interacting. Without it a token nobody released stalls an agent
/// until the editor restarts.
/// </param>
/// <param name="SilenceWarningSeconds">
/// How long an agent holding control may issue no command at all before the
/// banner says it is not responding. Silence, not work: a long build is one
/// command that takes minutes, and stays welcome.
/// </param>
/// <param name="SilenceGraceSeconds">
/// How much longer after that warning before control returns to the user. It is
/// a further amount rather than a second absolute threshold, so there is always
/// a visible warning before control changes hands — whatever the two are set to.
/// </param>
/// <param name="InputInjectionEnabled">
/// Whether an agent may drive the interface itself — click buttons, drag handles,
/// press keys. Off by default, and deliberately so: it is a debug and self-test
/// instrument, and an agent that clicks instead of calling <c>set-voxels</c> would
/// quietly rot the semantic surface the whole project is built around.
/// </param>
/// <param name="ViewCubeCorner">
/// Which corner of the viewport the navigation cube sits in. A display
/// setting with no field on the settings form — see the ledger; it rides
/// through this record so that saving the form leaves it where it was rather
/// than resetting a value the page never showed.
/// </param>
/// <param name="AnimationsEnabled">
/// Whether the chrome animates at all. Off, everything arrives where it belongs
/// at once. A flag rather than the bottom of the speed dial: "how long" and
/// "whether" are different questions, and one of them is an accessibility need.
/// </param>
/// <param name="AnimationSpeed">
/// How long those transitions take, as a multiple of the shipped pace.
/// </param>
/// <param name="Theme">Which way round the chrome is drawn — see <see cref="EditorTheme"/>.</param>
/// <param name="AccentColor">
/// The one colour the editor picks things out in — a selected row, a pressed
/// tool, the focus ring, the selection box in the scene. Carried as an
/// <c>#RRGGBB</c> string rather than as a colour, because the preferences file
/// is meant to stay worth opening in a text editor, and because a colour type
/// would be the one piece of engine arithmetic in this record.
/// </param>
/// <param name="PanelPlacements">
/// Where each chrome panel stands — see <c>docs/panels.md</c>. Display
/// settings on the same terms as <paramref name="ViewCubeCorner"/>: no field
/// on the form, carried through untouched so saving it moves no panel.
/// </param>
/// <param name="Language">
/// Which language the interface is written in. The setting reaches the words
/// alone: numbers keep the formatting they have always had, because a decimal
/// comma would have to agree with what the inspector's spin boxes parse, and
/// that is a decision about input rather than about display.
/// </param>
/// <param name="ViewportOcclusion">
/// Whether the creases a model's own cells shade are drawn.
/// </param>
/// <param name="ViewportShadows">
/// Whether the key light casts. The fill never cast anything and the room is
/// furniture, so this is the whole of the scene's shadowing.
/// </param>
/// <param name="ViewportFactures">
/// Whether a material's relief is drawn. Its colour, its gloss and its metal are
/// what it <em>is</em> and are never switched off — a mirror that stopped being a
/// mirror to save a millisecond would be the editor lying about the document.
/// </param>
/// <param name="GlassPanels">
/// Whether the chrome's surfaces read the screen behind them and blur it.
/// </param>
/// <param name="AutosaveEnabled">
/// Whether a recovery snapshot of the open document is kept while it is being
/// worked on. A copy beside the editor's own settings, never the user's file:
/// what the next launch offers back when the editor stopped before a save.
/// </param>
/// <param name="AutosaveIntervalSeconds">
/// How often that snapshot is refreshed, counted from the previous one and only
/// when something has changed since it.
/// </param>
public readonly record struct PreferenceValues(
    bool McpEnabled,
    int McpPort,
    string ClientConfigPath,
    bool CountdownEnabled,
    double CountdownSeconds,
    double MaximumWaitSeconds,
    double SilenceWarningSeconds,
    double SilenceGraceSeconds,
    bool InputInjectionEnabled,
    ViewCubeCorner ViewCubeCorner,
    IReadOnlyList<PanelPlacement> PanelPlacements,
    bool AnimationsEnabled,
    AnimationSpeed AnimationSpeed,
    EditorTheme Theme,
    string AccentColor,
    EditorLanguage Language,
    bool ViewportOcclusion,
    bool ViewportShadows,
    bool ViewportFactures,
    bool GlassPanels,
    bool AutosaveEnabled,
    double AutosaveIntervalSeconds);
