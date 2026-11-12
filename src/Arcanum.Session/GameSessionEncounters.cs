using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-1159 (schema 95, ledger PO19): how often a row of the encounter tables fired: its message number and its use count. The original saves one pair for every row (0x558730); only the rows used at least once are kept.</summary>
public sealed record SessionEncounterUse(int Line, int Count);

/// <summary>ARC-WORLD-1159 (E2-c, schema 95, the <c>encounters</c> section): the use counts of the encounter rows that fired (the rows with a TriggerCount cap stop at it). The Random Encounters event itself is a saved event of the queue.</summary>
public sealed record SessionEncounterState(IReadOnlyList<SessionEncounterUse> Uses)
{
    public static SessionEncounterState Empty { get; } = new([]);
    /// <summary>The bound of the saved list (project bound: a table row is one message line).</summary>
    public const int MaxUses = 8192;
    public const int MaxCount = 1_000_000;
}

/// <summary>Where and how the roll is made, as the host knows it (ledger PO15–PO16): the map, the roll's place, the hero's tile (the centre of the blocked-tile search, 0x559260) and the host's tile tests.</summary>
public sealed record SessionEncounterSite(SessionMapId Map, EncounterPlace Place, CellCoord HeroCell)
{
    /// <summary>The host's map for the blocked-tile search (0x4F4E40); null: no relocation is possible (a blocked spot loses its creature).</summary>
    public IDropMap? DropMap { get; init; }
    /// <summary>Whether a creature cannot stand on the tile (0x4D7110); null: no tile is blocked (a spot outside the host's loaded window cannot be judged).</summary>
    public Func<CellCoord, bool>? Blocked { get; init; }
}

/// <summary>
/// ARC-WORLD-1159: the pass world of the Random Encounters event (kind 34). The host answers where the hook acts (the outdoor map, the hero in no named area, 0x5595B0) and the tables the module loaded;
/// the session rolls, counts and creates. A world without it (or with no tables) makes the event do nothing but re-arm.
/// </summary>
public interface ISessionEncounterWorld : ISessionEventWorld
{
    /// <summary>The tables of <c>rules/wmap_rnd.mes</c>; null: none loaded (no event is armed, no roll happens).</summary>
    EncounterTables? EncounterTables { get; }
    /// <summary>The place of the roll, or null when the hook does nothing: not the outdoor map, the hero in a named area, the game started with <c>-norandom</c> (0x5595B0).</summary>
    SessionEncounterSite? EncounterSite();
}

public enum SessionEncounterOutcome { NoSite, Stopped, Fired }

/// <summary>What one Random Encounters event came to (ARC-WORLD-1159): the roll's outcome (the original's stop or Fired), the table and row, the creatures made, and where the party stood: the host closes the window it shows (the world map, or the rest window).</summary>
public sealed record SessionEncounterNotice(long AtMs, SessionEncounterOutcome Outcome, EncounterOutcome Roll, EncounterPlace? Place, int Table, int Row, IReadOnlyList<SessionCreatedObject> Made, int Lost, bool Rearmed);

public sealed partial class GameSessionOwner
{
    /// <summary>The longest list of encounter notices the session keeps for its host to take (a host that never takes them does not grow it).</summary>
    public const int MaxEncounterNotices = 64;

    private readonly SortedDictionary<int, int> _encounterUses = [];
    private readonly List<SessionEncounterNotice> _encounterNotices = [];

    /// <summary>The use counts of the encounter rows that fired, ascending by row.</summary>
    public IReadOnlyList<SessionEncounterUse> QueryEncounterUses() { ThrowIfDisposed(); return _encounterUses.Select(pair => new SessionEncounterUse(pair.Key, pair.Value)).ToArray(); }

    /// <summary>The pending Random Encounters events' deadlines (game ms), in queue order: one in a running chain.</summary>
    public IReadOnlyList<long> QueryRandomEncounterDeadlines() { ThrowIfDisposed(); return _events.QueryPending(SessionEventKinds.RandomEncounter).Select(e => e.DeadlineMs).ToArray(); }

    /// <summary>The encounters that fired or stopped since the last call, oldest first, and clears the list. The host closes the world map or the rest window for a fired one (0x5595B0); not saved.</summary>
    public IReadOnlyList<SessionEncounterNotice> TakeEncounterNotices()
    {
        ThrowIfDisposed();
        var taken = _encounterNotices.ToArray();
        _encounterNotices.Clear();
        return taken;
    }

