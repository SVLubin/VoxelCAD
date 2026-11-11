using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// Which element of a group drag sets the step the whole group takes.
/// </summary>
/// <remarks>
/// A group has to move <b>rigidly</b>: whatever the pull, the parts must keep the
/// spacing they had, because a selection that changes shape while it is being
/// dragged is not a selection being dragged. Handing each element the raw pull and
/// letting each round it its own way does not do that — measured, on a pull of one
/// cell: an Integer element stood still and then jumped a whole cell while a Float
/// one beside it glided, so the gap between them swung by a full cell and back.
/// <para>
/// So the pull is quantised <b>once</b> and the granted step is handed to
/// everybody. The element that quantises it has to be one that cannot take a
/// finer step than the others, or the rest would round what it was given and the
/// deformation would come back — hence: <b>a constrained element paces the group,
/// and only a group with nothing constrained in it moves freely.</b> Within one
/// channel there are exactly two modes, one free and one not, so "constrained" is
/// the whole of the ordering this needs.
/// </para>
/// <para>
/// The cost, and it is the right one: select a Float element together with an
/// Integer one and the Float element moves in whole cells for as long as they are
/// dragged together. That is what moving as a group means; dragging them apart is
/// what dragging them separately is for.
/// </para>
/// </remarks>
internal static class GroupDragPace
{
    /// <summary>
    /// The index of the pace-setter. The first constrained element, or
    /// <paramref name="anchorIndex"/> when nothing in the group is constrained.
    /// </summary>
    /// <remarks>
    /// Which constrained one is picked does not matter: they all quantise a pull to
    /// the same whole number of steps, since a mode's lattice has one spacing
    /// however it is offset. The anchor is the fallback rather than the preference
    /// because it is the element the ghost and the readout already speak for, so an
    /// unconstrained group keeps saying exactly what it does.
    /// </remarks>
    public static int Choose(IReadOnlyList<FieldMode> modes, int anchorIndex)
    {
        for (var index = 0; index < modes.Count; index++)
        {
            if (modes[index] != FieldMode.Float)
                return index;
        }

        return anchorIndex;
    }
}
