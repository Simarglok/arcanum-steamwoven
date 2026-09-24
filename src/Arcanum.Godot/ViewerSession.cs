using Arcanum.Data;
using Arcanum.Presentation.Animation;

namespace Arcanum.Godot;

// Godot-free state machine. Workers return managed data; this class alone decides
// whether a completion is still current, and only its drain invokes the UI sink.
internal sealed class ViewerSession : IDisposable
{
    private readonly ViewerWorkCoordinator _work;
    private long _archiveGeneration;
    private long _entryGeneration;
    private long _targetGeneration;
    private SelectedArtSnapshot? _selected;
    private FrameTarget? _desiredTarget;
    private FrameTarget? _failedTarget;
    private bool _closed;

    internal ViewerSession(ViewerWorkCoordinator? work = null)
    {
        _work = work ?? new ViewerWorkCoordinator();
        Timeline = new AnimationTimeline(1, 0);
    }

    internal AnimationTimeline Timeline { get; }
    internal SelectedArtSnapshot? Selected => _selected;
    internal FrameTarget? DesiredTarget => _desiredTarget;
    internal ViewerWorkCoordinator WorkCoordinator => _work;

    internal void OpenArchive(string path, IViewerPublicationSink? sink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _archiveGeneration++;
        _entryGeneration++;
        _selected = null;
        _desiredTarget = null;
        _failedTarget = null;
        ResetSelection();
        _work.Invalidate();
        sink?.SelectionInvalidated(clearEntries: true);
        _work.Submit(new OpenArchiveViewerWorkRequest(_archiveGeneration, path));
    }

    internal void SelectEntry(DatEntry entry, IViewerPublicationSink? sink = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entryGeneration++;
        _selected = null;
        _desiredTarget = null;
        _failedTarget = null;
        ResetSelection();
        _work.Invalidate();
        sink?.SelectionInvalidated(clearEntries: false);
        _work.Submit(new LoadEntryViewerWorkRequest(new EntryIdentity(_archiveGeneration, _entryGeneration, entry.Ordinal), entry));
    }

    internal void RequestTarget(int group, int palette)
    {
        if (_selected is null || _closed) return;
        SetTarget(group, Timeline.CurrentFrame, palette);
    }

    internal void Stop(int group, int palette)
    {
        Timeline.Stop();
        SetTarget(group, Timeline.CurrentFrame, palette);
    }

    internal void Advance(double elapsedSeconds, int group, int palette)
    {
        var advance = Timeline.Advance(elapsedSeconds);
        if (advance.Status is TimelineAdvanceStatus.FrameChanged or TimelineAdvanceStatus.Completed)
        {
            SetTarget(group, Timeline.CurrentFrame, palette);
        }
    }

    internal void DrainCompletions(IViewerPublicationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        while (_work.TryDequeueCompletion(out var completion) && completion is not null)
        {
            switch (completion)
            {
                case ViewerArchiveOpened opened when opened.OpenRequest.ArchiveGeneration == _archiveGeneration && !_closed:
                    sink.ArchiveOpened(opened.Entries);
                    break;
                case ViewerEntryLoaded loaded when IsCurrent(loaded.LoadRequest.Identity):
                    CommitEntry(loaded, sink);
                    break;
                case ViewerFrameDecoded decoded when IsCurrent(decoded.DecodeRequest.Target):
                    PublishFrame(decoded, sink);
                    break;
                case ViewerWorkFailure failure:
                    HandleFailure(failure, sink);
                    break;
            }
        }
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _selected = null;
        _desiredTarget = null;
        _failedTarget = null;
        _work.Close();
    }

    public void Dispose() => Close();

    private void CommitEntry(ViewerEntryLoaded loaded, IViewerPublicationSink sink)
    {
        var snapshot = new SelectedArtSnapshot(loaded.LoadRequest.Identity, loaded.Bytes, loaded.Metadata);
        _selected = snapshot;
        _failedTarget = null;
        Timeline.ResetAsset(snapshot.Metadata.Header.FramesPerGroup, snapshot.Metadata.Header.FramesPerSecond);
        sink.EntryCommitted(snapshot);
        SetTarget(0, 0, 0);
    }

