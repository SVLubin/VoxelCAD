using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace VoxelCAD.Model;

/// <summary>
/// The sample projects the editor ships with, read from this assembly's own
/// resources. Each is an ordinary project file — <c>assets/samples/*.vcad</c> in
/// the repository — named by its file name without the extension.
/// </summary>
/// <remarks>
/// Embedded rather than read from <c>res://</c>, because the editor is not the only
/// reader: the headless CLI and the tests open the same samples, and neither has an
/// engine to resolve a resource path with. A sample may wear the shipped materials,
/// so <see cref="StandardMaterials.Install"/> has to have run first, exactly as for
/// any file read from disk.
/// </remarks>
internal static class Samples
{
    private const string Prefix = "VoxelCAD.Samples.";
    private const string Extension = ".vcad";

    private static readonly Assembly Owner = typeof(Samples).Assembly;

    public static IReadOnlyList<string> Names { get; } = Owner
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)
            && name.EndsWith(Extension, StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..^Extension.Length])
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Opens a sample as a new project with an empty history. Its references, were
    /// it ever to carry any, are taken as written: a sample has no directory for a
    /// relative path to be relative to.
    /// </summary>
    public static Project Open(string name)
    {
        string? match = Names.FirstOrDefault(
            candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new KeyNotFoundException(
                $"There is no sample called '{name}'. Samples: {string.Join(", ", Names)}.");

        using Stream stream = Owner.GetManifestResourceStream(Prefix + match + Extension)
            ?? throw new InvalidOperationException($"The sample '{match}' is listed but not readable.");
        using var reader = new StreamReader(stream);

        return ProjectReader.Read(ProjectFileStorage.FromYaml(reader.ReadToEnd()));
    }
}
