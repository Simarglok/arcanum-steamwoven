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
    /// <summary>The first gate write an NPC's subturn hook could not store (ARC-WORLD-095; the hook went on, as the original's writes
    /// cannot fail).</summary>
    public GameSessionFailure WriteFailure { get; init; }

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
    /// One frame of the round. An NPC's subturn ends at once without an AI host (<paramref name="ai"/> null: the original ends the
    /// subturn of an NPC that starts no animation, action points 0, then the completion check, ledger TB5), which
    /// <see cref="GameSessionOwner.EndCombatTurn"/> is. With one (ARC-WORLD-095) the NPC acts in its subturn, as
    /// <see cref="Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/> says. On the hero's subturn the completion check
    /// runs once the hero is idle (<paramref name="heroIdle"/>): an active hero with action points keeps its subturn, else it passes on.
    /// The frame stops at the hero's held subturn, after a new round began (so the event pump runs between rounds), at the end of the
    /// round, at a refused call, or after <see cref="MaxSubturnsPerFrame"/> subturns.
    /// </summary>
    public static AreaTurnBasedStep Step(GameSessionOwner owner, ISessionCombatWorld world, bool heroIdle, int maxSubturns = MaxSubturnsPerFrame) =>
        Step(owner, world, heroIdle, null, maxSubturns);

    /// <summary>
    /// ARC-DEV-136: the host's frame of the round, without the turn-based event's completion (<see cref="Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/> with
    /// <c>eventFired</c> false): the observation of the round, the NPCs' subturn hooks, and the hero's callback while its actions are no runs.
    /// </summary>
    public static AreaTurnBasedStep Frame(GameSessionOwner owner, ISessionCombatWorld world, bool heroIdle, AreaAiHost? ai) =>
        Step(owner, world, heroIdle, ai, MaxSubturnsPerFrame, eventFired: false);

    /// <summary>
    /// One pass of the round with the map's AI host (ARC-WORLD-095). The host first follows the round (<see cref="AreaAiHost.ObserveRound"/>:
    /// the interrupts of a round's start and of each turn set). An NPC whose subturn just started gets its subturn hook
    /// (<see cref="GameSessionOwner.RunAiSubturnHook"/>, 0x4B7010 from the turn set 0x4B6E70: the heartbeat, on "continue" one AI pass,
    /// and without a run the end of its subturn); while its goal run goes on the subturn waits for it (the run pays its steps and its
    /// attack through the round's check and consume). When the run has ended, the hook runs again (the goal stepper's end of a done run,
    /// 0x423B8D: the round host's next frame, as the section says; ARC-DEV-136 keeps that placement).
    /// <para>ARC-DEV-136 (MIG13 S5b, goal-table.md §3.7, TL39): the completion is the turn-based event's. The animation system's callback
    /// (<see cref="GameSessionOwner.AnimationsDone"/>, 0x4B7080) is called by the goal table when a slot is freed with no significant goal left and
    /// asks for that event (2 ms ahead, one pending); this pass is the event's handler when <paramref name="eventFired"/> is true (the default, the
    /// pass the frame step hands out when the event falls due): <see cref="GameSessionOwner.CompleteCombatActions"/>, 0x4B7150, spends the rest of a participant the callbacks
    /// marked as stalled and passes the turn of one without action points, whatever the actor's own run is playing (the handler tests no animation, GT23); for an NPC it waits while the hero's
    /// viewer-hosted action plays (the event is asked for again). With <paramref name="eventFired"/> false (the host's frame, <see cref="Frame"/>) it only follows the round and plays the hooks; the
    /// hero's actions are no runs yet (S10-S13), so a hero that is idle with nothing significant playing and no event pending stands for the free of its last slot and calls the callback itself.
    /// ARC-WORLD-099: each NPC subturn end among the calls' events asks the NPC's end-combat script (<see cref="AreaAiHost.SubturnsEnded"/>).
    /// Without <paramref name="ai"/> an NPC's subturn ends at once.</para>
    /// </summary>
    public static AreaTurnBasedStep Step(GameSessionOwner owner, ISessionCombatWorld world, bool heroIdle, AreaAiHost? ai, int maxSubturns = MaxSubturnsPerFrame, bool eventFired = true)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubturns);
        var events = new List<SessionCombatEvent>();
        var calls = 0;
        var writeFailure = GameSessionFailure.None;
        // GR19 (ARC-DEV-078): a round a load brought takes its load rule first (Original rebuilds it as round 1, Steamwoven resumes it);
        // a rebuilt round's begin ends the frame, as a new round does, so the event pump runs before its first subturn is played.
        if (owner.QueryCombatRound() is not null)
        {
            var resumed = owner.ResumeLoadedCombatRound(owner.Version, world);
            if (resumed.Disposition == GameSessionDisposition.Rejected) return new(events, 1, resumed.Failure);
            if (resumed.Events.Count > 0)
            {
                events.AddRange(resumed.Events);
                return new(events, 1, GameSessionFailure.None);
            }
        }
        for (var i = 0; i < maxSubturns && owner.QueryCombatRound() is not null; i++)
        {
            ai?.ObserveRound();
            // The observation's completion check (a turn set's interrupt freeing the last run) may have passed the turn on: the new
            // subturn is observed first.
            var observed = ai?.DrainRoundEvents() ?? [];
            events.AddRange(observed);
            if (owner.QueryCombatRound() is not { } round) break;
            if (observed.Any(e => e.Kind == SessionCombatEventKind.RoundBegan)) break;
            if (observed.Any(e => e.Kind == SessionCombatEventKind.SubturnStarted)) continue;
            if (round.Actor.IsHero && !heroIdle) break;
            SessionCombatResult result;
            if (round.Actor.IsHero)
            {
                if (!eventFired)
                {
                    // The hero's actions are no runs of the table yet: idle with nothing significant playing is the free of its last slot (TL39), so the callback is called here, once while
                    // its event is pending. An active hero with points keeps its turn (the callback answers false at once). A refused request (a full queue) is the viewer's to answer by running the
                    // handler's pass itself (`GameSessionOwner.TurnBasedEventRefusals`).
                    if (owner.QueryGoals().SignificantGoalCount == 0 && owner.QueryRealStep(SessionRealStep.TurnBasedStep) is null) owner.AnimationsDone();
                    break;
                }
                result = owner.CompleteCombatActions(owner.Version, world);
            }
            else if (ai is null) result = owner.EndCombatTurn(owner.Version, round.Actor, world);
            else
            {
                var npc = round.Actor.Entity!;
                var starts = owner.QueryCombatSubturnStarts();
                var started = ai.HookedSubturn != starts;
                if (!started && ai.AnimationRunning(npc))
                {
                    if (!eventFired) break;
                    // ARC-DEV-136 (GT23): the handler 0x4B7150 tests nothing about animations: a forced participant spends the rest and one without points passes whatever is playing, so the event's pass
                    // answers a run that goes on exactly as it answers none (TH5: an NPC that starts an action it cannot pay passes on, the action cut by the turn set's interrupt). The hero's
                    // viewer-hosted action is the one exception (it stands for a slot, see below): the event is asked for again until the hero is idle.
                    if (!heroIdle) { owner.RequestTurnBasedEvent(); break; }
                    result = owner.CompleteCombatActions(owner.Version, world);
                }
                else
                {
                    // ARC-DEV-136: the run's end called the callback when it freed the last significant goal, and the event it asked for brought this pass: the completion follows the hook.
                    // The hero's viewer-hosted action is a significant animation, which holds the callback: the event is asked for again until the hero is idle.
                    var completes = eventFired && !started && heroIdle;
                    if (eventFired && !started && !heroIdle) owner.RequestTurnBasedEvent();
                    ai.HookedSubturn = starts;
                    var hook = owner.RunAiSubturnHook(owner.Version, round.Actor, ai);
                    calls++;
                    if (hook.Failure != GameSessionFailure.None) return new(events, calls, hook.Failure) { WriteFailure = writeFailure };
                    if (writeFailure == GameSessionFailure.None) writeFailure = hook.WriteFailure;
                    events.AddRange(ai.DrainRoundEvents());
                    // The stepper's fidget call for the done run's owner follows its hook (ARC-WORLD-099, TH8).
                    ai.FidgetAfterHook(npc);
                    if (hook.Round is { } ended)
                    {
                        if (ended.Disposition == GameSessionDisposition.Rejected) return new(events, calls, ended.Failure) { WriteFailure = writeFailure };
                        events.AddRange(ended.Events);
                        ai.SubturnsEnded(ended.Events);
                        if (ended.Disposition != GameSessionDisposition.Changed || ended.Events.Any(e => e.Kind == SessionCombatEventKind.RoundBegan)) break;
                        continue;
                    }
                    // A run started (or the hook did not act): the subturn waits for it, unless the completion check passes it on.
                    if (!completes || owner.QueryCombatRound() is not { } now || now.Actor != round.Actor) break;
                    result = owner.CompleteCombatActions(owner.Version, world);
                }
            }
            calls++;
            if (result.Disposition == GameSessionDisposition.Rejected) return new(events, calls, result.Failure) { WriteFailure = writeFailure };
            events.AddRange(result.Events);
            ai?.SubturnsEnded(result.Events);
            if (result.Disposition != GameSessionDisposition.Changed || result.Events.Any(e => e.Kind == SessionCombatEventKind.RoundBegan)) break;
        }
        return new(events, calls, GameSessionFailure.None) { WriteFailure = writeFailure };
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
        // GR19 (ARC-DEV-078): a loaded round takes its load rule before the charge can commit (and so keep it as loaded).
        var resumed = owner.ResumeLoadedCombatRound(owner.Version, world);
        if (resumed.Disposition == GameSessionDisposition.Rejected) return AreaHeroCharge.Failed;
        var before = resumed.Events;
        events = before;
        if (owner.QueryCombatRound() is null) return AreaHeroCharge.Free;
        switch (owner.QueryCombatActionGate(SessionCombatant.Hero, cost))
        {
            case SessionCombatGate.NotTheirTurn: return AreaHeroCharge.NotTheirTurn;
            case SessionCombatGate.Inactive: return AreaHeroCharge.Inactive;
        }
        var result = owner.ConsumeActionPoints(owner.Version, SessionCombatant.Hero, cost, world);
        if (result.Disposition == GameSessionDisposition.Rejected) return AreaHeroCharge.Failed;
        events = [.. before, .. result.Events];
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
