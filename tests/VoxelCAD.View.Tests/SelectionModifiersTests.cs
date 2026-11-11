using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which modifier on a click asks for what. Held here rather than left to each
/// surface, because the viewport and the project tree have to break it the same
/// way — a modifier meaning one thing in the scene and another in the list is a
/// thing nobody could learn.
/// </summary>
public sealed class SelectionModifiersTests
{
    [Fact]
    public void APlainClickReplaces() =>
        Assert.Equal(
            SelectionIntent.Replace,
            SelectionModifiers.Resolve(command: false, shift: false));

    /// <summary>
    /// The command modifier is Cmd on macOS and Ctrl elsewhere, which is the
    /// engine's own answer — and on macOS it is the only one a left click can
    /// carry, since the platform turns a Ctrl+click into a right button press
    /// before anything here sees it.
    /// </summary>
    [Fact]
    public void TheCommandModifierToggles() =>
        Assert.Equal(
            SelectionIntent.Toggle,
            SelectionModifiers.Resolve(command: true, shift: false));

    [Fact]
    public void ShiftExtends() =>
        Assert.Equal(
            SelectionIntent.Extend,
            SelectionModifiers.Resolve(command: false, shift: true));

    /// <summary>
    /// With both held the toggle wins. An arbitrary tie-break rather than a
    /// meaning, asserted so that the two surfaces cannot drift apart on it.
    /// </summary>
    [Fact]
    public void BothHeldMeansToggle() =>
        Assert.Equal(
            SelectionIntent.Toggle,
            SelectionModifiers.Resolve(command: true, shift: true));
}
