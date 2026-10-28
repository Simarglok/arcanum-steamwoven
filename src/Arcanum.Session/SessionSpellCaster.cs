using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// An NPC on the shown map as a cast reads it (ARC-MAGIC-014): its combat values (life target, facts, combatant, stats; ARC-COMBAT-005)
/// and the stored fields the magic rules read besides — its stored magic resistance (resistance 4, no armour or effects: O9) and its
/// object, spell and critter flags.
/// </summary>
public sealed record SpellCastCritter(SessionCritterCombat Combat, int StoredMagicResistance, int ObjectFlags, int SpellFlags, int CritterFlags,
    int CritterFlags2);

/// <summary>
/// The map a cast happens on (ARC-MAGIC-014), for any map: the target functions' questions (<see cref="ISpellTargetWorld"/>, MT8; the
/// hero is the object <see cref="SessionSpellCaster.Hero"/>), the line of fire from a parent to a tile (the I2 port, 0x4ADE00), the NPCs'
/// combat values and stored fields, the weapon an NPC wields, the items a critter wears (the acid) and an object's damage step (the
/// host's object rules; an object that cannot take damage answers null).
/// </summary>
public interface ISpellCastMap : ISpellTargetWorld
{
    SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile);

    SpellCastCritter? Critter(string entity);

    CombatWeapon WieldedWeapon(string entity);

    IReadOnlyList<SpellAcidItem> WornItems(string entity);

    /// <summary>A non-critter object's combat values and kind for the damage step, or null when it takes none.</summary>
    (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id);

    /// <summary>Applies a step's damage to a non-critter object (its object rules); false when that is not ready.</summary>
    bool DamageObject(string id, AttackDamage damage);
}

/// <summary>What a cast left for the host to show or do: the run note's kind and run, the local player's spell.mes message, the
/// component types that are not ready yet, and the objects a component touched.</summary>
/// <summary>One entry of the cast host's log; <see cref="Detail"/> carries an eye candy's <c>Play:</c> flags (ARC-MAGIC-017).</summary>
public sealed record SpellCastEvent(SpellCastEventKind Kind, int Run, int Value = 0, string? Object = null, int Detail = 0);

/// <summary>How a casting goal's start went (<see cref="SessionSpellCaster.StartGoal"/>): it started, or it ends because the parent's
/// action points refused, because the source cannot afford the cast, because the target is destroyed or off, because the caster
/// cannot turn to its target (ARC-MAGIC-018: stoned, stunned or paralysed), or because there is no such run.</summary>
public enum SpellCastGoalOutcome { Started, NoActionPoints, CannotAfford, TargetGone, CannotTurn, NoRun }

/// <summary>A casting goal's start and its action-point charge (null when the parent is not a combatant). ARC-MAGIC-018:
/// <see cref="Facing"/> is the facing turn's answer for a started goal (null when the run has no parent).</summary>
public sealed record SpellCastGoalStart(SpellCastGoalOutcome Outcome, SessionCombatResult? ActionPoints)
{
    public SpellFacing? Facing { get; init; }
}

/// <summary>How the spell UI's cast at a target went (<see cref="SessionSpellCaster.CastAt"/>): nothing (a missing, dead or
/// unconscious caster), the caster cannot afford it (spell.mes 600), no line of fire (604), the run was refused (the invocation
/// check or the session: <see cref="SpellCastAttempt.Run"/>), or the run started and waits for its casting goal.</summary>
public enum SpellCastAttemptOutcome { Nothing, CannotAfford, NoLineOfFire, Refused, Started }

/// <summary>The spell UI's cast: its outcome, the spell.mes message the UI showed, the run start and the started run's slot.</summary>
public sealed record SpellCastAttempt(SpellCastAttemptOutcome Outcome, int? Message = null, SessionSpellRunResult? Run = null, int? RunId = null);

public enum SpellCastEventKind
{
    /// <summary>spell.mes <see cref="SpellCastEvent.Value"/> for the local player (601 maintenance, 602 checks or a shelled source,
    /// 603 a shelled target, 643 a resisting target).</summary>
    Message,
    /// <summary>A payment charged its payer this much fatigue.</summary>
    FatiguePaid,
    /// <summary>A Damage component's step on <see cref="SpellCastEvent.Object"/> dealt this many hit points.</summary>
    Damaged,
    /// <summary>A Heal component healed <see cref="SpellCastEvent.Object"/> (the value is the hit points regained).</summary>
    Healed,
    /// <summary>A component of a type no slice implements yet (the value is its <see cref="SpellComponentType"/>) did nothing.</summary>
    ComponentNotReady,
    /// <summary>A target resisted.</summary>
    Resisted,
    /// <summary>The run was freed.</summary>
    Freed,
    /// <summary>ARC-MAGIC-017: an EyeCandy component's Add (0x451FE0) played eye-candy entry <see cref="SpellCastEvent.Value"/> (the
    /// table index, 6 · spell + the component's number) on <see cref="SpellCastEvent.Object"/>, with the <c>Play:</c> flags in
    /// <see cref="SpellCastEvent.Detail"/>: a presentation event the host draws. Its callbacks (play flags 0x8, 0x10) are not
    /// ready: the run's CALLBACK actions do not follow the animation.</summary>
    EyeCandyAdded,
    /// <summary>ARC-MAGIC-017: an EyeCandy component's Remove (0x4CD940) took entry <see cref="SpellCastEvent.Value"/> off
    /// <see cref="SpellCastEvent.Object"/>: only this run's, or any run's with the <c>no_id</c> play flag (0x100).</summary>
    EyeCandyRemoved,
}

