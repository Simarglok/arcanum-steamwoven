using System.Collections.Concurrent;
using System.Collections.Immutable;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Residency;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §3.5, §6 risk 6): the viewer's driver of the host's chart over the loaded sectors. It owns the map's
/// <see cref="LoadedSectorChart"/>, follows the session's sector cache (<see cref="Request"/>, every frame, from the main thread: a cheap compare of the
/// loaded sector ids), and reads the sectors that entered the cache on one background task, since a read that is not the loaded window's takes seconds
/// (<see cref="ContentSectorTerrain"/>); the chart publishes each sync as one replacement, and the AI host reads <see cref="Steps"/> at each question, so the
/// main thread never waits for a read. The sectors of the current window cost nothing (<see cref="WindowSectorTerrain"/>: the window's own evidence,
/// restricted); the fallback port opens the game folder's stack on the task, when the first other sector is wanted. Derived data: built with the map's cache,
/// dropped with it. Not saved.
/// </summary>
public sealed class AreaSectorChart : IDisposable
{
    private readonly Func<ISectorTerrain?> _fallbackFactory;
    private readonly LoadedSectorChart _chart;
    private readonly object _gate = new();
    private readonly SwitchTerrain _terrain;
    private ImmutableArray<SectorCacheSlot> _pending;
    private long[]? _requested;
    private bool _running, _disposed;

    /// <summary>The lines the background task has to report (a sync's reads, its failures), for the main thread to print.</summary>
    public ConcurrentQueue<string> Log { get; } = new();

    /// <param name="fallbackFactory">Opens the port of the sectors the window does not hold (called once, on the task; null: the port is unavailable and
    /// those sectors count as failed reads).</param>
    public AreaSectorChart(Func<ISectorTerrain?> fallbackFactory)
    {
        _fallbackFactory = fallbackFactory ?? throw new ArgumentNullException(nameof(fallbackFactory));
        _terrain = new(this);
        _chart = new(_terrain);
    }

    /// <summary>The chart's evidence over the loaded sectors read so far, or null before the first.</summary>
    public AreaStepSnapshot? Steps => _chart.Steps;

    /// <summary>The sectors the chart holds evidence of.</summary>
    public IReadOnlyList<ResidentSector> Sectors => _chart.Sectors;

    /// <summary>Makes the window's own evidence the free source of its sectors (a window move gives a new one; the sectors already read stay).</summary>
    public void UseWindow(AreaSnapshot window, AreaStepSnapshot steps) => _terrain.Seed = new WindowSectorTerrain(window, steps, _terrain.Fallback);

    /// <summary>Asks the chart to follow the cache's loaded sectors. Cheap when the sector ids are the ones last asked; otherwise starts or continues the
    /// background sync (the latest request wins: a sync in flight is followed by one for the newest set).</summary>
    public void Request(ImmutableArray<SectorCacheSlot> loaded)
    {
        var ids = loaded.IsDefault ? [] : loaded.Select(slot => slot.Sector).Order().ToArray();
        lock (_gate)
        {
            if (_disposed || (_requested is { } last && last.AsSpan().SequenceEqual(ids))) return;
            _requested = ids;
            _pending = loaded.IsDefault ? [] : loaded;
            if (_running) return;
            _running = true;
        }
        Task.Run(Loop);
    }

    /// <summary>Runs the pending request on the calling thread until none is left (the task's body; tests call it directly through <see cref="Request"/>
    /// and wait with <see cref="Idle"/>).</summary>
    private void Loop()
    {
        try
        {
            while (true)
            {
                ImmutableArray<SectorCacheSlot> slots;
                lock (_gate)
                {
                    if (_disposed || _pending.IsDefault) { _running = false; break; }
                    slots = _pending; _pending = default;
                }
                var done = _chart.Sync(slots);
                if (done.Read.Count > 0 || done.Dropped.Count > 0 || done.Failed.Count > 0)
                    Log.Enqueue($"ARC-DEV-118 chart read={string.Join(" ", done.Read.Select(s => $"{s.X},{s.Y}"))} dropped={string.Join(" ", done.Dropped.Select(s => $"{s.X},{s.Y}"))}"
                        + $" held={_chart.Sectors.Count}" + string.Concat(done.Failed.Select(f => $" failed={f.Sector.X},{f.Sector.Y}:{f.Detail}")));
            }
        }
        catch (Exception error)
        {
            Log.Enqueue($"ARC-DEV-118 chart sync failed {error.GetType().Name}: {error.Message}");
            lock (_gate) { _running = false; _pending = default; _requested = null; }
        }
        finally { if (_disposed) _terrain.CloseFallback(); }
    }

    /// <summary>Whether no sync is running or pending (tests wait on it).</summary>
    public bool Idle { get { lock (_gate) return !_running && _pending.IsDefault; } }

    /// <summary>Stops following the cache; a sync in flight ends its read and closes the fallback port.</summary>
    public void Dispose()
    {
        bool idle;
        lock (_gate) { _disposed = true; idle = !_running; }
        if (idle) _terrain.CloseFallback();
    }

    /// <summary>The port the chart reads through: the current window's seed, else the lazily opened fallback.</summary>
    private sealed class SwitchTerrain(AreaSectorChart owner) : ISectorTerrain
    {
        private ISectorTerrain? _fallback;
        private bool _opened;
        public volatile ISectorTerrain? Seed;
        public ISectorTerrain Fallback { get; } = new Lazy(owner);

        public SectorTerrainRead Read(ResidentSector sector) => (Seed ?? Fallback).Read(sector);

        internal ISectorTerrain? Open()
        {
            lock (this)
            {
                if (!_opened) { _opened = true; _fallback = owner._fallbackFactory(); }
                return _fallback;
            }
        }

        public void CloseFallback() { lock (this) { (_fallback as IDisposable)?.Dispose(); _fallback = null; _opened = true; } }
    }

    private sealed class Lazy(AreaSectorChart owner) : ISectorTerrain
    {
        public SectorTerrainRead Read(ResidentSector sector) => owner._terrain.Open() is { } port ? port.Read(sector)
            : new(SectorTerrainStatus.Refused, null, "the sector port is unavailable");
    }
}