    private void SetTarget(int group, int frame, int palette)
    {
        var snapshot = _selected;
        if (snapshot is null || _closed) return;
        var semanticTarget = new FrameTarget(snapshot.Identity, 0, group, frame, palette);
        if (_desiredTarget is { } current && current.SameSemanticTarget(semanticTarget)) return;
        var target = semanticTarget with { TargetGeneration = ++_targetGeneration };
        _desiredTarget = target;
        _failedTarget = null;
        _work.Submit(new DecodeFrameViewerWorkRequest(snapshot, target));
    }

    private void HandleFailure(ViewerWorkFailure failure, IViewerPublicationSink sink)
    {
        switch (failure.Request)
        {
            case OpenArchiveViewerWorkRequest open when open.ArchiveGeneration == _archiveGeneration && !_closed:
                ResetSelection();
                sink.SelectionInvalidated(clearEntries: true);
                sink.PublishFailure(failure.Exception);
                break;
            case LoadEntryViewerWorkRequest load when IsCurrent(load.Identity):
                ResetSelection();
                sink.SelectionInvalidated(clearEntries: false);
                sink.PublishFailure(failure.Exception);
                break;
            case DecodeFrameViewerWorkRequest decode when IsCurrent(decode.Target) && _failedTarget != decode.Target:
                // Exact-target identity is checked before every error side effect.
                _failedTarget = decode.Target;
                Timeline.Pause();
                sink.PublishFailure(failure.Exception);
                break;
        }
    }

    // Publication is worker-result processing too: a current target can still fail
    // during UI-side geometry/resource validation. Treat that exactly like a decode
    // failure so a tick cannot retry it indefinitely. ViewerApp prepares resources
    // before mutating its canvas and disposes an uncommitted replacement on failure.
    private void PublishFrame(ViewerFrameDecoded decoded, IViewerPublicationSink sink)
    {
        try
        {
            sink.PublishFrame(decoded.DecodeRequest.Snapshot, decoded.DecodeRequest.Target, decoded.Frame);
        }
        catch (Exception exception) when (IsCurrent(decoded.DecodeRequest.Target) && _failedTarget != decoded.DecodeRequest.Target)
        {
            _failedTarget = decoded.DecodeRequest.Target;
            Timeline.Pause();
            sink.PublishFailure(exception);
        }
    }

    private void ResetSelection() => Timeline.ResetAsset(1, 0);

    private bool IsCurrent(EntryIdentity identity) => !_closed && _selected is null &&
        identity.ArchiveGeneration == _archiveGeneration && identity.EntryGeneration == _entryGeneration;

    private bool IsCurrent(FrameTarget target) => !_closed && _selected is not null &&
        ReferenceEquals(_selected.Identity, target.Entry) && _desiredTarget == target;
}

internal sealed record EntryIdentity(long ArchiveGeneration, long EntryGeneration, int EntryOrdinal);
internal sealed record SelectedArtSnapshot(EntryIdentity Identity, byte[] Bytes, ArtMetadata Metadata);
internal sealed record FrameTarget(EntryIdentity Entry, long TargetGeneration, int Group, int Frame, int Palette)
{
    internal bool SameSemanticTarget(FrameTarget other) => Entry == other.Entry && Group == other.Group && Frame == other.Frame && Palette == other.Palette;
}

internal interface IViewerPublicationSink
{
    void SelectionInvalidated(bool clearEntries);
    void ArchiveOpened(IReadOnlyList<ViewerEntry> entries);
    void EntryCommitted(SelectedArtSnapshot snapshot);
    void PublishFrame(SelectedArtSnapshot snapshot, FrameTarget target, ArtFrame frame);
    void PublishFailure(Exception exception);
}
