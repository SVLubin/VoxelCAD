using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// What a file picker offers when somebody is choosing a reference.
/// </summary>
/// <remarks>
/// One place, because three surfaces raise this dialog — the project tree's
/// context menu, the File menu and the inspector's own Change button — and a
/// picker that offered different formats depending on which of them opened it
/// would be three answers to one question. The formats themselves come from the
/// model (<see cref="ReferenceFormats"/>), which is the layer entitled to say what
/// a reference can be.
/// </remarks>
internal static class ReferenceDialogs
{
    /// <summary>
    /// A picker for any reference at all, or narrowed to the one kind an element
    /// already is: a component is fixed for an element's lifetime, so offering a
    /// model to a picture would be offering a choice that is then refused.
    /// </summary>
    public static FileDialogRequest Open(string title, ComponentKind kind = ComponentKind.None)
    {
        var filtersList = new List<FileDialogFilter>();

        if (kind != ComponentKind.ReferenceModel)
            Add(filtersList, ReferenceFormats.ImageExtensions, "Pictures");

        if (kind != ComponentKind.ReferenceImage)
            Add(filtersList, ReferenceFormats.ModelExtensions, "3D models");

        return new FileDialogRequest(
            FileDialogMode.Open,
            title,
            filtersList,
            SuggestedPath: string.Empty);
    }

    private static void Add(
        List<FileDialogFilter> filtersList,
        IReadOnlyList<string> extensions,
        string label)
    {
        foreach (string extension in extensions)
            filtersList.Add(new FileDialogFilter($"*{extension}", label));
    }
}
