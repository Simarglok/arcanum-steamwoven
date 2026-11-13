using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-110: the cast host's side of a trap the viewer meets: the effect of a fired native run (<see cref="Apply"/>: the row of the
/// trapped object, the run, the use script's counters and the trap state's object flags; the viewer builds the
/// <see cref="SessionTrapFiring"/> with the cast map's ids and calls <see cref="SessionSpellCaster.ApplyTrapEffect"/>) and the player's
/// spell flags (field 0x14), which the spot gate and the step trigger read.
/// </summary>
public sealed record AreaTrapEffects(Func<AreaObjectRecord, NativeTrapRun, int, int, SessionTrapEffectResult> Apply, int PlayerSpellFlags)
{
    /// <summary>ARC-DEV-260: the same effect with no triggerer (<see cref="Apply"/> has the hero): a trap set off by a ranged attack or by damage with
    /// no attacker (ledger OD4). Null while the host cannot (such a trap is then not met).</summary>
    public Func<AreaObjectRecord, NativeTrapRun, int, int, SessionTrapEffectResult>? ApplyWithoutTriggerer { get; init; }

    /// <summary>ARC-WORLD-1469 (ledger TT8, TT9): the same effect with a critter other than the hero as the triggerer (<c>entity</c>: the NPC or companion that stepped on a trap object or used
    /// a trapped one): its floating line, its eye candy and, for a radius 0 run, the victim are its. Null while the host cannot (such a trap is then not met by a non-hero).</summary>
    public Func<string, AreaObjectRecord, NativeTrapRun, int, int, SessionTrapEffectResult>? ApplyByCritter { get; init; }

    /// <summary>ARC-WORLD-1469 (TT9): a critter's spell flags (field 0x14) now, which the step trigger reads (a mover with flag 0x2 or 0x4 sets off no trap object); null: none set.</summary>
    public Func<string, int>? SpellFlagsOf { get; init; }
}

/// <summary>
/// ARC-WORLD-1469: a non-hero walker that meets a trap (<see cref="AreaTraps.StepNpc"/>, <see cref="AreaTraps.UseNpc"/>): the critter's entity (the triggerer of the object entry point), whether the PC
/// leads it (the observer of TT2: a companion knows the spotted and the PC-placed traps, a free NPC every trap the PC did not place) and, when it has a sheet, the spot roll's inputs (null: it cannot
/// roll, so a trap it does not know is met without a spot attempt).
/// </summary>
public sealed record AreaTrapWalker(string Entity, bool LedByPc, SessionNpcSpot? Spot);

/// <summary>
/// ARC-WORLD-110: one meeting of the PC with a trap through the object entry point (a use, or a step onto a trap object): the trap's row,
/// the cause, the session's answer (<see cref="GameSessionOwner.TriggerTrap"/>) and the effect of a fired native run (null when none
/// was applied: no run, or 30007).
/// </summary>
public sealed record AreaTrapMeeting(AreaObjectRecord Row, SessionTrapCause Cause, SessionTrapResult Result, SessionTrapEffectResult? Effect)
{
    public SessionTrapOutcome Outcome => Result.Outcome;
    public bool Rejected => Result.Mutation.Disposition == GameSessionDisposition.Rejected;

    /// <summary>The use stops here (skip default): the trap was spotted now, a native run fired (the trap code answers skip default,
    /// TT8), or the session refused the meeting. An ordinary trap script that fired (no native run) runs as the use script.</summary>
    public bool Cancels => Rejected || Outcome == SessionTrapOutcome.Spotted || Outcome == SessionTrapOutcome.Fired && Result.Run is not null;

    /// <summary>A step's walk ends on the tile (TT9): the trap was spotted now or fired.</summary>
    public bool EndsWalk => !Rejected && Outcome is SessionTrapOutcome.Spotted or SessionTrapOutcome.Fired;