/// <summary>
/// ARC-MAGIC-014 (magic plan MT20a, the cast host): the world the run rules ask, over the session and a map — the run host
/// (<see cref="ISpellRunHost"/>), the components' damage world (<see cref="ISpellDamageWorld"/>) and the event pass's magic host
/// (<see cref="ISessionSpellWorld"/>) — for any caster, critter and map. The hero is the PC (<see cref="Hero"/>); NPCs and objects
/// come from the map. Damage and Heal (MT12) are applied to the session's life; the other component types are not ready and report
/// <see cref="SpellCastEventKind.ComponentNotReady"/>. Payments the rules return are applied through <see cref="Apply"/>.
/// </summary>
public sealed class SessionSpellCaster : ISpellRunHost, ISpellDamageWorld, ISessionSpellWorld
{
    /// <summary>The PC's entity id in the run rules and the map (the session's player entity).</summary>
    public const string Hero = SessionEventKinds.PlayerEntity;

    private readonly GameSessionOwner _owner;
    private readonly Func<int, SessionSpellRecord?> _records;
    private readonly ISpellCastMap _map;
    private readonly ISessionEventWorld _events;
    private readonly List<SpellCastEvent> _log = [];
    // The runs whose goal reached its release frame (the goal's frame flag 0x8): their goal's end frees nothing. A freed run's id
    // leaves the set, since the slot is reused.
    private readonly HashSet<int> _released = [];
    // ARC-MAGIC-018: the runs whose missile flies (goal 28): their release comes when it arrives.
    private readonly HashSet<int> _missiles = [];
    // Whose charge cells a payment spends: the payer the rules asked for last (only the hero's are modelled). The rules ask the payer
    // before the cells of every payment; a run without a source asks neither and pays nothing.
    private string? _cellsPayer;

    public SessionSpellCaster(GameSessionOwner owner, Func<int, SessionSpellRecord?> records, ISpellCastMap map, ISessionEventWorld events)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>The session this host casts in.</summary>
    public GameSessionOwner Owner => _owner;

    /// <summary>What the casts so far left for the host, in order.</summary>
    public IReadOnlyList<SpellCastEvent> Log => _log;

    private MagicAnomalyProfile Profile => _owner.Rules.Magic;

    // ---- the objects ----

    /// <summary>The run rules' object for a map object (the hero with its aptitude), or null.</summary>
    public SpellRunObject? RunObject(string id) =>
        id == Hero ? _owner.QueryHero() is null ? null : new(Hero, SpellObjectFacts.PcType, HeroStat(CharacterStat.MagickTechAptitude) ?? 0)
        : _map.Critter(id) is { } npc ? new(id, SpellObjectFacts.NpcType, NpcStat(npc, CharacterStat.MagickTechAptitude) ?? 0)
        : _map.Object(id) is { } o ? new(id, o.Type) : null;

    /// <summary>The invocation check's facts of a map object (the hero's from the session), or null.</summary>
    public SpellObjectFacts? Facts(string id)
    {
        if (_map.Object(id) is not { } o) return null;
        var aptitude = id == Hero ? HeroStat(CharacterStat.MagickTechAptitude) ?? 0 : _map.Critter(id) is { } npc ? NpcStat(npc, CharacterStat.MagickTechAptitude) ?? 0 : 0;
        var intelligence = id == Hero ? HeroStat(CharacterStat.Intelligence) ?? 0 : _map.Critter(id) is { } c ? NpcStat(c, CharacterStat.Intelligence) ?? 0 : 0;
        return new(id, o.Type, o.Location, o.ObjectFlags, o.SpellFlags, o.CritterFlags, aptitude, intelligence);
    }

    /// <summary>The invocation world over the map (MT7): the line of fire and the BEGIN area tests (MT8's areas).</summary>
    public ISpellInvocationWorld InvocationWorld() =>
        new SpellInvocationAreas(_map, spell => _records(spell) is { } r && r.Areas.Length > 0 ? r.Areas[0].Targets : 0, _map.LineOfFire, Profile);

    // ---- ISpellRunHost ----

    public SpellRunRecord Record(int spell) => _records(spell)?.Run ?? new();

    public SpellStageFacts StageFacts(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var source = run.Source;
        var life = source is null ? null : Life(source.Id);
        var parent = run.Parent;
        return new(life?.Status.IsDead == true, life?.Status.IsUnconscious == true, parent is null ? 0 : Stat(parent, 4) ?? 0,
            parent is null ? 0 : Stat(parent, 6) ?? 0, parent?.Id == Hero, source is null ? null : Payer(source), Caster(source));
    }

    /// <summary>The charge cells of the payer asked for last (<see cref="Payer"/>): the hero's; any other payer's are not modelled yet
    /// and refuse.</summary>
    public ISpellCellSource Cells => _cellsPayer == Hero ? _owner.HeroCellSource() ?? NoCells.Instance : NoCells.Instance;

    public bool FlagsAllow(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var record = Record(run.Spell).Invocation;
        return SpellInvocationRules.FlagsAllow(run.Parent is { } p ? Facts(p.Id) : null, run.Target is { } t ? Facts(t.Id) : null, record);
    }

    public int SpellFlags(SpellRunObject who) => _map.Object(who.Id)?.SpellFlags ?? 0;

