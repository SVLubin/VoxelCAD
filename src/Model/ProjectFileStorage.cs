using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VoxelCAD.Model;

/// <summary>
/// Places documents into storage and reads them back. The second of the two
/// serialization layers: it decides file layout, and knows nothing about the
/// model.
/// </summary>
/// <remarks>
/// Only the monolithic container is implemented. The multi-file mode — a
/// <c>.vcadproj</c> index beside separate assets — reuses the same documents,
/// which is why the split exists; see the tech-debt ledger.
/// </remarks>
internal static class ProjectFileStorage
{
    public const string MonolithicExtension = ".vcad";
    public const string MultiFileExtension = ".vcadproj";

    public static void Save(ProjectDocument document, string path)
    {
        EnsureSupported(path);
        RewriteReferencePaths(document, DirectoryOf(path), toStored: true);

        // Never in place: a crash or a full disk halfway through File.WriteAllText
        // left a truncated project, which is the one loss no recovery snapshot can
        // repair — the snapshot is deleted after a save and holds an older state.
        AtomicFile.WriteAllText(path, ToYaml(document));
    }

    public static ProjectDocument Load(string path)
    {
        EnsureSupported(path);

        ProjectDocument document = FromYaml(File.ReadAllText(path));

        RewriteReferencePaths(document, DirectoryOf(path), toStored: false);

        return document;
    }

    /// <summary>
    /// Turns every reference's path between the form the document carries and the
    /// form the file stores. It happens here because here is the only place that
    /// knows where the file itself sits — the layer above builds documents and has
    /// never been told the project's own path.
    /// </summary>
    /// <remarks>
    /// The document being rewritten on the way out is freshly built by
    /// <see cref="ProjectWriter"/> for this one save, so mutating it in place
    /// changes nothing anybody else is holding. <see cref="ToYaml"/> and
    /// <see cref="FromYaml"/> deliberately do not do this: without a path there is
    /// no directory to be relative to.
    /// </remarks>
    private static void RewriteReferencePaths(
        ProjectDocument document,
        string directory,
        bool toStored)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        foreach (SceneDocument scene in document.Scenes)
            RewriteReferencePaths(scene.Elements, directory, toStored);

        foreach (SceneDocument prefab in document.Prefabs)
            RewriteReferencePaths(prefab.Elements, directory, toStored);
    }

    private static void RewriteReferencePaths(
        List<ElementDocument> elementsList,
        string directory,
        bool toStored)
    {
        foreach (ElementDocument element in elementsList)
        {
            if (element.Reference is { } reference)
            {
                reference.Path = toStored
                    ? ReferencePaths.ToStored(reference.Path, directory)
                    : ReferencePaths.FromStored(reference.Path, directory);
            }

            RewriteReferencePaths(element.Children, directory, toStored);
        }
    }

    private static string DirectoryOf(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    public static string ToYaml(ProjectDocument document) =>
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithEventEmitter(next => new QuotedStringEmitter(next))
            // Absent rather than empty: a group would otherwise carry a blank
            // "voxels:" with trailing whitespace, and every leaf an empty child
            // list.
            .ConfigureDefaultValuesHandling(
                DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .Build()
            .Serialize(document);

    public static ProjectDocument FromYaml(string yaml) =>
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()
            .Deserialize<ProjectDocument>(yaml);

    private static void EnsureSupported(string path)
    {
        var extension = Path.GetExtension(path);

        if (string.Equals(extension, MonolithicExtension, StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(extension, MultiFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"The multi-file mode ({MultiFileExtension}) is not implemented yet; "
                + $"save as {MonolithicExtension}.");
        }

        throw new ArgumentException(
            $"Expected a {MonolithicExtension} project, got '{extension}'.", nameof(path));
    }
}
