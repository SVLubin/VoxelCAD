using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Which modes a transform channel accepts. The scene enforces this, and the
/// inspector builds its mode toggles from the same source, so the UI can never
/// offer a mode the model would refuse.
/// </summary>
public static class FieldModeHelper
{
    private static readonly FieldMode[] LinearModes = { FieldMode.Float, FieldMode.Integer };

    private static readonly FieldMode[] AngularModes =
    {
        FieldMode.Float,
        FieldMode.Integer,
        FieldMode.Snap90
    };

    public static IReadOnlyList<FieldMode> GetAllowedModes(TransformChannel channel) =>
        channel == TransformChannel.Rotation ? AngularModes : LinearModes;

    public static bool IsAllowed(TransformChannel channel, FieldMode mode) =>
        mode != FieldMode.Snap90 || channel == TransformChannel.Rotation;
}
