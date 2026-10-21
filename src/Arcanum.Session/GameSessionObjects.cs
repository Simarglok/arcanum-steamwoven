using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

/// <summary>What became of a non-critter object an attack can damage (ARC-COMBAT-008).</summary>
public enum SessionObjectCondition
{
    Whole,
    /// <summary>Destroyed (removed): a door, a scenery already busted, a trap without a destroyed ART, or a container that spilled.</summary>
    Destroyed,
    /// <summary>A scenery or trap that took its destroyed ART (a busted window is the lock domain's busted bit).</summary>
    Busted,
    /// <summary>A respawning scenery switched off until its respawn event fires.</summary>
    Off,
}

/// <summary>
/// The T0 facts of a portal, container, scenery or trap an attack can damage, read by the host from the object's data: its kind, its
/// stored hit points (hp_pts, hp_adj, hp_damage), its material and stored resistances (normal, fire, electrical, poison, magic, as
/// stored), the invulnerable object flag (0x400000), whether a portal is a window (it has a busted ART), a container a junk pile
/// (prototype file 3043, the container whose number less 20 is 3023), a scenery respawning (scenery flag 0x20) with its respawn field (71), whether a scenery or trap has a destroyed
/// ART (field 25 not −1) and is busted (its busted flag 0x2), whether its use script is a trap (the trap test 0x4BBDD0), its cell
/// (where a container spills) and a portal's or container's lock baseline (<see cref="SessionOpenableBaseline"/>).
/// </summary>
public sealed record SessionObjectBaseline
{
    public required DamageableObjectKind Kind { get; init; }
    public int Points { get; init; }
    public int Adjustment { get; init; }
    public int Damage { get; init; }
    public ObjectMaterial Material { get; init; }
    public DamageResistances Resistances { get; init; } = DamageResistances.None;
    public bool Invulnerable { get; init; }
    public bool Window { get; init; }
    public bool JunkPile { get; init; }
    public bool Respawning { get; init; }
    public int RespawnDelay { get; init; }
    public bool HasDestroyedArt { get; init; }
    public bool Busted { get; init; }
    public bool Trapped { get; init; }
    public CellCoord Cell { get; init; }
    public SessionOpenableBaseline? Openable { get; init; }
}

/// <summary>
/// The bridge from stored fields (ARC-DATA-021, ARC-COMBAT-008) to an object's damage baseline: the common hit points, material,
/// resistances and invulnerable flag, a portal's window bit (bit 10 of its portal ART clear), a junk pile (prototype file 3043, the
/// container whose number less 20 is 3023), a scenery's respawn flag (0x20), busted flag (0x2) and respawn field, a scenery's or
/// trap's destroyed ART, and a trap's busted flag. Null for a type that is no portal, container, scenery or trap, a material
/// outside the fourteen or values outside the rules' bounds. Whether its use script is a trap is the host's (<paramref name="trapped"/>).
/// </summary>
public static class SessionObjectFacts
{
    public const int JunkPilePrototypeFile = ObjectDamageRules.JunkPilePrototype + 20;
    private const uint InvulnerableObject = 0x400000, RespawnableScenery = 0x20, BustedFlag = 0x2;

