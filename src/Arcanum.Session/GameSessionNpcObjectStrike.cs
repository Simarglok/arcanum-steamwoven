using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

/// <summary>
/// ARC-COMBAT-1140 (ledger RA4, RH5, PF1, PF8, OD1–OD5; tail T4e): an NPC's ranged attack on a non-critter object: the door, window, container, scenery or trap (<see cref="Target"/>), or the wall (<see cref="Wall"/>),
/// that stops its line of fire and so replaces its target (RA4), or one its arrow meets past its destination; exactly one is named. <see cref="Attacker"/> is the shooter; <see cref="Distance"/> the tile distance to the object
/// (within the weapon's reach); <see cref="AttackerWorn"/> the items it wears now (for the weapon's wear on an object left whole, and a critical's drops); <see cref="AttackerCell"/> and <see cref="DropMap"/> where those drops go;
/// <see cref="Flight"/> lets a bow or firearms attack with a projectile art fly; <see cref="TakingDamage"/> the object's taking-damage script (point 0x17, GT198–GT199) as <see cref="SessionObjectAttack.TakingDamage"/>.
/// </summary>
public sealed record SessionNpcObjectAttack(SessionCritterCombat Attacker)
{
    public SessionObjectTarget? Target { get; init; }
    public bool Wall { get; init; }
    public int Distance { get; init; } = 1;
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? AttackerWorn { get; init; }
    public CellCoord? AttackerCell { get; init; }
    public IDropMap? DropMap { get; init; }
    public bool Flight { get; init; }
    public Func<bool>? TakingDamage { get; init; }
    /// <summary>ARC-SCRIPT-1483: as <see cref="SessionNpcAttack.HitScripts"/>, for a hit on an object.</summary>
    public Action<SessionHitScriptEvent>? HitScripts { get; init; }
}

public sealed partial class GameSessionOwner
{
    /// <summary>The key a shot's carried target has when it is the wall: walls share one key and take nothing (as the hero's).</summary>
    public const string NpcShotWallTarget = "wall";

    /// <summary>
    /// ARC-COMBAT-1140 (RA4, RH5, PF1, no new read: the dispatcher 0x4B3170 → the ranged path 0x4B39B0 serves any attacker): an NPC's ranged attack whose line of fire an object or a wall stops. The blocker replaces the target
    /// whatever its type (RA4) and a target that is no critter is hit without the skill roll (RH5), so the attack is decided here as the hero's object attack is (<see cref="AttackObject"/>): the weapon's reach, the ammunition of a
    /// companion (an NPC led by a PC spends its own stack, ARC-DEV-283), the resolution with the object as a portal, container or other object, the armed critical-miss fate. A bow or firearms attack whose wielded weapon has a
    /// projectile art and whose host can fly it (<see cref="SessionNpcObjectAttack.Flight"/>) is decided at the shot and its damage waits for the strike (<see cref="LandNpcShotOnObject"/>); without the art or the budget it
    /// lands at once. The damage step then is the hero's object damage with the NPC as the attacker: the object's stored resistance and a portal's or container's material reduction, the break at 0 hit points
    /// (<see cref="SessionObjectAttackResult.Host"/> names the trap, BUST script, sounds and spill the host owns), the shooter's weapon wear while the object stays whole, a critical self-hit on the shooter.
    /// Refused before any roll as <see cref="AttackObject"/> is and with <see cref="GameSessionFailure.AttackUnsupported"/> for a weapon this session cannot run.
    /// <b>Not modelled:</b> the trap's and the BUST script's NPC triggerer and breaker (they run with none, as for damage with no
    /// attacker), the weapon's item event at the struck tile (PF10), the experience of an object, the attack event on the critter the shot was aimed at.
    /// </summary>
    public SessionObjectAttackResult AttackObjectByNpc(GameSessionVersion expected, SessionNpcObjectAttack attack) => NpcObjectAttack(expected, attack, null);