    public int? PlayerSpellFlags => _map.Object(Hero)?.SpellFlags;

    public bool PlayerPerceptionExtraordinary => HeroStat(CharacterStat.Perception) >= CharacterStatRules.ExtraordinaryThreshold;

    public SpellPayer? Payer(SpellRunObject who)
    {
        ArgumentNullException.ThrowIfNull(who);
        _cellsPayer = who.Id;
        if (!who.IsCritter) return new(SpellPayerKind.Other);
        if (Life(who.Id) is not { } life) return null;
        var limiting = who.Id != Hero && _map.Critter(who.Id) is { } npc && (npc.CritterFlags & unchecked((int)0x80000000)) != 0;
        // Worn mana stores are not modelled yet (no item store state): the fatigue pays all.
        return new(SpellPayerKind.Critter, limiting, null, life.Status.Fatigue, life.Record.State.FatigueDamage);
    }

    public SpellResistRecord ResistRecord(int spell) => _records(spell)?.Resist ?? new(spell, false, false, false, 0, SpellResistanceRules.NoSave, 0);

    public SpellResistTarget ResistTarget(SpellRun run, SpellRunTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        var record = ResistRecord(run.Spell);
        if (target.Object is not { } obj) return new(false, SpellTargetKind.Other, false, 0, 0, 0, false);
        var kind = obj.Id == Hero ? SpellTargetKind.Pc : _map.Critter(obj.Id) is not null ? SpellTargetKind.Npc : SpellTargetKind.Other;
        var npc = obj.Id == Hero ? null : _map.Critter(obj.Id);
        var statValue = record.HasSave ? Stat(obj, record.ResistStat) ?? 0 : 0;
        // The hero stores no magic resistance of its own in the session (its pre-made fields hold none).
        return new(obj.Id == run.Parent?.Id, kind, ((npc?.CritterFlags2 ?? 0) & 0x2000) != 0, npc?.StoredMagicResistance ?? 0,
            obj.Id == Hero ? HeroStat(CharacterStat.MagickTechAptitude) ?? 0 : npc is null ? 0 : NpcStat(npc, CharacterStat.MagickTechAptitude) ?? 0,
            statValue, record.ResistStat is >= 0 and <= 7 && statValue >= CharacterStatRules.ExtraordinaryThreshold);
    }

    public ISpellRollSource Rolls => _owner.SpellRolls;

    public ISpellFateSave FateSave => _owner.HeroMagickSave;

    public bool IsLocalPlayer(SpellRunObject? who) => who?.Id == Hero;

