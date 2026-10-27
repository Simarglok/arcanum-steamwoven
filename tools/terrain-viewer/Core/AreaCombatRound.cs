using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-COMBAT-020: one critter of the shown map as the round's scan sees it: who it is, its world cell, when it last entered its
/// sector (<see cref="AreaSectorEntries"/>; 0 for a critter that has not moved since the map loaded) and its map row (null for the
/// hero).
/// </summary>
public readonly record struct AreaScanCritter(SessionCombatant Combatant, CellCoord World, long Entered, AreaObjectKey? Key);

/// <summary>
/// ARC-COMBAT-020: when the hero and the companions last entered a sector (the original re-attaches a moving object to the front of
/// its new sector's list, ledger TB13). A higher number is a later entry; a critter never observed is 0.
/// </summary>
public sealed class AreaSectorEntries
{
    private readonly Dictionary<string, (long Sector, long Entered)> _entries = new(StringComparer.Ordinal);
    private long _next;
    private const string HeroKey = "";

    /// <summary>Records where the critter (null: the hero) stands; a new sector takes the next entry number.</summary>
    public void Observe(string? entity, CellCoord world)
    {
        var key = entity ?? HeroKey;
        var sector = AreaCombatScan.Sector(world);
        if (_entries.TryGetValue(key, out var known) && known.Sector == sector) return;
        _entries[key] = (sector, ++_next);
    }

    /// <summary>The critter's (null: the hero's) last entry number, 0 when never observed.</summary>
    public long Of(string? entity) => _entries.TryGetValue(entity ?? HeroKey, out var known) ? known.Entered : 0;

    public void Clear() { _entries.Clear(); _next = 0; }
}

/// <summary>
/// ARC-COMBAT-020: the order of the original's scan of a square (ledger TB13, 0x440B40): sector rows by increasing y, the sectors
/// of a row by increasing x, and in a sector the object attached last first. Objects loaded with the map are attached in load
/// order (approximated by their source order: terrain, static, then mobile objects, each in file order, so the later one comes
/// first); a critter that walked into the sector since was attached after all of them, the latest entry first.
/// </summary>
public static class AreaCombatScan
{
    /// <summary>The tiles of a sector side.</summary>
    public const int SectorTiles = 64;

    /// <summary>The packed sector of a world cell (row, then column), comparable in scan order.</summary>
    public static long Sector(CellCoord world) => ((long)world.Y / SectorTiles << 32) | (uint)(world.X / SectorTiles);

    public static IReadOnlyList<AreaScanCritter> Order(IEnumerable<AreaScanCritter> critters)
    {
        ArgumentNullException.ThrowIfNull(critters);
        return critters.OrderBy(c => c.World.Y / SectorTiles).ThenBy(c => c.World.X / SectorTiles)
            .ThenByDescending(c => c.Entered)
            .ThenByDescending(c => c.Key is { } key ? (int)key.Kind : int.MaxValue)
            .ThenByDescending(c => c.Key?.Sector ?? long.MaxValue)
            .ThenByDescending(c => c.Key?.SourceIndex ?? int.MaxValue)
            .ToArray();
    }

    /// <summary>
    /// The critters of a map for the scan: the hero at <paramref name="heroWorld"/> and each NPC of <paramref name="npcs"/> where
    /// <paramref name="place"/> stands it (null: not on this map, left out), in scan order. Destroyed, switched-off and carried
    /// critters are the caller's to leave out (the original does not list them); dead, unconscious and hidden ones stay.
    /// </summary>
    public static IReadOnlyList<AreaScanCritter> Critters(IEnumerable<(string Entity, AreaObjectKey Key)> npcs, CellCoord heroWorld,
        Func<string, CellCoord?> place, AreaSectorEntries entries)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(entries);
        var list = new List<AreaScanCritter> { new(SessionCombatant.Hero, heroWorld, entries.Of(null), null) };
        foreach (var (entity, key) in npcs)
            if (place(entity) is { } cell) list.Add(new(SessionCombatant.Critter(entity), cell, entries.Of(entity), key));
        return Order(list);
    }
}

/// <summary>
/// ARC-COMBAT-020: the AREA viewer's answers to the turn-based round (<see cref="ISessionCombatWorld"/>): the critters of the
/// shown map in scan order (<see cref="AreaCombatScan"/>; the session keeps those in its square) and an NPC's facts from its
/// stored fields (<see cref="Facts"/>), or null when it has none or is gone. Neither answer calls back into the session.
/// </summary>
public sealed class AreaCombatWorld : ISessionCombatWorld
{
    private const uint DontDraw = 0x100, Sleeping = 0x1000;

