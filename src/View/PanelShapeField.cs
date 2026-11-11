namespace VoxelCAD.View;

/// <summary>
/// The shape a panel and its landing outline are both made of, as shader source:
/// a rounded box with a rounded box subtracted from it, and the coverage rule
/// that rasterises it. Two materials concatenate this, so the Γ cannot be drawn
/// one way in one place and another way in the other.
/// </summary>
/// <remarks>
/// A string rather than a <c>.gdshaderinc</c> because these shaders are built in
/// code and have no resource path to include from.
/// <para>
/// <b>Nothing here may read the screen.</b> The glass declares its own sampler;
/// the ghost declares none, which is what keeps it a mark about a surface rather
/// than a second surface — and keeps one screen read per glass panel true (see
/// <c>docs/panels.md</c>). A sampler moved in here would be taken by both,
/// silently, once per frame for the length of every drag.
/// </para>
/// </remarks>
internal static class PanelShapeField
{
    public const string Source = """
        // Per surface, so one material serves every panel: the surface's own
        // size in canvas units, its corner radius, and the notch bitten out.
        instance uniform vec2 rect_size = vec2(100.0, 100.0);
        instance uniform float corner_radius = 3.0;
        instance uniform float border_width = 1.0;

        // The stub a panel grown from its tab keeps: x is how wide it is, y how
        // far down it reaches. Zero leaves an ordinary rounded card.
        instance uniform vec2 stub = vec2(0.0, 0.0);

        // 0 places the stub on the left, 1 on the right — a column mirrors.
        instance uniform float stub_on_right = 0.0;

        float rounded_box(vec2 point, vec2 half_size, float radius) {
            vec2 q = abs(point) - half_size + radius;
            return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
        }

        // Intersection — max — with the corner it creates rounded at the same
        // radius as every other corner. Plain max leaves a right angle, which
        // is what the two corners the notch carves out used to be. Exact for
        // perpendicular edges, and it changes nothing further than `radius`
        // from either boundary, so the notch's own concave fillet is untouched.
        float rounded_intersection(float a, float b, float radius) {
            vec2 u = max(vec2(radius + a, radius + b), vec2(0.0));
            return min(-radius, max(a, b)) + length(u);
        }

        float panel_distance(vec2 p) {
            vec2 half_size = rect_size * 0.5;
            float radius = min(corner_radius, min(half_size.x, half_size.y));
            float distance = rounded_box(p - half_size, half_size, radius);

            // The notch: everything below the stub, on the stub's own side. Its
            // edges are placed one by one rather than as a centre and a half —
            // only the corner that lands inside the surface should round, so the
            // other three are pushed clear of it, and growing the box evenly
            // would have carried the inner edge out with them.
            if (stub.x > 0.5) {
                float clear = radius + 1.0;
                float left = stub_on_right > 0.5 ? rect_size.x - stub.x : -clear;
                float right = stub_on_right > 0.5 ? rect_size.x + clear : stub.x;
                float top = stub.y;
                float bottom = rect_size.y + clear;

                vec2 notch_centre = vec2((left + right) * 0.5, (top + bottom) * 0.5);
                vec2 notch_half = vec2((right - left) * 0.5, (bottom - top) * 0.5);

                distance = rounded_intersection(
                    distance, -rounded_box(p - notch_centre, notch_half, radius), radius);
            }

            return distance;
        }

        // How much of the shape a pixel covers, and how much of that coverage is
        // the hairline. Antialiased in *pixels*, not in canvas units: the
        // window's stretch has been measured at 2.58, and softening by a unit
        // there is a three-pixel smear on every edge in the chrome.
        //
        // The true gradient rather than fwidth, which is the L1 norm — 41% too
        // large where the field runs at 45 degrees, which is every rounded
        // corner. And half of it, because it is spent as the *half*-width of a
        // smoothstep either side: a whole one leaves the hairline no solid core.
        //
        // The hairline is drawn *inwards* from the outline; straddling it put
        // half the line where the coverage erases it. The inner edge is floored
        // at zero so a window scaled down until a pixel is wider than the line
        // cannot invert the ramp.
        //
        // NOTE: y is 1 *everywhere outside the shape*, not only on the hairline.
        // It becomes a hairline only after it meets x. Multiply it through the
        // coverage; added to it, the notch and every corner past the rounding
        // flood with solid colour.
        vec2 panel_coverage(float distance) {
            float aa = max(0.5 * length(vec2(dFdx(distance), dFdy(distance))), 0.0001);

            return vec2(
                1.0 - smoothstep(-aa, aa, distance),
                1.0 - smoothstep(max(border_width - aa, 0.0), border_width + aa, -distance));
        }
        """;
}
