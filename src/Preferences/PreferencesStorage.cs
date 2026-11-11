using System;
using System.IO;
using System.Text.Json;

namespace VoxelCAD.Preferences;

/// <summary>
/// Reads and writes the preferences file. Nothing here throws at the caller: an
/// editor that refused to start because a settings file was hand-edited badly
/// would be worse than one that starts with defaults and says so in the log.
/// </summary>
internal static class PreferencesStorage
{
    public const string FileName = "preferences.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static PreferencesDocument Load(string path)
    {
        if (!File.Exists(path))
            return new PreferencesDocument();

        try
        {
            return JsonSerializer.Deserialize<PreferencesDocument>(File.ReadAllText(path), JsonOptions)
                ?? new PreferencesDocument();
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException)
        {
            return new PreferencesDocument();
        }
    }

    /// <summary>Returns false when the settings could not be kept, so a caller can say so.</summary>
    public static bool TrySave(string path, PreferencesDocument document)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
            FilePermissions.RestrictToOwner(path);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
