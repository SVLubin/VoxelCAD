namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which language the editor's interface is written in.
/// </summary>
/// <remarks>
/// <see cref="English"/> is first so that a missing key and a value outside the
/// enum both land on what the editor ships with — the rule
/// <see cref="EditorTheme"/> and <see cref="AnimationSpeed"/> already keep. It is
/// also the language the source is written in, so falling back to it means
/// falling back to the msgid rather than to another catalogue that could be
/// missing the same entry.
/// <para>
/// A closed set rather than a free code, and that is the honest shape: adding a
/// language means adding its catalogue <b>and</b> its plural rule, so it is a
/// change to the code however it is spelled. An enum is where the compiler makes
/// somebody answer both questions, which is this codebase's standing preference
/// for naming what qualifies over letting a fallback rot as the set grows.
/// </para>
/// <para>
/// The agent-facing surface is deliberately not part of this. An MCP command's
/// description is read by a model, not by the user, and translating it would make
/// the editor's vocabulary depend on a display setting.
/// </para>
/// </remarks>
public enum EditorLanguage
{
    English,
    Russian,
    German,
    French,
    Spanish,
    BrazilianPortuguese
}
