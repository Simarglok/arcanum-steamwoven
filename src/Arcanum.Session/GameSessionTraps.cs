using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>An object that may carry a trap: its runtime map and its session entity id (a trap object or a trapped object).</summary>
public readonly record struct SessionTrapped(SessionMapId Map, string Entity);

/// <summary>
/// The T0 trap facts of one object, read by the host from its map data: whether it is a trap object (type 17) or another
/// object, its use script (slot 1: number and counters), the script file's header flags and the object flags (field 0x13,
/// of which the trap state reads the PC-placed and spotted bits).
/// </summary>
public readonly record struct SessionTrapBaseline(TrapCarrier Carrier, int Script, uint HeaderFlags, int Counters, int ObjectFlags);

/// <summary>An object's current trap state: its trap class (None once removed), its use script's number (0 once removed),
/// its object flags with the spotted mark, and whether the trap was removed (a removed trap object is destroyed).</summary>
public readonly record struct SessionTrapState(TrapClass Class, int Script, int ObjectFlags, bool Removed)
{
    public bool Trapped => Class != TrapClass.None;
    public bool Spotted => (ObjectFlags & TrapRules.SpottedFlag) != 0;
    public bool PlacedByPc => (ObjectFlags & TrapRules.PlacedByPcFlag) != 0;
}

/// <summary>How a trap meets its triggerer (ledger TT7–TT9).</summary>
public enum SessionTrapCause
{
    /// <summary>A use of the object (the object entry point at the use point): the spot attempt first, then the run, whose
    /// auto-removal is written back.</summary>
    Use,
    /// <summary>A mover arrives on the trap object's tile: as a use, unless the mover's spell flags keep it off the trap.</summary>
    Step,
    /// <summary>An attack brings the object to 0 hit points (ledger OD4): the run with no spot attempt and no write-back,
    /// so an auto-removing trap stays.</summary>
    Attack,
}

/// <summary>What one trap meeting did.</summary>
public enum SessionTrapOutcome
{
    /// <summary>The object carries no trap (or it was removed): the use goes on as an ordinary one.</summary>
    NoTrap,
    /// <summary>A step by a mover with spell flag 0x2 or 0x4: nothing runs.</summary>
    SteppedOver,
    /// <summary>The triggerer spotted the trap now: it is marked when the triggerer is on the PC's side, and the use is
    /// cancelled (skip default).</summary>
    Spotted,
    /// <summary>The trap went off: a native run (<see cref="SessionTrapResult.Run"/>), or an ordinary script the host runs
    /// through the dispatcher when the run is null.</summary>
    Fired,
}

/// <summary>One trap meeting: the session's mutation, the outcome, the native run (null for an ordinary trap script), the
/// trap's state after it, and whether the player gets the spotted line (<c>mes\trap.mes</c> line 0, ledger TT4). A rejected
/// mutation carries outcome NoTrap and no run: nothing happened.</summary>
public sealed record SessionTrapResult(GameSessionMutationResult Mutation, SessionTrapOutcome Outcome, NativeTrapRun? Run, SessionTrapState State,
    bool PlayerLine = false);

/// <summary>One disarm attempt: the session's mutation, the outcome, the device prototype it yields (0: none; the item is
/// the caller's to create in the disarmer's inventory), the run of a sprung trap and the trap's state after it. A rejected
/// mutation carries outcome NotTrapped, no device and no run: nothing happened.</summary>
public sealed record SessionTrapDisarmResult(GameSessionMutationResult Mutation, TrapDisarm Outcome, int DevicePrototype, NativeTrapRun? Run,
    SessionTrapState State);

/// <summary>One spot trap roll of the hero (ledger SP1): the session's answer, the shared check (null when refused before the roll), whether
/// the hero spotted the trap (the roll passed; the caller marks it), and whether the master's second roll was drawn.</summary>
public sealed record SessionSpotRoll(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, bool Spotted,
    bool SecondRoll, GameSessionVersion Version);

