using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One entry of the finish list as the panel needs it: what to select, what to
/// write, and whether a person may pick it.
/// </summary>
/// <remarks>
/// A finish nobody may pick is still listed, because the list is also how the
/// panel says what a material wears: a value missing from it leaves the chooser
/// showing whatever the previous material wore. The label is the presenter's, on
/// <see cref="MaterialPresetOption"/>'s terms.
/// </remarks>
public readonly record struct MaterialFinishOption(
    MaterialFinish Finish,
    string Label,
    bool Choosable);
