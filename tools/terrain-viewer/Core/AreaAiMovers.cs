using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-WORLD-092: an NPC the AI host walked off its map data's cell: its world cell now, its facing and the step it is taking
/// (from <see cref="From"/>, started at <see cref="StepStartMs"/> game milliseconds, lasting <see cref="StepMs"/>).
/// </summary>
public sealed record AreaAiMover(string Entity, AreaObjectKey Key, CellCoord Origin, CellCoord Cell, NeighborDirection Facing)
{
    public CellCoord? From { get; init; }
    public long StepStartMs { get; init; }
    public int StepMs { get; init; }
    /// <summary>ARC-WORLD-111: whether the step is run (drawn with the run ART), else walked.</summary>
    public bool Running { get; init; }

    /// <summary>ARC-WORLD-111: whether the NPC is on its way at <paramref name="nowMs"/> (drawn walking or running).</summary>
    public bool Stepping(long nowMs) => From is not null && Progress(nowMs) < 1;

    /// <summary>The step's progress 0..1 at <paramref name="nowMs"/> (1 without a step).</summary>
    public double Progress(long nowMs) => From is null || StepMs <= 0 ? 1 : Math.Clamp((nowMs - StepStartMs) / (double)StepMs, 0, 1);
}

/// <summary>
/// ARC-WORLD-092: the NPCs of the shown map that the AI host moved, in world cells (an NPC never moved stands on its map data's
/// cell and has no entry). The goal machine's step puts an NPC on its next cell at once (it stands there when the run's next step
/// comes); the drawing slides its body there over the step's time (ARC-WORLD-111: the viewer draws the walk or run cycle on it). ARC-DEV-094: where an NPC stands is session state (<see cref="GameSessionOwner.RecordNpcPlace"/>); a host built again (a load, a map change, a window move) restores its movers from it. Caller-serialized; no Godot.
/// </summary>
public sealed class AreaAiMovers
{
    private readonly Dictionary<string, AreaAiMover> _movers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<AreaAiMover> All => _movers.Values;

    /// <summary>Counts the changes (a step, a turn, a reset), so a caller can keep what it derived while nothing changed.</summary>
    public long Version { get; private set; }

    public AreaAiMover? Of(string entity) => _movers.GetValueOrDefault(entity);

    /// <summary>The NPC's world cell now: its mover's, else <paramref name="origin"/>.</summary>
    public CellCoord CellOf(string entity, CellCoord origin) => _movers.TryGetValue(entity, out var mover) ? mover.Cell : origin;

    /// <summary>The NPC's facing now: its mover's, else <paramref name="facing"/>.</summary>
    public NeighborDirection FacingOf(string entity, NeighborDirection facing) => _movers.TryGetValue(entity, out var mover) ? mover.Facing : facing;

    /// <summary>Starts the NPC's step to <paramref name="next"/> (a neighbour of its cell), facing it unless <paramref name="keepFacing"/>, run when <paramref name="running"/>.</summary>
    public void Step(string entity, AreaObjectKey key, CellCoord origin, CellCoord next, long nowMs, int stepMs, bool running = false, bool keepFacing = false)
    {
        var from = CellOf(entity, origin);
        // ARC-DEV-141: a pushed NPC slides without turning (the knockback step moves the object and sets no rotation, GT61).
        var facing = keepFacing ? FacingOf(entity, NeighborDirection.XPlus) : AreaCombatHost.Facing(from, next) ?? FacingOf(entity, NeighborDirection.XPlus);
        _movers[entity] = new AreaAiMover(entity, key, origin, next, facing) { From = from, StepStartMs = nowMs, StepMs = stepMs, Running = running };
        Version++;
    }

    /// <summary>
    /// ARC-DEV-094 (MIG7c): stands the NPC where the session recorded it (<see cref="GameSessionOwner.QueryNpcPlaces"/>), with no step under way: what a
    /// load, a map change or a window move gives back to a mover the host had before.
    /// </summary>
    public void Restore(string entity, AreaObjectKey key, CellCoord origin, CellCoord cell, NeighborDirection facing)
    {
        _movers[entity] = new AreaAiMover(entity, key, origin, cell, facing);
        Version++;
    }