    public static SessionObjectBaseline? FromCombatFields(Arcanum.Data.Combat.CombatBreakableFields fields, CellCoord cell,
        SessionOpenableBaseline? openable = null, bool trapped = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var common = fields.Object;
        DamageableObjectKind? kind = common.Type switch
        {
            Arcanum.Data.Combat.CombatFieldReader.PortalType => DamageableObjectKind.Portal,
            Arcanum.Data.Combat.CombatFieldReader.ContainerType => DamageableObjectKind.Container,
            Arcanum.Data.Combat.CombatFieldReader.SceneryType => DamageableObjectKind.Scenery,
            Arcanum.Data.Combat.CombatFieldReader.TrapType => DamageableObjectKind.Trap,
            _ => null,
        };
        if (kind is not { } k || !Enum.IsDefined((ObjectMaterial)common.Material)) return null;
        var scenery = k == DamageableObjectKind.Scenery;
        var breakable = scenery || k == DamageableObjectKind.Trap;
        var baseline = new SessionObjectBaseline
        {
            Kind = k,
            Points = common.HitPoints,
            Adjustment = common.HitPointAdjustment,
            Damage = Math.Max(0, common.HitPointDamage),
            Material = (ObjectMaterial)common.Material,
            Resistances = Resistances(common.Resistances),
            Invulnerable = (common.Flags & InvulnerableObject) != 0,
            Window = k == DamageableObjectKind.Portal && common.CurrentArt >> 28 == 3 && (common.CurrentArt & 0x400) == 0,
            JunkPile = k == DamageableObjectKind.Container && common.Prototype == JunkPilePrototypeFile,
            Respawning = scenery && (fields.TypeFlags & RespawnableScenery) != 0,
            RespawnDelay = scenery ? fields.RespawnDelay : 0,
            HasDestroyedArt = breakable && fields.DestroyedArt != Arcanum.Data.Combat.CombatBreakableFields.NoArt,
            Busted = breakable && (fields.TypeFlags & BustedFlag) != 0,
            Trapped = trapped,
            Cell = cell,
            Openable = k is DamageableObjectKind.Portal or DamageableObjectKind.Container ? openable : null,
        };
        return ValidBaseline(baseline) ? baseline : null;
    }

    /// <summary>An item's material, resistances and invulnerable flag for an attack on it where it lies
    /// (<see cref="SessionObjectAttack.ItemFacts"/>); null for an object that is not an item or a material outside the fourteen.</summary>
    public static SessionObjectBaseline? ItemFacts(Arcanum.Data.Combat.CombatFields fields, bool trapped = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Item is null || !Enum.IsDefined((ObjectMaterial)fields.Object.Material)) return null;
        return new SessionObjectBaseline
        {
            Kind = DamageableObjectKind.Item,
            Material = (ObjectMaterial)fields.Object.Material,
            Resistances = Resistances(fields.Object.Resistances),
            Invulnerable = (fields.Object.Flags & InvulnerableObject) != 0,
            Trapped = trapped,
        };
    }

    // Stored resistances are in the source's resistance order: normal, fire, electrical, poison, magic.
    private static DamageResistances Resistances(IReadOnlyList<int> stored) => new(stored[0], stored[3], stored[2], stored[1]);

    private static bool ValidBaseline(SessionObjectBaseline b) => GameSessionOwner.ValidObjectFacts(b) &&
        (b.Kind != DamageableObjectKind.Portal || b.Openable is not null) && (b.Openable is null || b.Openable.Value.JunkPile == b.JunkPile);
}

/// <summary>A non-critter object of a map: its runtime map, its session entity id and its baseline.</summary>
public sealed record SessionObjectTarget(SessionMapId Map, string Entity, SessionObjectBaseline Baseline);

/// <summary>An object's current state: its hit-point damage, current hit points and condition (recorded, or the baseline's).</summary>
public readonly record struct SessionObjectState(int Damage, int HitPoints, SessionObjectCondition Condition);

/// <summary>
/// One attack of the hero on a non-critter object (<see cref="GameSessionOwner.AttackObject"/>): a portal, container, scenery or trap
/// (<see cref="Target"/>) or an item on the ground (<see cref="Item"/> with its durability baseline). <see cref="Aim"/>,
/// <see cref="Distance"/> and <see cref="HeroWorn"/> as for <see cref="SessionAttack"/>.
/// </summary>
public sealed record SessionObjectAttack
{
    public SessionObjectTarget? Target { get; init; }
    public SessionWearItem? Item { get; init; }
    /// <summary>For an item: its stored material and resistances (normal, fire, electrical, poison, magic) and the invulnerable flag.</summary>
    public SessionObjectBaseline? ItemFacts { get; init; }
    public HitLocation? Aim { get; init; }
    public int Distance { get; init; } = 1;
    public IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? HeroWorn { get; init; }
    /// <summary>The actor's map for a critical self-hit's drops (ARC-COMBAT-010, as <see cref="SessionAttack.DropMap"/>).</summary>
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
}

