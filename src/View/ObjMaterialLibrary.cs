using System;
using System.Collections.Generic;
using System.Globalization;

namespace VoxelCAD.View;

/// <summary>
/// An OBJ file's companion material library, read without an engine.
/// </summary>
internal static class ObjMaterialLibrary
{
    /// <summary>
    /// Every material the text names. An entry nobody can parse falls back to the
    /// default rather than stopping the read: a reference with one grey surface is
    /// worth more than a reference that would not load.
    /// </summary>
    public static Dictionary<string, ObjMaterial> Read(string text)
    {
        var materialsByNameDictionary = new Dictionary<string, ObjMaterial>();
        var name = string.Empty;
        ObjMaterial current = ObjMaterial.Default;

        foreach (string line in text.Split('\n'))
        {
            string[] words = Split(line);

            if (words.Length == 0)
                continue;

            switch (words[0].ToLowerInvariant())
            {
                case "newmtl":
                    Store();

                    name = words.Length > 1 ? words[1] : string.Empty;
                    current = ObjMaterial.Default;
                    break;

                case "kd" when words.Length >= 4:
                    current = current with
                    {
                        Diffuse = new Godot.Color(
                            Number(words[1]),
                            Number(words[2]),
                            Number(words[3]))
                    };
                    break;

                case "d" when words.Length >= 2:
                    current = current with { Opacity = Math.Clamp(Number(words[1]), 0f, 1f) };
                    break;

                // The same number said the other way round, and both are legal.
                case "tr" when words.Length >= 2:
                    current = current with { Opacity = Math.Clamp(1f - Number(words[1]), 0f, 1f) };
                    break;

                case "map_kd" when words.Length >= 2:
                    // Options such as -s and -o may stand before the file name; the
                    // name is the last word, which is true of every one of them.
                    current = current with { DiffuseMap = words[^1] };
                    break;
            }
        }

        Store();

        return materialsByNameDictionary;

        void Store()
        {
            if (!string.IsNullOrEmpty(name))
                materialsByNameDictionary[name] = current;
        }
    }

    private static string[] Split(string line)
    {
        int comment = line.IndexOf('#');
        string body = comment >= 0 ? line[..comment] : line;

        return body.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static float Number(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;
}