    /// <summary>
    /// ARC-DEV-119 (MIG9 S5c): gives a mover the map record and origin a later window holds for the same NPC (a window move keeps the host, so the
    /// movers stay; the T0 record the overlay hides is the new window's). The cell, facing and step stay; none without a mover or with the same record.
    /// </summary>
    public void Rekey(string entity, AreaObjectKey key, CellCoord origin)
    {
        if (!_movers.TryGetValue(entity, out var mover) || (mover.Key == key && mover.Origin == origin)) return;
        _movers[entity] = mover with { Key = key, Origin = origin };
        Version++;
    }

    /// <summary>Turns the NPC toward <paramref name="toward"/> where it stands (no turn on the same cell).</summary>
    public void Face(string entity, AreaObjectKey key, CellCoord origin, CellCoord toward)
    {
        var at = CellOf(entity, origin);
        if (AreaCombatHost.Facing(at, toward) is not { } facing) return;
        _movers[entity] = _movers.TryGetValue(entity, out var mover) ? mover with { Facing = facing, From = null, StepMs = 0 }
            : new AreaAiMover(entity, key, origin, at, facing);
        Version++;
    }

    public void Clear() { _movers.Clear(); Version++; }

    /// <summary>Ends the slides done by <paramref name="nowMs"/>: the cell a mover left is no longer held.</summary>
    public void Settle(long nowMs)
    {
        foreach (var mover in _movers.Values.Where(m => m.From is not null && m.Progress(nowMs) >= 1).ToArray())
        {
            _movers[mover.Entity] = mover with { From = null, StepMs = 0 };
            Version++;
        }
    }

    /// <summary>Whether the NPC stands off its map data's cell, or is on its way (its T0 record is moved).</summary>
    public bool IsMoved(string entity) => _movers.TryGetValue(entity, out var mover) && (mover.Cell != mover.Origin || mover.From is not null);

    /// <summary>The T0 records the movers left (their body is drawn where they stand now).</summary>
    public IEnumerable<AreaObjectKey> Moved => _movers.Values.Where(m => IsMoved(m.Entity)).Select(m => m.Key);

    /// <summary>The world cells the movers hold: the cell each stands on and, during a step, the cell it left.</summary>
    public IEnumerable<CellCoord> Held()
    {
        foreach (var mover in _movers.Values)
        {
            yield return mover.Cell;
            if (mover.From is { } from && from != mover.Cell) yield return from;
        }
    }

    /// <summary>
    /// The movers' T0 body commands (<paramref name="body"/>: the NPC's command on its T0 cell, or null) shifted by the projected offset
    /// of where it stands at <paramref name="nowMs"/> from its T0 cell, ordered at its cell; frame, flip and visual stay the T0 body's,
    /// as for the companions.
    /// </summary>
    public IReadOnlyList<AreaDrawCommand> Commands(Func<AreaObjectKey, AreaDrawCommand?> body, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(body);
        var moved = new List<AreaDrawCommand>();
        foreach (var mover in _movers.Values)
        {
            if (body(mover.Key) is not { } command) continue;
            var t = mover.Progress(nowMs);
            var from = mover.From ?? mover.Cell;
            double du = from.X + (mover.Cell.X - from.X) * t - mover.Origin.X, dv = from.Y + (mover.Cell.Y - from.Y) * t - mover.Origin.Y;
            int px = (int)Math.Round(40 * (dv - du), MidpointRounding.AwayFromZero), py = (int)Math.Round(20 * (du + dv), MidpointRounding.AwayFromZero);
            moved.Add(command with
            {
                Destination = new TerrainRect(checked(command.Destination.X + px), checked(command.Destination.Y + py), command.Destination.Width, command.Destination.Height),
                MapX = mover.Cell.X, MapY = mover.Cell.Y,
            });
        }
        return moved.AsReadOnly();
    }
}