    private readonly int _runtimeMap;
    private readonly Func<IReadOnlyList<AreaScanCritter>> _critters;
    private readonly Func<string, SessionCombatantFacts?> _facts;

    /// <param name="runtimeMap">The shown map; a scan of another map lists nothing.</param>
    /// <param name="critters">The shown map's critters in scan order, the hero once.</param>
    /// <param name="facts">An NPC's facts, or null when it has none or is gone.</param>
    public AreaCombatWorld(int runtimeMap, Func<IReadOnlyList<AreaScanCritter>> critters, Func<string, SessionCombatantFacts?> facts)
    {
        _runtimeMap = runtimeMap;
        _critters = critters ?? throw new ArgumentNullException(nameof(critters));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    }

    public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) =>
        map.RuntimeMap != _runtimeMap ? [] : _critters().Select(c => new SessionCombatCandidate(c.Combatant, c.World)).ToArray();

    public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => map.RuntimeMap != _runtimeMap ? null : _facts(entity);

    /// <summary>
    /// An NPC's facts for the round: its effective Speed in <paramref name="state"/> (its session life record's state, else the
    /// stored one), its life facts and stored life state, hidden from drawing (object flag 0x100) and sleeping (critter flag
    /// 0x1000), the bits the original's skip test reads (ledger TB4).
    /// </summary>
    public static SessionCombatantFacts Facts(SessionCritterCombat combat, CombatFields fields, LifeState state)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(fields);
        return new()
        {
            Speed = combat.EffectiveStat(CharacterStat.Speed, state),
            Life = combat.Facts,
            Loaded = combat.Loaded,
            Hidden = (fields.Object.Flags & DontDraw) != 0,
            Sleeping = ((fields.Critter?.Flags ?? 0) & Sleeping) != 0,
        };
    }
}

/// <summary>How a hero's action fared against the round (<see cref="AreaTurnBased.ChargeHero"/>).</summary>
public enum AreaHeroCharge
{
    /// <summary>No round: nothing to pay.</summary>
    Free,
    Paid,
    /// <summary>Beyond its action points: the hero took the overexertion's fatigue damage and acts once more.</summary>
    Overexerted,
    /// <summary>Not enough action points: the action does not happen and the turn passed on.</summary>
    Refused,
    /// <summary>Another participant's subturn: the original's world handler ignores the hero's commands.</summary>
    NotTheirTurn,
    /// <summary>The hero cannot act (unconscious, stunned, paralysed, stoned).</summary>
    Inactive,
    /// <summary>The session refused the call (a stale version or a budget); nothing happened.</summary>
    Failed,
}

/// <summary>One frame of the round's host (<see cref="AreaTurnBased.Step"/>): the round's events in order, the session calls made
/// and the failure of a refused call (none: <see cref="GameSessionFailure.None"/>).</summary>
public sealed record AreaTurnBasedStep(IReadOnlyList<SessionCombatEvent> Events, int Calls, GameSessionFailure Failure)
{
    public static AreaTurnBasedStep None { get; } = new([], 0, GameSessionFailure.None);
}

/// <summary>
/// ARC-COMBAT-020: the AREA viewer's turn-based round host, over the session's round domain (ARC-COMBAT-003). Pure decisions and
/// session calls; the viewer supplies whether the hero is idle and the world.
/// </summary>
public static class AreaTurnBased
{
    /// <summary>The subturns one frame passes at most.</summary>
    public const int MaxSubturnsPerFrame = 64;

