using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The pictures references stand for, decoded once and kept. Keyed by path, so
/// two references pointing at one file cost one decode and one texture between
/// them — which is the whole of what linking rather than embedding gives up
/// nothing for.
/// </summary>
/// <remarks>
/// <para>
/// The key carries the file's own timestamp and length beside its name, so a
/// picture edited in another application is decoded afresh the next time this is
/// asked. It is not a watch: nothing here notices a file changing on its own, and
/// what asks again is an edit to the reference. That is the honest half of "linked
/// rather than copied" — see the ledger.
/// </para>
/// <para>
/// A file that will not decode answers null rather than throwing. A reference may
/// name a picture that is not there, is not readable, or is not really a picture,
/// and none of those is a reason to take the editor down.
/// </para>
/// </remarks>
internal static class ReferenceAssets
{
    private static readonly Dictionary<string, (string Stamp, Texture2D? Texture)>
        _picturesByPathDictionary = new();

    public static Texture2D? Picture(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string stamp = Stamp(path);

        if (_picturesByPathDictionary.TryGetValue(path, out (string Stamp, Texture2D? Texture) held)
            && held.Stamp == stamp)
        {
            return held.Texture;
        }

        Texture2D? texture = Decode(path);

        _picturesByPathDictionary[path] = (stamp, texture);

        return texture;
    }

    private static Texture2D? Decode(string path)
    {
        // Asked before the engine is: a reference naming a file that is not there
        // is an ordinary state — the inspector says so and the plane draws a
        // placeholder — and the loader answers it by pushing an engine error with a
        // thirty-frame backtrace into the one channel there is for diagnosing a
        // running editor. A file that is there and will not decode still logs, and
        // should: that one is a surprise.
        if (!File.Exists(path))
            return null;

        Image? image = Image.LoadFromFile(path);

        if (image == null || image.IsEmpty())
            return null;

        // Eight texels to a cell is normal for a reference seen across a room, so
        // minification is the usual case rather than the exception — and on a
        // picture with any pattern in it that is crawl rather than sharpness.
        // Compressed formats cannot take a chain and do not need one.
        if (!image.IsCompressed())
            image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// What makes an entry stale. A file that is not there stamps as absent, so a
    /// picture that arrives later is picked up rather than remembered as missing.
    /// </summary>
    private static string Stamp(string path)
    {
        try
        {
            var file = new FileInfo(path);

            return file.Exists
                ? $"{file.LastWriteTimeUtc.Ticks}:{file.Length}"
                : "absent";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            return "absent";
        }
    }
}