    /// <summary>Identifiers, outcomes and numbers only.</summary>
    public string Describe()
    {
        if (Rejected) return $"refused by the session ({Result.Mutation.Failure})";
        var what = Outcome switch
        {
            SessionTrapOutcome.Spotted => "spotted" + (Result.PlayerLine ? $" (mes\\trap.mes {AreaTraps.SpottedLine})" : ""),
            SessionTrapOutcome.Fired when Result.Run is { } run => $"script {run.Script} went off (mes\\trap.mes {TrapEffectRules.FiredLine})"
                + (Result.State.Removed ? ", removed" : ""),
            SessionTrapOutcome.Fired => $"ordinary trap script {Result.State.Script} runs",
            _ => Outcome.ToString(),
        };
        if (Effect is { Victims.Count: > 0 } effect)
            what += "; " + string.Join(", ", effect.Victims.Select(v => $"{v.Entity} {v.Hit}{(v.HitPointDamage > 0 ? $" -{v.HitPointDamage} HP" : "")}"
                + (v.SpellRun is { } run ? $" spell run {run}" : "")));
        return what;
    }
}

/// <summary>
/// ARC-SKILLS-1156: one disarm traps press of the hero on a trapped object (<see cref="AreaTraps.Disarm"/>): the object's row, the session's roll
/// (<see cref="GameSessionOwner.RollHeroDisarmTrap"/>: the turn-based charge, the check, the disarm), the effect of a sprung native trap and the
/// device item an expert's critical success created for the hero (null when none was owed or the world has no item data for it).
/// </summary>
public sealed record AreaTrapDisarm(AreaObjectRecord Row, SessionDisarmRoll Roll, SessionTrapEffectResult? Effect, InventoryMutationResult? Device)
{
    /// <summary>The ordinary (non-native) trap script a critical failure set off, run as the use script with the hero as the triggerer; null when none ran.</summary>
    public AreaScriptedUse? Script { get; init; }

    public bool Rejected => Roll.Disposition == GameSessionDisposition.Rejected;

    /// <summary>The disarm outcome (<see cref="TrapDisarm.NotTrapped"/> for a rejected press).</summary>
    public TrapDisarm Outcome => Roll.Disarm?.Outcome ?? TrapDisarm.NotTrapped;

    /// <summary>Identifiers, outcomes and numbers only.</summary>
    public string Describe()
    {
        if (Rejected) return $"refused by the session ({Roll.Failure})";
        var check = Roll.Check is { } c ? $" (roll {c.Check.Roll} against {c.Check.Effectiveness - c.Check.Difficulty}, {(c.Success ? "pass" : "fail")}{(c.Critical ? " critical" : "")}{(Roll.SecondRoll ? ", second roll" : "")})" : "";
        var what = Outcome switch
        {
            TrapDisarm.Removed => "disarmed",
            TrapDisarm.RemovedWithDevice => "disarmed, its device " + (Device is { Item: not null, Verdict: SessionInsertVerdict.Accepted } ? "is in the pack" : Device is { Item: not null } ? "lies on the hero's tile" : "could not be made"),
            TrapDisarm.Failed => "the disarm failed",
            TrapDisarm.Sprung => "the disarm failed critically: the trap goes off" + (Roll.Disarm?.Run is { } run ? $" (script {run.Script})" : ""),
            TrapDisarm.NotKnown => "no known trap there",
            _ => "no trap there",
        };
        if (Effect is { Victims.Count: > 0 } effect)
            what += "; " + string.Join(", ", effect.Victims.Select(v => $"{v.Entity} {v.Hit}{(v.HitPointDamage > 0 ? $" -{v.HitPointDamage} HP" : "")}"));
        return what + check;
    }
}

/// <summary>ARC-WORLD-1469: one trap meeting of a non-hero walker (<see cref="AreaAiHost.TakeTrapMeetings"/>): who met it and what happened.</summary>
public sealed record AreaTrapNotice(string Walker, AreaTrapMeeting Meeting);