/// <summary>What the host does after an object hit: the scripts, sounds and visual effects the session does not run.</summary>
[Flags]
public enum SessionObjectHostSteps
{
    None = 0,
    /// <summary>The object's trap (its use script, slot 1) runs with the hero as triggerer, before the break (a ranged attack would
    /// pass no triggerer).</summary>
    TrapFires = 1 << 0,
    /// <summary>The object's BUST script (point 30) runs with the hero as triggerer.</summary>
    BustScript = 1 << 1,
    /// <summary>The bust sound (the misc sound of a scenery or trap, the item's).</summary>
    BustSound = 1 << 2,
    /// <summary>The destroy sound.</summary>
    DestroySound = 1 << 3,
    /// <summary>The container's spill eye candy (1203) on the first container or item at its cell.</summary>
    SpillEyeCandy = 1 << 4,
    /// <summary>The witnesses of the hero's attack on an object it left whole (0x4AEE50 with 1: NPCs near that guard the object's
    /// faction lower their reaction by 20, then may turn hostile).</summary>
    WitnessesHearAttack = 1 << 5,
    /// <summary>The witnesses of the hero breaking the object (0x4AEE50 with 0: possible hostility).</summary>
    WitnessesHearBreak = 1 << 6,
}

/// <summary>
/// The result of <see cref="GameSessionOwner.AttackObject"/>: the resolution (null when refused before the rolls); for a hit on the
/// object, its hit outcome (<see cref="Object"/>, a portal, container, scenery or trap) or the item's durability result
/// (<see cref="Item"/>); for a critical self-hit the hero's life outcome; the attack's wear (the hero's weapon only while the object
/// stayed whole, armour on a self-hit); and what the host does (<see cref="Host"/>). A later step that was refused is
/// <see cref="StepFailure"/>; the steps before it stay applied.
/// </summary>
public sealed record SessionObjectAttackResult(GameSessionDisposition Disposition, GameSessionFailure Failure, AttackResolution? Resolution,
    ObjectHitOutcome? Object, ItemDurabilityResult? Item, LifeOutcome? Life, SessionAttackWearResult? Wear, SessionObjectHostSteps Host,
    GameSessionFailure StepFailure, GameSessionVersion Version)
{
    private readonly IReadOnlyList<SessionCriticalStep>? _criticals;

    /// <summary>The critical item steps of a critical self-hit on the hero (ARC-COMBAT-009); empty otherwise.</summary>
    public IReadOnlyList<SessionCriticalStep> Criticals { get => _criticals ?? Array.Empty<SessionCriticalStep>(); init => _criticals = value; }
}

/// <summary>
/// ARC-COMBAT-008: the hero's attack on a non-critter object, and the hit-point damage and condition of portals, containers, scenery
/// and traps on any map (contract docs/world/object-damage-runtime.md, ledger "Object damage"). An object's state is its T0 baseline
/// until a hit; the hit records its damage and condition as map changes of its entity (<see cref="ObjectDamageProperty"/>,
/// <see cref="ObjectConditionProperty"/>), which the snapshot already carries, so the domain adds no schema. A busted window takes the
/// lock domain's busted bit on frame 0 (ARC-WORLD-051) in the same change; a spilled container's items lie on the ground at its cell
/// (<see cref="QueryMapInventory"/>, <see cref="PickUpItem"/>); a respawning scenery comes back by its respawn event
/// (<see cref="SessionEventKinds.ObjectRespawn"/>). Items on the ground take the hit through <see cref="DamageItem"/>.
/// </summary>
public sealed partial class GameSessionOwner
{
    public const string ObjectDamageProperty = "object.damage", ObjectConditionProperty = "object.condition";