    private SessionEncounterState CaptureEncounters() => new(_encounterUses.Select(pair => new SessionEncounterUse(pair.Key, pair.Value)).ToArray());

    // Shapes: the lines ascending once, the counts within the bound; the rows themselves are the host's tables (a row that no longer exists is dropped silently when the tables change).
    private static GameSessionFailure ImportEncounters(SessionEncounterState saved, out SortedDictionary<int, int> uses)
    {
        uses = [];
        if (saved is null || saved.Uses is null || saved.Uses.Count > SessionEncounterState.MaxUses) return GameSessionFailure.InvalidSnapshot;
        foreach (var use in saved.Uses)
            if (use is null || use.Line < EncounterTables.TableBase || use.Count is < 1 or > SessionEncounterState.MaxCount || !uses.TryAdd(use.Line, use.Count)) return GameSessionFailure.InvalidSnapshot;
        return GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-WORLD-1159 (0x559640 from a new game's start, ledger PO17): arms the Random Encounters chain when the host has tables and none is pending, 300 to 700 game minutes on. The original adds the first event when a
    /// new game starts; here it is the first event pass of a host that has the tables (a build's old save has none pending), so a session no host with the tables ever passed never draws. One revision when it armed.
    /// </summary>
    private void ArmRandomEncounter(ISessionEventWorld world)
    {
        if (world is not ISessionEncounterWorld { EncounterTables.Status: EncounterTablesStatus.Loaded } || _events.Any(SessionEventKinds.RandomEncounter)) return;
        if (!ScheduleRandomEncounter(_clock.QueryClockSnapshot().GameTimestampMs) || _revision == long.MaxValue) return;
        Revise();
    }

    // The next event 300 to 700 game minutes on (the draw first, so an exhausted generator arms nothing). Beyond the event queue's bound the chain ends (project bound).
    [OriginalFunction(0x559640, OriginalFunctionScope.Full)]
    private bool ScheduleRandomEncounter(long now)
    {
        if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || !_random.TryNext((int)EncounterRules.MinDelayMinutes, (int)EncounterRules.MaxDelayMinutes, out var minutes)) return false;
        var delay = EncounterRules.DelayMs(minutes);
        if (now > long.MaxValue - delay) return false;
        return _events.ScheduleAfter(SessionEventKinds.RandomEncounter, GameEventPayload.Of([], now), delay, EventClocks(now)).Disposition != GameEventScheduleDisposition.Rejected;
    }

    /// <summary>
    /// ARC-WORLD-1159 (the hook 0x5595B0, the roll 0x558DE0, the creatures 0x559260; ledger PO13–PO19): the Random Encounters event came due. It re-arms first (0x559640), then asks the host where the hook acts; the
    /// roll (<see cref="EncounterRules.Roll"/>) draws from the session's generator in the original's order and counts the picked row's use (also when its groups came to no monster). A fired roll creates one critter per
    /// rolled creature (<c>oid:critter:n</c>, <see cref="SessionCreatedKind.Critter"/>, no encounter flag: the roll does not set it, ledger PO9) on the ring spots of <see cref="EncounterRules.Spot"/>; a spot the host calls
    /// blocked is searched again from the hero's tile (0x4F4E40 with six steps) and a creature still blocked, or on the tile of another critter, is destroyed unmade. Limits: the creatures do not face the roll
    /// location, and a spot outside the host's loaded window is not tested.
    /// </summary>
    [OriginalFunction(0x5595B0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x559260, OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireRandomEncounter(GameEventFiring firing)
    {
        // The re-armed event, the draws and the counts are one revision (a snapshot of the same revision must not differ).
        var outcome = RunRandomEncounter(firing);
        if (_revision < long.MaxValue) Revise();
        return outcome;
    }

    private GameEventHandlerOutcome RunRandomEncounter(GameEventFiring firing)
    {
        var now = firing.NowMs;
        var rearmed = ScheduleRandomEncounter(now);
        void Notice(SessionEncounterOutcome outcome, EncounterOutcome roll, EncounterPlace? place, int table = -1, int row = -1, IReadOnlyList<SessionCreatedObject>? made = null, int lost = 0)
        {
            if (_encounterNotices.Count < MaxEncounterNotices) _encounterNotices.Add(new(now, outcome, roll, place, table, row, made ?? [], lost, rearmed));
        }
        var world = PassWorldAs<ISessionEncounterWorld>();
        if (world?.EncounterTables is not { Status: EncounterTablesStatus.Loaded } tables || world.EncounterSite() is not { } site || !SessionValidation.Map(site.Map))
        {
            Notice(SessionEncounterOutcome.NoSite, EncounterOutcome.NoTables, null);
            return GameEventHandlerOutcome.Completed;
        }
        var who = new EncounterWho(_clock.QueryClockSnapshot().Hour, _hero?.State.Level ?? -1, GlobalFlagSet, line => _encounterUses.TryGetValue(line, out var count) ? count : 0);
        EncounterRoll roll;
        try { roll = EncounterRules.Roll(tables, site.Place, who, new SessionEncounterRolls(_random)); }
        catch (SessionEncounterRolls.ExhaustedException)
        {
            Notice(SessionEncounterOutcome.Stopped, EncounterOutcome.ChanceMissed, site.Place);
            return GameEventHandlerOutcome.Completed;
        }
        if (roll.UsedRow is { } used) _encounterUses[used] = (_encounterUses.TryGetValue(used, out var before) ? before : 0) + 1;
        if (!roll.Fired)
        {
            Notice(SessionEncounterOutcome.Stopped, roll.Outcome, site.Place, roll.Table, roll.UsedRow ?? -1);
            return GameEventHandlerOutcome.Completed;
        }

        // 0x559260: each group's creatures, one after the other, on the spot of their number; the order of the draws of the relocation is the creature's.
        var made = new List<SessionCreatedObject>();
        var lost = 0;
        var taken = new HashSet<CellCoord>();
        var rolls = new SessionDropRolls(_random);
        var drops = site.DropMap;
        for (var group = 0; group < roll.Sizes!.Count; group++)
        {
            for (var n = 0; n < roll.Sizes[group]; n++)
            {
                var (x, y) = EncounterRules.Spot(site.Place.Location, roll.West, n);
                if (x is < 0 or >= 1 << 24 || y is < 0 or >= 1 << 24) { lost++; continue; }
                var spot = new CellCoord((int)x, (int)y);
                if (CreateObjectRefusal(SessionCreatedKind.Critter, roll.Prototypes![group], site.Map, spot, 0, 0) is not null) { lost++; continue; }
                if (site.Blocked?.Invoke(spot) == true)
                {
                    // 0x4F4E40 from the hero with six steps in a drawn direction; the new tile must not be blocked either.
                    CellCoord? moved = null;
                    if (drops is not null)
                    {
                        try { moved = DropPlacementRules.PlaceOnce(site.HeroCell, EncounterRules.Ring, DropOwner.PcSide, drops, rolls.Roll(DropRoll.Direction, 0, DropPlacementRules.MaxDirection)); }
                        catch (SessionDropRolls.ExhaustedException) { }
                    }
                    if (moved is not { } relocated || site.Blocked(relocated) || !DropCellFits(relocated)) { lost++; continue; }
                    spot = relocated;
                }
                // A creature on a critter's tile is destroyed: the host's critters and the ones this roll made already.
                if (!taken.Add(spot) || site.DropMap?.HasCritter(spot) == true) { lost++; continue; }
                if (!DropCellFits(spot) || _revision >= long.MaxValue - 2) { lost++; continue; }
                made.Add(CreateCreatedObject(SessionCreatedKind.Critter, roll.Prototypes![group], site.Map, spot, 0, 0, now));
            }
        }
        Notice(SessionEncounterOutcome.Fired, EncounterOutcome.Fired, site.Place, roll.Table, roll.UsedRow ?? -1, made, lost);
        return GameEventHandlerOutcome.Completed;
    }

    private bool GlobalFlagSet(int index)
    {
        var flags = Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        return read.Status == GlobalFlagReadStatus.Known && read.Value is true;
    }
}

internal sealed class SessionEncounterRolls(Arcanum.Simulation.Randomness.SeededRandom random) : IEncounterRolls
{
    public int Roll(int min, int max) => random.TryNext(min, max, out var value) ? value : throw new ExhaustedException();
    internal sealed class ExhaustedException : Exception;
}