/// <summary>What the walk check before one step found (TT10): the trap object it spotted (null: none, the step goes), the session's
/// mark, and how many spot trap rolls it made on the tile's unknown traps (ARC-MAGIC-057, ledger SP1).</summary>
public sealed record AreaTrapWalk(AreaObjectRecord? Spotted, GameSessionMutationResult? Mark, int Rolls);

/// <summary>
/// ARC-WORLD-110: the AREA viewer's trap host for the PC (docs/world/trap-state-runtime.md, ledger TT3, TT9, TT10, EX20): the use of a
/// trapped object (<see cref="Use"/>, which the use-script host asks before the use script), the walk's check of the next tile
/// (<see cref="WalkCheck"/>) and the step onto a trap object (<see cref="Step"/>). Each asks the session's trap state with the row's
/// baseline from <see cref="AreaUseScriptHost.TrapBaseline"/>; a fired native run's effect is the cast host's
/// (<see cref="AreaTrapEffects"/>). The spot attempt of an unknown trap spots without a roll when the player's spell flags hold
/// 0x100 (TT3), else by the hero's spot trap roll (ARC-MAGIC-057, ledger SP1: the shared check, the master's second roll, no spot of a
/// magical trap below expert; its failure leaves the trap unknown and the use or the step goes on). No trap is consumed without
/// its effect: without a cast host (<paramref name="effects"/> answering null), for a 30002 shot (its shooter search is a later slice)
/// and for an ordinary trap script on a step (the step's dispatch with the trap as attachee and extra object is a later slice) the trap
/// is not met at all, so a use goes on as before (the dispatcher stops at a native trap script) and a step meets nothing. Only the PC
/// meets traps here.
/// </summary>
public sealed class AreaTraps
{
    /// <summary>The line of <c>mes\trap.mes</c> the player gets for a trap spotted (TT4).</summary>
    public const int SpottedLine = 0;
    private const int TrapObjectType = 17;
    private readonly GameSessionOwner _session;
    private readonly AreaUseScriptHost _host;
    private readonly Func<AreaObjectRecord, bool> _gone;
    private readonly Func<AreaTrapEffects?> _effects;
    private readonly Func<AreaObjectRecord, int>? _light;
    private readonly Dictionary<(long X, long Y), List<AreaObjectRecord>> _trapObjects = [];

    /// <param name="shown">The shown map's rows (its snapshot's): its trap objects (type 17) in snapshot order are the ones a step meets.</param>
    /// <param name="gone">Whether the session destroyed or switched off a row (a removed trap object is destroyed).</param>
    /// <param name="effects">The cast host's side now, or null while there is none.</param>
    /// <param name="light">ARC-WORLD-1149 (FR-LIGHT-011): the light level 0..255 at a trap object's cell for the spot roll's darkness term; null: full light.</param>
    public AreaTraps(GameSessionOwner session, AreaUseScriptHost host, IEnumerable<AreaObjectRecord> shown, Func<AreaObjectRecord, bool> gone,
        Func<AreaTrapEffects?> effects, Func<AreaObjectRecord, int>? light = null)
    {
        _light = light;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(shown);
        _gone = gone ?? throw new ArgumentNullException(nameof(gone));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        foreach (var row in shown)
            if (row.Type == TrapObjectType && row.WorldX is { } x && row.WorldY is { } y)
            {
                if (!_trapObjects.TryGetValue((x, y), out var list)) _trapObjects[(x, y)] = list = [];
                list.Add(row);
            }
    }

    /// <summary>The shown map's trap objects on the tile that the session has not destroyed or switched off, in snapshot order.</summary>
    public IReadOnlyList<AreaObjectRecord> TrapObjectsOn(CellCoord cell) =>
        _trapObjects.TryGetValue((cell.X, cell.Y), out var list) ? list.Where(row => !_gone(row)).ToArray() : [];

