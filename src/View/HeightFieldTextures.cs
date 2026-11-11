using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Turns one height field into the pair a facture needs. The normal map is
/// differentiated from the very heights the albedo is shaded from, so the two
/// describe the same surface — the same promise the noise path keeps by sharing
/// one noise resource.
/// </summary>
internal static class HeightFieldTextures
{
    public static FinishTexturePair Create(float[] heights, FinishRecipe recipe) =>
        new(
            CreateAlbedo(heights, recipe),
            CreateNormal(heights, recipe));

    /// <summary>
    /// Grey, from the recipe's floor up to white, multiplied into the flat colour
    /// by the material.
    /// </summary>
    private static Texture2D CreateAlbedo(float[] heights, FinishRecipe recipe)
    {
        Image image = Image.CreateEmpty(recipe.Size, recipe.Size, false, Image.Format.Rgb8);

        for (var y = 0; y < recipe.Size; y++)
        {
            for (var x = 0; x < recipe.Size; x++)
            {
                float shade = Mathf.Lerp(recipe.AlbedoFloor, 1f, heights[y * recipe.Size + x]);
                image.SetPixel(x, y, new Color(shade, shade, shade));
            }
        }

        image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Central differences, wrapped at the edges so the normal map tiles as
    /// exactly as the height field does. Encoded the way Godot reads a normal
    /// map: the vector remapped from minus-one-to-one into zero-to-one.
    /// </summary>
    private static Texture2D CreateNormal(float[] heights, FinishRecipe recipe)
    {
        Image image = Image.CreateEmpty(recipe.Size, recipe.Size, false, Image.Format.Rgb8);

        for (var y = 0; y < recipe.Size; y++)
        {
            for (var x = 0; x < recipe.Size; x++)
            {
                float left = At(heights, recipe, x - 1, y);
                float right = At(heights, recipe, x + 1, y);
                float down = At(heights, recipe, x, y - 1);
                float up = At(heights, recipe, x, y + 1);

                var normal = new Vector3(
                    (left - right) * recipe.BumpStrength,
                    (down - up) * recipe.BumpStrength,
                    1f).Normalized();

                image.SetPixel(x, y, new Color(
                    normal.X * 0.5f + 0.5f,
                    normal.Y * 0.5f + 0.5f,
                    normal.Z * 0.5f + 0.5f));
            }
        }

        // Renormalised: averaging four unit normals gives a shorter one, and a
        // shortened normal reads as a flatter surface. Without this a facture
        // quietly loses its relief with distance rather than merely losing detail.
        image.GenerateMipmaps(renormalize: true);

        return ImageTexture.CreateFromImage(image);
    }

    private static float At(float[] heights, FinishRecipe recipe, int x, int y)
    {
        int wrappedX = Wrap(x, recipe.Size);
        int wrappedY = Wrap(y, recipe.Size);

        return heights[wrappedY * recipe.Size + wrappedX];
    }

    private static int Wrap(int value, int size) => ((value % size) + size) % size;
}
