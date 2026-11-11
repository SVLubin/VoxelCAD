using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The materials the editor ships with. Every project has all of them, no project
/// saves any of them, and a cell that wears one goes on wearing it in whatever
/// document it is pasted into.
/// </summary>
/// <remarks>
/// The id is <b>derived from the name</b>, which is the whole of what makes that
/// promise keepable: a file names a standard material only by the id its cells
/// already carry, and the same name answers the same id in every document, on
/// every machine, for as long as the name does not change. Minted ids could not do
/// this — the library is not written to the file, so there would be nothing for a
/// reader to resolve them against.
/// <para>
/// A standard material is otherwise an ordinary <see cref="Material"/>: it can be
/// recoloured, retuned and worn like any other. What it cannot be is deleted or
/// renamed, because both would break the only link a saved file has to it.
/// </para>
/// </remarks>
public static class StandardMaterials
{
    /// <summary>
    /// Cells to one tile of the scan. A scanned tile is 64 texels, so eight cells
    /// puts eight texels on a voxel — coarse enough to read as squares and fine
    /// enough to carry a brick course.
    /// </summary>
    public const float DefaultScale = 8f;

    // Derived once for the process, not once per project. Hashing three hundred
    // names is nothing; hashing them again for every document is not — measured,
    // it took the parallel test host down outright, which is what a few hundred
    // thousand one-shot native hashes across threads buys.
    private static (Guid Id, MaterialSnapshot State)[]? _library;

    // Installed by the composition root rather than built on demand, and the
    // reason is measured rather than architectural taste: a single Material held
    // in a static of this layer crashes the Presenter test host outright — a
    // SIGSEGV at address zero, serial or parallel, with an empty library green
    // over repeated runs. The mechanism is not understood; what is known is that
    // it is the objects existing in this layer's statics and not the work of
    // making them, not their number, and not anything that reads them.
    //
    // Installing it from the editor is also the honest seam. The library is the
    // *editor's*: a document neither defines it nor saves it, so the thing that
    // is the editor is what should put it there. A test that wants it says so.
    private static Material[] _shared = Array.Empty<Material>();
    private static Dictionary<Guid, Material> _byId = new();

    /// <summary>
    /// Puts the shipped library in place for this process. Called by the
    /// composition root and by the headless CLI; idempotent, so calling it twice
    /// is harmless and calling it never leaves a project with its own materials
    /// and nothing else — which is exactly what a test wants unless it says
    /// otherwise.
    /// </summary>
    public static void Install()
    {
        if (_shared.Length > 0)
            return;

        var shared = new Material[Library().Length];
        var byId = new Dictionary<Guid, Material>(shared.Length);

        for (var i = 0; i < shared.Length; i++)
        {
            shared[i] = new Material(Library()[i].Id, Library()[i].State);
            byId[shared[i].Id] = shared[i];
        }

        _shared = shared;
        _byId = byId;
    }

    /// <summary>Whether an id names one of the shipped materials.</summary>
    public static bool Contains(Guid id) => _byId.ContainsKey(id);

    internal static bool TryGet(Guid id, out Material material) =>
        _byId.TryGetValue(id, out material!);

    /// <summary>
    /// The id a shipped material wears, derived from its name so that a file can
    /// name one by the id its cells already carry.
    /// </summary>
    /// <remarks>
    /// <b>These bytes are frozen.</b> Nothing writes the library to a file, so a
    /// document names a shipped material by this id alone — change how it is
    /// computed and every document ever saved loses the materials it was wearing.
    /// <see cref="StandardMaterialTests"/> pins two of them to their literal
    /// values for that reason, and a change here is meant to fail there.
    /// <para>
    /// The two stamped bytes follow RFC 4122's <em>byte</em> layout — version in
    /// the high nibble of byte six, variant in the top bits of byte eight — but
    /// <see cref="Guid(System.ReadOnlySpan{byte})"/> reads the first three fields
    /// little-endian, so only the variant lands where the printed form shows it.
    /// Measured across the whole library: every id has the variant nibble the RFC
    /// asks for, and the version nibble is whatever the hash put there. Nothing
    /// reads either — they were stamped to keep a derived id clear of a minted
    /// one, which 128 bits of SHA-256 already does — so this is recorded rather
    /// than corrected: correcting it would be a migration for decoration.
    /// </para>
    /// </remarks>
    public static Guid Derive(string name)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), hash);

        Span<byte> id = hash[..16];

        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id);
    }

    /// <summary>
    /// What a project seeds itself with: every shipped material, white, with the
    /// maps left to say what it is. The colour is white and the two dials are open
    /// because a scan carries its own albedo, its own gloss and its own metal —
    /// a tint or a ceiling here would be the editor arguing with the photograph.
    /// </summary>
    /// <summary>
    /// The shipped materials themselves — one set for the process, handed to every
    /// project rather than built per project.
    /// </summary>
    /// <remarks>
    /// Shared because they are the <b>editor's</b> and not the document's: every
    /// project has exactly the same three hundred, no project writes any of them,
    /// and a per-document copy would be three hundred objects apiece for state
    /// that cannot differ. It is also what makes them unchangeable rather than
    /// merely unsaved — see <see cref="Project.SetMaterialColor"/> — and that is
    /// the honest reading of "the file does not carry them": a change with nowhere
    /// to be saved is a change that would vanish on the next open.
    /// <para>
    /// It was measured into existence rather than reasoned: three hundred fresh
    /// materials per project took the parallel test host down outright, at any
    /// library size from fifty up, while the same suite with none was green three
    /// times over.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<Material> Shared => _shared;

    private static (Guid Id, MaterialSnapshot State)[] Library()
    {
        if (_library is not null)
            return _library;

        var library = new (Guid, MaterialSnapshot)[StandardMaterialNames.All.Length];

        for (var i = 0; i < library.Length; i++)
        {
            string name = StandardMaterialNames.All[i];

            library[i] = (
                Derive(name),
                new MaterialSnapshot(
                    name,
                    // Built rather than Colors.White: touching a Godot static
                    // class from a type initializer crashed the test host outright
                    // — a jump to address zero inside RunClassInitEx, which is a
                    // binding into an engine the host does not have. A struct
                    // constructor is arithmetic and reaches nothing.
                    new Color(1f, 1f, 1f),
                    Appearance: new MaterialAppearance(
                        Roughness: 1f,
                        Metallic: 1f,
                        Emission: 0f,
                        Finish: MaterialFinish.Standard,
                        FinishScale: DefaultScale,
                        FinishRelief: 1f)));
        }

        return _library = library;
    }

}
