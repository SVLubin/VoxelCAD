using System.Collections.Generic;
using System.Threading.Tasks;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Answers a panel presenter's questions with whatever the test decided, and
/// remembers what it was asked.
/// </summary>
/// <remarks>
/// The shell's own fake is a whole window; a panel presenter is handed only this
/// narrow slice, and a test of one should be able to say "the user said no"
/// without building a menu bar to say it through.
/// </remarks>
internal sealed class FakeDialogService : IDialogService
{
    private readonly List<(string Title, string Message)> _errorsList = new();

    /// <summary>What every confirmation is answered with. Cancel by default: a test that
    /// never says otherwise is a user who never pressed the dangerous button.</summary>
    public ConfirmResult Answer { get; set; } = ConfirmResult.Cancel;

    public ConfirmRequest? LastConfirmRequest { get; private set; }

    public int ConfirmCount { get; private set; }

    public IReadOnlyList<(string Title, string Message)> Errors => _errorsList;

    public Task<string?> AskPathAsync(FileDialogRequest request) =>
        Task.FromResult<string?>(null);

    public Task<ConfirmResult> AskConfirmAsync(ConfirmRequest request)
    {
        LastConfirmRequest = request;
        ConfirmCount++;

        return Task.FromResult(Answer);
    }

    public void ShowError(string title, string message) => _errorsList.Add((title, message));
}
