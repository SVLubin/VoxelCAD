using System;
using System.Globalization;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Writes a colour as text and reads one back. Pure, and separate from the field
/// that shows it, because what a colour looks like written down is a decision
/// worth testing and a Godot control is not testable at all.
/// </summary>
/// <remarks>
/// Reading quantises, and that is honest rather than regrettable: a hex field
/// cannot express the float the wheel produced, and a value somebody typed is a
/// value somebody chose. What must not happen is the display quantising a colour
/// nobody touched, which is why the field writes only on commit and a refresh
/// from the model never round-trips through the text.
/// </remarks>
internal static class ColorText
{
    public static string Write(Color color, ColorTextFormat format) => format switch
    {
        ColorTextFormat.Hex => WriteHex(color),
        ColorTextFormat.Rgb => WriteRgb(color),
        ColorTextFormat.Hsv => WriteHsv(color),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    /// <summary>
    /// False for anything that is not a colour in this format. The alpha of the
    /// colour handed in is kept wherever the text does not carry one, so typing
    /// six hex digits into a translucent material does not quietly make it solid.
    /// </summary>
    public static bool TryRead(string text, ColorTextFormat format, Color current, out Color color)
    {
        color = current;
        var trimmed = text.Trim();

        return format switch
        {
            ColorTextFormat.Hex => TryReadHex(trimmed, current, out color),
            ColorTextFormat.Rgb => TryReadRgb(trimmed, current, out color),
            ColorTextFormat.Hsv => TryReadHsv(trimmed, current, out color),
            _ => false
        };
    }

    private static string WriteHex(Color color) =>
        color.A >= 1f
            ? $"#{Channel(color.R):X2}{Channel(color.G):X2}{Channel(color.B):X2}"
            : $"#{Channel(color.R):X2}{Channel(color.G):X2}{Channel(color.B):X2}{Channel(color.A):X2}";

    private static string WriteRgb(Color color) =>
        color.A >= 1f
            ? $"{Channel(color.R)}, {Channel(color.G)}, {Channel(color.B)}"
            : $"{Channel(color.R)}, {Channel(color.G)}, {Channel(color.B)}, {Channel(color.A)}";

    private static string WriteHsv(Color color)
    {
        // Godot's H is a turn rather than degrees, and S and V are fractions.
        var hue = (int)MathF.Round(color.H * 360f);
        var saturation = (int)MathF.Round(color.S * 100f);
        var value = (int)MathF.Round(color.V * 100f);

        return $"{hue}°, {saturation}%, {value}%";
    }

    private static bool TryReadHex(string text, Color current, out Color color)
    {
        color = current;

        string digits = text.TrimStart('#');

        if (digits.Length != 6 && digits.Length != 8)
            return false;

        foreach (char digit in digits)
        {
            if (!Uri.IsHexDigit(digit))
                return false;
        }

        var parsed = new Color(digits);

        // Six digits say nothing about alpha, so the colour keeps the one it had.
        color = digits.Length == 6 ? parsed with { A = current.A } : parsed;

        return true;
    }

    private static bool TryReadRgb(string text, Color current, out Color color)
    {
        color = current;

        string[] parts = Split(text);

        if (parts.Length is not (3 or 4))
            return false;

        var channels = new float[4];
        channels[3] = current.A;

        for (var index = 0; index < parts.Length; index++)
        {
            if (!TryReadNumber(parts[index], out float value))
                return false;

            channels[index] = Math.Clamp(value, 0f, 255f) / 255f;
        }

        color = new Color(channels[0], channels[1], channels[2], channels[3]);

        return true;
    }

    private static bool TryReadHsv(string text, Color current, out Color color)
    {
        color = current;

        string[] parts = Split(text);

        if (parts.Length is not (3 or 4))
            return false;

        if (!TryReadNumber(parts[0], out float hue)
            || !TryReadNumber(parts[1], out float saturation)
            || !TryReadNumber(parts[2], out float value))
        {
            return false;
        }

        float alpha = current.A;

        if (parts.Length == 4)
        {
            if (!TryReadNumber(parts[3], out float typed))
                return false;

            alpha = Math.Clamp(typed, 0f, 255f) / 255f;
        }

        // Wrapped rather than clamped: a hue is an angle, and 370 is 10.
        float turns = Mathf.PosMod(hue, 360f) / 360f;

        color = Color.FromHsv(
            turns,
            Math.Clamp(saturation, 0f, 100f) / 100f,
            Math.Clamp(value, 0f, 100f) / 100f,
            alpha);

        return true;
    }

    /// <summary>
    /// Commas or spaces, and the units are optional: what somebody pastes has its
    /// own punctuation, and refusing it teaches nothing.
    /// </summary>
    private static string[] Split(string text) =>
        text.Replace('°', ' ')
            .Replace('%', ' ')
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool TryReadNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static int Channel(float value) =>
        Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
