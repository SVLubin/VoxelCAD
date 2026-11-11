using System.Collections.Generic;
using System.Text;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The names the 26 view-cube zones answer to on the command surface —
/// <c>front</c>, <c>top</c>, <c>top-front-right</c>. Generated from the zone
/// rather than tabulated, for the same reason the poses are: 26 literals are 26
/// chances to write one of them down wrong.
/// </summary>
/// <remarks>
/// Reading is lenient and writing is canonical. Any order of the parts parses, so
/// <c>front-top-right</c> reaches the same zone as <c>top-front-right</c>, but a
/// name this hands back always reads vertical, then depth, then horizontal — one
/// spelling per zone in every answer and every refusal.
/// <para>
/// Public for the same reason <see cref="ViewCubeGeometry"/> is: the command
/// surface spells the zones, and it must spell them the way the widget means them.
/// </para>
/// </remarks>
public static class ViewCubeDirections
{
    private const string Top = "top";
    private const string Bottom = "bottom";
    private const string Front = "front";
    private const string Back = "back";
    private const string Right = "right";
    private const string Left = "left";

    /// <summary>
    /// The direction each word names, in the scene's own frame: +Y is up, +Z is
    /// towards the front of the scene, +X is to its right.
    /// </summary>
    private static readonly Dictionary<string, Vector3I> PartsDictionary = new()
    {
        [Top] = new Vector3I(0, 1, 0),
        [Bottom] = new Vector3I(0, -1, 0),
        [Front] = new Vector3I(0, 0, 1),
        [Back] = new Vector3I(0, 0, -1),
        [Right] = new Vector3I(1, 0, 0),
        [Left] = new Vector3I(-1, 0, 0)
    };

    /// <summary>
    /// The zone a name means, or false. Refuses an unknown word, a name that says
    /// the same axis twice (<c>top-bottom</c>), and the empty name — each of which
    /// is a typo to correct rather than something to guess at.
    /// </summary>
    public static bool TryParse(string name, out Vector3I zone)
    {
        zone = Vector3I.Zero;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (string part in name.Trim().ToLowerInvariant().Split('-'))
        {
            if (!PartsDictionary.TryGetValue(part, out Vector3I direction))
                return false;

            // Two words about the same axis cancel or contradict; either way the
            // caller meant something they did not say.
            if (Dot(zone, direction) != 0)
                return false;

            zone += direction;
        }

        return ViewCubeGeometry.IsZone(zone);
    }

    /// <summary>The one spelling of a zone: vertical, then depth, then horizontal.</summary>
    public static string GetName(Vector3I zone)
    {
        var name = new StringBuilder();

        Append(name, zone.Y, Top, Bottom);
        Append(name, zone.Z, Front, Back);
        Append(name, zone.X, Right, Left);

        return name.ToString();
    }

    /// <summary>
    /// Every accepted name, faces first, then edges, then corners — the order they
    /// are worth reading in, and the order a refusal lists them.
    /// </summary>
    public static IReadOnlyList<string> GetNames()
    {
        var namesList = new List<string>();

        for (var parts = 1; parts <= 3; parts++)
        {
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            for (var z = -1; z <= 1; z++)
            {
                var zone = new Vector3I(x, y, z);

                if (ViewCubeGeometry.IsZone(zone) && CountParts(zone) == parts)
                    namesList.Add(GetName(zone));
            }
        }

        return namesList;
    }

    private static void Append(StringBuilder name, int component, string positive, string negative)
    {
        if (component == 0)
            return;

        if (name.Length > 0)
            name.Append('-');

        name.Append(component > 0 ? positive : negative);
    }

    private static int CountParts(Vector3I zone) =>
        Mathf.Abs(zone.X) + Mathf.Abs(zone.Y) + Mathf.Abs(zone.Z);

    private static int Dot(Vector3I left, Vector3I right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;
}
