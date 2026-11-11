using System.Threading.Tasks;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The narrow slice of the shell that asks the user something. A presenter given
/// this cannot reach the menus or the window title, which is the point: dialogs
/// are needed all over, the rest of the chrome is not.
/// </summary>
/// <remarks>
/// File picking uses the native OS dialog — on desktop, CAD users expect their own
/// file browser. Confirmations and errors are in-app, themed with everything else.
/// Only <see cref="CommonTypes"/> structs cross this boundary.
/// </remarks>
public interface IDialogService
{
    /// <summary>Null when the user cancelled.</summary>
    Task<string?> AskPathAsync(FileDialogRequest request);

    Task<ConfirmResult> AskConfirmAsync(ConfirmRequest request);

    void ShowError(string title, string message);
}
