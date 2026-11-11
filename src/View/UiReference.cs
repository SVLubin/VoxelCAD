using System;

namespace VoxelCAD.View;

/// <summary>
/// How a control is addressed. This is the argument the injection commands take,
/// so it has to be an identifier before it is a label: unambiguous within one
/// snapshot, and unchanged by anything but the shape of the tree.
/// </summary>
/// <remarks>
/// A reference is node names joined by <c>/</c>. Where a control was never given
/// a name, Godot invents one like <c>@Button@31</c> off a global counter — which
/// is different every run and therefore useless as an address — so those segments
/// are replaced by <c>Type@index</c>, the child's position among its siblings.
/// <para>
/// The two forms cannot collide: Godot forbids <c>@</c> in a name anybody chose,
/// so a segment containing one is always the generated form. That is also why
/// naming the controls worth aiming at is worth doing — <c>.../Undo</c> survives
/// a sibling being inserted above it, and <c>.../Button@3</c> does not.
/// </para>
/// </remarks>
internal static class UiReference
{
    public const char Separator = '/';

    /// <summary>
    /// The segment for one node: its own name when it has a real one, else its
    /// type and where it sits among its siblings.
    /// </summary>
    public static string ComposeSegment(string name, string type, int siblingIndex) =>
        IsGenerated(name) ? $"{type}@{siblingIndex}" : name;

    public static string Append(string parent, string segment) =>
        parent.Length == 0 ? segment : parent + Separator + segment;

    /// <summary>
    /// Whether <paramref name="reference"/> is the root itself or sits under it.
    /// An empty root is the whole tree.
    /// </summary>
    /// <remarks>
    /// The separator is part of the test on purpose: <c>Undo</c> must not be read
    /// as a root of <c>UndoRedoRow</c>.
    /// </remarks>
    public static bool IsUnder(string reference, string root) =>
        root.Length == 0
        || reference == root
        || reference.StartsWith(root + Separator, StringComparison.Ordinal);

    /// <summary>
    /// How many levels below <paramref name="root"/> the reference sits. Zero for
    /// the root itself, so depth is counted from where the caller started looking
    /// rather than from the shell.
    /// </summary>
    public static int DepthUnder(string reference, string root) =>
        CountSegments(reference) - CountSegments(root);

    /// <summary>
    /// A name Godot invented rather than one anybody chose. Those start with the
    /// character a chosen name may not contain.
    /// </summary>
    private static bool IsGenerated(string name) =>
        name.Length == 0 || name[0] == '@';

    private static int CountSegments(string reference)
    {
        if (reference.Length == 0)
            return 0;

        var count = 1;

        foreach (char character in reference)
        {
            if (character == Separator)
                count++;
        }

        return count;
    }
}