/// <summary>ARC-MAGIC-1189: an NPC or a companion that spots a trap: its life target (an NPC), its advancement sheet and its effects' modifiers (its skills), and whether the PC
/// leads it (a companion: it knows spotted traps and marks the ones it spots; a free NPC knows every trap the PC did not place and marks none).</summary>
public sealed record SessionNpcSpot(SessionLifeTarget Spotter, SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers, bool LedByPc);

/// <summary>One disarm traps press of the hero (ledger SP3): the session's answer, the turn-based charge of the press (null outside a round), the
/// shared check (null when refused or gated before the roll), whether the master's second roll was drawn, whether the PC's fate point was used,
/// and the disarm itself (<see cref="SessionTrapDisarmResult"/>: its outcome is NotTrapped or NotKnown for a trap nothing was rolled for).</summary>
public sealed record SessionDisarmRoll(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCombatResult? Charge, SkillCheckResult? Check,
    bool SecondRoll, bool Fated, SessionTrapDisarmResult? Disarm, GameSessionVersion Version);

/// <summary>
/// ARC-WORLD-076: the trap state owner of trap objects and trapped objects on any map (ledger TT1–TT10), for the spot
/// trap roll (<see cref="RollHeroSpotTrap"/>, ARC-MAGIC-057), the disarm skill (S10), the trap devices (ARC-TECHNOLOGY-011/012) and the hosts' use and step paths. An object's trap is
/// its T0 baseline until a spot mark or a removal; each write records the object's trap state as one map change of its
/// entity (<see cref="TrapStateProperty"/>: bit 1 spotted, bit 2 removed, bit 4 placed by the PC (ARC-WORLD-125), and above bit 8 the script number the removal
/// cleared), which the snapshot already carries, so the domain adds no schema. A removal holds only while the baseline still
/// names that script, so a trap armed again later (a trap device, TP2) is seen; the spotted bit stays, as the original keeps
/// object flag 0x2000000 through a removal (TT5). A removed trap object is also recorded destroyed (<see cref="ObjectConditionProperty"/>). The rolls are
/// the caller's (the spot trap and disarm traps skills); the native runs' effects (spell and attacks, 0x4BCDC0) are a later
/// slice: the session returns the run.
/// </summary>
public sealed partial class GameSessionOwner
{
    public const string TrapStateProperty = "trap.state";
    private const long TrapSpottedBit = 1, TrapRemovedBit = 2, TrapPlacedBit = 4;
    private const int TrapScriptShift = 8;

