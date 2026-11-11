using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The application settings form. Passive like every other view: it shows the
/// values it is given and reports the ones it holds, and has no idea where they
/// are kept or what happens when they change.
/// </summary>
public interface ISettingsView
{
    /// <summary>
    /// Something in the form was changed, carrying the whole of it. There is no
    /// accepting and no cancelling: a preference takes effect when it is set.
    /// </summary>
    /// <remarks>
    /// The whole record rather than the one field that moved, because a
    /// preference is stored as a unit and the form is the only thing that knows
    /// what is in every other control.
    /// <para>
    /// What counts as a change is the form's own decision and the one piece of
    /// judgement in it: a checkbox changes when it is clicked, a typed number
    /// when it is committed rather than while the digits arrive, and a colour
    /// when its picker is put away. Live means "as soon as the user has said
    /// it", not "on every keystroke".
    /// </para>
    /// </remarks>
    event Action<PreferenceValues> Edited;

    /// <summary>
    /// The form was dismissed — its own button, Escape, or a press outside the
    /// card. Nothing is applied or undone by it; everything already is.
    /// </summary>
    event Action CloseRequested;

    /// <summary>
    /// A new token was asked for. It does not close the form: the point of the
    /// button is to see the replacement, and the path it is written to, before
    /// deciding anything else.
    /// </summary>
    event Action RegenerateTokenRequested;

    /// <summary>
    /// Loads the form. The token, the endpoint and the autosave folder are shown,
    /// never edited — one is generated, one is composed from the port, and one is
    /// where the editor keeps its own files.
    /// </summary>
    void Fill(PreferenceValues values, string token, string endpoint, string autosaveDirectory);

    /// <summary>One line under the form: what the endpoint made of it.</summary>
    void SetStatus(string message);

    /// <summary>
    /// One line on the Files page: what the recovery snapshot is doing — when the
    /// last one was written, what it is waiting for, or why it could not be. Kept
    /// on the endpoint status's own terms: pushed whether or not the form is up.
    /// </summary>
    void SetAutosaveStatus(string message);

    void Open();

    void Close();
}
