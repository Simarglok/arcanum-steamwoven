using System.Collections.Concurrent;
using Arcanum.Data;

namespace Arcanum.Godot;

// The sole worker-side owner of a DAT archive. It executes exactly one data operation
// at a time and retains only one replaceable latest pending operation.
internal sealed class ViewerWorkCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<ViewerWorkCompletion> _completions = new();
    private readonly Func<ViewerWorkRequest, ViewerWorkCompletion>? _testExecutor;
    private readonly Action<ViewerWorkRequest>? _beforeTestExecution;
    private ViewerWorkRequest? _active;
    private ViewerWorkRequest? _pending;
    private DatArchive? _archive;
    private bool _closed;
    private int _activeOperations;
    private int _maximumObservedConcurrency;
    private int _completedOperations;
    private int _archiveDisposals;

    internal ViewerWorkCoordinator(Func<ViewerWorkRequest, ViewerWorkCompletion>? testExecutor = null,
        Action<ViewerWorkRequest>? beforeTestExecution = null)
    {
        _testExecutor = testExecutor;
        _beforeTestExecution = beforeTestExecution;
    }

    internal int ActiveOperations { get { lock (_gate) return _activeOperations; } }
    internal int PendingCount { get { lock (_gate) return _pending is null ? 0 : 1; } }
    internal int MaximumObservedConcurrency { get { lock (_gate) return _maximumObservedConcurrency; } }
    internal int CompletedOperations { get { lock (_gate) return _completedOperations; } }
    internal int ArchiveDisposals => Volatile.Read(ref _archiveDisposals);

    internal void Submit(ViewerWorkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_closed || request is CloseViewerWorkRequest) return;
            if (_active is null)
            {
                _active = request;
                _ = Task.Run(PumpAsync);
            }
            else
            {
                _pending = request;
            }
        }
    }

    // Cancellation is deliberately logical. Active synchronous parser work is allowed
    // to finish, but the session rejects its completion by immutable identity.
    internal void Invalidate()
    {
        lock (_gate)
        {
            _pending = null;
        }
    }

    internal bool TryDequeueCompletion(out ViewerWorkCompletion? completion) => _completions.TryDequeue(out completion);

    internal void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _pending = null;
            if (_active is null)
            {
                DisposeArchiveLocked();
            }
        }
    }

    public void Dispose() => Close();

    private async Task PumpAsync()
    {
        while (true)
        {
            ViewerWorkRequest work;
            lock (_gate)
            {
                work = _active ?? throw new InvalidOperationException("Viewer coordinator lost its active operation.");
                _activeOperations++;
                _maximumObservedConcurrency = Math.Max(_maximumObservedConcurrency, _activeOperations);
            }

            ViewerWorkCompletion completion;
            try
            {
                completion = await Task.Run(() =>
                {
                    _beforeTestExecution?.Invoke(work);
                    return _testExecutor?.Invoke(work) ?? Execute(work);
                });
            }
            catch (Exception exception)
            {
                completion = new ViewerWorkFailure(work, exception);
            }
            finally
            {
                lock (_gate) _activeOperations--;
            }

            _completions.Enqueue(completion);
            lock (_gate)
            {
                _completedOperations++;
                if (_closed)
                {
                    _active = null;
                    DisposeArchiveLocked();
                    return;
                }

                _active = _pending;
                _pending = null;
                if (_active is null) return;
            }
        }
    }

    private ViewerWorkCompletion Execute(ViewerWorkRequest work) => work switch
    {
        OpenArchiveViewerWorkRequest open => Open(open),
        LoadEntryViewerWorkRequest load => Load(load),
        DecodeFrameViewerWorkRequest decode => Decode(decode),
        _ => throw new InvalidOperationException($"Unsupported viewer work item {work.GetType().Name}."),
    };

    private ViewerWorkCompletion Open(OpenArchiveViewerWorkRequest request)
    {
        var opened = DatArchive.Open(request.Path, ViewerState.ReaderOptions);
        var previous = _archive;
        _archive = opened;
        if (previous is not null)
        {
            previous.Dispose();
            Interlocked.Increment(ref _archiveDisposals);
        }
        return new ViewerArchiveOpened(request, ViewerState.ArtEntries(opened));
    }

    private ViewerWorkCompletion Load(LoadEntryViewerWorkRequest request)
    {
        var archive = _archive ?? throw new InvalidOperationException("No DAT archive is open.");
        var bytes = archive.ReadEntry(request.Entry);
        return new ViewerEntryLoaded(request, bytes, ArtDecoder.Inspect(bytes, ViewerState.DecoderOptions));
    }

    private static ViewerWorkCompletion Decode(DecodeFrameViewerWorkRequest request) =>
        new ViewerFrameDecoded(request, ArtDecoder.Decode(request.Snapshot.Bytes,
            new ArtFrameSelection(request.Target.Group, request.Target.Frame, request.Target.Palette), ViewerState.DecoderOptions));

    private void DisposeArchiveLocked()
    {
        var archive = _archive;
        _archive = null;
        if (archive is not null)
        {
            archive.Dispose();
            Interlocked.Increment(ref _archiveDisposals);
        }
    }
}

internal abstract record ViewerWorkRequest;
internal sealed record OpenArchiveViewerWorkRequest(long ArchiveGeneration, string Path) : ViewerWorkRequest;
internal sealed record LoadEntryViewerWorkRequest(EntryIdentity Identity, DatEntry Entry) : ViewerWorkRequest;
internal sealed record DecodeFrameViewerWorkRequest(SelectedArtSnapshot Snapshot, FrameTarget Target) : ViewerWorkRequest;
internal sealed record CloseViewerWorkRequest : ViewerWorkRequest;

internal abstract record ViewerWorkCompletion(ViewerWorkRequest Work);
internal sealed record ViewerArchiveOpened(OpenArchiveViewerWorkRequest OpenRequest, IReadOnlyList<ViewerEntry> Entries) : ViewerWorkCompletion(OpenRequest);
internal sealed record ViewerEntryLoaded(LoadEntryViewerWorkRequest LoadRequest, byte[] Bytes, ArtMetadata Metadata) : ViewerWorkCompletion(LoadRequest);
internal sealed record ViewerFrameDecoded(DecodeFrameViewerWorkRequest DecodeRequest, ArtFrame Frame) : ViewerWorkCompletion(DecodeRequest);
internal sealed record ViewerWorkFailure(ViewerWorkRequest Request, Exception Exception) : ViewerWorkCompletion(Request);