    /// <summary>
    /// The PC uses the object (0x441980 through the object entry point, TT7): null when it carries no trap now or no cast host is there;
    /// otherwise the meeting (<see cref="GameSessionOwner.TriggerTrap"/> with cause Use), whose <see cref="AreaTrapMeeting.Cancels"/>
    /// says whether the use script still runs.
    /// </summary>
    public AreaTrapMeeting? Use(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Meet(row, SessionTrapCause.Use);
    }

    /// <summary>
    /// The walk check before the PC steps onto <paramref name="next"/> (0x427110, TT10): every trap object there the player does not know
    /// gets a spot attempt (the detect flag, else the hero's spot trap roll); the first one spotted is marked and blocks the step, which ends
    /// the walk (EX20). Known traps are skipped.
    /// Nothing without a cast host; a mark the session refuses lets the step go (<see cref="AreaTrapWalk.Mark"/> says so).
    /// </summary>
    public AreaTrapWalk WalkCheck(CellCoord next) =>
        WalkCheckAs(TrapObserver.Player, next, (trapped, baseline, row, flags) => _session.RollHeroSpotTrap(_session.Version, trapped, baseline, flags, LightAt(row)));

    /// <summary>
    /// ARC-MAGIC-1189 (ledger TT10, SP2): the same walk check for an NPC or a companion that is about to step onto <paramref name="next"/> (0x427110 asks the
    /// mover, whatever it is): every trap object there the NPC does not know (<see cref="GameSessionOwner.NpcSpotObserver"/>: a free NPC knows every trap the PC did
    /// not place, a companion led by the PC the spotted and the placed ones) gets a spot attempt, the detect flag of the player's spells or the NPC's own roll
    /// (<see cref="GameSessionOwner.RollNpcSpotTrap"/>: its spot trap skill, the light at the trap, its powered shield); the first one spotted blocks the step. A companion
    /// marks the trap it spots, which the player knows from then on; a free NPC marks none, so it rolls again at its next meeting (TT4). Nothing without a cast host.
    /// </summary>
    public AreaTrapWalk WalkCheckNpc(SessionNpcSpot spot, CellCoord next)
    {
        ArgumentNullException.ThrowIfNull(spot);
        return WalkCheckAs(GameSessionOwner.NpcSpotObserver(spot), next,
            (trapped, baseline, row, flags) => _session.RollNpcSpotTrap(_session.Version, spot, trapped, baseline, flags, LightAt(row)));
    }

    private AreaTrapWalk WalkCheckAs(TrapObserver observer, CellCoord next, Func<SessionTrapped, SessionTrapBaseline, AreaObjectRecord, int, SessionSpotRoll> roll)
    {
        if (_effects() is not { } effects) return new(null, null, 0);
        var rolls = 0;
        foreach (var row in TrapObjectsOn(next))
        {
            if (_host.TrapBaseline(row) is not { } baseline) continue;
            var trapped = AreaUseScriptHost.Trapped(row);
            if (!_session.QueryTrap(trapped, baseline).Trapped || _session.TrapKnownTo(trapped, baseline, observer)) continue;
            switch (_session.TrapSpotGate(trapped, baseline, observer, effects.PlayerSpellFlags))
            {
                case TrapSpotGate.Detected:
                    // A mark the session refuses (a full budget) blocks nothing: the trap would stay unknown and block the tile for good.
                    var mark = _session.MarkTrapSpotted(_session.Version, trapped, baseline, observer);
                    return new(mark.Disposition == GameSessionDisposition.Rejected ? null : row, mark, rolls);
                case TrapSpotGate.Roll:
                    var spotRoll = roll(trapped, baseline, row, effects.PlayerSpellFlags);
                    if (spotRoll.Disposition == GameSessionDisposition.Rejected) break;
                    rolls++;
                    if (!spotRoll.Spotted) break;
                    var marked = _session.MarkTrapSpotted(_session.Version, trapped, baseline, observer);
                    return new(marked.Disposition == GameSessionDisposition.Rejected ? null : row, marked, rolls);
            }
        }
        return new(null, null, rolls);
    }

