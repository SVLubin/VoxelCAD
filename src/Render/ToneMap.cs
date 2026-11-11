using Godot;

namespace VoxelCAD.Render;

/// <summary>
/// Turns accumulated radiance into a colour a screen can show: a filmic curve
/// that rolls the highlights off instead of clipping them, then the sRGB transfer
/// the file and the panel both expect.
/// </summary>
/// <remarks>
/// Narkowicz's fit of the ACES curve. It is not the engine's own tone mapper —
/// Godot's is inside its renderer and reachable from nothing here — so a render
/// and the viewport are graded differently by construction. That is honest rather
/// than accidental: the viewport shows a working scene under a fixed exposure,
/// and a render is a picture with one.
/// </remarks>
public static class ToneMap
{
    // Narkowicz's fit of the ACES curve. Named rather than inlined because the
    // resolve shader restates the expression and must not restate the numbers.
    internal const float CurveA = 2.51f;
    internal const float CurveB = 0.03f;
    internal const float CurveC = 2.43f;
    internal const float CurveD = 0.59f;
    internal const float CurveE = 0.14f;

    public static Color Apply(Color linear) =>
        new Color(Curve(linear.R), Curve(linear.G), Curve(linear.B)).LinearToSrgb();

    private static float Curve(float value)
    {
        float x = Mathf.Max(value, 0f);
        float mapped = x * (CurveA * x + CurveB) / (x * (CurveC * x + CurveD) + CurveE);

        return Mathf.Clamp(mapped, 0f, 1f);
    }
}