    /// <summary>
    /// ARC-COMBAT-1140 (PF8, RH5, OD1–OD5): an NPC's shot in flight (<see cref="SessionNpcAttack.Flight"/>, <see cref="SessionNpcObjectAttack.Flight"/>) strikes the object (or the wall) it met on its way: the object the shot was
    /// decided for (<see cref="AttackObjectByNpc"/>) takes the carried hit; any other object the arrow met past its destination (an obstruction, PF3) a forced hit: torso, no critical, the shot's weapon magic
    /// (<see cref="AttackResolver.ForcedHit"/>). The damage is rolled now with the shooter's weapon, skill and combatant the shot carried (a shooter that died or lost its weapon since still strikes) and every step of
    /// <see cref="AttackObjectByNpc"/> after the resolution follows; the shot's ammunition went at the shot. The shot ends with the strike. Refused as <see cref="AttackObject"/> is and with
    /// <see cref="GameSessionFailure.InvalidAttack"/> for a shot that is no NPC arrow in flight, another shooter's, or a missed one striking its own target.
    /// </summary>
    public SessionObjectAttackResult LandNpcShotOnObject(GameSessionVersion expected, SessionShot shot, SessionNpcObjectAttack strike) => NpcObjectAttack(expected, strike, shot);

    private SessionObjectAttackResult NpcObjectAttack(GameSessionVersion expected, SessionNpcObjectAttack attack, SessionShot? landing)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(attack.Attacker);
        if (_hero is null) return RejectObject(GameSessionFailure.AttackUnavailable);
        if ((attack.Target is null ? 0 : 1) + (attack.Wall ? 1 : 0) != 1) return RejectObject(GameSessionFailure.InvalidObject);
        var attacker = attack.Attacker;
        var resolved = Resolve(attacker.Target, out var attackerRecord, out var attackerFacts, out _);
        if (resolved != GameSessionFailure.None) return RejectObject(resolved);
        var struckKey = attack.Wall ? NpcShotWallTarget : attack.Target!.Entity;
        NpcArrow? arrow = null;
        // The strike of a shot in flight: the shot is the NPC's; a missed shot does not strike the object it was decided for (a wall is no target of its own: walls share one key and take nothing).
        if (landing is { } shotId && (!_npcArrows.TryGetValue(shotId, out arrow) || arrow.Shooter != attacker.Entity || !arrow.Hit && arrow.Target == struckKey && !attack.Wall))
            return RejectObject(GameSessionFailure.InvalidAttack);
        AttackTargetKind kind;
        Combatant defender;
        if (attack.Wall)
        {
            kind = AttackTargetKind.Wall;
            defender = new Combatant();
        }
        else
        {
            var target = attack.Target!;
            if (!ValidObject(target)) return RejectObject(GameSessionFailure.InvalidObject);
            if (ObjectState(target).Condition is SessionObjectCondition.Destroyed or SessionObjectCondition.Off) return RejectObject(GameSessionFailure.ObjectUnavailable);
            // A scenery the session busted became invulnerable at its break (its object flag 0x400000).
            (defender, kind) = ObjectCombatOf(target);
        }
        var attackerSide = new SessionWearSide { Life = attacker.Target, Worn = attack.AttackerWorn };
        var objectSide = new SessionWearSide();
        if (!ValidWear(attackerSide, objectSide, targetBroken: false, wear: null)) return RejectObject(GameSessionFailure.InvalidWear);
        var attackerStatus = LifeRules.Evaluate(attackerRecord.State, attackerFacts!);
        SessionNpcAttackValues values;
        // The items the host says the NPC wears now: a weapon a critical dropped or destroyed no longer strikes; a shot in flight carries its own.
        if (arrow is not null) values = arrow.Values;
        else
        {
            try { values = Levelled(attacker).AttackValues(attackerStatus, attackerRecord.State, _rules.Combat, attack.AttackerWorn?.Keys); }
            catch (ArgumentException) { return RejectObject(GameSessionFailure.InvalidAttack); }
        }
        if (attack.Distance < 0 || arrow is null && attack.Distance > values.Range || attack.AttackerCell is { } from && !DropCellFits(from)) return RejectObject(GameSessionFailure.InvalidAttack);
        if (!values.Supported) return RejectObject(GameSessionFailure.AttackUnsupported);
        var weapon = values.Weapon;
        // The weapon's art subtype is item data, passed with its baseline, as for the hero's.
        if (arrow is null && attack.AttackerWorn?.TryGetValue(EquipmentSlot.Weapon, out var wielded) == true && weapon.Kind == WeaponKind.Weapon)
            weapon = weapon with { ArtSubtype = wielded.ArtSubtype };
        if (!IsCurrent(expected)) return RejectObject(GameSessionFailure.StaleVersion);
        // The fate may take a revision before the damage; the ammunition's spend another.
        if (_revision >= long.MaxValue - 2) return RejectObject(GameSessionFailure.RevisionExhausted);
        if (attack.Target is { Baseline: { Kind: DamageableObjectKind.Scenery, Respawning: true } } && !RespawnFits()) return RejectObject(GameSessionFailure.EventBudgetExceeded);
        // ARC-DEV-282 (PF1): a shot on the ranged path with a weapon that has a projectile art flies, and the hit's damage waits for the strike.
        var flies = arrow is null && attack.Flight && values.Range > 1 && weapon.Kind == WeaponKind.Weapon && attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.MissileArt is not null;
        var projectiles = flies ? ProjectileRules.Projectiles(values.Skill, values.Training) : 0;
        if (flies && ShotsInFlight > MaxShotsInFlight - projectiles) { flies = false; projectiles = 0; }
        // ARC-DEV-283 (RA3, AM4, 0x4B65D0): a companion's shot spends the weapon's ammunition from its own stack before the other rolls, all or nothing; once per attack, at the shot.
        var ammunition = arrow is null && weapon.Kind == WeaponKind.Weapon && weapon.AmmoType is { } ammoType && weapon.AmmoConsumption > 0 && PcLeads(attacker.Entity!)
            ? (Holder: new SessionAmmoHolder(attacker.Entity), Type: ammoType, Count: weapon.AmmoConsumption) : ((SessionAmmoHolder Holder, int Type, int Count)?)null;
        var unsupplied = ammunition is { } need && (need.Type is < 0 or >= SessionAmmoRecord.AmmoTypes || (NpcStackOfType(attacker.Entity!, need.Type)?.Units ?? 0) < need.Count);

