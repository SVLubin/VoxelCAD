using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// A material in the project registry. Voxels reference one by <see cref="Id"/>,
/// so recolouring a material repaints every voxel using it.
/// </summary>
/// <remarks>
/// Colour is one <see cref="Color"/> whose alpha is the opacity — a separate
/// opacity field would be a second way to say the same thing, which is also why
/// <see cref="MaterialAppearance"/> beside it has no transparency of its own.
/// </remarks>
public interface IMaterial
{
    Guid Id { get; }

    string Name { get; }

    Color Color { get; }

    /// <summary>
    /// How the surface takes light, and what facture is laid over it. Stored as
    /// the values themselves rather than as the name of a preset: a preset writes
    /// several of these at once and is not referred to again, which is what makes
    /// tuning one of them afterwards possible at all.
    /// </summary>
    MaterialAppearance Appearance { get; }

    /// <summary>
    /// Whether this is one of the materials the editor ships with. Published for
    /// the same reason <see cref="IProject.IsMaterialInUse"/> is: a surface has to
    /// grey the bin before it is pressed, and the model refuses by throwing, which
    /// is right for a programming error and useless to a button.
    /// </summary>
    bool IsStandard { get; }
}
