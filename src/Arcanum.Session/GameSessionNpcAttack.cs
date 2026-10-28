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

public sealed partial class GameSessionOwner
{
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
    public SessionAttackResult AttackByNpc(GameSessionVersion expected, SessionNpcAttack attack)
    {
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
        try { values = Levelled(attacker).AttackValues(attackerStatus, attackerRecord.State, _rules.Combat); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        if (attack.Distance < 0 || attack.Distance > values.Range || attack.AttackerCell is { } from && !DropCellFits(from) ||
            attack.TargetCell is { } to && !DropCellFits(to)) return RejectAttack(GameSessionFailure.InvalidAttack);
        if (!values.Supported) return RejectAttack(GameSessionFailure.AttackUnsupported);
        if (!IsCurrent(expected)) return RejectAttack(GameSessionFailure.StaleVersion);
        // The fate may take a revision before the damage.
        if (_revision >= long.MaxValue - 1) return RejectAttack(GameSessionFailure.RevisionExhausted);

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
        var weapon = values.Weapon;
        // The weapon's art subtype is item data, passed with its baseline, as for the hero's.
        if (attack.AttackerWorn?.TryGetValue(EquipmentSlot.Weapon, out var wielded) == true && weapon.Kind == WeaponKind.Weapon)
            weapon = weapon with { ArtSubtype = wielded.ArtSubtype };
        var striker = values.Combatant;
        if (attack.AttackerWorn is { } attackerWorn) striker = striker with { Worn = CombatantRules.WornItems(attackerWorn.Keys) };
        var request = new AttackRequest
        {
            Attacker = striker,
            Target = defender,
            Weapon = weapon,
            Skill = values.Skill,
            SkillLevel = values.SkillLevel,
            Training = values.Training,
            Distance = attack.Distance,
            Difficulty = _difficulty,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        try { resolution = AttackResolver.Resolve(request, rolls, OriginalCriticalEffects.Instance, _rules.Combat); }
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
        if (resolution.Refusal != AttackRefusal.None || LifeDamage.FromAttack(resolution) is not { } damage)
            return new(resolution.Fate != FateUse.None ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None,
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