        var request = new AttackRequest
        {
            Attacker = values.Combatant,
            Target = defender,
            TargetKind = kind,
            Weapon = weapon,
            Skill = values.Skill,
            SkillLevel = values.SkillLevel,
            Training = values.Training,
            Distance = arrow is null ? attack.Distance : 1,
            Difficulty = _difficulty,
            DeferHit = flies,
            AmmunitionShort = unsupplied,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        try
        {
            resolution = arrow is null ? AttackResolver.Resolve(request, rolls, OriginalCriticalEffects.Instance, _rules.Combat)
                : AttackResolver.LandHit(request, arrow.Hit && arrow.Target == struckKey ? arrow.Check : AttackResolver.ForcedHit(arrow.Check.WeaponMagicCancelled),
                    rolls, OriginalCriticalEffects.Instance, _rules.Combat);
        }
        catch (TrialCombatRolls.ExhaustedException) { return RejectObject(GameSessionFailure.RandomExhausted); }
        catch (ArgumentException) { return RejectObject(GameSessionFailure.InvalidAttack); }
        _random = rolls.Trial;
        // The shot ends with its strike (as an NPC's strike on a critter).
        if (landing is { } strikeShot) _npcArrows.Remove(strikeShot);
        // The hero's armed "critical miss" fate turns an NPC's attack into a critical miss (FT6): cleared at once, in a revision of its own.
        if (resolution.Fate != FateUse.None && arrow is null)
        {
            ClearUsedAttackFate(resolution.Fate);
            Revise();
        }
        var changed = resolution.Fate != FateUse.None && arrow is null;
        SessionAmmoUse? spentAmmo = null;
        if (ammunition is { } spend && resolution.Refusal == AttackRefusal.None)
        {
            spentAmmo = SpendAmmo(Version, spend.Holder, spend.Type, spend.Count);
            if (spentAmmo is { Disposition: GameSessionDisposition.Rejected } refused)
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, SessionObjectHostSteps.None, refused.Failure, Version) { Ammunition = refused };
        }
        changed |= spentAmmo is { Disposition: GameSessionDisposition.Changed };
        // A shot that flies is in flight from now on, hit or miss, after its ammunition went; it carries the object it was decided for.
        IReadOnlyList<SessionShot> flying = [];
        if (flies && resolution.Refusal == AttackRefusal.None)
        {
            var carried = new NpcArrow(attacker.Entity!, values with { Weapon = weapon }, resolution, resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit, struckKey,
                attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.Entity);
            var shots = new SessionShot[projectiles];
            for (var i = 0; i < projectiles; i++)
            {
                shots[i] = new SessionShot(++_nextShot);
                _npcArrows.Add(shots[i], carried);
            }
            flying = shots;
        }
        // ARC-SCRIPT-1483: the hit's combat-point scripts run before the damage step; a shot that flies asks at its strike.
        if (resolution.Refusal == AttackRefusal.None && !attack.Wall && !flies && (resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit))
            attack.HitScripts?.Invoke(new(arrow?.WeaponItem ?? attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.Entity, resolution.Outcome == AttackOutcome.CriticalHit, arrow is not null));
        // ARC-DEV-261 (GT198): an invulnerable object's script is asked too (before the invulnerable gate); a wall has none.
        if (resolution.Refusal == AttackRefusal.None && !attack.Wall && resolution.Damage is { Prevented: not DamagePrevention.None, Recipient: DamageRecipient.Target }) attack.TakingDamage?.Invoke();
        if (resolution.Refusal != AttackRefusal.None || resolution.Damage is not { Prevented: DamagePrevention.None } damage)
            return new(Disposition(changed), GameSessionFailure.None, resolution, null, null, null, null, SessionObjectHostSteps.None, GameSessionFailure.None, Version) { Ammunition = spentAmmo, Shots = flying };

