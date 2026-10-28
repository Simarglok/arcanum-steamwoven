using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
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

/// <summary>How a cast while the caster's casting goal plays went (<see cref="SessionSpellCaster.Queue"/>, SU13): queued beneath the
/// playing goal, refused (the run freed), or there is no such run.</summary>
public enum SpellCastQueueOutcome { Queued, Refused, NoRun }

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
    /// <summary>ARC-MAGIC-022: a payment spent a charge of the item <see cref="SpellCastEvent.Object"/>; the value is the charges left.</summary>
    ChargeSpent,
    /// <summary>ARC-MAGIC-022: the session refused to save a payment's spent charge of the item <see cref="SpellCastEvent.Object"/>
    /// (the value is the <see cref="GameSessionFailure"/>): the spell was paid without it.</summary>
    ChargeNotSaved,
    /// <summary>ARC-MAGIC-024: this many item events were dropped unfired (<see cref="SessionSpellCaster.FireItemEvents"/>): beyond
    /// <see cref="SessionSpellCaster.MaxItemEventsPerFiring"/> in one firing.</summary>
    ItemEventsDropped,
}

/// <summary>
/// ARC-MAGIC-014 (magic plan MT20a, the cast host): the world the run rules ask, over the session and a map — the run host
/// (<see cref="ISpellRunHost"/>), the components' damage world (<see cref="ISpellDamageWorld"/>) and the event pass's magic host
/// (<see cref="ISessionSpellWorld"/>) — for any caster, critter and map. The hero is the PC (<see cref="Hero"/>); NPCs and objects
/// come from the map. Damage and Heal (MT12) are applied to the session's life; the other component types are not ready and report
/// <see cref="SpellCastEventKind.ComponentNotReady"/>. Payments the rules return are applied through <see cref="Apply"/>.
/// </summary>
public sealed partial class SessionSpellCaster : ISpellRunHost, ISpellDamageWorld, ISessionSpellWorld
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
    // ARC-MAGIC-024: the item events this host's life steps called for, not fired yet, and whether a firing is under way (the runs its
    // executor calls start add theirs to the end).
    private readonly List<SessionItemEvent> _itemEvents = [];
    private bool _firingItems;
    // ARC-MAGIC-025, MG46: the chain of the item event being fired (null outside a firing) and, per run an item event started, the
    // chain it was started in with its source (a slot reused by any other run is dropped); not saved, as runs are not.
    private IReadOnlyCollection<string>? _firingChain;
    private readonly Dictionary<int, (string? Source, IReadOnlyCollection<string> Chain)> _itemChains = [];

    /// <summary>A host over <paramref name="map"/>; with <paramref name="items"/> (ARC-MAGIC-020) the items a critter of the map holds
    /// are objects too (<see cref="Item"/>), so their spells can run with the item as source.</summary>
    public SessionSpellCaster(GameSessionOwner owner, Func<int, SessionSpellRecord?> records, ISpellCastMap map, ISessionEventWorld events,
        Func<string, SessionItemSpells?>? items = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        ArgumentNullException.ThrowIfNull(map);
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _items = items;
        _map = items is null ? map : new ItemCastMap(map, Item);
    }

    /// <summary>The session this host casts in.</summary>
    public GameSessionOwner Owner => _owner;

    /// <summary>What the casts so far left for the host, in order.</summary>
    public IReadOnlyList<SpellCastEvent> Log => _log;

    /// <summary>
    /// ARC-MAGIC-024: the item spell executors this host fires the item events of its own life steps through: a spell's damage step
    /// (DH1: the run's parent the attacker, its other object or else its parent the damage source), a payment's fatigue (0x450420) and
    /// the life steps of its event passes (the fatigue setter's going unconscious, IC4). They fire after the run's pass, once the
    /// notes are applied (<see cref="Act"/>, <see cref="ProcessEvents"/>), since the session refuses a run start while the run rules
    /// act. None: nothing fires and the events are dropped.
    /// </summary>
    public ISessionItemTriggers? ItemTriggers { get; set; }

    /// <summary>ARC-MAGIC-024: the item events one firing walks at most, those the runs it starts call for included (a technical bound:
    /// the goals of those runs play at once, so a chain the original spreads over frames would not end here, MG46).</summary>
    public const int MaxItemEventsPerFiring = 64;

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

    /// <summary>ARC-MAGIC-022: the whole record of <paramref name="spell"/> the host knows, or null.</summary>
    public SessionSpellRecord? SpellRecord(int spell) => _records(spell);

    /// <summary>ARC-MAGIC-022: the hero's life status, or null without a hero.</summary>
    public LifeStatus? HeroLife => Life(Hero)?.Status;

    public SpellStageFacts StageFacts(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var source = run.Source;
        var life = source is null ? null : Life(source.Id);
        var parent = run.Parent;
        return new(life?.Status.IsDead == true, life?.Status.IsUnconscious == true, parent is null ? 0 : Stat(parent, 4) ?? 0,
            parent is null ? 0 : Stat(parent, 6) ?? 0, parent?.Id == Hero, source is null ? null : Payer(source), Caster(source));
    }

    /// <summary>The charge cells of the payer asked for last (<see cref="Payer"/>): the hero's, also for an item the hero holds (its
    /// parent's stack, ARC-MAGIC-020); any other payer's are not modelled yet and refuse.</summary>
    public ISpellCellSource Cells => _cellsPayer == Hero ? _owner.HeroCellSource() ?? NoCells.Instance
        : _cellsPayer is not null && Item(_cellsPayer) is { Holder: Hero } item ? _owner.HeroCellSource(item.Complexity) ?? NoCells.Instance
        : NoCells.Instance;

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
        if (!who.IsCritter) return Item(who.Id) is { } item ? new(SpellPayerKind.Item, ItemCharges: item.Charges) : new(SpellPayerKind.Other);
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
    /// rejected leaves no goal to free the run: it is freed as a lost missile's is. Null when no missile of the run flies or the run
    /// no longer waits.
    /// </summary>
    public SessionSpellRunResult? ArriveMissile(int run)
    {
        if (!_missiles.Remove(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
        var begun = ReleaseRun(r);
        if (begun?.Disposition == GameSessionDisposition.Rejected) Act(new(SessionSpellRunActionKind.Free, run));
        return begun;
    }

    /// <summary>
    /// ARC-MAGIC-018: the missile's goal ended without its release (goal 28's end, 0x429E70: a leg whose line is refused, the map left,
    /// a point off the map): the original destroys the missile and frees nothing, so the run keeps its slot, never begun (MG44, owner
    /// decision 2026-10-28: fixed in both rule modes, no switch): the run is freed unpaid. Null when no missile of the run flies.
    /// </summary>
    [OriginalFunction(0x429E70, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? LoseMissile(int run)
    {
        if (!_missiles.Remove(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
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
    /// ARC-MAGIC-019 (SU13): a run the spell UI started (<see cref="CastAt"/>) while the caster's casting goal plays is queued beneath
    /// that goal (0x455C30's queue branch, 0x44D9B0): the new goal goes to the bottom of the caster's goal stack, so queued casts play
    /// in the order they were cast, each once the goals above it are done, and start then through <see cref="StartGoal"/>.
    /// <paramref name="depth"/> is the playing goal's stack index: the casts already queued plus its own subgoals. Refused
    /// (<see cref="SpellUiRules.QueueRefused"/>: depth 4 or more, or a turn-based round) the run is freed unpaid (0x4558D0), silently.
    /// </summary>
    [OriginalFunction(0x44D9B0, OriginalFunctionScope.Partial)]
    public SpellCastQueueOutcome Queue(int run, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        if (_owner.QuerySpellRuns()[run] is null) return SpellCastQueueOutcome.NoRun;
        if (!SpellUiRules.QueueRefused(depth, _owner.QueryCombatRound() is not null)) return SpellCastQueueOutcome.Queued;
        Act(new(SessionSpellRunActionKind.Free, run));
        return SpellCastQueueOutcome.Refused;
    }

    /// <summary>
    /// ARC-MAGIC-019: a queued cast whose goal never plays, because its caster's goal stack ended through the end step or was
    /// interrupted. Its only end node, the casting goal 26's animation reset (0x42B090), frees nothing, so in the original the run
    /// keeps its slot (MG43); fixed in both modes (owner decision 2026-10-28): a run still waiting for its BEGIN is freed, unpaid.
    /// Null when nothing was freed.
    /// </summary>
    public SessionSpellRunResult? DropQueued(int run) =>
        _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r) ? null : Act(new(SessionSpellRunActionKind.Free, run));

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
    /// The spell UI's cast at a picked target (0x57C110, ARC-MAGIC-015) by <paramref name="caster"/>, which is the run's parent and,
    /// without <paramref name="item"/>, its source: nothing when the caster is missing, dead or unconscious; a critter caster must afford the spell's <c>Cost:</c> (0x4508A0:
    /// <see cref="SpellCostRules.CheckCost"/>, the payment asked without applying), else spell.mes 600; the invocation (0x455A20) takes
    /// the target object, or else the tile; no line of fire (0x456BC0) shows spell.mes 604; then the run starts (0x455AC0: the
    /// invocation check, a slot and the run), whose casting goal is the host's (<see cref="StartGoal"/>).
    /// ARC-MAGIC-022: with <paramref name="item"/> (an item the host knows that <paramref name="caster"/> holds) the item is the
    /// invocation's source and the caster its parent, with the power the caster draws from it, as the item use's slot-0 cast sets the
    /// activation's source (0x57BC70: an item's holder is the caster); an item source skips the cost check.
    /// </summary>
    [OriginalFunction(0x57C110, OriginalFunctionScope.Partial)]
    public SpellCastAttempt CastAt(string caster, int spell, string? targetObject, SpellLocation targetTile, string? item = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (Life(caster) is not { } life || life.Status.IsDead || life.Status.IsUnconscious) return new(SpellCastAttemptOutcome.Nothing);
        if (item is not null && Item(item)?.Holder != caster) return new(SpellCastAttemptOutcome.Nothing);
        if (_records(spell) is not { } record || RunObject(item ?? caster) is not { } source || Facts(caster) is not { } facts ||
            (item is null ? facts : Facts(item)) is not { } sourceFacts)
            return new(SpellCastAttemptOutcome.Nothing);
        if (source.IsCritter)
        {
            var cost = SpellCostRules.CheckCost(record.Run.Cost, Caster(source), false);
            if (Payer(source) is not { } payer || !SpellPaymentRules.Pay(payer, cost, false, spell, record.Run.Payment, Cells).Succeeded)
                return Refused(SpellCastAttemptOutcome.CannotAfford, SpellUiRules.CannotAffordMessage);
        }
        var target = targetObject is null ? null : Facts(targetObject);
        if (targetObject is not null && target is null) return new(SpellCastAttemptOutcome.Nothing);
        var invocation = new SpellInvocation(spell, facts, sourceFacts, target, target is null ? targetTile : SpellLocation.None,
            item is null ? null : ItemPower(Item(item)!, facts), ParentIsLocalPlayer: caster == Hero);
        var world = InvocationWorld();
        if (!SpellInvocationRules.HasLineOfFire(invocation, world)) return Refused(SpellCastAttemptOutcome.NoLineOfFire, SpellUiRules.NoLineOfFireMessage);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation), record.Run, world, _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run)
            return new(SpellCastAttemptOutcome.Refused, null, started);
        _itemChains.Remove(run.Id);
        return new(SpellCastAttemptOutcome.Started, null, started, run.Id);
    }

    private SpellCastAttempt Refused(SpellCastAttemptOutcome outcome, int message)
    {
        _log.Add(new(SpellCastEventKind.Message, -1, message));
        return new(outcome, message);
    }

    /// <summary>
    /// The targeting cursor's test of a candidate (0x4F2830's area test, ARC-MAGIC-015): the BEGIN area with <paramref name="mask"/>
    /// (the cursor's) for <paramref name="caster"/> as self and source, the candidate object (if any) as candidate and target and the
    /// tile (none with an object, except for the picker's second test, ARC-MAGIC-021) as both tiles. The answer carries the area
    /// test's failure reason for the cursor's message.
    /// </summary>
    public SpellTargetTest TestTarget(string caster, int spell, ulong mask, string? candidate, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (_records(spell) is not { } record || record.Areas.Length == 0 || _map.Object(caster) is not { } self) return new(false);
        var basis = new SpellTargetContext(record.Areas[0] with { Targets = mask }, self, self, self.Location, CandidateTile: tile, TargetTile: tile);
        if (candidate is not null)
            return _map.Object(candidate) is { } o && (o.ObjectFlags & PickerRefused) == 0
                ? SpellTargetRules.Test(basis with { Candidate = o, Target = o }, _map, Profile) : new(false);
        return SpellTargetRules.Test(basis, _map, Profile);
    }

    /// <summary>
    /// ARC-MAGIC-021: the targeting cursor's pick (0x4F28A0, <see cref="SpellUiRules.Pick"/>) for <paramref name="caster"/> as the
    /// local PC: the object the screen pick found under the point (<paramref name="atPoint"/>, a map id, found without the objects
    /// <see cref="PickSpares"/> names), else the objects on <paramref name="tile"/> (null: no tile under the point) in the map's order,
    /// else the tile, each through <see cref="TestTarget"/>. The caster's followers are the map's party of the caster.
    /// </summary>
    public SpellPick PickTarget(string caster, int spell, ulong mask, string? atPoint, SpellLocation? tile)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var followers = Followers(caster);
        return SpellUiRules.Pick(mask, caster, followers, atPoint is null ? null : _map.Object(atPoint), tile, _map,
            (o, at) => TestTarget(caster, spell, mask, o?.Id, at));
    }

    /// <summary>ARC-MAGIC-021: which map objects the pick sees through for this mask (SU15, <see cref="SpellUiRules.PickSpares"/>), the
    /// screen pick's extra skip while the cursor is open; the followers are read once, when it is made.</summary>
    public Func<string, bool> PickSpared(string caster, ulong mask)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var followers = Followers(caster);
        return id => _map.Object(id ?? throw new ArgumentNullException(nameof(id))) is { } o && SpellUiRules.PickSpares(mask, o, caster, followers);
    }

    // The follower list of the pick (0x441260): the map's party of the caster, the caster left out.
    private HashSet<string> Followers(string caster) =>
        _map.Object(caster) is { } self ? _map.Party(self).Select(o => o.Id).Where(id => id != caster).ToHashSet(StringComparer.Ordinal) : [];

    // A run whose BEGIN has not run yet: the only one a release frame begins.
    private static bool AwaitsBegin(SpellRun run) => run.Action == SpellRunAction.Begin && (run.Flags & SpellRunFlags.Processed) == 0;

    /// <summary>The object flag the picker's wrapper refuses an object target for (0x4F2680).</summary>
    public const int PickerRefused = SpellUiRules.ClickThrough;

    /// <summary>One action on the session's runs through this host, with its notes applied.</summary>
    public SessionSpellRunResult Act(SessionSpellRunAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = _owner.QuerySpellRuns();
        var result = _owner.ActOnSpellRun(_owner.Version, this, action);
        Apply(result.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        FirePendingItems();
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
        ISessionEventWorld world = _events switch
        {
            ISessionAiWorld ai => new AiPassWorld(this, ai),
            ISessionScriptWorld scripts => new ScriptPassWorld(this, scripts),
            _ => new PassWorld(this, _events),
        };
        var pass = _owner.ProcessEvents(world, budget);
        // ARC-MAGIC-024: the pass's life steps call for theirs too (the encumbrance drain, a poison death fires nothing without a killer).
        foreach (var firing in pass.Life) _itemEvents.AddRange(firing.Life.ItemEvents);
        foreach (var firing in pass.SpellRuns.Where(f => f.Ran)) Apply(firing.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        FirePendingItems();
        return pass;
    }

    /// <summary>
    /// ARC-MAGIC-024: fires <paramref name="events"/> (a life step's <see cref="SessionLifeResult.ItemEvents"/> the host made itself:
    /// a run step's fatigue, the overexertion, a script's damage or kill) after any this host has not fired yet, through
    /// <see cref="ItemTriggers"/> (<see cref="GameSessionOwner.FireItemEvents"/> with the map's worn items of an NPC), in order; the
    /// events the started runs' life steps call for follow, up to <see cref="MaxItemEventsPerFiring"/> in all (the rest are dropped
    /// and logged). During a firing the events only join it; a call the session refuses (from inside the run rules or an event pass)
    /// leaves them for the next firing. Nothing without <see cref="ItemTriggers"/>: the events are dropped.
    /// </summary>
    public IReadOnlyList<SessionItemTriggerFiring> FireItemEvents(IEnumerable<SessionItemEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var list = events.ToArray();
        if (list.Any(e => e is null)) throw new ArgumentException("Null item event.", nameof(events));
        _itemEvents.AddRange(list);
        return FirePendingItems();
    }

    /// <summary>ARC-MAGIC-024: the dying animation of <paramref name="critter"/> ended (0x42FF40, IC5): its dying with no other object
    /// when it is dead (<see cref="GameSessionOwner.QueryDyingEndItemEvents"/>), fired as <see cref="FireItemEvents"/> does.</summary>
    public IReadOnlyList<SessionItemTriggerFiring> DyingAnimationEnded(string critter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        return FireItemEvents(LifeTarget(critter) is { } target ? _owner.QueryDyingEndItemEvents(target) : []);
    }

    private IReadOnlyList<SessionItemTriggerFiring> FirePendingItems()
    {
        if (_firingItems) return [];
        if (ItemTriggers is not { } triggers)
        {
            _itemEvents.Clear();
            return [];
        }
        var fired = new List<SessionItemTriggerFiring>();
        _firingItems = true;
        try
        {
            for (var walked = 0; _itemEvents.Count > 0; walked++)
            {
                if (walked == MaxItemEventsPerFiring)
                {
                    _log.Add(new(SpellCastEventKind.ItemEventsDropped, -1, _itemEvents.Count));
                    _itemEvents.Clear();
                    break;
                }
                // A call from inside the run rules or an event pass (a host's callback) is refused: the events wait for the outer
                // action's or pass's firing.
                _firingChain = _itemEvents[0].Chain;
                var result = _owner.FireItemEvents(triggers, [_itemEvents[0]], WornItemIds, IsItem);
                _firingChain = null;
                if (result.Failure != GameSessionFailure.None) break;
                _itemEvents.RemoveAt(0);
                fired.AddRange(result.Firings);
            }
        }
        catch
        {
            // A host's executor that throws ends the firing: nothing left over fires on a later, unrelated action.
            _itemEvents.Clear();
            throw;
        }
        finally
        {
            _firingItems = false;
            _firingChain = null;
        }
        return fired;
    }

    // ARC-MAGIC-025: an object of the map (or an item a critter holds) of an item type, 5..14 (0x4CBE70's item branch).
    private bool IsItem(string id) => _map.Object(id) is { Type: >= SpellObjectFacts.FirstItemType and <= SpellObjectFacts.LastItemType };

    // MG46: a run an item event started remembers the chain of that event (none outside a firing).
    private void Chained(int run, IReadOnlyCollection<string>? chain) => _itemChains[run] = (_owner.QuerySpellRuns()[run]?.Source?.Id, chain ?? []);

    // MG46: the chain of the events a run's damage step calls for: for a run an item event started, the chain it was started in and its
    // own source; null for any other run.
    private IReadOnlyCollection<string>? ChainOf(SpellRun run)
    {
        if (run.Triggers == 0) return null;
        var known = _itemChains.TryGetValue(run.Id, out var entry) && entry.Source == run.Source?.Id ? entry.Chain : [];
        return run.Source?.Id is { } source && !known.Contains(source) ? [.. known, source] : known;
    }

    // An NPC's worn items in slot order (the map's), or null for an entity that is no NPC of the map.
    private IReadOnlyList<string>? WornItemIds(string id) =>
        id != Hero && _map.Critter(id) is not null ? [.. _map.WornItems(id).OrderBy(item => item.Slot).Select(item => item.Id)] : null;

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
    private class ScriptPassWorld(SessionSpellCaster caster, ISessionScriptWorld scripts) : PassWorld(caster, scripts), ISessionScriptWorld
    {
        public bool ObjectExists(SessionMapId map, string entity) => scripts.ObjectExists(map, entity);
        public bool RunScriptCall(SessionScriptCall call) => scripts.RunScriptCall(call);
    }

    // ARC-WORLD-092: the pass world of a host whose event world is also the AI scheduler's host, so the AI ticks run through it.
    private sealed class AiPassWorld(SessionSpellCaster caster, ISessionAiWorld ai) : ScriptPassWorld(caster, ai), ISessionAiWorld
    {
        public SessionLifeTarget? AiLifeTarget(string entity) => ai.AiLifeTarget(entity);
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => ai.ScanCombatants(map);
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => ai.CombatantFacts(map, entity);
        public long DistanceToPc(string npc) => ai.DistanceToPc(npc);
        public bool RunScript(AiScriptPoint point, string npc, string? triggerer) => ai.RunScript(point, npc, triggerer);
        public bool RunWaypoints(string npc) => ai.RunWaypoints(npc);
        public void RunStandpoints(string npc) => ai.RunStandpoints(npc);
        public bool IsGenerator(string npc) => ai.IsGenerator(npc);
        public long? Spawn(string npc) => ai.Spawn(npc);
        public bool IsEncounterCreature(string npc) => ai.IsEncounterCreature(npc);
        public void Destroy(string npc) => ai.Destroy(npc);
        public void InterruptAnimations(string npc) => ai.InterruptAnimations(npc);
        public bool SectorLoaded(string npc) => ai.SectorLoaded(npc);
        public AiCatchUpFacts CatchUp(string npc) => ai.CatchUp(npc);
        public void MoveToLeader(string npc) => ai.MoveToLeader(npc);
        public bool MapClearing => ai.MapClearing;
        public bool Sleeping(string npc) => ai.Sleeping(npc);
        public bool Hidden(string npc) => ai.Hidden(npc);
        public bool PcHidden => ai.PcHidden;
        public AiConversation Conversation(string npc) => ai.Conversation(npc);
        public bool ConversationCallback => ai.ConversationCallback;
        public void EndConversation(string npc) => ai.EndConversation(npc);
        public int AnimationPriority(string npc) => ai.AnimationPriority(npc);
        public bool StunAnimationRunning(string npc) => ai.StunAnimationRunning(npc);
        public bool AnimationRunning(string npc) => ai.AnimationRunning(npc);
        public void ReWield(string npc, bool allSlots) => ai.ReWield(npc, allSlots);
        public string? Leader(string npc) => ai.Leader(npc);
        public void EndMaintainedSpells(string npc) => ai.EndMaintainedSpells(npc);
        public bool Heal(string npc) => ai.Heal(npc);
        public bool SearchItems(string npc) => ai.SearchItems(npc);
        public void UpdateDanger(string npc) => ai.UpdateDanger(npc);
        public void Perform(string npc) => ai.Perform(npc);
        public bool Loading => ai.Loading;
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
        // ARC-MAGIC-022: an item source's spent charge (and with it the magical flag) is saved. The notes of an event pass are applied
        // after the pass, so two payments of one pass were both made against the count before it: one that finds no charge left
        // spends nothing (the original refuses the second, NoCharges). A spend the session refuses (a full record budget) is logged.
        if (payment.ChargeSpent)
        {
            if (_items?.Invoke(payer) is not { } raw || raw.Entity != payer) return;
            var left = _owner.QueryItemCharges(payer) ?? raw.Charges;
            if (left <= 0) return;
            var spent = _owner.SpendItemCharge(_owner.Version, payer, Math.Max(1, raw.Charges));
            _log.Add(spent.Disposition == GameSessionDisposition.Changed ? new(SpellCastEventKind.ChargeSpent, run, left - 1, payer)
                : new(SpellCastEventKind.ChargeNotSaved, run, (int)spent.Failure, payer));
            return;
        }
        if (payment.FatigueDamageAdded <= 0 || LifeTarget(payer) is not { } target) return;
        var result = _owner.PaySpellFatigue(_owner.Version, target, payment.FatigueDamageAdded);
        if (result.Disposition == GameSessionDisposition.Changed) _log.Add(new(SpellCastEventKind.FatiguePaid, run, payment.FatigueDamageAdded, payer));
        _itemEvents.AddRange(result.ItemEvents);
    }

    private void ApplyDamage(SpellRun run, SpellDamageComponent data, SpellRunObject target)
    {
        var result = SpellLifeComponents.Damage(data, run, target, this, Rolls, Profile);
        foreach (var step in result.Steps)
        {
            if (step.Life is { } life && LifeTarget(step.Object) is { } who)
            {
                // DH1: the run's parent is the attacker, its other object (else its parent) the damage source.
                var applied = _owner.ApplyDamage(_owner.Version, who, life, new(run.Parent?.Id, (run.Other ?? run.Parent)?.Id));
                if (applied.Outcome is { } outcome) _log.Add(new(SpellCastEventKind.Damaged, run.Id, outcome.HitPointDamage, step.Object));
                // MG46: the damage step of a run an item event started carries its chain.
                var chain = ChainOf(run);
                _itemEvents.AddRange(chain is null ? applied.ItemEvents : applied.ItemEvents.Select(e => e with { Chain = chain }));
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
        _itemEvents.AddRange(healed.ItemEvents);
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