    public IReadOnlyList<SpellRunTarget> Collect(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell) is not { } record || (int)run.Action >= record.Areas.Length) return [];
        var context = Context(run, record.Areas[(int)run.Action]);
        return SpellTargetRules.Collect(context, _map, Profile).Entries.Select(e => new SpellRunTarget(e.Object is { } o ? RunObject(o.Id) ?? new(o.Id, o.Type) : null, e.Tile))
            .ToArray();
    }

    public bool UseSpellAtMaximum(SpellRunObject pc) =>
        pc.Id == Hero && _owner.QueryHeroFate() is { } fate && FateRules.IsArmed(fate, FateKind.SpellAtMaximum)
        && _owner.UseHeroFate(_owner.Version, FateKind.SpellAtMaximum).Disposition == GameSessionDisposition.Changed;

    public SpellComponentGate Gate(SpellRun run, int component, SpellRunTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell)?.Component(run.Action, component) is not { } c) return new(false, target);
        var context = Context(run, c.Area) with { Candidate = target.Object is { } o ? _map.Object(o.Id) : null, CandidateTile = target.Tile };
        if (!SpellTargetRules.Test(context, _map, Profile).Passes) return new(false, target, c.ItemTriggers);
        var (aimed, tile) = SpellTargetRules.ReAim(c.ApplyTargets, context.Candidate, target.Tile, context.Self, context.Source, null, run.TargetTile,
            context.Target);
        return new(true, new(aimed is null ? null : RunObject(aimed.Id) ?? new(aimed.Id, aimed.Type), tile), c.ItemTriggers);
    }

    public SpellComponentOutcome Apply(SpellRun run, int component, SpellRunTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell)?.Component(run.Action, component) is not { } c) return SpellComponentOutcome.None;
        if (c.Damage is { } data && target.Object is { } obj)
        {
            if (c.Type == SpellComponentType.Damage) ApplyDamage(run, data, obj);
            else ApplyHeal(run, data, obj);
            return SpellComponentOutcome.None;
        }
        if (c.Type == SpellComponentType.EyeCandy) { EyeCandy(run, c, target); return SpellComponentOutcome.None; }
        if (c.Damage is null) _log.Add(new(SpellCastEventKind.ComponentNotReady, run.Id, (int)c.Type, target.Object?.Id));
        return SpellComponentOutcome.None;
    }

    /// <summary>ARC-MAGIC-017: the EyeCandy component (0x451FE0, ledger EC rows) on its target object (none: nothing): an Add plays
    /// entry 6 · spell + number unless the run is forced to end, a Remove takes that entry off. Presentation only: the host draws it
    /// from the log; the callbacks are not ready (a callback play flag makes the original's run wait for the animation).</summary>
    private void EyeCandy(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (target.Object is not { } obj || component.EyeCandy is not { } data) return;
        var index = SpellEyeCandyRules.EntryIndex(run.Spell, data.Number);
        if (data.Add)
        {
            if ((run.Flags & SpellRunFlags.ForcedEnd) == 0) _log.Add(new(SpellCastEventKind.EyeCandyAdded, run.Id, index, obj.Id, (int)data.Play));
        }
        else _log.Add(new(SpellCastEventKind.EyeCandyRemoved, run.Id, index, obj.Id, (int)data.Play));
    }

    public SpellTimeFacts TimeFacts(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(run.Parent?.IsCritter == true, run.Parent is { IsCritter: true } parent ? Caster(parent).MasteredCollege : SpellBookRules.NoMastery,
            run.Target?.IsCritter == true, run.Parent?.Aptitude ?? 0, run.Target?.Aptitude ?? 0);
    }

    /// <summary>A stat by the original's number (0..7 the primary stats, 8..16 the derived ones; 17 the level), or null.</summary>
    public int? Stat(SpellRunObject who, int stat)
    {
        ArgumentNullException.ThrowIfNull(who);
        if (stat == 17) return who.Id == Hero ? _owner.QueryHeroExperience()?.Level : _map.Critter(who.Id)?.Combat.Level;
        if (CharacterStatOf(stat) is not { } s) return null;
        return who.Id == Hero ? HeroStat(s) : _map.Critter(who.Id) is { } npc ? NpcStat(npc, s) : null;
    }

    public bool MaintainBarHasSlot(SpellRun run) => true;

    /// <summary>The charge of a run freed before its BEGIN (0x456FA0 through 0x4507D0): the run cost with mastery, applied, by its
    /// source; in the original whatever the run (MG40), in Steamwoven only when its BEGIN would have paid.</summary>
    public IReadOnlyList<SpellPayment> ChargeBegin(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Source is not { } source || Payer(source) is not { } payer) return [];
        var record = Record(run.Spell);
        if (!Profile.GoalEndChargesAnyRun && !SpellCostRules.BeginPays(run.Free, run.Parent?.IsCritter == true, record.Technology, record.SkipsBeginCost))
            return [];
        var cost = SpellCostRules.RunCost(record.Cost, run.Spell, Caster(source), false);
        return [SpellPaymentRules.Pay(payer, cost, true, run.Spell, record.Payment, Cells)];
    }

    // ---- ISpellDamageWorld ----

    public Combatant Combatant(SpellRunObject who)
    {
        ArgumentNullException.ThrowIfNull(who);
        if (who.Id == Hero) return _owner.QueryHeroAttack()?.Combatant ?? new Combatant { IsPc = true };
        if (_map.Critter(who.Id) is { } npc && _owner.QueryLife(npc.Combat.Target).View is { } life)
            return npc.Combat.Combatant(life.Status, life.Record.State, _owner.Rules.Combat);
        return _map.ObjectCombat(who.Id)?.Combatant ?? new Combatant();
    }

    public AttackTargetKind Kind(SpellRunObject who) =>
        who.Id == Hero || _map.Critter(who.Id) is not null ? AttackTargetKind.Critter : _map.ObjectCombat(who.Id)?.Kind ?? AttackTargetKind.OtherObject;

    public CombatWeapon WieldedWeapon(SpellRunObject who) =>
        who.Id == Hero ? _owner.QueryHeroAttack()?.Weapon ?? CombatWeapon.Unarmed() : _map.WieldedWeapon(who.Id);

    public GameDifficulty Difficulty => _owner.Difficulty;

    public int CurrentHitPoints(SpellRunObject who) => Life(who.Id)?.Status.HitPoints ?? 0;

    public int CurrentFatigue(SpellRunObject who) => Life(who.Id)?.Status.Fatigue ?? 0;

    public IReadOnlyList<SpellAcidItem> WornItems(SpellRunObject critter) => _map.WornItems(critter.Id);

    // ---- ISessionSpellWorld ----

    public ISpellRunHost SpellRuns => this;

    public LifeFacts? CritterFacts(SessionMapId map, string entity) => _events.CritterFacts(map, entity);

    // ---- the casting goal (0x429B50..0x429CD0) ----

    /// <summary>
    /// The casting goal's start (0x429B50): the run's parent pays <see cref="TurnBasedRules.CastSpellCost"/> action points
    /// (0x4B7CD0: only in a turn-based round, in its own subturn; a refusal ends the goal), then the pre-animation check (0x450940,
    /// PC3): unless the run is free or has no source, its source must afford the BEGIN cost (the maintenance for another action) —
    /// no mastery, doubled for a dwarf — asked without applying. A goal that ends here frees the run with its BEGIN charged
    /// (<see cref="EndGoal"/>, 0x429CD0). Then a target destroyed or switched off ends the goal too (0x429BC0).
    /// </summary>
    [OriginalFunction(0x429B50, OriginalFunctionScope.Partial)]
    public SpellCastGoalStart StartGoal(int run, ISessionCombatWorld combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (_owner.QuerySpellRuns()[run] is not { } r) return new(SpellCastGoalOutcome.NoRun, null);
        // A new goal for the slot: a release this host remembers belongs to an earlier run, freed elsewhere.
        _released.Remove(run);
        _missiles.Remove(run);
        SessionCombatResult? points = null;
        if (r.Parent is { } parent && Combatant(parent.Id) is { } combatant)
        {
            points = _owner.ConsumeActionPoints(_owner.Version, combatant, TurnBasedRules.CastSpellCost, combat);
            if (points.Charge == SessionActionPointUse.Refused || points.Disposition == GameSessionDisposition.Rejected)
                return new(SpellCastGoalOutcome.NoActionPoints, points);
        }
        if (SpellCostRules.PreAnimationChecked(r.Free, r.Source is not null))
        {
            var source = r.Source!;
            // PC3: a failed record lookup refuses.
            if (_records(r.Spell)?.Run is not { } record) return new(SpellCastGoalOutcome.CannotAfford, points);
            var cost = SpellCostRules.PreAnimationCost(r.Action == SpellRunAction.Begin, record.Cost, record.MaintainCost, Caster(source), false);
            if (Payer(source) is { } payer && !SpellPaymentRules.Pay(payer, cost, false, r.Spell, record.Payment, Cells).Succeeded)
                return new(SpellCastGoalOutcome.CannotAfford, points);
        }
        // 0x429BC0: a target destroyed or switched off ends the goal before its animation.
        if (r.Target is { } target && ((_map.Object(target.Id)?.ObjectFlags ?? 0) & (SpellTargetObject.Destroyed | SpellTargetObject.Off)) != 0)
            return new(SpellCastGoalOutcome.TargetGone, points);
        // ARC-MAGIC-018: the facing turn (0x42C850) after the range node; a refusal ends the goal through its end node, as above.
        var facing = Facing(r);
        if (facing?.Kind == SpellFacingKind.Refused) return new(SpellCastGoalOutcome.CannotTurn, points) { Facing = facing };
        return new(SpellCastGoalOutcome.Started, points) { Facing = facing };
    }

    /// <summary>ARC-MAGIC-018: the casting goal's facing turn (0x42C850, <see cref="SpellFacingRules.Turn"/>) of the run's parent toward
    /// its target object; null without a parent on the map.</summary>
    private SpellFacing? Facing(SpellRun run)
    {
        if (run.Parent is not { } parent || _map.Object(parent.Id) is not { } caster) return null;
        var target = run.Target is { } t ? _map.Object(t.Id) : null;
        return SpellFacingRules.Turn(caster.SpellFlags, caster.IsCritter, caster.CritterFlags, Cell(caster.Location), run.Target?.Id == parent.Id,
            target is null ? null : Cell(target.Location));
    }

    private static CellCoord Cell(SpellLocation location) => new(unchecked((int)location.X), unchecked((int)location.Y));

    /// <summary>
    /// The casting goal's release frame (0x429C80): the target is checked again (0x456A90, <see cref="TargetStillValid"/>); then the
    /// run's BEGIN is processed (0x456F70) and its notes applied. A failed re-check begins nothing: in the original the run keeps its
    /// slot (MG41), in Steamwoven it is freed unpaid; null answers then.
    /// </summary>
    [OriginalFunction(0x429C80, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? Release(int run)
    {
        // The release gate fires once per goal (0x42CAA0, the frame flag 0x8): a run already released or begun answers null.
        if (_released.Contains(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
        // The release gate marks the goal's frame handled before the re-check (0x42CAA0): the goal's end then frees nothing.
        _released.Add(run);
        var begun = ReleaseRun(r);
        // A BEGIN the session rejected left the run untouched: its goal's end still frees it.
        if (begun?.Disposition == GameSessionDisposition.Rejected) _released.Remove(run);
        return begun;
    }

    // The release's re-check and BEGIN (0x429C80), for the casting goal and the missile's arrival.
    private SessionSpellRunResult? ReleaseRun(SpellRun r)
    {
        if (TargetStillValid(r)) return Act(new(SessionSpellRunActionKind.Begin, r.Id));
        if (!Profile.ReleaseRefusalKeepsRun) Act(new(SessionSpellRunActionKind.Free, r.Id));
        return null;
    }

    /// <summary>
    /// ARC-MAGIC-018: the casting goal's release frame for a spell whose projectile eye candy has art (0x429C40, the host's test): the
    /// release gate marks the frame handled (0x42CAA0), so the goal's end frees nothing, and a missile is launched (0x4248A0, goal 28)
    /// instead of the release: no re-check and no BEGIN now; they come when it arrives (<see cref="ArriveMissile"/>). False, and
    /// nothing done, for a run already released or begun, or none.
    /// </summary>
    [OriginalFunction(0x429C40, OriginalFunctionScope.Partial)]
    public bool LaunchMissile(int run)
    {
        if (_released.Contains(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return false;
        _released.Add(run);
        _missiles.Add(run);
        return true;
    }

    /// <summary>
    /// ARC-MAGIC-018: the missile arrived (goal 28's release, 0x429C80): the target re-check and the run's BEGIN, as the casting goal's
    /// release does; a failed re-check keeps the run in the original (MG41) and frees it unpaid in Steamwoven. A BEGIN the session
    /// rejected leaves no goal to free the run: it is lost as below. Null when no missile of the run flies or the run no longer waits.
    /// </summary>
    public SessionSpellRunResult? ArriveMissile(int run)
    {
        if (!_missiles.Remove(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
        var begun = ReleaseRun(r);
        if (begun?.Disposition == GameSessionDisposition.Rejected && !Profile.MissileLossKeepsRun) Act(new(SessionSpellRunActionKind.Free, run));
        return begun;
    }

    /// <summary>
    /// ARC-MAGIC-018: the missile's goal ended without its release (goal 28's end, 0x429E70: a leg whose line is refused, the map left,
    /// a point off the map): the missile is destroyed and nothing is freed, so in the original the run keeps its slot, never begun
    /// (MG44); Steamwoven frees it unpaid. Null when no missile of the run flies, or under the original's rule.
    /// </summary>
    [OriginalFunction(0x429E70, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? LoseMissile(int run)
    {
        if (!_missiles.Remove(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r) || Profile.MissileLossKeepsRun) return null;
        return Act(new(SessionSpellRunActionKind.Free, run));
    }

    /// <summary>ARC-MAGIC-018: whether a missile of the run flies.</summary>
    public bool MissileFlies(int run) => _missiles.Contains(run);

    /// <summary>
    /// The casting goal ended (0x429CD0). After its release frame (the goal's frame flag 0x8), or for a run whose BEGIN ran, nothing
    /// is freed and null answers; before it the run is freed with its BEGIN cost charged (0x456FA0, see <see cref="ChargeBegin"/>).
    /// A goal refused before its animation (<paramref name="refused"/>: action points, the pre-animation check or the target) ends
    /// the same way in the original (MG39); Steamwoven frees that run unpaid.
    /// </summary>
    [OriginalFunction(0x429CD0, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? EndGoal(int run, bool refused = false)
    {
        if (_released.Contains(run) || _owner.QuerySpellRuns()[run] is not { } r || (r.Flags & SpellRunFlags.Processed) != 0) return null;
        return Act(new(refused && !Profile.GoalRefusalChargesBegin ? SessionSpellRunActionKind.Free : SessionSpellRunActionKind.FreeCharged, run));
    }

    /// <summary>
    /// The release frame's target re-check (0x456A90): the record's BEGIN area passes at once when it is 0, exactly 0x10, or has
    /// the self bit without the tile bit (high word 0x8000); otherwise the picker's wrapper (0x4F2680) over the area test (MT8):
    /// a target object is the candidate and the target with no tiles (refused for object flag 0x800), retried with its location as
    /// both tiles; a target tile is both tiles. The wrapper's further global gate (0x603D20) is the picker's and not modelled.
    /// </summary>
    [OriginalFunction(0x456A90, OriginalFunctionScope.Partial)]
    public bool TargetStillValid(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell) is not { } record || record.Areas.Length == 0) return true;
        var mask = record.Areas[0].Targets;
        if (mask == 0 || mask == 0x10 || ((mask & 0x1) != 0 && ((mask >> 32) & 0x8000) == 0)) return true;
        var basis = Context(run, record.Areas[0]);
        if (run.Target is { } t)
        {
            if (_map.Object(t.Id) is not { } o || (o.ObjectFlags & PickerRefused) != 0) return false;
            var asObject = basis with { Candidate = o, Target = o, CandidateTile = SpellLocation.None, TargetTile = SpellLocation.None };
            return SpellTargetRules.Test(asObject, _map, Profile).Passes
                || SpellTargetRules.Test(asObject with { CandidateTile = o.Location, TargetTile = o.Location }, _map, Profile).Passes;
        }
        return SpellTargetRules.Test(basis with { Candidate = null, Target = null, CandidateTile = run.TargetTile, TargetTile = run.TargetTile }, _map,
            Profile).Passes;
    }

    /// <summary>
    /// The spell UI's cast at a picked target (0x57C110, ARC-MAGIC-015) by <paramref name="caster"/>, which is the run's source and
    /// parent: nothing when the caster is missing, dead or unconscious; a critter caster must afford the spell's <c>Cost:</c> (0x4508A0:
    /// <see cref="SpellCostRules.CheckCost"/>, the payment asked without applying), else spell.mes 600; the invocation (0x455A20) takes
    /// the target object, or else the tile; no line of fire (0x456BC0) shows spell.mes 604; then the run starts (0x455AC0: the
    /// invocation check, a slot and the run), whose casting goal is the host's (<see cref="StartGoal"/>).
    /// </summary>
    [OriginalFunction(0x57C110, OriginalFunctionScope.Partial)]
    public SpellCastAttempt CastAt(string caster, int spell, string? targetObject, SpellLocation targetTile)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (Life(caster) is not { } life || life.Status.IsDead || life.Status.IsUnconscious) return new(SpellCastAttemptOutcome.Nothing);
        if (_records(spell) is not { } record || RunObject(caster) is not { } source || Facts(caster) is not { } facts)
            return new(SpellCastAttemptOutcome.Nothing);
        if (source.IsCritter)
        {
            var cost = SpellCostRules.CheckCost(record.Run.Cost, Caster(source), false);
            if (Payer(source) is not { } payer || !SpellPaymentRules.Pay(payer, cost, false, spell, record.Run.Payment, Cells).Succeeded)
                return Refused(SpellCastAttemptOutcome.CannotAfford, SpellUiRules.CannotAffordMessage);
        }
        var target = targetObject is null ? null : Facts(targetObject);
        if (targetObject is not null && target is null) return new(SpellCastAttemptOutcome.Nothing);
        var invocation = new SpellInvocation(spell, facts, facts, target, target is null ? targetTile : SpellLocation.None, ParentIsLocalPlayer: caster == Hero);
        var world = InvocationWorld();
        if (!SpellInvocationRules.HasLineOfFire(invocation, world)) return Refused(SpellCastAttemptOutcome.NoLineOfFire, SpellUiRules.NoLineOfFireMessage);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation), record.Run, world, _owner.SpellRolls);
        return started.Disposition == GameSessionDisposition.Changed && started.Creation?.Run is { } run
            ? new(SpellCastAttemptOutcome.Started, null, started, run.Id)
            : new(SpellCastAttemptOutcome.Refused, null, started);
    }

    private SpellCastAttempt Refused(SpellCastAttemptOutcome outcome, int message)
    {
        _log.Add(new(SpellCastEventKind.Message, -1, message));
        return new(outcome, message);
    }

    /// <summary>
    /// The targeting cursor's test of a candidate (0x4F2830's area test, ARC-MAGIC-015): the BEGIN area with <paramref name="mask"/>
    /// (the cursor's) for <paramref name="caster"/> as self and source, the candidate object as candidate and target, or with none the
    /// tile as both tiles. The answer carries the area test's failure reason for the cursor's message.
    /// </summary>
    public SpellTargetTest TestTarget(string caster, int spell, ulong mask, string? candidate, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (_records(spell) is not { } record || record.Areas.Length == 0 || _map.Object(caster) is not { } self) return new(false);
        var basis = new SpellTargetContext(record.Areas[0] with { Targets = mask }, self, self, self.Location);
        if (candidate is not null)
            return _map.Object(candidate) is { } o && (o.ObjectFlags & PickerRefused) == 0
                ? SpellTargetRules.Test(basis with { Candidate = o, Target = o }, _map, Profile) : new(false);
        return SpellTargetRules.Test(basis with { CandidateTile = tile, TargetTile = tile }, _map, Profile);
    }

    // A run whose BEGIN has not run yet: the only one a release frame begins.
    private static bool AwaitsBegin(SpellRun run) => run.Action == SpellRunAction.Begin && (run.Flags & SpellRunFlags.Processed) == 0;

    /// <summary>The object flag the picker's wrapper refuses an object target for (0x4F2680).</summary>
    public const int PickerRefused = 0x800;

    /// <summary>One action on the session's runs through this host, with its notes applied.</summary>
    public SessionSpellRunResult Act(SessionSpellRunAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = _owner.QuerySpellRuns();
        var result = _owner.ActOnSpellRun(_owner.Version, this, action);
        Apply(result.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        return result;
    }

    /// <summary>
    /// An event pass with this host as the magic world (the magic-tech events run through it) and, when the host's event world is a
    /// script world, that world for deferred SCR calls. The notes of the firings that ran are applied after the pass, so several
    /// ticks of one run in a pass are each checked against the fatigue before the pass.
    /// </summary>
    public SessionEventPassResult ProcessEvents(int budget = 500)
    {
        var before = _owner.QuerySpellRuns();
        var pass = _owner.ProcessEvents(_events is ISessionScriptWorld scripts ? new ScriptPassWorld(this, scripts) : new PassWorld(this, _events), budget);
        foreach (var firing in pass.SpellRuns.Where(f => f.Ran)) Apply(firing.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        return pass;
    }

    // The pass world: this host for the magic-tech events, the host's event world for the rest (its party facts when it has them; a
    // world without them answers none, as the pass then does).
    private class PassWorld(SessionSpellCaster caster, ISessionEventWorld events) : ISessionSpellWorld, ISessionPartyEventWorld
    {
        public ISpellRunHost SpellRuns => caster;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => events.CritterFacts(map, entity);
        public ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => (events as ISessionPartyEventWorld)?.ReactionNpcFacts(map, entity);
        public ReactionPcFacts? ReactionPcFacts() => (events as ISessionPartyEventWorld)?.ReactionPcFacts();
    }

    // The pass world of a host whose event world also runs deferred script calls.
    private sealed class ScriptPassWorld(SessionSpellCaster caster, ISessionScriptWorld scripts) : PassWorld(caster, scripts), ISessionScriptWorld
    {
        public bool ObjectExists(SessionMapId map, string entity) => scripts.ObjectExists(map, entity);
        public bool RunScriptCall(SessionScriptCall call) => scripts.RunScriptCall(call);
    }

    private SessionCombatant? Combatant(string id) =>
        id == Hero ? SessionCombatant.Hero : _map.Critter(id) is not null ? SessionCombatant.Critter(id) : null;

    // ---- notes ----

    /// <summary>
    /// Applies what the run rules left for the host (<see cref="SpellRunNote"/>): a payment's fatigue (the stage's from the run's
    /// source, a reflection's from its payer; worn mana stores are not modelled yet), and records the local player's messages,
    /// resisting targets and freed runs in <see cref="Log"/>.
    /// </summary>
    public void Apply(IReadOnlyList<SpellRunNote> notes, Func<int, SpellRun?> runs)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(runs);
        foreach (var note in notes)
        {
            switch (note.Kind)
            {
                case SpellRunNoteKind.Payments when runs(note.Run)?.Source is { } source:
                    foreach (var payment in note.Payments ?? []) Pay(note.Run, source.Id, payment);
                    break;
                case SpellRunNoteKind.ReflectionPayment when note.Target?.Object is { } payer:
                    foreach (var payment in note.Payments ?? []) Pay(note.Run, payer.Id, payment);
                    break;
                case SpellRunNoteKind.Message: _log.Add(new(SpellCastEventKind.Message, note.Run, note.Value)); break;
                case SpellRunNoteKind.Resisted:
                    _log.Add(new(SpellCastEventKind.Resisted, note.Run, 0, note.Target?.Object?.Id));
                    if (IsLocalPlayer(runs(note.Run)?.Parent)) _log.Add(new(SpellCastEventKind.Message, note.Run, ResistedMessage));
                    break;
                case SpellRunNoteKind.Freed: _released.Remove(note.Run); _missiles.Remove(note.Run); _log.Add(new(SpellCastEventKind.Freed, note.Run)); break;
            }
        }
    }

    /// <summary>spell.mes: a resisting target (the host's feedback, 0x45A520).</summary>
    public const int ResistedMessage = 643;

    private void Pay(int run, string payer, SpellPayment payment)
    {
        if (payment.FatigueDamageAdded <= 0 || LifeTarget(payer) is not { } target) return;
        var result = _owner.PaySpellFatigue(_owner.Version, target, payment.FatigueDamageAdded);
        if (result.Disposition == GameSessionDisposition.Changed) _log.Add(new(SpellCastEventKind.FatiguePaid, run, payment.FatigueDamageAdded, payer));
    }

    private void ApplyDamage(SpellRun run, SpellDamageComponent data, SpellRunObject target)
    {
        var result = SpellLifeComponents.Damage(data, run, target, this, Rolls, Profile);
        foreach (var step in result.Steps)
        {
            if (step.Life is { } life && LifeTarget(step.Object) is { } who)
            {
                var applied = _owner.ApplyDamage(_owner.Version, who, life);
                if (applied.Outcome is { } outcome) _log.Add(new(SpellCastEventKind.Damaged, run.Id, outcome.HitPointDamage, step.Object));
            }
            else if (step.Life is null && step.Damage.Prevented == DamagePrevention.None && _map.DamageObject(step.Object, step.Damage))
                _log.Add(new(SpellCastEventKind.Damaged, run.Id, step.Damage.HitPointDamage, step.Object));
        }
    }

    private void ApplyHeal(SpellRun run, SpellDamageComponent data, SpellRunObject target)
    {
        if (LifeTarget(target.Id) is not { } who) return;
        var before = _owner.QueryLife(who).View?.Status.HitPoints ?? 0;
        // The raise script (point 0x12) runs no SCR yet: it allows the raise.
        var healed = _owner.Heal(_owner.Version, who, SpellLifeComponents.Heal(data, run, Rolls), () => true);
        if (healed.Disposition == GameSessionDisposition.Changed)
            _log.Add(new(SpellCastEventKind.Healed, run.Id, (_owner.QueryLife(who).View?.Status.HitPoints ?? 0) - before, target.Id));
    }

    // ---- helpers ----

    private SpellTargetContext Context(SpellRun run, SpellTargetArea area)
    {
        var self = run.Parent is { } p ? _map.Object(p.Id) : null;
        var source = run.Source is { } s ? _map.Object(s.Id) : null;
        var origin = source?.Location ?? run.StartLocation;
        return new(area, self, source, origin, Target: run.Target is { } t ? _map.Object(t.Id) : null, TargetTile: run.TargetTile,
            Other: run.Other is { } o ? _map.Object(o.Id) : null);
    }

    /// <summary>The session's life target of a critter (the hero, or an NPC of the map), or null.</summary>
    public SessionLifeTarget? LifeTarget(string id) => id == Hero ? _owner.QueryHero() is null ? null : SessionLifeTarget.Hero : _map.Critter(id)?.Combat.Target;

    private SessionLifeView? Life(string id) => LifeTarget(id) is { } target ? _owner.QueryLife(target).View : null;

    private SpellCaster Caster(SpellRunObject? source)
    {
        if (source is null || !source.IsCritter) return default;
        if (source.Id == Hero && _owner.QueryHero() is { } hero)
            return new(true, (int)hero.Race, _owner.QueryHeroSpellBook()?.MasteredCollege ?? SpellBookRules.NoMastery);
        return _map.Critter(source.Id) is { } npc ? new(true, (int)npc.Combat.Race, _owner.QueryNpcSpellBook(source.Id)?.MasteredCollege ?? SpellBookRules.NoMastery)
            : new(true, 0, SpellBookRules.NoMastery);
    }

    private int? HeroStat(CharacterStat stat)
    {
        if (_owner.QueryHeroLife() is not { } life) return null;
        var state = life.Record.State;
        return _owner.QueryHeroStats(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)))?.Snapshot?.GetEffective(stat);
    }

    private int? NpcStat(SpellCastCritter npc, CharacterStat stat) =>
        _owner.QueryLife(npc.Combat.Target).View is { } life ? npc.Combat.EffectiveStat(stat, life.Record.State) : null;

    // The original's stat numbers the character owner models (0..7 primary, 8..16 derived).
    private static CharacterStat? CharacterStatOf(int stat) => stat switch
    {
        >= 0 and <= 7 => (CharacterStat)stat,
        8 => CharacterStat.CarryWeight, 9 => CharacterStat.DamageBonus, 10 => CharacterStat.AcAdjustment, 11 => CharacterStat.Speed,
        12 => CharacterStat.HealRate, 13 => CharacterStat.PoisonRecovery, 14 => CharacterStat.ReactionModifier,
        15 => CharacterStat.MaxFollowers, 16 => CharacterStat.MagickTechAptitude,
        _ => null,
    };

    private sealed class NoCells : ISpellCellSource
    {
        public static readonly NoCells Instance = new();

        public SpellCellPayment Pay(SpellCellRequest request) => new(false, 0);
    }
}
