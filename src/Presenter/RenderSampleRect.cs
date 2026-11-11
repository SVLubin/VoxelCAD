using System;
using System.Collections.Generic;
using System.Globalization;

namespace VoxelCAD.Presenter;

/// <summary>
/// A rectangle of a rendered picture to be read back as a number, in pixels of
/// that picture.
/// </summary>
/// <remarks>
/// Its own type because the parsing is the part a test can reach: what a caller
/// writes is <c>x,y,w,h</c>, several of them separated by semicolons, and getting
/// that wrong quietly would mean measuring somewhere other than where the check
/// meant to.
/// </remarks>
public readonly record struct RenderSampleRect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Every rectangle in <c>x,y,w,h;x,y,w,h</c>. Empty pieces are skipped, so a
    /// trailing semicolon is not an error; anything else that does not parse is,
    /// because a rectangle nobody can read is a measurement nobody can trust.
    /// </summary>
    public static IReadOnlyList<RenderSampleRect> ParseAll(string text)
    {
        var rects = new List<RenderSampleRect>();

        foreach (string piece in text.Split(';'))
        {
            if (piece.Trim().Length == 0)
                continue;

            rects.Add(Parse(piece));
        }

        if (rects.Count == 0)
            throw new ArgumentException("A sample needs at least one 'x,y,w,h' rectangle.");

        return rects;
    }

    public override string ToString() => $"{X},{Y} {Width}x{Height}";

    private static RenderSampleRect Parse(string piece)
    {
        string[] parts = piece.Split(',');

        if (parts.Length != 4)
            throw new ArgumentException(
                $"A sample rectangle is 'x,y,w,h', got '{piece.Trim()}'.");

        int[] numbers = new int[4];

        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(
                    parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
            {
                throw new ArgumentException(
                    $"A sample rectangle is four whole numbers, got '{piece.Trim()}'.");
            }
        }

        if (numbers[2] <= 0 || numbers[3] <= 0)
            throw new ArgumentException(
                $"A sample rectangle has a positive width and height, got '{piece.Trim()}'.");

        return new RenderSampleRect(numbers[0], numbers[1], numbers[2], numbers[3]);
    }
}