    /// <summary>The condition the session recorded for an object of <paramref name="kind"/> (a condition the kind does not have counts
    /// as none), or <see cref="SessionObjectCondition.Whole"/> without one: what a host draws (a destroyed or switched-off object is
    /// not drawn) without the object's baseline. Throws for an invalid map or entity.</summary>
    public SessionObjectCondition QueryObjectCondition(SessionMapId map, string entity, DamageableObjectKind kind)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map) || !SessionValidation.Entity(entity) || !Enum.IsDefined(kind))
            throw new ArgumentException("Invalid map, entity or kind.", nameof(entity));
        return RecordedObject(_maps, map, entity) is { Condition: { } condition } && Admits(kind, condition) ? condition : SessionObjectCondition.Whole;
    }

    /// <summary>The object's current state: its recorded damage and condition, or its baseline's.</summary>
    public SessionObjectState QueryObject(SessionObjectTarget target)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (!ValidObject(target)) throw new ArgumentException("Invalid object or baseline.", nameof(target));
        return ObjectState(target);
    }

    /// <summary>
    /// The hero attacks a non-critter object once (the original's attack, then the object branch of 0x4B4390 and the break 0x43F1C0):
    /// the attack is resolved (<see cref="AttackResolver.Resolve"/> with the object as a portal, container or other object: no skill
    /// roll, its stored resistances clamped 0..95, a portal's or container's material reduction, the game difficulty), then its
    /// hit-point damage (at least 0) is added to the object's: at current hit points 0 or less a trapped object's trap fires (the
    /// host's) and the object breaks (<see cref="ObjectDamageRules.Break"/>; an item through <see cref="DamageItem"/>). An object the
    /// hit left whole wears the hero's weapon by its material (<see cref="ApplyAttackWear"/>); a critical self-hit hurts the hero and
    /// wears its own armour. Refused before any roll, in order: no hero (<see cref="GameSessionFailure.AttackUnavailable"/>), an
    /// invalid target (<see cref="GameSessionFailure.InvalidObject"/>), an object destroyed or switched off or an item no longer on
    /// the ground (<see cref="GameSessionFailure.ObjectUnavailable"/>), hero items that do not hold
    /// (<see cref="GameSessionFailure.InvalidWear"/>), an aim at the torso, a distance outside 0..reach
    /// (<see cref="GameSessionFailure.InvalidAttack"/>), a weapon that is not melee (<see cref="GameSessionFailure.AttackUnsupported"/>),
    /// the version, the revision, and for a respawning scenery a full event budget. The resolution's draws are spent once it ran,
    /// also when nothing changes (a hit for 0 on an object whose damage is recorded); each later step commits its own revision.
    /// Not modelled here: the taking-damage script, the trap and BUST scripts, sounds, eye candy, the guards' reaction and the
    /// inventory source of a container never opened.
    /// </summary>
    public SessionObjectAttackResult AttackObject(GameSessionVersion expected, SessionObjectAttack attack)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(attack);
        if (_hero is null) return RejectObject(GameSessionFailure.AttackUnavailable);
        if ((attack.Target is null) == (attack.Item is null)) return RejectObject(GameSessionFailure.InvalidObject);
        AttackTargetKind kind;
        Combatant defender;
        if (attack.Target is { } target)
        {
            if (!ValidObject(target)) return RejectObject(GameSessionFailure.InvalidObject);
            var state = ObjectState(target);
            if (state.Condition is SessionObjectCondition.Destroyed or SessionObjectCondition.Off) return RejectObject(GameSessionFailure.ObjectUnavailable);
            kind = target.Baseline.Kind switch
            {
                DamageableObjectKind.Portal => AttackTargetKind.Portal,
                DamageableObjectKind.Container => AttackTargetKind.Container,
                _ => AttackTargetKind.OtherObject,
            };
            // A scenery the session busted became invulnerable at its break (its object flag 0x400000).
            defender = ObjectDefender(target.Baseline, target.Baseline.Invulnerable || target.Baseline.Kind == DamageableObjectKind.Scenery &&
                RecordedObject(_maps, target.Map, target.Entity) is { Condition: SessionObjectCondition.Busted });
        }
        else
        {
            var item = attack.Item!;
            if (attack.ItemFacts is not { Kind: DamageableObjectKind.Item } facts || !ValidFacts(facts) || !SessionValidation.Entity(item.Entity) ||
                !ItemDurabilityRules.IsValid(item.Baseline)) return RejectObject(GameSessionFailure.InvalidObject);
            if (!OnGround(item.Entity)) return RejectObject(GameSessionFailure.ObjectUnavailable);
            kind = AttackTargetKind.OtherObject;
            defender = ObjectDefender(facts, facts.Invulnerable || item.Invulnerable);
        }

        var hero = QueryHeroAttack()!;
        var heroSide = new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = attack.HeroWorn };
        var objectSide = new SessionWearSide();
        if (!ValidWear(heroSide, objectSide, targetBroken: false, wear: null)) return RejectObject(GameSessionFailure.InvalidWear);
        if (attack.HeroWorn?.TryGetValue(Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, out var wielded) == true && hero.Weapon.Kind == WeaponKind.Weapon)
            hero = hero with { Weapon = hero.Weapon with { ArtSubtype = wielded.ArtSubtype } };
        if (attack.Aim is HitLocation.Torso || attack.Aim is { } aim && !Enum.IsDefined(aim) || attack.Distance < 0 || attack.Distance > hero.Range)
            return RejectObject(GameSessionFailure.InvalidAttack);
        if (!hero.Supported) return RejectObject(GameSessionFailure.AttackUnsupported);
        if (!IsCurrent(expected)) return RejectObject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return RejectObject(GameSessionFailure.RevisionExhausted);
        if (attack.Target is { Baseline: { Kind: DamageableObjectKind.Scenery, Respawning: true } } && !RespawnFits())
            return RejectObject(GameSessionFailure.EventBudgetExceeded);

        var request = new AttackRequest
        {
            Attacker = hero.Combatant,
            Target = defender,
            TargetKind = kind,
            Weapon = hero.Weapon,
            Skill = hero.Skill,
            SkillLevel = hero.SkillLevel,
            Training = hero.Training,
            AimedLocation = attack.Aim,
            Distance = attack.Distance,
            Difficulty = _difficulty,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        try { resolution = AttackResolver.Resolve(request, rolls, OriginalCriticalEffects.Instance, _rules.Combat); }
        catch (TrialCombatRolls.ExhaustedException) { return RejectObject(GameSessionFailure.RandomExhausted); }
        catch (ArgumentException) { return RejectObject(GameSessionFailure.InvalidAttack); }
        _random = rolls.Trial;
        if (resolution.Fate != FateUse.None)
        {
            ClearUsedAttackFate(resolution.Fate);
            _revision++;
        }
        var changed = resolution.Fate != FateUse.None;
        if (resolution.Refusal != AttackRefusal.None || resolution.Damage is not { Prevented: DamagePrevention.None } damage)
            return new(changed ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None, resolution, null, null,
                null, null, SessionObjectHostSteps.None, GameSessionFailure.None, Version);

        if (damage.Recipient == DamageRecipient.Attacker)
        {
            // A critical self-hit: the hero takes the damage and its own armour and weapon the wear (ARC-COMBAT-007).
            var applied = ApplyDamage(Version, SessionLifeTarget.Hero, LifeDamage.FromAttack(resolution)!);
            if (applied.Disposition == GameSessionDisposition.Rejected)
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, SessionObjectHostSteps.None, applied.Failure, Version);
            var criticals = new List<SessionCriticalStep>();
            var (selfWear, selfFailure) = CriticalAndWearSteps(resolution, applied.Outcome!, heroSide, objectSide, null, null, attack.DropMap, criticals);
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, applied.Outcome, selfWear, SessionObjectHostSteps.None,
                selfFailure, Version) { Criticals = criticals };
        }

        var amount = Math.Clamp(damage.HitPointDamage, 0, ItemDurabilityLimits.MaxDamageStep);
        ObjectHitOutcome? hit = null;
        ItemDurabilityResult? itemResult = null;
        var host = SessionObjectHostSteps.None;
        bool broken;
        if (attack.Target is { } struck)
        {
            var state = ObjectState(struck);
            var facts = Facts(struck.Baseline, state.Condition);
            hit = ObjectDamageRules.Hit(facts, state.Damage, amount);
            var write = ObjectWrite(struck, hit);
            if (write.Disposition == GameSessionDisposition.Rejected)
                return new(Disposition(changed), GameSessionFailure.None, resolution, hit, null, null, null, SessionObjectHostSteps.None, write.Failure, Version);
            broken = hit.Broken;
            host = HostSteps(struck.Baseline, hit);
        }
        else
        {
            var item = attack.Item!;
            itemResult = DamageItem(Version, item.Entity, item.Baseline, amount);
            if (itemResult.Value.Disposition == GameSessionDisposition.Rejected)
                return new(Disposition(changed), GameSessionFailure.None, resolution, null, itemResult, null, null, SessionObjectHostSteps.None,
                    itemResult.Value.Failure, Version);
            broken = itemResult.Value.Break != ItemBreakStep.None;
            host = broken ? SessionObjectHostSteps.WitnessesHearBreak : SessionObjectHostSteps.WitnessesHearAttack;
            if (broken && attack.ItemFacts!.Trapped) host |= SessionObjectHostSteps.TrapFires;
            host |= itemResult.Value.Break switch
            {
                ItemBreakStep.Broken => SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound,
                ItemBreakStep.Deleted => SessionObjectHostSteps.DestroySound,
                _ => SessionObjectHostSteps.None,
            };
        }
        var wear = resolution.Wear is { Any: true }
            ? WearSteps(new SessionAttackWear { Resolution = resolution, Attacker = heroSide, Target = objectSide, TargetBroken = broken })
            : null;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, hit, itemResult, null, wear, host, WearFailure(wear), Version);
    }

    // The original's respawn event (kind 33; 0x43F5F0): the scenery is switched on again (its off state and respawn mark cleared).
    [Arcanum.Simulation.OriginalFunction(0x43F5F0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireObjectRespawn(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0];
        var map = new SessionMapId(entity.Map);
        if (RecordedObject(_maps, map, entity.Code) is not { Condition: SessionObjectCondition.Off }) return GameEventHandlerOutcome.Refused;
        var result = ApplyChanges(Version, null, [new(map, MapChangeKey.ForEntity(entity.Code, ObjectConditionProperty), null)]);
        if (result.Disposition == GameSessionDisposition.Rejected) return GameEventHandlerOutcome.Refused;
        _passRespawns?.Add(new(firing.Event, map, entity.Code));
        return GameEventHandlerOutcome.Completed;
    }

    // The queue's existence check for a respawn event's scenery when the host cannot answer it: the session's own record.
    private bool RespawnObjectExists(GameEventEntity entity) =>
        RecordedObject(_maps, new(entity.Map), entity.Code) is { Condition: SessionObjectCondition.Off };

    /// <summary>The cell where a spilled container's items lie, or null when the holder is no container of the seed or did not spill
    /// (ARC-COMBAT-008).</summary>
    internal CellCoord? SpillCell(SessionMapId map, InventoryDomain.Seed seed, string holder) =>
        seed.Holders.TryGetValue(holder, out var row) && row.Kind == SessionHolderKind.Container &&
        RecordedObject(_maps, map, holder) is { Condition: SessionObjectCondition.Destroyed, Spill: { } cell } ? cell : null;

    /// <summary>The spill cell of a seed item whose container spilled, or null.</summary>
    internal CellCoord? SpilledCell(SessionMapId map, InventoryDomain.Seed seed, string item) =>
        seed.HolderOf.TryGetValue(item, out var holder) ? SpillCell(map, seed, holder) : null;

    private GameSessionMutationResult ObjectWrite(SessionObjectTarget target, ObjectHitOutcome hit)
    {
        var condition = hit.Break switch
        {
            ObjectBreak.Destroyed or ObjectBreak.ContainerSpilled => SessionObjectCondition.Destroyed,
            ObjectBreak.SceneryBusted or ObjectBreak.TrapBusted => SessionObjectCondition.Busted,
            ObjectBreak.SceneryRespawns => SessionObjectCondition.Off,
            _ => ObjectState(target).Condition,
        };
        var edits = new List<MapChangeEdit>
        {
            new(target.Map, MapChangeKey.ForEntity(target.Entity, ObjectDamageProperty), hit.DamageAfterBreak),
            new(target.Map, MapChangeKey.ForEntity(target.Entity, ObjectConditionProperty),
                condition == SessionObjectCondition.Whole ? null : (long)condition | (hit.Break == ObjectBreak.ContainerSpilled ? SpillValue(target.Baseline.Cell) : 0)),
        };
        // A busted window: the lock domain's busted bit on frame 0, as SetOpenableBusted writes it (ledger LK12).
        if (hit.Break == ObjectBreak.WindowBusted && target.Baseline.Openable is { } openableBaseline)
        {
            var openable = new SessionOpenable(target.Map, target.Entity);
            var (flags, _) = Current(openable, openableBaseline);
            edits.AddRange(Edits(openable, openableBaseline.Kind, flags | LockRules.BustedBit, 0));
        }
        var applied = ApplyChanges(Version, null, edits);
        if (applied.Disposition != GameSessionDisposition.Changed || hit.Break != ObjectBreak.SceneryRespawns) return applied;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var delay = ObjectDamageRules.RespawnDelayMs(target.Baseline.RespawnDelay);
        var scheduled = _events.ScheduleAfter(SessionEventKinds.ObjectRespawn,
            GameEventPayload.Of([new(target.Map.RuntimeMap, target.Entity)], now, delay), delay, new(0, now, 0));
        if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("An admitted respawn event could not be scheduled.");
        return applied;
    }

    private bool RespawnFits()
    {
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        return _events.QueryPending().Count(e => e.Kind == SessionEventKinds.ObjectRespawn) < SessionEventKinds.MaxObjectRespawns &&
            _events.Count < _events.Limits.MaxPending && _events.NextSequence < long.MaxValue && now <= long.MaxValue - SessionEventKinds.MaxRespawnDelayMs;
    }

    private static SessionObjectHostSteps HostSteps(SessionObjectBaseline baseline, ObjectHitOutcome hit)
    {
        if (!hit.Broken) return SessionObjectHostSteps.WitnessesHearAttack;
        var steps = SessionObjectHostSteps.WitnessesHearBreak;
        if (baseline.Trapped) steps |= SessionObjectHostSteps.TrapFires;
        return steps | hit.Break switch
        {
            ObjectBreak.WindowBusted or ObjectBreak.SceneryRespawns => SessionObjectHostSteps.BustScript,
            ObjectBreak.SceneryBusted or ObjectBreak.TrapBusted => SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound,
            ObjectBreak.ContainerSpilled => SessionObjectHostSteps.SpillEyeCandy,
            ObjectBreak.Destroyed => baseline.Kind == DamageableObjectKind.Portal ? SessionObjectHostSteps.None : SessionObjectHostSteps.DestroySound,
            _ => SessionObjectHostSteps.None,
        };
    }

    private static Combatant ObjectDefender(SessionObjectBaseline facts, bool invulnerable) => new()
    {
        Material = facts.Material,
        // The object resistance getter (0x43D6D0) clamps a non-critter's stored values to 0..95.
        Resistances = new(Math.Clamp(facts.Resistances.Normal, 0, 95), Math.Clamp(facts.Resistances.Poison, 0, 95),
            Math.Clamp(facts.Resistances.Electrical, 0, 95), Math.Clamp(facts.Resistances.Fire, 0, 95)),
        Conditions = invulnerable ? CombatantConditions.Invulnerable : CombatantConditions.None,
    };

    private static DamageableObject Facts(SessionObjectBaseline baseline, SessionObjectCondition condition) => new()
    {
        Kind = baseline.Kind,
        MaxHitPoints = baseline.Points + baseline.Adjustment,
        Window = baseline.Window,
        JunkPile = baseline.JunkPile,
        Respawning = baseline.Respawning,
        HasDestroyedArt = baseline.HasDestroyedArt,
        Busted = baseline.Busted || condition == SessionObjectCondition.Busted,
    };

    private SessionObjectState ObjectState(SessionObjectTarget target)
    {
        var recorded = RecordedObject(_maps, target.Map, target.Entity);
        var damage = recorded?.Damage ?? target.Baseline.Damage;
        // A recorded condition counts only where the kind has it (busted: scenery and traps; off: scenery); another one, as only
        // a crafted save or a host's other use of the entity writes, is ignored.
        var condition = recorded?.Condition is { } written && Admits(target.Baseline.Kind, written) ? written
            : target.Baseline.Busted ? SessionObjectCondition.Busted : SessionObjectCondition.Whole;
        return new(damage, target.Baseline.Points + target.Baseline.Adjustment - damage, condition);
    }

    private static bool Admits(DamageableObjectKind kind, SessionObjectCondition condition) => condition switch
    {
        SessionObjectCondition.Busted => kind is DamageableObjectKind.Scenery or DamageableObjectKind.Trap,
        SessionObjectCondition.Off => kind == DamageableObjectKind.Scenery,
        _ => true,
    };

    // The recorded damage and condition of an object (a condition value out of range counts as none); a spilled container's
    // condition word carries its cell above the condition.
    private static (int? Damage, SessionObjectCondition? Condition, CellCoord? Spill)? RecordedObject(
        IReadOnlyDictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps, SessionMapId map, string entity)
    {
        if (!SessionValidation.Map(map) || !SessionValidation.Entity(entity) || !maps.TryGetValue(map, out var changes)) return null;
        int? damage = changes.TryGetValue(MapChangeKey.ForEntity(entity, ObjectDamageProperty), out var d) && d is >= 0 and <= ItemDurabilityLimits.MaxDamage
            ? (int)d : null;
        SessionObjectCondition? condition = null;
        CellCoord? spill = null;
        if (changes.TryGetValue(MapChangeKey.ForEntity(entity, ObjectConditionProperty), out var word) && word >= 0)
        {
            var value = (SessionObjectCondition)(word & 0xFF);
            if (value is SessionObjectCondition.Destroyed or SessionObjectCondition.Busted or SessionObjectCondition.Off) condition = value;
            if (word >> 8 is var packed and > 0) spill = new CellCoord((int)((packed - 1) & 0xFFFFFF), (int)(((packed - 1) >> 24) & 0xFFFFFF));
        }
        return damage is null && condition is null ? null : (damage, condition, spill);
    }

    // The spill cell above the condition byte (each coordinate in 24 bits, +1 so a cell at 0,0 is told from none).
    private static long SpillValue(CellCoord cell) => (((long)cell.Y << 24 | (uint)cell.X) + 1) << 8;

    private bool OnGround(string item) =>
        _inventory.Seeds.Any(pair => !_inventory.Taken.Contains(item) && LooseCell(pair.Key, pair.Value, item) is not null);

    private static bool ValidObject(SessionObjectTarget target) =>
        target is not null && target.Baseline is not null && SessionValidation.Map(target.Map) && SessionValidation.Entity(target.Entity) &&
        target.Entity != SessionEventKinds.AbsentRole && target.Baseline.Kind != DamageableObjectKind.Item && ValidFacts(target.Baseline) &&
        (target.Baseline.Openable is null ? target.Baseline.Kind is not DamageableObjectKind.Portal
            : target.Baseline.Kind is DamageableObjectKind.Portal or DamageableObjectKind.Container &&
              target.Baseline.Openable.Value.Kind == (target.Baseline.Kind == DamageableObjectKind.Portal ? OpenableKind.Portal : OpenableKind.Container) &&
              target.Baseline.Openable.Value.JunkPile == target.Baseline.JunkPile &&
              ValidOpenable(new(target.Map, target.Entity), target.Baseline.Openable.Value));

    internal static bool ValidObjectFacts(SessionObjectBaseline b) => ValidFacts(b);

    private static bool ValidFacts(SessionObjectBaseline b)
    {
        static bool Bounded(int value) => Math.Abs((long)value) <= ItemDurabilityLimits.MaxMagnitude;
        if (!Enum.IsDefined(b.Kind) || !Enum.IsDefined(b.Material) || !Bounded(b.Points) || !Bounded(b.Adjustment) ||
            b.Damage is < 0 or > ItemDurabilityLimits.MaxDamage) return false;
        var r = b.Resistances;
        if (new[] { r.Normal, r.Poison, r.Electrical, r.Fire }.Any(v => !Bounded(v))) return false;
        if (b.Window && b.Kind != DamageableObjectKind.Portal || b.JunkPile && b.Kind != DamageableObjectKind.Container ||
            (b.Respawning || b.RespawnDelay != 0) && b.Kind != DamageableObjectKind.Scenery ||
            (b.HasDestroyedArt || b.Busted) && b.Kind is not (DamageableObjectKind.Scenery or DamageableObjectKind.Trap)) return false;
        return b.Kind != DamageableObjectKind.Container || b.Cell is { X: >= 0 and < 1 << 24, Y: >= 0 and < 1 << 24 };
    }

    private static GameSessionDisposition Disposition(bool changed) => changed ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange;

    private static GameSessionFailure WearFailure(SessionAttackWearResult? wear) =>
        wear is null ? GameSessionFailure.None : wear.Disposition == GameSessionDisposition.Rejected ? wear.Failure : wear.StepFailure;

    private SessionObjectAttackResult RejectObject(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, null, null, null, SessionObjectHostSteps.None, GameSessionFailure.None, Version);
}
