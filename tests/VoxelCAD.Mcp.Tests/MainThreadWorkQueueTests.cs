using System;
using System.Threading;
using System.Threading.Tasks;
using VoxelCAD.Mcp;
using Xunit;

namespace VoxelCAD.Mcp.Tests;

/// <summary>
/// The guarantee the model depends on: work handed in from anywhere runs on the
/// thread that pumps the queue, and nowhere else.
/// </summary>
public sealed class MainThreadWorkQueueTests
{
    [Fact]
    public void WorkRunsOnTheThreadThatPumps()
    {
        var queue = new MainThreadWorkQueue();
        var pumpThread = Environment.CurrentManagedThreadId;

        // Task.Run unwraps the inner Task, so this completes only when the pump runs.
        Task<int> result = Task.Run(() => queue.Run(() => Environment.CurrentManagedThreadId));

        Thread.Sleep(20);
        Assert.False(result.IsCompleted);

        queue.Pump();

        Assert.Equal(pumpThread, result.Result);
    }

    [Fact]
    public void AResultComesBackToTheCaller()
    {
        var queue = new MainThreadWorkQueue();
        Task<int> work = queue.Run(() => 6 * 7);

        queue.Pump();

        Assert.Equal(42, work.Result);
    }

    [Fact]
    public void AnExceptionCrossesBackToTheCaller()
    {
        var queue = new MainThreadWorkQueue();
        Task<int> work = queue.Run<int>(() => throw new InvalidOperationException("boom"));

        queue.Pump();

        AggregateException aggregate = Assert.Throws<AggregateException>(() => _ = work.Result);
        Assert.IsType<InvalidOperationException>(aggregate.InnerException);
    }

    [Fact]
    public void PumpingAnEmptyQueueIsHarmless()
    {
        new MainThreadWorkQueue().Pump();
    }

    // --- work that spans frames ---

    /// <summary>
    /// A synthesized gesture is only a gesture if its frames are real frames, so
    /// the work it leaves behind is advanced a step per pump.
    /// </summary>
    [Fact]
    public void WorkLeftRunningIsSteppedOncePerPump()
    {
        var frames = 3;
        var queue = new MainThreadWorkQueue(() => --frames > 0);

        Task<string> work = queue.Run(() => "started");

        queue.Pump();
        queue.Pump();
        queue.Pump();

        Assert.Equal(0, frames);
    }

    /// <summary>
    /// And the command that started it does not answer until it has finished —
    /// otherwise "the drag is done" would be a reply sent mid-drag.
    /// </summary>
    [Fact]
    public void TheAnswerWaitsForTheWorkToDrain()
    {
        var frames = 3;
        var queue = new MainThreadWorkQueue(() => --frames > 0);

        Task<string> work = queue.Run(() => "done");

        queue.Pump();
        Assert.False(work.IsCompleted);

        queue.Pump();
        Assert.False(work.IsCompleted);

        queue.Pump();
        Assert.True(work.IsCompleted);
        Assert.Equal("done", work.Result);
    }

    /// <summary>
    /// The ordering guarantee: a read issued after a drag must not run while the
    /// drag is still going, or it reports the state from halfway through.
    /// </summary>
    [Fact]
    public void NothingBehindTheWorkOvertakesIt()
    {
        var frames = 3;
        var queue = new MainThreadWorkQueue(() => --frames > 0);
        var orderList = new System.Collections.Generic.List<string>();

        Task<bool> drag = queue.Run(() =>
        {
            orderList.Add("drag");

            return true;
        });

        Task<bool> read = queue.Run(() =>
        {
            orderList.Add("read");

            return true;
        });

        queue.Pump();
        Assert.Equal(new[] { "drag" }, orderList);
        Assert.False(read.IsCompleted);

        queue.Pump();
        Assert.Equal(new[] { "drag" }, orderList);

        // The pump that drains the last frame also runs what was waiting.
        queue.Pump();

        Assert.Equal(new[] { "drag", "read" }, orderList);
        Assert.True(drag.Result);
        Assert.True(read.Result);
    }

    /// <summary>Work that finishes inside its own pump is the ordinary case and stays fast.</summary>
    [Fact]
    public void WorkThatLeavesNothingRunningAnswersAtOnce()
    {
        var queue = new MainThreadWorkQueue(() => false);

        Task<int> first = queue.Run(() => 1);
        Task<int> second = queue.Run(() => 2);

        queue.Pump();

        Assert.Equal(1, first.Result);
        Assert.Equal(2, second.Result);
    }

    /// <summary>A failure still crosses back, even from an item that left work running.</summary>
    [Fact]
    public void AFailureWaitsForTheWorkToDrainToo()
    {
        var frames = 2;
        var queue = new MainThreadWorkQueue(() => --frames > 0);

        Task<int> work = queue.Run<int>(() => throw new InvalidOperationException("boom"));

        queue.Pump();
        Assert.False(work.IsCompleted);

        queue.Pump();

        AggregateException aggregate = Assert.Throws<AggregateException>(() => _ = work.Result);
        Assert.IsType<InvalidOperationException>(aggregate.InnerException);
    }
}
