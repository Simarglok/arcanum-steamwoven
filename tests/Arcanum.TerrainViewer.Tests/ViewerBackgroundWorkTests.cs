using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-DEV-3304: the engine teardown waits for the background tasks that call into the engine.
[TestClass]
public sealed class ViewerBackgroundWorkTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void CloseWaitsForARunningTaskThatSeesTheWorkClosing()
    {
        var work = new ViewerBackgroundWork();
        Assert.IsFalse(work.Closing);
        var sawClosing = false;
        var task = work.Track(Task.Run(() =>
        {
            sawClosing = SpinWait.SpinUntil(() => work.Closing, Long);
        }));
        var (waited, left) = work.Close(Long);
        Assert.AreEqual((1, 0), (waited, left));
        Assert.IsTrue(task.IsCompletedSuccessfully);
        Assert.IsTrue(sawClosing);
        Assert.IsTrue(work.Closing);
    }

    [TestMethod]
    public void AFailedOrCancelledTaskCountsAsEnded()
    {
        var work = new ViewerBackgroundWork();
        var gate = new TaskCompletionSource();
        var failing = work.Track(Task.Run(async () => { await gate.Task; throw new InvalidOperationException(); }));
        var cancelled = new TaskCompletionSource();
        work.Track(cancelled.Task);
        gate.SetResult();
        cancelled.SetCanceled();
        Assert.AreEqual(0, work.Close(Long).Left);
        Assert.IsTrue(failing.IsFaulted);
    }

    [TestMethod]
    public void CloseGivesUpAfterItsTimeAndSaysWhatStillRuns()
    {
        var work = new ViewerBackgroundWork();
        var never = new TaskCompletionSource<int>();
        Task<int> tracked = work.Track(never.Task);
        Assert.AreSame(never.Task, tracked);
        Assert.AreEqual((1, 1), work.Close(TimeSpan.FromMilliseconds(20)));
        never.SetResult(1);
    }

    [TestMethod]
    public void AnEndedTaskIsForgotten()
    {
        var work = new ViewerBackgroundWork();
        work.Track(Task.CompletedTask);
        Assert.IsTrue(SpinWait.SpinUntil(() => work.Tracked == 0, Long));
        Assert.AreEqual((0, 0), work.Close(Long));
    }
}