        if (damage.Recipient == DamageRecipient.Attacker)
        {
            // A critical self-hit: the NPC takes the damage and its own armour and weapon the wear (as ARC-WORLD-092's attack on a critter does); it has no killer.
            var applied = ApplyDamage(Version, attacker.Target, LifeDamage.FromAttack(resolution)!);
            if (applied.Disposition == GameSessionDisposition.Rejected)
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, SessionObjectHostSteps.None, applied.Failure, Version) { Ammunition = spentAmmo, Shots = flying };
            var criticals = new List<SessionCriticalStep>();
            var (selfWear, selfFailure) = CriticalAndWearSteps(resolution, applied.Outcome!, attackerSide, objectSide, Levelled(attacker), attack.AttackerCell, attack.DropMap, criticals,
                (weapon.AmmoType, weapon.AmmoConsumption));
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, applied.Outcome, selfWear, SessionObjectHostSteps.None, selfFailure, Version)
            { Criticals = criticals, Ammunition = spentAmmo, Shots = flying, ItemEvents = applied.ItemEvents };
        }

        // ARC-DEV-261 (GT198–GT199): the object's taking-damage script comes first in the damage apply; a veto ends the whole step.
        if (attack.TakingDamage is { } asks && !asks())
            return new(Disposition(changed), GameSessionFailure.None, resolution, null, null, null, null, SessionObjectHostSteps.None, GameSessionFailure.None, Version) { Ammunition = spentAmmo, Shots = flying, Vetoed = true };
        // A hit on a wall is prevented (the damage step leaves at once for a wall), so an object is named here.
        if (attack.Target is not { } struck)
            return new(Disposition(changed), GameSessionFailure.None, resolution, null, null, null, null, SessionObjectHostSteps.None, GameSessionFailure.None, Version) { Ammunition = spentAmmo, Shots = flying };
        var amount = Math.Clamp(damage.HitPointDamage, 0, ItemDurabilityLimits.MaxDamageStep);
        var state = ObjectState(struck);
        var hit = ObjectDamageRules.Hit(Facts(struck.Baseline, state.Condition), state.Damage, amount);
        var write = ObjectWrite(struck, hit, out var kept);
        if (write.Disposition == GameSessionDisposition.Rejected)
            return new(Disposition(changed), GameSessionFailure.None, resolution, hit, null, null, null, SessionObjectHostSteps.None, write.Failure, Version) { Ammunition = spentAmmo, Shots = flying };
        // ARC-AI-2084: the witness steps stay: 0x4AEE50 hears only a PC or a PC-led NPC's damage, which the host's rule decides (the AI host's GuardWitnesses leaves any other attacker out).
        var host = HostSteps(struck.Baseline, hit, kept);
        var wear = resolution.Wear is { Any: true }
            ? WearSteps(new SessionAttackWear { Resolution = resolution, Attacker = attackerSide, Target = objectSide, TargetBroken = hit.Broken })
            : null;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, hit, null, null, wear, host, WearFailure(wear), Version) { Ammunition = spentAmmo, Shots = flying };
    }
}
