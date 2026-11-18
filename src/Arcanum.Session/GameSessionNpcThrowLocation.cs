using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-COMBAT-2523 (ledger TW11, 0x4C7160 with no defender, 0x4B3170 → 0x4B39B0, 0x4B2870): an NPC's throw that names no struck critter (a weapon of its list that "default throws", or a generic item such as a grenade) rolls as the
    /// hero's location throw does (<see cref="AttackRequest.NoDefender"/>): the hit-location roll, the technology failure and magick cancel rolls (they cannot succeed for a throw), the hit and the critical (only the distance, the
    /// line of fire's cost, the light at the tile and the thrower's own flags count, no dodge; the critical is rolled for a hit and a miss alike), the critical miss's own rolls and, for any miss, the scatter direction (0, 7). A hit flies to the
    /// location and lands striking nothing (<see cref="SessionNpcShotFacts.LocationTarget"/>); a miss flies toward its scattered tile (a thrown weapon's check is carried, so the critters it passes take a forced hit); a critical
    /// miss turns on the thrower: its damage step, then the wear and critical steps. The item leaves the NPC's hand first (a revision of its own, no decay) and lies on the thrower's cell while it flies; a host that cannot fly a thrown weapon
    /// lands it at once on the hit location or the scatter tile. A random stream that cannot give the rolls (the bounded project stream) throws the item unrolled instead of refusing it after its scripts ran.
    /// NOT READY: a generic item's flight carries no check, so a critter a missed grenade passes ends it without a strike; the self-hit's sounds, floaters and the thrower's death reaction are the host's.
    /// </summary>
    private SessionNpcThrowResult ThrowNpcAtLocation(GameSessionVersion expected, SessionNpcThrow request, bool generic, bool struck)
    {
        SessionNpcThrowResult Refused(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
        // The thrower's own checks, as the attack's: its life record, its worn items, the wielded weapon (TW7); an inactive thrower throws nothing.
        var resolved = Resolve(request.Thrower.Target, out var record, out var facts, out _);
        if (resolved != GameSessionFailure.None) return Refused(resolved);
        var throwerSide = new SessionWearSide { Life = request.Thrower.Target, Worn = request.ThrowerWorn };
        // A thrown weapon's worn items must hold (the dispatcher's own check); a grenade's host may name worn items the list does not hold (a critter's wielded weapon the seed does not list): its wear takes none then.
        if (!ValidWear(throwerSide, new SessionWearSide(), targetBroken: false, wear: null))
        {
            if (!generic) return Refused(GameSessionFailure.InvalidWear);
            throwerSide = throwerSide with { Worn = null };
        }
        var status = LifeRules.Evaluate(record.State, facts!);
        var levelled = Levelled(request.Thrower);
        SessionNpcAttackValues? values = null;
        Combatant thrower;
        int level;
        SkillTraining training;
        try
        {
            (level, training) = levelled.SkillValues(CombatSkill.Throwing, record.State);
            if (generic) thrower = levelled.Combatant(status, record.State, _rules.Combat);
            else
            {
                values = levelled.AttackValues(status, record.State, _rules.Combat, request.ThrowerWorn?.Keys);
                thrower = values.Combatant;
            }
        }
        catch (ArgumentException) { return Refused(GameSessionFailure.InvalidAttack); }
        // ARC-COMBAT-2699 (TW7, TW8): the weapon's own skill is not asked (0x42AB90), only that the attack is supported (melee, bow or firearms): a bow or firearm reaches the ammunition check.
        if (values is not null && (values.Weapon.Kind != WeaponKind.Weapon || !values.Supported)) return Refused(GameSessionFailure.AttackUnsupported);
        if (!status.IsActive) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        var distance = ProjectileRules.Distance(request.ThrowerCell, request.Target);
        if (distance < 0) return Refused(GameSessionFailure.InvalidAttack);
        if (!IsCurrent(expected)) return Refused(GameSessionFailure.StaleVersion);
        // The flight: a generic item always flies (a full shot budget refuses it); a thrown weapon flies when the host can fly it and the budget has room, else it lands at once.
        var flies = generic || request.Flight;
        if (flies && ShotsInFlight > MaxShotsInFlight - 1)
        {
            if (generic) return Refused(GameSessionFailure.BudgetExceeded);
            flies = false;
        }
        // A location throw may take three revisions before the damage (the fate, the item leaving the hand, the self-hit).
        if (_revision >= long.MaxValue - 2) return Refused(GameSessionFailure.RevisionExhausted);

        var range = ThrowRules.Range(thrower.Strength, training, request.Weight);
        CombatWeapon weapon;
        if (values is null) weapon = ThrowRules.ThrownWeapon(null, false, request.Weight, 0, range);
        else
        {
            weapon = values.Weapon with { Range = range };
            if (request.ThrowerWorn?.GetValueOrDefault(EquipmentSlot.Weapon) is { } wielded) weapon = weapon with { ArtSubtype = wielded.ArtSubtype };
        }
        // ARC-COMBAT-2415 (TW8, 0x4B65D0): a thrown weapon's ammunition check comes before any roll: a thrower led by a PC with too few units throws nothing and leaves the item on its tile switched off.
        var ammo = values is null ? null : NpcThrowAmmunition(request.Thrower.Entity!, weapon);
        if (ammo is { Short: true })
        {
            DrawHitLocationBeforeAmmunitionRefusal();
            return NpcThrowRefusedForAmmunition(request, null);
        }
        // A throw whose struck critter was named but not rolled for (a grenade flies to the location whatever the line names) carries the line of the critter, not of the location: it counts for nothing.
        var line = struck ? null : request.LineOfFire;
        var check = new AttackRequest
        {
            Attacker = thrower, Target = thrower with { LightLevel = request.TargetLight ?? AttackRules.MaxLightLevel }, TargetKind = AttackTargetKind.Critter,
            Weapon = weapon, Skill = CombatSkill.Throwing, SkillLevel = level, Training = training,
            Distance = distance, CoverRating = line?.Cost ?? 0, LineOfFireBlocked = line?.Blocked ?? false,
            Difficulty = _difficulty, DeferHit = true, NoDefender = true,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        int? scatterRotation = null;
        try
        {
            resolution = AttackResolver.Resolve(check, rolls, OriginalCriticalEffects.Instance, _rules.Combat);
            // 0x4B39B0 (0x4CEF00(0, 7)): a missed throw's direction follows the dispatcher's decisions.
            if (resolution.Refusal == AttackRefusal.None && resolution.Outcome is not (AttackOutcome.Hit or AttackOutcome.CriticalHit))
                scatterRotation = rolls.Roll(CombatRollPurpose.ThrowScatter, 0, ThrowRules.ScatterDirections - 1);
        }
        catch (TrialCombatRolls.ExhaustedException) { return ThrowNpcUnrolled(expected, request, generic, ammo); }
        catch (ArgumentException) { return Refused(GameSessionFailure.InvalidAttack); }
        if (resolution.Refusal != AttackRefusal.None) return Refused(GameSessionFailure.InvalidAttack);
        _random = rolls.Trial;
        // The fate the resolver used (FT6) is cleared at once, in a revision of its own before the item leaves.
        if (resolution.Fate != FateUse.None)
        {
            ClearUsedAttackFate(resolution.Fate);
            Revise();
        }
        var hit = resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
        var scatter = scatterRotation is { } rotation ? ScatterThrow(request.Target, level, rotation, request.DropMap, request.ThrowerCell) : (CellCoord?)null;
        var destination = scatter ?? request.Target;
        // The item leaves the thrower's hand: it lies on its cell while it flies (0x4415C0, no decay), or lands at once on the hit location or the scatter tile.
        var leaving = LandNpcItem(request.Item, flies ? request.ThrowerCell : destination);
        if (leaving != GameSessionFailure.None) return new(GameSessionDisposition.Changed, leaving, null, null, Version);
        // ARC-COMBAT-2415: the weapon's ammunition goes for a hit or a miss, before the item flies.
        SessionAmmoUse? spentAmmo = null;
        if (ammo is { } spend)
        {
            spentAmmo = SpendAmmo(Version, spend.Holder, spend.Type, spend.Count);
            if (spentAmmo is { Disposition: GameSessionDisposition.Rejected } refusedSpend)
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, refusedSpend.Failure, Version) { Ammunition = refusedSpend }, null, Version);
        }
        if (values is not null) throwerSide = throwerSide with { Worn = request.ThrowerWorn?.Where(pair => pair.Key != EquipmentSlot.Weapon).ToDictionary(pair => pair.Key, pair => pair.Value) };
        IReadOnlyList<SessionShot> shots = [];
        if (flies)
        {
            var shot = new SessionShot(++_nextShot);
            _npcShots[shot] = new NpcShot(request.Thrower.Entity!, new SessionThrownShot(request.Item, range));
            // A thrown weapon carries its check: a hit strikes nothing, a miss strikes the critters it passes with a forced hit (the strike is the weapon's as thrown). A generic item's flight is free.
            if (values is not null)
                _npcArrows[shot] = new NpcArrow(request.Thrower.Entity!, values with { Weapon = weapon, Skill = CombatSkill.Throwing, SkillLevel = level, Training = training }, resolution, hit,
                    SessionNpcShotFacts.LocationTarget, request.Item) { ThrownArt = request.ThrowerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.MissileArt };
            shots = [shot];
        }

        SessionAttackResult Attack(DamageRecipient? recipient = null, LifeOutcome? life = null, GameSessionFailure stepFailure = GameSessionFailure.None) =>
            new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, life, null, null, stepFailure, Version) { Shots = shots, Scatter = scatter, Ammunition = spentAmmo };
        SessionNpcThrowResult Done(SessionAttackResult attack) =>
            new(GameSessionDisposition.Changed, GameSessionFailure.None, attack, flies ? null : destination, Version) { Shots = shots, Destination = flies ? destination : null };

        if (resolution.Damage is null || LifeDamage.FromAttack(resolution) is not { } damage) return Done(Attack());
        // A critical miss: the throw turns on the thrower (the attacker is the victim, as in any attack's critical-miss procedure).
        var recipientOfDamage = resolution.Damage.Recipient;
        // The thrower's taking-damage script comes first in the damage apply (GT198, GT199); a veto ends the step.
        if (request.TakingDamage is { } asks && !asks(recipientOfDamage)) return Done(Attack(recipientOfDamage) with { Vetoed = true });
        var applied = ApplyDamage(Version, request.Thrower.Target, damage, killer: null);
        if (applied.Disposition == GameSessionDisposition.Rejected) return Done(Attack(recipientOfDamage) with { StepFailure = applied.Failure });
        var outcome = applied.Outcome!;
        var criticals = new List<SessionCriticalStep>();
        var (wear, stepFailure) = CriticalAndWearSteps(resolution, outcome, throwerSide, new SessionWearSide(), levelled, request.ThrowerCell, request.DropMap, criticals, (weapon.AmmoType, weapon.AmmoConsumption));
        return Done(Attack(recipientOfDamage, outcome, stepFailure) with { Wear = wear, Criticals = criticals });
    }

    // The throw without its rolls (a random stream that cannot give them): a generic item flies to the location, a weapon flies there when the host can fly it, else lands on it, as before ARC-COMBAT-2523.
    // ARC-COMBAT-2699 (TW8): the ammunition a thrown weapon takes goes at the throw whether or not its rolls could be drawn, as the hero's unrolled throw spends it (the enough-units check came before).
    private SessionNpcThrowResult ThrowNpcUnrolled(GameSessionVersion expected, SessionNpcThrow request, bool generic, (SessionAmmoHolder Holder, int Type, int Count, bool Short)? ammo)
    {
        SessionNpcThrowResult thrown;
        if (generic || request.Flight && ShotsInFlight <= MaxShotsInFlight - 1) thrown = ThrowNpcGeneric(expected, request);
        else
        {
            var failure = LandNpcItem(request.Item, request.Target);
            thrown = failure == GameSessionFailure.None ? new(GameSessionDisposition.Changed, GameSessionFailure.None, null, request.Target, Version)
                : new(GameSessionDisposition.Rejected, failure, null, null, Version);
        }
        if (ammo is not { } spend || thrown.Disposition == GameSessionDisposition.Rejected) return thrown;
        var spent = SpendAmmo(Version, spend.Holder, spend.Type, spend.Count);
        var stepFailure = spent.Disposition == GameSessionDisposition.Rejected ? spent.Failure : GameSessionFailure.None;
        return thrown with
        {
            Attack = new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, null, null, null, stepFailure, Version) { Ammunition = spent },
            Version = Version,
        };
    }
}
