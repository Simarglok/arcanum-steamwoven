using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-092: one melee attack of an NPC (the AI's attempt-attack hit step, 0x42A630 → 0x4B3BB0 → 0x4B3170) on the hero
/// (<see cref="Target"/> null) or on another NPC, for <see cref="GameSessionOwner.AttackByNpc"/>. <see cref="Distance"/> is the tile
/// distance (within the weapon's reach); <see cref="AttackerWorn"/> and <see cref="TargetWorn"/> are the two sides' wear-slot items with
/// their durability baselines for the attack's wear (the hero's must be exactly the items it wears; null: that side's items take no
/// wear); <see cref="AttackerCell"/> and <see cref="TargetCell"/> the NPCs' cells where a critical drops their items (null: not
/// applied); <see cref="DropMap"/> the shown map for the drop's placement. The attack is never aimed (the AI's goal stores none).
/// </summary>
public sealed record SessionNpcAttack(SessionCritterCombat Attacker)
{
    public SessionCritterCombat? Target { get; init; }
    public int Distance { get; init; } = 1;
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? AttackerWorn { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? TargetWorn { get; init; }
    public CellCoord? AttackerCell { get; init; }
    public CellCoord? TargetCell { get; init; }
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
}

/// <summary>
/// ARC-WORLD-113: one throw of an NPC's item (the throw goal 37's throw, 0x42AB90 → 0x4B3C00, ledger TW rows) for
/// <see cref="GameSessionOwner.ThrowByNpc"/>: the <see cref="Thrower"/> on <see cref="ThrowerCell"/> throws <see cref="Item"/>, an item
/// of its list (in this slice the weapon it wields), at the goal's location <see cref="Target"/>; <see cref="Weight"/> is the item's
/// effective weight (0x461370: its stored weight plus its magic adjustment, the host's, as for the hero's throw), which sets its
/// thrown range. The critter the line of fire to the location names (TW4: a blocker before it, or the critter on its tile) is
/// <see cref="Struck"/> (an NPC) or the hero (<see cref="StruckHero"/>), standing on <see cref="StruckCell"/>; with neither the item
/// lands on the location unchecked. <see cref="LineOfFire"/> is the line to the struck critter (the check's cover); the worn items and
/// the drop map are <see cref="SessionNpcAttack"/>'s.
/// </summary>
public sealed record SessionNpcThrow(SessionCritterCombat Thrower, string Item, int Weight, CellCoord ThrowerCell, CellCoord Target)
{
    public SessionCritterCombat? Struck { get; init; }
    public bool StruckHero { get; init; }
    public CellCoord? StruckCell { get; init; }
    public SessionLineOfFire? LineOfFire { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? ThrowerWorn { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? StruckWorn { get; init; }
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
}

/// <summary>ARC-WORLD-113: what <see cref="GameSessionOwner.ThrowByNpc"/> did: the attack on the struck critter (null without one or
/// when refused before it) and the cell the item landed on (null: it was not thrown).</summary>
public sealed record SessionNpcThrowResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionAttackResult? Attack,
    CellCoord? Landed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    // ARC-WORLD-113: a throw's facts for the NPC attack: the item, its weight, the goal's location, the thrower's cell, the struck
    // critter's cell and the line to it.
    private sealed record NpcThrowing(string Item, int Weight, CellCoord Target, CellCoord From, CellCoord Struck, SessionLineOfFire? Line);

    /// <summary>
    /// ARC-WORLD-113 (NPC AI slice I10, the throw goal 37; ledger TW rows, TA rows): an NPC throws an item of its list at a location,
    /// decided and landed at once. In order, as the original's throw (0x4B3C00) and dispatcher (0x4B3170) for a thrower that is not a
    /// PC: with a struck critter the attack is <see cref="AttackByNpc"/>'s with the throwing skill's level and training (skill 3, TW3)
    /// and the item's thrown range (<see cref="ThrowRules.Range"/>, the check's range: impossible beyond it), the line of fire's cover,
    /// no reach test; a hit lands the item on the struck critter's cell, a miss scatters it from the location
    /// (<see cref="ThrowRules.ScatterTiles"/> with the thrower's throwing level, a rotation drawn after the check, RH2, PS3); the
    /// item leaves the NPC (worn no more) and lies on the landing cell in a revision of its own before the damage, moved there without
    /// the drop's placement, so it joins no junk pile and gets no decay (TW6, ID9); the damage, experience, critical and wear steps
    /// follow with the thrower's hand empty (its critical miss finds no weapon). Without a struck critter nothing is rolled and the item
    /// lands on the location. NOT READY: the projectile's flight (the item lands at once, no stray strike on the tiles a miss passes,
    /// PF6, and no item event at its end, PF10), a non-critter blocker (the host names none), a thrown item other than the weapon the
    /// NPC wields (generic items, grenades) or an item the session added to the NPC (no seed holds it), the throw and remove-item
    /// scripts (an item with any script point is refused) and the unwear of its item spells. Refused before any roll: no hero
    /// (<see cref="GameSessionFailure.AttackUnavailable"/>), a cell outside 0 .. 2^24 − 1, a weight outside 0 ..
    /// <see cref="ThrowRules.MaxWeight"/>, a struck critter without its cell or both struck kinds, a no-drop item
    /// (<see cref="GameSessionFailure.InvalidAttack"/>), an item not in the NPC's seed list (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// an item with scripts (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), an item that is not the wielded melee weapon
    /// (<see cref="GameSessionFailure.AttackUnsupported"/>), then <see cref="AttackByNpc"/>'s refusals (without a struck critter the
    /// thrower's: its life record, its worn items), the version, the revision. A resolver refusal throws nothing (NoChange, as the
    /// hero's), as does an inactive thrower without a struck critter.
    /// </summary>
    [OriginalFunction(0x4B3C00, OriginalFunctionScope.Partial)]
    public SessionNpcThrowResult ThrowByNpc(GameSessionVersion expected, SessionNpcThrow request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Thrower);
        SessionNpcThrowResult Refused(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
        if (_hero is null) return Refused(GameSessionFailure.AttackUnavailable);
        var struck = request.StruckHero || request.Struck is not null;
        if (!SessionValidation.Entity(request.Item) || !DropCellFits(request.ThrowerCell) || !DropCellFits(request.Target) ||
            request.StruckCell is { } at && !DropCellFits(at) || struck != request.StruckCell.HasValue || request.StruckHero && request.Struck is not null ||
            request.Weight is < 0 or > ThrowRules.MaxWeight || request.LineOfFire is { Cost: < 0 or > AttackResolver.MaxMagnitude })
            return Refused(GameSessionFailure.InvalidAttack);
        // An item a critical or an earlier throw dropped, or one destroyed, is no longer the NPC's.
        if (NpcItemSeed(request.Thrower.Entity, request.Item) is not { } listed || Gone(request.Item)) return Refused(GameSessionFailure.ItemNotAvailable);
        if (listed.Facts.ScriptPoints.Count > 0) return Refused(GameSessionFailure.ItemScriptUnsupported);
        if (listed.Facts.Kind.Type != SessionItemType.Weapon || request.ThrowerWorn?.GetValueOrDefault(EquipmentSlot.Weapon) is not { } wielded ||
            wielded.Entity != request.Item) return Refused(GameSessionFailure.AttackUnsupported);
        // The remove check (0x466DA0) keeps a no-drop item: the attack starter never throws it.
        if (wielded.NoDrop) return Refused(GameSessionFailure.InvalidAttack);
        if (!struck)
        {
            // The thrower's own checks, as the attack's: its life record, its worn items, the wielded melee weapon (TW7); an inactive
            // thrower throws nothing (the resolver's refusal on the attack's path).
            var resolved = Resolve(request.Thrower.Target, out var record, out var facts, out _);
            if (resolved != GameSessionFailure.None) return Refused(resolved);
            if (!ValidWear(new SessionWearSide { Life = request.Thrower.Target, Worn = request.ThrowerWorn }, new SessionWearSide(), targetBroken: false, wear: null))
                return Refused(GameSessionFailure.InvalidWear);
            var status = LifeRules.Evaluate(record.State, facts!);
            SessionNpcAttackValues values;
            try { values = Levelled(request.Thrower).AttackValues(status, record.State, _rules.Combat, request.ThrowerWorn?.Keys); }
            catch (ArgumentException) { return Refused(GameSessionFailure.InvalidAttack); }
            if (values.Weapon.Kind != WeaponKind.Weapon || values.Skill != CombatSkill.Melee) return Refused(GameSessionFailure.AttackUnsupported);
            if (!status.IsActive) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
            if (!IsCurrent(expected)) return Refused(GameSessionFailure.StaleVersion);
            if (_revision == long.MaxValue) return Refused(GameSessionFailure.RevisionExhausted);
            var failure = LandNpcItem(request.Item, request.Target);
            return failure == GameSessionFailure.None ? new(GameSessionDisposition.Changed, GameSessionFailure.None, null, request.Target, Version)
                : Refused(failure);
        }
        var attack = new SessionNpcAttack(request.Thrower)
        {
            Target = request.Struck,
            Distance = ProjectileRules.Distance(request.ThrowerCell, request.StruckCell!.Value),
            AttackerWorn = request.ThrowerWorn,
            TargetWorn = request.StruckWorn,
            AttackerCell = request.ThrowerCell,
            TargetCell = request.StruckHero ? null : request.StruckCell,
            DropMap = request.DropMap,
        };
        var result = NpcAttack(expected, attack,
            new NpcThrowing(request.Item, request.Weight, request.Target, request.ThrowerCell, request.StruckCell.Value, request.LineOfFire), out var landed);
        return new(result.Disposition, result.Failure, result.Disposition == GameSessionDisposition.Rejected ? null : result, landed, Version);
    }

    // ARC-WORLD-113: the NPC's thrown seed item lies on the cell (the map change a critical's drop writes), worn no more; TW6: moved
    // there (0x4EDF20 → 0x43E770), not placed by the drop, so it settles into no junk pile and schedules no decay. One revision.
    private GameSessionFailure LandNpcItem(string item, CellCoord cell)
    {
        var written = ApplyChanges(Version, null, [new(_actor.Map, MapChangeKey.ForEntity(item, ItemDroppedProperty), DroppedValue(cell))]);
        if (written.Disposition == GameSessionDisposition.Rejected) return written.Failure;
        _inventory.Unwear(item);
        return GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-WORLD-092: an NPC attacks the hero or another NPC once with its melee attack (<see cref="SessionCritterCombat.AttackValues"/>):
    /// the hit step and resolution take no test of whether the attacker is a PC (ledger EX rows of the execution study), so the steps
    /// are <see cref="AttackCritter"/>'s with the NPC as attacker: the resolution under the active rules and difficulty (rolls
    /// <c>combat.*</c>; the hero's armed "critical miss" fate turns it into a critical miss and is used up), the damage to the target, or to the attacker on a critical self-hit, then on an
    /// NPC target's hit-point damage the pool share, which the pool loses although only a PC attacker gets it (XP10), and on its death
    /// the kill award to the attacker's PC leader, else nothing and the worth stays (XP9), then the critical and wear steps with the NPC
    /// as attacker. Refused before any roll, in order: no hero (<see cref="GameSessionFailure.AttackUnavailable"/>), an attacker or
    /// target whose values do not hold, an attacker that attacks itself (<see cref="GameSessionFailure.InvalidAttack"/>), wear items
    /// that do not hold (<see cref="GameSessionFailure.InvalidWear"/>), a distance below 0 or beyond the reach, or a drop cell outside
    /// the drop's packing (<see cref="GameSessionFailure.InvalidAttack"/>), an attack other than melee
    /// (<see cref="GameSessionFailure.AttackUnsupported"/>), the version, the revision. A resolver refusal is NoChange with the
    /// resolution. Not modelled: the target's taking-damage and dying scripts, the attack event (the host's), backstab and the
    /// awareness the resolver reads (the target always sees the attacker), an NPC's ammunition and the light (every position is lit).
    /// </summary>
    public SessionAttackResult AttackByNpc(GameSessionVersion expected, SessionNpcAttack attack) => NpcAttack(expected, attack, null, out _);

    // The NPC's attack; ARC-WORLD-113: a throw (<paramref name="throwing"/>) checks with the throwing skill and thrown range and lands
    // its item (<paramref name="landed"/>).
    private SessionAttackResult NpcAttack(GameSessionVersion expected, SessionNpcAttack attack, NpcThrowing? throwing, out CellCoord? landed)
    {
        landed = null;
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(attack.Attacker);
        if (_hero is null) return RejectAttack(GameSessionFailure.AttackUnavailable);
        var attacker = attack.Attacker;
        var resolved = Resolve(attacker.Target, out var attackerRecord, out var attackerFacts, out _);
        if (resolved != GameSessionFailure.None) return RejectAttack(resolved);
        var target = attack.Target;
        var targetLife = target?.Target ?? SessionLifeTarget.Hero;
        resolved = Resolve(targetLife, out var targetRecord, out var targetFacts, out _);
        if (resolved != GameSessionFailure.None) return RejectAttack(resolved);
        if (target is not null && string.Equals(target.Entity, attacker.Entity, StringComparison.Ordinal)) return RejectAttack(GameSessionFailure.InvalidAttack);
        var attackerSide = new SessionWearSide { Life = attacker.Target, Worn = attack.AttackerWorn };
        var targetSide = new SessionWearSide { Life = targetLife, Worn = attack.TargetWorn };
        if (!ValidWear(attackerSide, targetSide, targetBroken: false, wear: null)) return RejectAttack(GameSessionFailure.InvalidWear);

        var attackerStatus = LifeRules.Evaluate(attackerRecord.State, attackerFacts!);
        SessionNpcAttackValues values;
        // The items the host says the NPC wears now: a weapon a critical dropped or destroyed no longer strikes.
        try { values = Levelled(attacker).AttackValues(attackerStatus, attackerRecord.State, _rules.Combat, attack.AttackerWorn?.Keys); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        if (attack.Distance < 0 || throwing is null && attack.Distance > values.Range || attack.AttackerCell is { } from && !DropCellFits(from) ||
            attack.TargetCell is { } to && !DropCellFits(to)) return RejectAttack(GameSessionFailure.InvalidAttack);
        if (!values.Supported) return RejectAttack(GameSessionFailure.AttackUnsupported);
        if (throwing is not null)
        {
            // TW7: the only installed "default throws" weapon is a melee one; another skill's would follow other rules.
            if (values.Weapon.Kind != WeaponKind.Weapon || values.Skill != CombatSkill.Melee) return RejectAttack(GameSessionFailure.AttackUnsupported);
            var (level, training) = Levelled(attacker).SkillValues(CombatSkill.Throwing, attackerRecord.State);
            values = values with
            {
                Skill = CombatSkill.Throwing, SkillLevel = level, Training = training,
                Weapon = values.Weapon with { Range = ThrowRules.Range(values.Combatant.Strength, training, throwing.Weight) },
            };
        }

        var targetStatus = LifeRules.Evaluate(targetRecord.State, targetFacts!);
        Combatant defender;
        if (target is null) defender = QueryHeroAttack()!.Combatant;
        else
        {
            try { defender = Levelled(target).Combatant(targetStatus, targetRecord.State, _rules.Combat); }
            catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
            // An earlier critical may have dropped or destroyed some of its map data's items (ARC-COMBAT-009).
            if (attack.TargetWorn is { } targetWorn) defender = defender with { Worn = CombatantRules.WornItems(targetWorn.Keys) };
        }
        if (!IsCurrent(expected)) return RejectAttack(GameSessionFailure.StaleVersion);
        // The fate may take a revision before the damage; a throw's landing another.
        if (_revision >= long.MaxValue - (throwing is null ? 1 : 2)) return RejectAttack(GameSessionFailure.RevisionExhausted);
        var weapon = values.Weapon;
        // The weapon's art subtype is item data, passed with its baseline, as for the hero's.
        if (attack.AttackerWorn?.TryGetValue(EquipmentSlot.Weapon, out var wielded) == true && weapon.Kind == WeaponKind.Weapon)
            weapon = weapon with { ArtSubtype = wielded.ArtSubtype };
        var striker = values.Combatant;
        var request = new AttackRequest
        {
            Attacker = striker,
            Target = defender,
            Weapon = weapon,
            Skill = values.Skill,
            SkillLevel = values.SkillLevel,
            Training = values.Training,
            Distance = attack.Distance,
            CoverRating = throwing?.Line?.Cost ?? 0,
            LineOfFireBlocked = throwing?.Line?.Blocked ?? false,
            Difficulty = _difficulty,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        int? scatterRotation = null;
        try
        {
            resolution = AttackResolver.Resolve(request, rolls, OriginalCriticalEffects.Instance, _rules.Combat);
            // ARC-WORLD-113: a missed throw's direction is drawn after the dispatcher's decisions (0x4B39B0, 0x4CEF00(0, 7)), as the hero's.
            if (throwing is not null && resolution.Refusal == AttackRefusal.None && resolution.Outcome is not (AttackOutcome.Hit or AttackOutcome.CriticalHit))
                scatterRotation = rolls.Roll(CombatRollPurpose.ThrowScatter, 0, ThrowRules.ScatterDirections - 1);
        }
        catch (TrialCombatRolls.ExhaustedException) { return RejectAttack(GameSessionFailure.RandomExhausted); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        _random = rolls.Trial;
        // The hero's armed "critical miss" fate turns an NPC's attack into a critical miss (FT6, 0x4B3170): cleared at once, in a
        // revision of its own before the damage steps, as for the hero's own fates.
        if (resolution.Fate != FateUse.None)
        {
            ClearUsedAttackFate(resolution.Fate);
            Revise();
        }
        // ARC-WORLD-113: the thrown item leaves the NPC's hand and lands at once: on the struck critter's cell for a hit, else on its
        // scattered tile; the thrower's later steps find its hand empty.
        var thrown = throwing is not null && resolution.Refusal == AttackRefusal.None;
        if (thrown)
        {
            landed = scatterRotation is { } rotation
                ? ScatterThrow(throwing!.Target, values.SkillLevel, rotation, attack.DropMap, throwing.From) : throwing!.Struck;
            var landing = LandNpcItem(throwing.Item, landed.Value);
            if (landing != GameSessionFailure.None)
            {
                landed = null;
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, landing, Version);
            }
            attackerSide = attackerSide with { Worn = attack.AttackerWorn?.Where(pair => pair.Key != EquipmentSlot.Weapon).ToDictionary(pair => pair.Key, pair => pair.Value) };
        }
        if (resolution.Refusal != AttackRefusal.None || LifeDamage.FromAttack(resolution) is not { } damage)
            return new(resolution.Fate != FateUse.None || thrown ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None,
                resolution, null, null, null, null, GameSessionFailure.None, Version);

        var recipient = resolution.Damage!.Recipient;
        var struck = recipient == DamageRecipient.Target ? target : attacker;
        var applied = ApplyDamage(Version, recipient == DamageRecipient.Target ? targetLife : attacker.Target, damage);
        if (applied.Disposition == GameSessionDisposition.Rejected)
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, null, null, null, applied.Failure, Version);
        var outcome = applied.Outcome!;
        SessionExperienceAward? killAward = null;
        var stepFailure = GameSessionFailure.None;
        if (struck is not null && _experience.Rules is not null)
        {
            var struckFacts = recipient == DamageRecipient.Target ? targetFacts! : attackerFacts!;
            var struckStatus = recipient == DamageRecipient.Target ? targetStatus : attackerStatus;
            // XP10: the pool loses the share whoever attacked; only a PC attacker gets it (a critical self-hit has none).
            if (recipient == DamageRecipient.Target && outcome.HitPointDamage > 0 && !struckFacts.Traits.HasFlag(LifeTraits.Stoned))
            {
                var hit = RecordNpcDamage(Version, struck.Experience, Math.Min(outcome.HitPointDamage, LifeLimits.MaxAmount),
                    Math.Clamp(struckStatus.MaxHitPoints, 1, LifeLimits.MaxAmount), byHero: false);
                if (hit.Disposition == GameSessionDisposition.Rejected) stepFailure = hit.Failure;
            }
            // XP9: the killer's PC leader gets the kill award; a self-hit has no killer.
            if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && outcome.Events.HasFlag(LifeEvents.Died))
            {
                var kill = RecordNpcKill(Version, struck.Experience, creditsHero: _party.Get(attacker.Entity).LedByPc);
                killAward = kill.Award;
                if (kill.Disposition == GameSessionDisposition.Rejected) stepFailure = kill.Failure;
            }
        }
        // ARC-UI-097: the hero's kill and injury log. A death the hero's follower dealt is a kill of the hero (0x4F5330, the killer's PC leader);
        // a hit that scars, blinds or cripples the hero is an injury (0x4F5690) whose cause is the attacker as the hero knows it.
        if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && struck is not null && outcome.Events.HasFlag(LifeEvents.Died)
            && _party.Get(attacker.Entity).LedByPc)
            stepFailure = RecordKillLog(Levelled(struck));
        else if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && struck is null)
            stepFailure = RecordHeroInjuries(outcome.Events, AttackerCause(attacker));
        SessionAttackWearResult? wear = null;
        var criticals = new List<SessionCriticalStep>();
        if (stepFailure == GameSessionFailure.None)
            (wear, stepFailure) = CriticalAndWearSteps(resolution, outcome, attackerSide, targetSide, struck is null ? null : Levelled(struck),
                recipient == DamageRecipient.Target ? attack.TargetCell : attack.AttackerCell, attack.DropMap, criticals);
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, outcome, null, killAward, stepFailure, Version)
        {
            Wear = wear,
            Criticals = criticals,
        };
    }

    // A follower the session levelled fights with its saved sheet's skills and level (ARC-SKILLS-002).
    private SessionCritterCombat Levelled(SessionCritterCombat npc) =>
        _advancement.Npcs.TryGetValue(npc.Entity, out var levelled) ? npc.WithSheet(levelled.Sheet) : npc;
}