    /// <summary>
    /// The PC's step arrives on <paramref name="cell"/> (0x4305D0, TT9): the first trap object there meets the PC through the object entry
    /// point (cause Step: the PC's spell flag 0x2 or 0x4 steps over it; a known trap fires too). Null when the tile holds no trap object
    /// or no cast host is there; <see cref="AreaTrapMeeting.EndsWalk"/> says whether the walk ends on the tile.
    /// </summary>
    public AreaTrapMeeting? Step(CellCoord cell) => TrapObjectsOn(cell) is [var first, ..] ? Meet(first, SessionTrapCause.Step) : null;

    /// <summary>
    /// ARC-WORLD-1469 (ledger TT9, TT7): a non-hero critter's step arrives on <paramref name="cell"/> (0x4305D0 runs for every mover): the first trap object there meets it through the object entry
    /// point with the critter as the triggerer (cause Step: a critter whose spell flags hold 0x2 or 0x4 steps over it; a trap it knows fires too, a free NPC knowing every trap the PC did not place;
    /// one it does not know gets the entry point's spot attempt with its own roll, E2). A trap it spots now is marked on the PC's side (a companion) and the step is cancelled, as the hero's; any
    /// other fires, the critter as the triggerer of the native run. Null when the tile holds no trap object, no cast host is there, or the host cannot apply a critter's trap
    /// (<see cref="AreaTrapEffects.ApplyByCritter"/>). <see cref="AreaTrapMeeting.EndsWalk"/> says whether the critter's walk ends on the tile.
    /// </summary>
    public AreaTrapMeeting? StepNpc(AreaTrapWalker walker, CellCoord cell)
    {
        ArgumentNullException.ThrowIfNull(walker);
        return TrapObjectsOn(cell) is [var first, ..] ? Meet(first, SessionTrapCause.Step, walker) : null;
    }

