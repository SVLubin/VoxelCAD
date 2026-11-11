using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace VoxelCAD.Model;

/// <summary>
/// Forces every string to a quoted or block scalar, never a plain one.
/// </summary>
/// <remarks>
/// Element names come from the user, and YAML reads plenty of them as something
/// else: <c>NO</c> becomes false, <c>1.10</c> becomes a number, <c>y</c> becomes
/// a boolean. Quoting unconditionally removes the whole class rather than
/// guessing which names are dangerous.
///
/// Multi-line strings — the voxel blob — get a literal block instead, so the
/// payload wraps and a changed model produces a local diff rather than one
/// rewritten megabyte-long line.
/// </remarks>
internal sealed class QuotedStringEmitter : ChainedEventEmitter
{
    public QuotedStringEmitter(IEventEmitter next) : base(next)
    {
    }

    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (eventInfo.Source.Type == typeof(string) && eventInfo.Source.Value is string value)
        {
            if (value.Contains('\n'))
                eventInfo.Style = ScalarStyle.Literal;
            else if (YamlScalarHelper.NeedsQuoting(value))
                eventInfo.Style = ScalarStyle.DoubleQuoted;
        }

        base.Emit(eventInfo, emitter);
    }
}