    /// <summary>
    /// One frame of the round. An NPC's subturn ends at once: the viewer has no AI, and the original ends the subturn of an NPC
    /// that starts no animation (action points 0, then the completion check, ledger TB5), which <see cref="GameSessionOwner.EndCombatTurn"/>
    /// is. On the hero's subturn the completion check runs once the hero is idle (<paramref name="heroIdle"/>): an active hero with
    /// action points keeps its subturn, else it passes on. The frame stops at the hero's held subturn, after a new round began
    /// (so the event pump runs between rounds), at the end of the round, at a refused call, or after
    /// <see cref="MaxSubturnsPerFrame"/> subturns.
    /// </summary>
    public static AreaTurnBasedStep Step(GameSessionOwner owner, ISessionCombatWorld world, bool heroIdle, int maxSubturns = MaxSubturnsPerFrame)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubturns);
        var events = new List<SessionCombatEvent>();
        var calls = 0;
        for (var i = 0; i < maxSubturns && owner.QueryCombatRound() is { } round; i++)
        {
            if (round.Actor.IsHero && !heroIdle) break;
            var result = round.Actor.IsHero ? owner.CompleteCombatActions(owner.Version, world) : owner.EndCombatTurn(owner.Version, round.Actor, world);
            calls++;
            if (result.Disposition == GameSessionDisposition.Rejected) return new(events, calls, result.Failure);
            events.AddRange(result.Events);
            if (result.Disposition != GameSessionDisposition.Changed || result.Events.Any(e => e.Kind == SessionCombatEventKind.RoundBegan)) break;
        }
        return new(events, calls, GameSessionFailure.None);
    }

    /// <summary>
    /// Charges a hero's action of <paramref name="cost"/> action points (a step, an attack, a pickup, a use): outside a round it is
    /// free; on another participant's subturn or with an inactive hero it does not happen; else the session's charge
    /// (<see cref="GameSessionOwner.ConsumeActionPoints"/>) decides, and a refusal passes the turn on.
    /// </summary>
    public static AreaHeroCharge ChargeHero(GameSessionOwner owner, int cost, ISessionCombatWorld world) => ChargeHero(owner, cost, world, out _);

    /// <summary>As <see cref="ChargeHero(GameSessionOwner, int, ISessionCombatWorld)"/>, with the round's events of the charge (a
    /// refusal's next subturn, a round's end and begin).</summary>
    public static AreaHeroCharge ChargeHero(GameSessionOwner owner, int cost, ISessionCombatWorld world, out IReadOnlyList<SessionCombatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(world);
        events = [];
        if (owner.QueryCombatRound() is null || owner.QueryHero() is null) return AreaHeroCharge.Free;
        switch (owner.QueryCombatActionGate(SessionCombatant.Hero, cost))
        {
            case SessionCombatGate.NotTheirTurn: return AreaHeroCharge.NotTheirTurn;
            case SessionCombatGate.Inactive: return AreaHeroCharge.Inactive;
        }
        var result = owner.ConsumeActionPoints(owner.Version, SessionCombatant.Hero, cost, world);
        if (result.Disposition == GameSessionDisposition.Rejected) return AreaHeroCharge.Failed;
        events = result.Events;
        return result.Charge switch
        {
            SessionActionPointUse.Paid => AreaHeroCharge.Paid,
            SessionActionPointUse.Overexerted => AreaHeroCharge.Overexerted,
            SessionActionPointUse.Refused => AreaHeroCharge.Refused,
            _ => AreaHeroCharge.Free,
        };
    }

    /// <summary>Whether the charged action goes ahead.</summary>
    public static bool Proceeds(AreaHeroCharge charge) => charge is AreaHeroCharge.Free or AreaHeroCharge.Paid or AreaHeroCharge.Overexerted;

    /// <summary>The action points of a step in the gait it starts with (walk 2, run 1 while the hero has fatigue left).</summary>
    public static int StepCost(MovementGait gait, int currentFatigue) => TurnBasedRules.StepCost(gait == MovementGait.Run, currentFatigue);

    /// <summary>Why an action did not happen, for the status line.</summary>
    public static string Refusal(AreaHeroCharge charge) => charge switch
    {
        AreaHeroCharge.Refused => "not enough action points: the turn passes on",
        AreaHeroCharge.NotTheirTurn => "not the hero's turn",
        AreaHeroCharge.Inactive => "the hero cannot act",
        AreaHeroCharge.Failed => "the round refused the action",
        _ => "",
    };

    /// <summary>The round for the status line: the setting, the turn, whose subturn and the action points left.</summary>
    public static string Status(bool turnBased, SessionCombatRound? round, Func<SessionCombatant, string> name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (round is null) return turnBased ? "turn-based: on, no round" : "turn-based: off";
        return $"turn-based: round {round.Turn}, {(round.Actor.IsHero ? "hero's turn" : name(round.Actor) + "'s turn")}, AP {round.ActionPoints}"
            + $" ({round.Current + 1}/{round.Participants.Count})";
    }
}
