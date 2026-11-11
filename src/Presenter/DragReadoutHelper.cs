using System;
using System.Globalization;
using System.Text;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// The line a drag shows while it is in flight: per axis, the value the model
/// applied and how far past it the pointer has been pulled.
/// </summary>
/// <remarks>
/// Two numbers, both of them facts already in hand — what landed, and what was
/// asked for. Deliberately not "0.2 to go", which would mean restating the model's
/// rounding rule here and keeping the restatement in step with it; where the next
/// step falls is the model's business, and the pair of numbers plus the ghost box
/// say enough to keep pulling.
/// <para>
/// A pure function beside <see cref="ActionLabelHelper"/> and
/// <see cref="ElapsedTimeHelper"/>, for the same reason: text is the presenter's,
/// and text is testable without an engine.
/// </para>
/// </remarks>
internal static class DragReadoutHelper
{
    /// <summary>
    /// Below this an offset is not worth showing: it is a pointer resting on the
    /// value rather than pulling away from it.
    /// </summary>
    private const float PendingEpsilon = 0.05f;

    private static readonly string[] AxisNames = { "X", "Y", "Z" };

    /// <summary>
    /// The readout for the axes named by <paramref name="axisMask"/> — bit per
    /// axis, so a plane drag reports both of its own and stays quiet about the third.
    /// Empty when nothing has been pulled yet, which is what hides the line rather
    /// than showing an offset of zero.
    /// </summary>
    /// <remarks>
    /// Invariant culture, because these are coordinates in a CAD readout: a decimal
    /// comma beside a comma-separated triple is a value that has to be parsed by eye.
    /// </remarks>
    public static string Format(
        TransformChannel channel,
        FieldMode mode,
        Vector3 applied,
        Vector3 raw,
        int axisMask)
    {
        var builder = new StringBuilder();

        for (var axis = 0; axis < 3; axis++)
        {
            if ((axisMask & (1 << axis)) == 0)
                continue;

            if (builder.Length > 0)
                builder.Append("   ");

            AppendAxis(builder, channel, mode, axis, applied[axis], raw[axis]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Which axes a cumulative delta has moved. Accumulated by the caller over the
    /// whole drag rather than read off one frame: a plane drag crosses zero on one
    /// axis all the time, and an axis blinking out of the line as it does would make
    /// the readout harder to read than no readout.
    /// </summary>
    public static int GetAxisMask(Vector3 delta)
    {
        var mask = 0;

        for (var axis = 0; axis < 3; axis++)
        {
            if (!Mathf.IsZeroApprox(delta[axis]))
                mask |= 1 << axis;
        }

        return mask;
    }

    private static void AppendAxis(
        StringBuilder builder,
        TransformChannel channel,
        FieldMode mode,
        int axis,
        float applied,
        float raw)
    {
        string unit = channel == TransformChannel.Rotation ? "°" : string.Empty;

        builder.Append(AxisNames[axis]);
        builder.Append(' ');
        builder.Append(FormatValue(applied, mode));
        builder.Append(unit);

        float pending = raw - applied;

        if (Mathf.Abs(pending) < PendingEpsilon)
            return;

        builder.Append(" (");
        builder.Append(pending > 0f ? '+' : '-');
        builder.Append(Mathf.Abs(pending).ToString("0.0", CultureInfo.InvariantCulture));
        builder.Append(unit);
        builder.Append(')');
    }

    /// <summary>
    /// The applied value at the precision its own mode can hold: a whole number
    /// where the model rounds to one, two decimals where it does not. Showing
    /// <c>4.00</c> for a cell the model keeps as 4 would suggest a precision the
    /// channel does not have.
    /// </summary>
    private static string FormatValue(float value, FieldMode mode) => mode switch
    {
        FieldMode.Float => value.ToString("0.##", CultureInfo.InvariantCulture),
        _ => Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
    };
}