    /// <summary>The object's current trap state: its recorded changes over its baseline.</summary>
    public SessionTrapState QueryTrap(SessionTrapped trapped, SessionTrapBaseline baseline)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline)) throw new ArgumentException("Invalid trapped object or baseline.", nameof(trapped));
        return TrapState(trapped, baseline);
    }

    /// <summary>Whether the observer knows the object's trap (ledger TT2); false when it carries none.</summary>
    public bool TrapKnownTo(SessionTrapped trapped, SessionTrapBaseline baseline, TrapObserver observer)
    {
        var state = QueryTrap(trapped, baseline);
        return state.Trapped && TrapRules.Known(observer, state.ObjectFlags);
    }

    /// <summary>The gate of a spot attempt on the object (ledger TT3): whether the observer's roll is needed at all.</summary>
    public TrapSpotGate TrapSpotGate(SessionTrapped trapped, SessionTrapBaseline baseline, TrapObserver observer, int playerSpellFlags)
    {
        var state = QueryTrap(trapped, baseline);
        return TrapRules.SpotGate(observer, false, state.Class, state.ObjectFlags, playerSpellFlags);
    }

    /// <summary>
    /// The hero's spot trap roll on an object whose trap it does not know (0x4BBE40's roll branch, ledger TT3, SP1 and SP2): the skill's shared
    /// check (<see cref="SkillCheckRules.Run"/>: the hero's spot trap level and training, the light at the trap, the hero's injuries, and the item
    /// modifier of the powered shield, <see cref="TrapRules.SpotItemModifier"/>) and the skill's own case (<see cref="TrapRules.SpotCase"/>: the master's
    /// second roll, no spotting of a magical trap below expert). Nothing is written but the generator's draws; the caller marks the trap
    /// (<see cref="MarkTrapSpotted"/>) when <see cref="SessionSpotRoll.Spotted"/>.
    /// Rolled only when the gate asks for a roll (<see cref="TrapSpotGate"/> answers Roll for the player with <paramref name="playerSpellFlags"/>);
    /// any other gate answers NoChange without a draw. The light at the trap is the caller's (ARC-WORLD-1149: the viewer passes the light at the trap
    /// object's cell; full light by default). The item modifier is the session's: the hero's worn item at location 1005 named 2866, its charges (ammunition
    /// type 2) and its aptitude. Refused: no hero or a dead one
    /// (<see cref="GameSessionFailure.SkillsUnavailable"/>), an invalid object or light (<see cref="GameSessionFailure.InvalidTrap"/>), the
    /// version, the generator or the revision exhausted.
    /// </summary>
    public SessionSpotRoll RollHeroSpotTrap(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline, int playerSpellFlags = 0,
        int light = SessionPickLockTarget.FullLight)
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead()) return RejectSpot(GameSessionFailure.SkillsUnavailable);
        if (!ValidTrapped(trapped, baseline) || light is < 0 or > SessionPickLockTarget.FullLight) return RejectSpot(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return RejectSpot(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (TrapRules.SpotGate(TrapObserver.Player, false, state.Class, state.ObjectFlags, playerSpellFlags) != Arcanum.Simulation.Traps.TrapSpotGate.Roll)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, false, false, Version);
        if (_revision == long.MaxValue) return RejectSpot(GameSessionFailure.RevisionExhausted);
        var request = HeroSkillRequest(Skill.SpotTrap, null);
        request = request with { Difficulty = request.Difficulty with { Light = light, Modifier = HeroSpotItemModifier() } };
        return FinishSpotRoll(request, state);
    }

    /// <summary>
    /// ARC-MAGIC-1189 (ledger SP2): an NPC's or a companion's own spot trap roll on an object whose trap it does not know (the same branch 0x4BBE40 and the
    /// same run 0x4C7160 with the NPC as the source): the shared check with the NPC's own spot trap level and training (its sheet and effects' modifiers),
    /// the light at the trap, its blindness and crippled limbs, and the item modifier of the powered shield it wears. No fate and no difficulty-setting term (the
    /// local PC's only). Rolled only when the gate asks for one for the spot's observer (<see cref="NpcSpotObserver"/>: a free NPC knows every trap the
    /// PC did not place; a companion led by the PC knows the spotted and the placed ones; the player's Detect Traps flag spots without a roll). Nothing is written but
    /// the generator's draws; the caller marks the trap (<see cref="MarkTrapSpotted"/> with the same observer; a free NPC marks nothing, so it rolls again at its next
    /// meeting, TT4). Refused as the hero's: a spotter that is the hero, whose sheet or modifiers do not hold, that is not the sheet's NPC or is dead
    /// (<see cref="GameSessionFailure.SkillsUnavailable"/>), an invalid object or light (<see cref="GameSessionFailure.InvalidTrap"/>), the version, the generator or the revision.
    /// </summary>
    public SessionSpotRoll RollNpcSpotTrap(GameSessionVersion expected, SessionNpcSpot spot, SessionTrapped trapped, SessionTrapBaseline baseline, int playerSpellFlags = 0,
        int light = SessionPickLockTarget.FullLight)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(spot);
        ArgumentNullException.ThrowIfNull(spot.Spotter);
        ArgumentNullException.ThrowIfNull(spot.Loaded);
        ArgumentNullException.ThrowIfNull(spot.Modifiers);
        if (spot.Spotter.IsHero || spot.Loaded.Entity != spot.Spotter.Entity || !AutoLevelRules.IsValid(spot.Loaded.Sheet) || !AutoLevelRules.IsValid(spot.Modifiers))
            return RejectSpot(GameSessionFailure.SkillsUnavailable);
        if (Resolve(spot.Spotter, out var npc, out var facts, out _) != GameSessionFailure.None || LifeRules.Evaluate(npc.State, facts!).IsDead)
            return RejectSpot(GameSessionFailure.SkillsUnavailable);
        if (!ValidTrapped(trapped, baseline) || light is < 0 or > SessionPickLockTarget.FullLight) return RejectSpot(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return RejectSpot(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (TrapRules.SpotGate(NpcSpotObserver(spot), false, state.Class, state.ObjectFlags, playerSpellFlags) != Arcanum.Simulation.Traps.TrapSpotGate.Roll)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, false, false, Version);
        if (_revision == long.MaxValue) return RejectSpot(GameSessionFailure.RevisionExhausted);
        var sheet = _advancement.Npcs.TryGetValue(spot.Loaded.Entity, out var levelled) ? levelled.Sheet : spot.Loaded.Sheet;
        var skill = QueryNpcSkills(spot.Loaded, spot.Modifiers)[Skill.SpotTrap];
        var aptitude = Math.Clamp(AutoLevelRules.DerivedStat(sheet, spot.Modifiers, CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var request = new SkillCheckRequest
        {
            Skill = Skill.SpotTrap,
            Level = skill.Level,
            Training = skill.Training,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = AutoLevelRules.EffectiveStat(sheet, spot.Modifiers, CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
                Difficulty = _difficulty,
            },
            Difficulty = new()
            {
                SourceInjuries = SourceInjuries(npc.State.Conditions),
                Light = light,
                Modifier = TrapRules.SpotItemModifier(NpcItemNameAt(spot.Spotter.Entity!, TrapRules.ShieldLocation), NpcStackOfType(spot.Spotter.Entity!, TrapRules.ChargeAmmoType)?.Units ?? 0, aptitude),
            },
        };
        return FinishSpotRoll(request, state);
    }

    /// <summary>The observer an NPC's spot attempt is made as (ledger TT2): a companion led by the PC, or an NPC without a PC leader.</summary>
    public static TrapObserver NpcSpotObserver(SessionNpcSpot spot) => spot.LedByPc ? TrapObserver.LedNpc : TrapObserver.FreeNpc;

    // The hero's modifier of the spot invocation (SP2): its worn item at 1005, its charges of ammunition type 2, its aptitude (stat 16).
    private int HeroSpotItemModifier()
    {
        var worn = _equipment.Capture().Worn.FirstOrDefault(item => item.Slot == EquipmentSlot.Shield)?.Entity;
        var name = worn is null ? 0 : HeroHeld().Where(item => item.Entity == worn).Select(item => item.Kind.Name).FirstOrDefault();
        return TrapRules.SpotItemModifier(name, HeroStackOfType(TrapRules.ChargeAmmoType)?.Units ?? 0, HeroAptitude());
    }

    // The name of the item an NPC's list holds at an inventory location as the session's lists show it (a dressed NPC's record over its map data, ARC-WORLD-103); 0 for none.
    private int NpcItemNameAt(string npc, int location)
    {
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc)
                foreach (var entity in holder.Items)
                    if (!_inventory.Taken.Contains(entity) && !Dropped(entity) && _inventory.Worn(npc, entity, seed.Items[entity].Facts).InventoryLocation == location)
                        return seed.Items[entity].Facts.Kind.Name;
        foreach (var item in _inventory.AddedTo(npc))
            if (_inventory.Worn(npc, item.Entity, InventoryDomain.MadeFacts(item.Kind)).InventoryLocation == location) return item.Kind.Name;
        return 0;
    }

    // The shared end of a spot roll: the check and the skill's own case from a trial copy of the generator, which the session keeps (one revision).
    private SessionSpotRoll FinishSpotRoll(SkillCheckRequest request, SessionTrapState state)
    {
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        bool spotted, second = false;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            (spotted, _) = TrapRules.SpotCase(check.Success, check.Critical, request.Training, state.Class, check.Check.Effectiveness, check.Check.Difficulty,
                () => { second = true; return rolls.D100(SkillCheckRoll.Check); });
        }
        catch (SkillRolls.ExhaustedException) { return RejectSpot(GameSessionFailure.RandomExhausted); }
        _random = rolls.Trial;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, spotted, second, Version);
    }

    private SessionSpotRoll RejectSpot(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, false, false, Version);

    /// <summary>Marks the trap spotted by the observer (ledger TT4): only the player or an NPC led by a PC marks it. One
    /// revision; NoChange when the object carries no trap, is marked already or the observer marks nothing.</summary>
    public GameSessionMutationResult MarkTrapSpotted(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline,
        TrapObserver observer)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline) || !Enum.IsDefined(observer)) return Reject(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (!state.Trapped) return NoChange();
        var mark = TrapRules.MarkSpotted(observer, baseline.Carrier, state.ObjectFlags);
        if (!mark.Marked || state.Spotted) return NoChange();
        return ApplyChanges(expected, null, TrapEdits(trapped, baseline, RecordedTrap(trapped, baseline) | TrapSpottedBit));
    }

    /// <summary>
    /// The object's trap meets a triggerer (ledger TT7–TT9). <paramref name="spottedNow"/> is the caller's spot attempt on a
    /// trap the triggerer does not know (the gate of <see cref="TrapSpotGate"/> and the spot trap roll); it is read only for a
    /// use or step. A use or step whose triggerer spots the trap now marks it (on the PC's side) and is cancelled; otherwise the
    /// trap fires: a native script's run, whose auto-removal clears the trap (a trap object is destroyed), or an ordinary trap
    /// script the host runs. An attack fires the trap and never removes it. A step must name a trap object; a non-critter
    /// spots nothing (TT3). One revision when the state changes; a rejected write reports nothing done.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4BCB00, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionTrapResult TriggerTrap(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline,
        TrapObserver triggerer, SessionTrapCause cause, bool spottedNow, int moverSpellFlags = 0)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline) || !Enum.IsDefined(cause) || !Enum.IsDefined(triggerer) ||
            cause == SessionTrapCause.Step && baseline.Carrier != TrapCarrier.TrapObject)
            return TrapRejected(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return TrapRejected(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (cause == SessionTrapCause.Step && !TrapRules.StepTriggers(moverSpellFlags)) return new(NoChange(), SessionTrapOutcome.SteppedOver, null, state);
        if (!state.Trapped) return new(NoChange(), SessionTrapOutcome.NoTrap, null, state);
        if (cause != SessionTrapCause.Attack && spottedNow && triggerer != TrapObserver.NotCritter && !TrapRules.Known(triggerer, state.ObjectFlags))
        {
            var marked = MarkTrapSpotted(expected, trapped, baseline, triggerer);
            if (marked.Disposition == GameSessionDisposition.Rejected) return TrapRejected(marked);
            return new(marked, SessionTrapOutcome.Spotted, null, TrapState(trapped, baseline),
                TrapRules.MarkSpotted(triggerer, baseline.Carrier, state.ObjectFlags).PlayerMessage);
        }
        return Fire(expected, trapped, baseline, state, cause);
    }

    /// <summary>
    /// A disarm attempt on the object (ledger TT5, TT6) from the caller's disarm traps result (<paramref name="success"/>,
    /// <paramref name="critical"/>) and the disarmer's training in it. Success removes the trap (a trap object is destroyed);
    /// an expert's critical success also returns its device prototype; a critical failure sets it off as a use would (the
    /// trap is known, so no spot). Training is 0..3 (none to master). One revision when the state changes; a rejected write
    /// reports nothing done.
    /// </summary>
    public SessionTrapDisarmResult DisarmTrap(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline,
        TrapObserver disarmer, bool success, bool critical, int disarmTraining)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline) || !Enum.IsDefined(disarmer) || disarmTraining is < 0 or > 3)
            return new(Reject(GameSessionFailure.InvalidTrap), TrapDisarm.NotTrapped, 0, null, default);
        if (!IsCurrent(expected)) return new(Reject(GameSessionFailure.StaleVersion), TrapDisarm.NotTrapped, 0, null, default);
        var state = TrapState(trapped, baseline);
        var device = TrapRules.DevicePrototype(state.Script, state.ObjectFlags, baseline.Counters);
        var outcome = TrapRules.Disarm(state.Class, state.Trapped && TrapRules.Known(disarmer, state.ObjectFlags), success, critical,
            disarmTraining, device);
        switch (outcome)
        {
            case TrapDisarm.Removed or TrapDisarm.RemovedWithDevice:
                var removed = Remove(expected, trapped, baseline, state);
                if (removed.Disposition == GameSessionDisposition.Rejected) return new(removed, TrapDisarm.NotTrapped, 0, null, default);
                return new(removed, outcome, outcome == TrapDisarm.RemovedWithDevice ? device : 0, null, TrapState(trapped, baseline));
            case TrapDisarm.Sprung:
                var fired = Fire(expected, trapped, baseline, state, SessionTrapCause.Use);
                if (fired.Mutation.Disposition == GameSessionDisposition.Rejected) return new(fired.Mutation, TrapDisarm.NotTrapped, 0, null, default);
                return new(fired.Mutation, outcome, 0, fired.Run, fired.State);
            default:
                return new(NoChange(), outcome, 0, null, state);
        }
    }

    /// <summary>The bound of a trap's difficulty (object field 0x134) the session takes.</summary>
    public const int MaxTrapDifficulty = 1_000;

    /// <summary>
    /// ARC-SKILLS-1156 (ledger SP3, E6-a second half): the hero's disarm traps press on an object whose trap it knows (the skill bar's flag-4
    /// invocation of the shared skill run's disarm case, then the trap disarm, ledger SP3 and TT5). The press's own charge comes first (a PC in a turn-based round pays 4 action points,
    /// <paramref name="world"/>: too few end its subturn and refuse the press as <see cref="GameSessionFailure.DisarmOutOfTurn"/>, as the
    /// pick pocket's), then the skill's shared check (<see cref="SkillCheckRules.Run"/>: the hero's level and training, the light at the trap, its
    /// injuries and the trap's difficulty <paramref name="trapDifficulty"/>, which the check reads only for a trap object, SP3: pass 0 for another
    /// carrier), the skill's own case (<see cref="TrapRules.DisarmCase"/>: the master's second roll) and the disarm (<see cref="DisarmTrap"/>:
    /// success removes the trap, an expert's critical success yields the device prototype the caller creates in the hero's pack, a critical failure
    /// sets it off). The rolls, the PC's fate point and the disarm are one revision (the charge has its own). A trap that is gone, or that the hero does
    /// not know, is NoChange with nothing charged or rolled (TT5: no trap there to disarm). NOT READY: the follower who acts instead (SP3). Refused:
    /// no hero or a dead one (<see cref="GameSessionFailure.SkillsUnavailable"/>), an invalid object, difficulty or light
    /// (<see cref="GameSessionFailure.InvalidTrap"/>), the version, the generator or the revision exhausted, a budget the removal cannot take.
    /// </summary>
    public SessionDisarmRoll RollHeroDisarmTrap(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline, int trapDifficulty = 0,
        int light = SessionPickLockTarget.FullLight, ISessionCombatWorld? world = null)
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead()) return RejectDisarm(GameSessionFailure.SkillsUnavailable);
        if (!ValidTrapped(trapped, baseline) || light is < 0 or > SessionPickLockTarget.FullLight || trapDifficulty is < -MaxTrapDifficulty or > MaxTrapDifficulty)
            return RejectDisarm(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return RejectDisarm(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (!state.Trapped || !TrapRules.Known(TrapObserver.Player, state.ObjectFlags))
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, false, false,
                new(NoChange(), state.Trapped ? TrapDisarm.NotKnown : TrapDisarm.NotTrapped, 0, null, state), Version);
        if (_revision >= long.MaxValue - 1) return RejectDisarm(GameSessionFailure.RevisionExhausted);
        SessionCombatResult? charge = null;
        if (world is not null && _combat.Round is not null)
        {
            // The press needs the hero's turn (as the pick pocket's goal): another's subturn or an inactive hero is refused before any charge.
            if (QueryCombatActionGate(SessionCombatant.Hero, TurnBasedRules.UseSkillCost) is SessionCombatGate.NotTheirTurn or SessionCombatGate.Inactive)
                return RejectDisarm(GameSessionFailure.DisarmOutOfTurn);
            charge = ConsumeActionPoints(Version, SessionCombatant.Hero, TurnBasedRules.UseSkillCost, world);
            if (charge.Disposition == GameSessionDisposition.Rejected) return RejectDisarm(charge.Failure) with { Charge = charge };
            if (charge.Charge == SessionActionPointUse.Refused) return RejectDisarm(GameSessionFailure.DisarmOutOfTurn) with { Charge = charge };
            // The charge moved the version; the press goes on from there.
            expected = Version;
        }
        var request = HeroSkillRequest(Skill.DisarmTraps, null);
        request = request with
        {
            Difficulty = request.Difficulty with
            {
                Light = light,
                TargetTrap = baseline.Carrier == TrapCarrier.TrapObject,
                TrapDifficulty = baseline.Carrier == TrapCarrier.TrapObject ? trapDifficulty : 0,
            },
        };
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        bool success, critical, second = false;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            (success, critical) = TrapRules.DisarmCase(check.Success, check.Critical, request.Training, check.Check.Effectiveness, check.Check.Difficulty,
                () => { second = true; return rolls.D100(SkillCheckRoll.Check); });
        }
        catch (SkillRolls.ExhaustedException) { return RejectDisarm(GameSessionFailure.RandomExhausted) with { Charge = charge }; }
        var (savedRandom, savedState) = (_random, _hero.State);
        _random = rolls.Trial;
        if (check.Fated && FateRules.Of(SkillCheckRules.Fate(Skill.DisarmTraps)) is { } fate) _hero.Apply(_hero.State.WithFate(FateRules.Use(_hero.State.Fate, fate).After));
        var disarm = DisarmTrap(expected, trapped, baseline, TrapObserver.Player, success, critical, (int)request.Training);
        if (disarm.Mutation.Disposition == GameSessionDisposition.Rejected)
        {
            // The removal could not be written: the rolls and the fate point go back, nothing happened.
            _random = savedRandom;
            _hero.Apply(savedState);
            return RejectDisarm(disarm.Mutation.Failure) with { Charge = charge };
        }
        // A failed disarm writes nothing, but the rolls were drawn: the attempt is one revision.
        if (disarm.Mutation.Disposition == GameSessionDisposition.NoChange) Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, charge, check, second, check.Fated, disarm, Version);
    }

    private SessionDisarmRoll RejectDisarm(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, false, false, null, Version);

    private SessionTrapResult Fire(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline, SessionTrapState state,
        SessionTrapCause cause)
    {
        var run = TrapRules.NativeRun(state.Script, baseline.HeaderFlags);
        // The object entry point writes the cleared number back (E3); the attack's direct trigger (0x4BCB00) loses it (OD6).
        if (run is not { AutoRemoves: true } || cause == SessionTrapCause.Attack) return new(NoChange(), SessionTrapOutcome.Fired, run, state);
        var removed = Remove(expected, trapped, baseline, state);
        if (removed.Disposition == GameSessionDisposition.Rejected) return TrapRejected(removed);
        return new(removed, SessionTrapOutcome.Fired, run, TrapState(trapped, baseline));
    }

    // The removal (0x4BC220, and the entry point's write-back of a cleared number, E3): the removed bit with the script it
    // cleared; a trap object is destroyed. The spotted bit stays.
    [Arcanum.Simulation.OriginalFunction(0x4BC220, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    private GameSessionMutationResult Remove(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline, SessionTrapState state) =>
        ApplyChanges(expected, null, TrapEdits(trapped, baseline,
            (state.Spotted ? TrapSpottedBit : 0) | (RecordedTrap(trapped, baseline) & TrapPlacedBit) | TrapRemovedBit | (long)baseline.Script << TrapScriptShift));

    private SessionTrapResult TrapRejected(GameSessionFailure failure) => TrapRejected(Reject(failure));

    private static SessionTrapResult TrapRejected(GameSessionMutationResult rejected) => new(rejected, SessionTrapOutcome.NoTrap, null, default);

    private SessionTrapState TrapState(SessionTrapped trapped, SessionTrapBaseline baseline)
    {
        var recorded = RecordedTrap(trapped, baseline);
        var flags = (recorded & TrapSpottedBit) != 0 ? TrapRules.MarkSpotted(TrapObserver.Player, baseline.Carrier, baseline.ObjectFlags).ObjectFlags
            : baseline.ObjectFlags;
        if ((recorded & TrapPlacedBit) != 0) flags |= TrapRules.PlacedByPcFlag;
        return (recorded & TrapRemovedBit) != 0 ? new(TrapClass.None, 0, flags, true)
            : new(TrapRules.Classify(baseline.Script, baseline.HeaderFlags), baseline.Script, flags, false);
    }

    // The recorded trap bits: 0 when none or when a crafted save holds a value outside them; a removal of another script than
    // the baseline's no longer counts (the object was armed again), its spotted bit does.
    private long RecordedTrap(SessionTrapped trapped, SessionTrapBaseline baseline)
    {
        if (_maps.Get(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, TrapStateProperty)) is not { } value || value < 0) return 0;
        var bits = value & ((1L << TrapScriptShift) - 1);
        var script = value >> TrapScriptShift;
        if (bits > (TrapSpottedBit | TrapRemovedBit | TrapPlacedBit) || script > Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber ||
            (bits & TrapRemovedBit) == 0 && script != 0) return 0;
        return (bits & TrapRemovedBit) != 0 && script != baseline.Script ? bits & (TrapSpottedBit | TrapPlacedBit) : bits;
    }

    private static MapChangeEdit[] TrapEdits(SessionTrapped trapped, SessionTrapBaseline baseline, long bits) =>
        baseline.Carrier == TrapCarrier.TrapObject && (bits & TrapRemovedBit) != 0
            ?
            [
                new(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, TrapStateProperty), bits),
                new(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, ObjectConditionProperty), (long)SessionObjectCondition.Destroyed),
            ]
            : [new(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, TrapStateProperty), bits)];

    private static bool ValidTrapped(SessionTrapped trapped, SessionTrapBaseline baseline) =>
        SessionValidation.Map(trapped.Map) && SessionValidation.Entity(trapped.Entity) && trapped.Entity != SessionEventKinds.AbsentRole &&
        Enum.IsDefined(baseline.Carrier) && baseline.Script is >= 0 and <= Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber;
}
