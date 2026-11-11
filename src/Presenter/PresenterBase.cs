using System;

namespace VoxelCAD.Presenter;

/// <summary>
/// Common ground for every presenter: it knows whether it is still the live one.
/// </summary>
/// <remarks>
/// Panel presenters are thrown away and rebuilt when the document changes, and
/// file operations are asynchronous — so a presenter can be disposed while it
/// waits on a dialog. Every continuation after an <c>await</c> checks
/// <see cref="IsAlive"/> before touching a view that by then belongs to its
/// successor.
/// <para>
/// Model events need the same guard. Deleting the active scene rebuilds the
/// scene-bound presenters from inside the very change event they are subscribed
/// to, and .NET hands the rest of that event to the handlers it snapshotted —
/// including the ones just disposed, which would then write over what their
/// successors had already put in the view.
/// </para>
/// </remarks>
internal abstract class PresenterBase : IDisposable
{
    /// <summary>False once disposed. The guard to check after every <c>await</c>.</summary>
    protected bool IsAlive { get; private set; } = true;

    public void Dispose()
    {
        if (!IsAlive)
            return;

        IsAlive = false;
        OnDispose();
    }

    /// <summary>Unsubscribe here. Called once, however many times Dispose is.</summary>
    protected abstract void OnDispose();
}