    /// <summary>
    /// ARC-WORLD-1469 (ledger TT7, 0x441980 with point 1): a non-hero critter uses a trapped object (a door its opener goal pushes, node 6 of goal 12, 0x428A10): the meeting of the
    /// object's trap with the critter as the triggerer, as <see cref="Use"/>'s for the hero, with the critter's own spot attempt. Null when the object carries no trap now, no cast host is there or
    /// the host cannot apply a critter's trap; <see cref="AreaTrapMeeting.Cancels"/> says whether the use stops (the opener's run then ends).
    /// </summary>
    public AreaTrapMeeting? UseNpc(AreaObjectRecord row, AreaTrapWalker walker)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(walker);
        return Meet(row, SessionTrapCause.Use, walker);
    }

    /// <summary>
    /// ARC-DEV-260 (ledger OD4, OD6): the trap of an object whose hit points an attack or a damage brought to 0 goes off (0x4BCB00 before the
    /// break, cause <see cref="SessionTrapCause.Attack"/>: no spot attempt, an auto-removing trap stays). <paramref name="byHero"/> says the hero is
    /// the triggerer (a melee attack); a ranged attack or a damage with no attacker has none. Null when the object carries no trap now, or a native
    /// trap has no cast host to apply its effect (the trap is not consumed) or is the 30002 shot. An ordinary trap script (no native run) is a
    /// <see cref="SessionTrapOutcome.Fired"/> meeting without a run: the caller runs it (<see cref="AreaUseScriptHost.RunTrapScript"/>).
    /// </summary>
    public AreaTrapMeeting? Attack(AreaObjectRecord row, bool byHero)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_host.TrapBaseline(row) is not { } baseline) return null;
        var trapped = AreaUseScriptHost.Trapped(row);
        var state = _session.QueryTrap(trapped, baseline);
        if (!state.Trapped) return null;
        var native = TrapRules.NativeRun(state.Script, baseline.HeaderFlags);
        var effects = _effects();
        if (native is not null && (effects is null || TrapEffectRules.Of(state.Script, baseline.Counters) is { Kind: TrapEffectKind.Shot })) return null;
        var apply = byHero ? effects?.Apply : effects?.ApplyWithoutTriggerer;
        if (native is not null && apply is null) return null;
        var result = _session.TriggerTrap(_session.Version, trapped, baseline, byHero ? TrapObserver.Player : TrapObserver.NotCritter, SessionTrapCause.Attack, false);
        if (result.Outcome != SessionTrapOutcome.Fired || result.Run is not { Effect: not NativeTrapEffect.None } run) return new(row, SessionTrapCause.Attack, result, null);
        return new(row, SessionTrapCause.Attack, result, apply!(row, run, baseline.Counters, result.State.ObjectFlags));
    }

    /// <summary>The trap difficulty of a trap object (type 17, object field 0x134 = 308, slot 37; 0 when the row holds none), which the disarm check
    /// reads for a trap object only (ledger SP3); 0 for any other row.</summary>
    public static int TrapDifficulty(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Type == TrapObjectType && row.EffectiveFields.FirstOrDefault(field => field.Slot == TrapDifficultySlot) is { Presence: AreaPresence.Value, Number: { } n }
            && n is >= int.MinValue and <= int.MaxValue ? (int)n : 0;
    }

    private const int TrapDifficultySlot = 37;

    /// <summary>The trap objects the player knows on <paramref name="cell"/> that the session has not destroyed, in snapshot order.</summary>
    public IReadOnlyList<AreaObjectRecord> KnownTrapObjectsOn(CellCoord cell) =>
        TrapObjectsOn(cell).Where(KnownTrap).ToArray();

    /// <summary>Whether the player knows a trap on the row now (the disarm skill's only valid target, TT5).</summary>
    public bool KnownTrap(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_gone(row) || _host.TrapBaseline(row) is not { } baseline) return false;
        var trapped = AreaUseScriptHost.Trapped(row);
        var state = _session.QueryTrap(trapped, baseline);
        // The 30002 shot is not met here, so it is no target either (Disarm refuses it).
        return state.Trapped && _session.TrapKnownTo(trapped, baseline, TrapObserver.Player) && !IsShot(state, baseline);
    }

    private static bool IsShot(SessionTrapState state, SessionTrapBaseline baseline) =>
        TrapRules.NativeRun(state.Script, baseline.HeaderFlags) is not null && TrapEffectRules.Of(state.Script, baseline.Counters) is { Kind: TrapEffectKind.Shot };

    /// <summary>
    /// ARC-SKILLS-1156 (ledger SP3, TT5, TT6): the hero's disarm traps press on <paramref name="row"/> (a trap object, a trapped container, door or
    /// scenery): the session's roll with the trap object's difficulty (field 0x134) and the light at the row
    /// (<see cref="GameSessionOwner.RollHeroDisarmTrap"/>, which charges the press in a round); a sprung native trap's effect through the cast host
    /// (the hero as the triggerer, as a use's); an expert's device (<paramref name="insert"/>: the hero's insert check) created in the hero's pack.
    /// Null for a row that carries no trap the player knows, or without a cast host (a sprung trap could not take effect). The follower who would act
    /// instead (SP3) is not modelled.
    /// </summary>
    public AreaTrapDisarm? Disarm(AreaObjectRecord row, ISessionCombatWorld? combat, SessionInsertCheck insert)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(insert);
        if (_effects() is not { } effects || !KnownTrap(row) || _host.TrapBaseline(row) is not { } baseline) return null;
        var state = _session.QueryTrap(AreaUseScriptHost.Trapped(row), baseline);
        // A native trap the host cannot make take effect (the 30002 shot, no cast host) is not met, so it is not set off by a failure either.
        if (IsShot(state, baseline)) return null;
        var roll = _session.RollHeroDisarmTrap(_session.Version, AreaUseScriptHost.Trapped(row), baseline, TrapDifficulty(row), LightAt(row), combat);
        SessionTrapEffectResult? effect = null;
        if (roll.Disarm is { Outcome: TrapDisarm.Sprung, Run: { Effect: not NativeTrapEffect.None } run } sprung)
            effect = effects.Apply(row, run, baseline.Counters, sprung.State.ObjectFlags);
        InventoryMutationResult? device = null;
        if (roll.Disarm is { Outcome: TrapDisarm.RemovedWithDevice, DevicePrototype: > 0 and var prototype })
            device = _host.GiveHeroPrototypeItem(prototype, insert);
        AreaScriptedUse? script = null;
        if (roll.Disarm is { Outcome: TrapDisarm.Sprung, Run: null })
            script = _host.RunTrapScript(row, _host.Pc, _session.Rules.Scripts);
        return new(row, roll, effect, device) { Script = script };
    }

    private int LightAt(AreaObjectRecord row) => _light?.Invoke(row) ?? SessionPickLockTarget.FullLight;

    private AreaTrapMeeting? Meet(AreaObjectRecord row, SessionTrapCause cause, AreaTrapWalker? walker = null)
    {
        if (_effects() is not { } effects || _host.TrapBaseline(row) is not { } baseline) return null;
        // The triggerer: the hero (its roll, its effect), or a walker's own (ARC-WORLD-1469: its observer, its roll, its effect with it as the triggerer).
        var observer = walker is null ? TrapObserver.Player : walker.LedByPc ? TrapObserver.LedNpc : TrapObserver.FreeNpc;
        Func<AreaObjectRecord, NativeTrapRun, int, int, SessionTrapEffectResult>? apply = walker is null ? effects.Apply
            : effects.ApplyByCritter is { } by ? (r, run, counters, flags) => by(walker.Entity, r, run, counters, flags) : null;
        if (apply is null) return null;
        var trapped = AreaUseScriptHost.Trapped(row);
        var state = _session.QueryTrap(trapped, baseline);
        if (!state.Trapped) return null;
        var native = TrapRules.NativeRun(state.Script, baseline.HeaderFlags);
        if (native is null && cause == SessionTrapCause.Step || TrapEffectRules.Of(state.Script, baseline.Counters) is { Kind: TrapEffectKind.Shot }) return null;
        // The spot attempt (E2, TT3): the player's detect-traps flag spots without a roll, else the triggerer's own spot trap roll (SP1, SP2).
        var gate = _session.TrapSpotGate(trapped, baseline, observer, effects.PlayerSpellFlags);
        var moverFlags = walker is null ? effects.PlayerSpellFlags : effects.SpellFlagsOf?.Invoke(walker.Entity) ?? 0;
        // A step with the step-over spell flags never reaches the entry point, so it never rolls (0x4305D0 before 0x441980).
        var rolls = gate == TrapSpotGate.Roll && (cause != SessionTrapCause.Step || TrapRules.StepTriggers(moverFlags));
        var spotted = gate == TrapSpotGate.Detected || rolls && (walker is null
            ? _session.RollHeroSpotTrap(_session.Version, trapped, baseline, effects.PlayerSpellFlags, LightAt(row)).Spotted
            : walker.Spot is { } spot && _session.RollNpcSpotTrap(_session.Version, spot, trapped, baseline, effects.PlayerSpellFlags, LightAt(row)).Spotted);
        var result = _session.TriggerTrap(_session.Version, trapped, baseline, observer, cause, spotted, cause == SessionTrapCause.Step ? moverFlags : 0);
        if (result.Outcome != SessionTrapOutcome.Fired || result.Run is not { Effect: not NativeTrapEffect.None } run) return new(row, cause, result, null);
        return new(row, cause, result, apply(row, run, baseline.Counters, result.State.ObjectFlags));
    }
}
