using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace VoxelCAD.Mcp;

/// <summary>
/// Hands work from a background thread to the main thread and waits for the
/// result. The HTTP host receives requests off-thread, but the model — and the
/// Godot scene mirroring it — may only be touched from the main thread, so every
/// request crosses here.
/// </summary>
/// <remarks>
/// The main thread drains the queue by calling <see cref="Pump"/> once per frame.
/// A caller's <see cref="Task"/> completes on whatever thread called Pump, which
/// is exactly the guarantee the model needs.
/// <para>
/// Most work finishes inside the pump that starts it. Some cannot: a synthesized
/// drag is only a drag if its frames are real frames, so it runs a step per pump
/// and leaves work behind it when the command that started it returns. Two things
/// follow, and they are the reason this class knows about it at all. The command's
/// own answer is held until the work drains — otherwise "the drag is done" would
/// be a reply sent while it was still going — and <b>nothing behind it runs in the
/// meantime</b>, so a read issued after a drag cannot overtake it and report the
/// state from halfway through.
/// </para>
/// </remarks>
public sealed class MainThreadWorkQueue
{
    /// <summary>
    /// Each item runs and hands back what to do once its work has finished
    /// draining — completing the caller's task, which may be several frames later
    /// than the item itself ran.
    /// </summary>
    private readonly ConcurrentQueue<Func<Action>> _pendingQueue = new();

    private readonly Func<bool>? _stepWorkInFlight;

    private Action? _finish;

    /// <param name="stepWorkInFlight">
    /// Advances whatever the last item left running, by one frame, and says
    /// whether more remains. Null when nothing in the host can span frames.
    /// </param>
    public MainThreadWorkQueue(Func<bool>? stepWorkInFlight = null)
    {
        _stepWorkInFlight = stepWorkInFlight;
    }

    public Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();

        _pendingQueue.Enqueue(() =>
        {
            try
            {
                T value = work();

                return () => completion.SetResult(value);
            }
            catch (Exception exception)
            {
                return () => completion.SetException(exception);
            }
        });

        return completion.Task;
    }

    /// <summary>
    /// Runs what is queued, and advances anything still in flight. Call from the
    /// main thread only.
    /// </summary>
    /// <remarks>
    /// Strictly in order, and one unfinished item at a time: an item that left
    /// work running owns the queue until it drains, so nothing behind it can act
    /// on a half-finished gesture.
    /// </remarks>
    public void Pump()
    {
        if (_finish != null)
        {
            if (StepWorkInFlight())
                return;

            Complete();
        }

        while (_pendingQueue.TryDequeue(out Func<Action>? work))
        {
            _finish = work();

            if (StepWorkInFlight())
                return;

            Complete();
        }
    }

    private bool StepWorkInFlight() => _stepWorkInFlight?.Invoke() ?? false;

    private void Complete()
    {
        Action? finish = _finish;
        _finish = null;

        finish?.Invoke();
    }
}
