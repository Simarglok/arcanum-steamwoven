using System.Collections.Concurrent;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-3304: the background tasks that call into the engine (the interface HD resamples and warm-up, the
/// launcher's installer and prototype builds). <see cref="Close"/> is called once, when the app leaves the tree: the work
/// reads <see cref="Closing"/> to skip what is still queued, and the call waits for what already runs, so no worker is inside
/// an engine call while the engine is torn down. Closing is one way.</summary>
public sealed class ViewerBackgroundWork
{
    private readonly ConcurrentDictionary<Task, byte> _running = new();
    private volatile bool _closing;

    /// <summary>True from the first <see cref="Close"/>: work that is still queued drops itself.</summary>
    public bool Closing => _closing;

    /// <summary>The tracked tasks that have not been forgotten yet (a task is forgotten as it ends).</summary>
    public int Tracked => _running.Count;

    /// <summary>Remembers <paramref name="task"/> until it ends and returns it.</summary>
    public T Track<T>(T task) where T : Task
    {
        ArgumentNullException.ThrowIfNull(task);
        _running[task] = 0;
        _ = task.ContinueWith(static (done, running) => ((ConcurrentDictionary<Task, byte>)running!).TryRemove(done, out _), _running,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    /// <summary>Marks the work as closing and waits for the running tasks, at most <paramref name="timeout"/> in all. A task
    /// that fails or is cancelled has ended like any other. Returns how many ran at the call and how many still run after it.</summary>
    public (int Waited, int Left) Close(TimeSpan timeout)
    {
        var tasks = _running.Keys.Where(task => !task.IsCompleted).ToArray(); // before the flag, which may end a task at once
        _closing = true;
        try { Task.WaitAll(tasks, timeout); }
        catch (AggregateException) { } // every task ended; its own owner reads its failure
        return (tasks.Length, tasks.Count(task => !task.IsCompleted));
    }
}
