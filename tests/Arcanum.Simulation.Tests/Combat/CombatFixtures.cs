using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>Plays back scripted rolls and fails on any roll out of order or left over.</summary>
internal sealed class ScriptedRolls : ICombatRollSource
{
    private readonly Queue<(CombatRollPurpose Purpose, int Value)> _queue;

    public ScriptedRolls(IEnumerable<(CombatRollPurpose Purpose, int Value)> rolls)
    {
        _queue = new Queue<(CombatRollPurpose, int)>(rolls);
    }

    public int Roll(CombatRollPurpose purpose, int min, int max)
    {
        if (_queue.Count == 0) Assert.Fail($"Unscripted roll {purpose} ({min}..{max}).");
        var (expected, value) = _queue.Dequeue();
        Assert.AreEqual(expected, purpose, "roll order");
        return value;
    }

    public void AssertConsumed() => Assert.AreEqual(0, _queue.Count, "scripted rolls left unused");
}

/// <summary>A seeded uniform source, standing in for the later session RNG.</summary>
internal sealed class SeededRolls : ICombatRollSource
{
    private readonly Random _random;

    public SeededRolls(int seed) => _random = new Random(seed);

    public int Roll(CombatRollPurpose purpose, int min, int max) => _random.Next(min, max + 1);
}

/// <summary>A critical-effect table that returns fixed effects and records the contexts it saw.</summary>
internal sealed class FixedCriticalEffects : ICriticalEffects
{
    public CriticalHitEffects Hit { get; init; }
    public CriticalMissEffects Miss { get; init; }
    public List<CriticalHitContext> HitContexts { get; } = new();
    public List<CriticalMissContext> MissContexts { get; } = new();

    public CriticalHitEffects RollHit(CriticalHitContext context, ICombatRollSource rolls)
    {
        HitContexts.Add(context);
        return Hit;
    }

    public CriticalMissEffects RollMiss(CriticalMissContext context, ICombatRollSource rolls)
    {
        MissContexts.Add(context);
        return Miss;
    }
}

internal static class CombatFixtures
{
    public const CombatRollPurpose LocationRoll = CombatRollPurpose.HitLocation;
    public const CombatRollPurpose TechnologyFailure = CombatRollPurpose.TechnologyFailure;
    public const CombatRollPurpose MagickCancel = CombatRollPurpose.MagickCancel;
    public const CombatRollPurpose AttackCheck = CombatRollPurpose.AttackCheck;
    public const CombatRollPurpose AttackCritical = CombatRollPurpose.AttackCritical;
    public const CombatRollPurpose DodgeCheck = CombatRollPurpose.DodgeCheck;
    public const CombatRollPurpose DodgeCritical = CombatRollPurpose.DodgeCritical;
    public const CombatRollPurpose DodgeCriticalMiss = CombatRollPurpose.DodgeCriticalMiss;
    public const CombatRollPurpose CriticalSelfHit = CombatRollPurpose.CriticalSelfHit;
    public const CombatRollPurpose Triple = CombatRollPurpose.DamageTriple;
    public const CombatRollPurpose DoubleRoll = CombatRollPurpose.DamageDouble;
    public const CombatRollPurpose OneAndHalf = CombatRollPurpose.DamageOneAndHalf;
    public const CombatRollPurpose Normal = CombatRollPurpose.DamageNormal;
    public const CombatRollPurpose Poison = CombatRollPurpose.DamagePoison;
    public const CombatRollPurpose Fatigue = CombatRollPurpose.DamageFatigue;

    /// <summary>A plain NPC with no skills, armour or resistances, in full light.</summary>
    public static Combatant Npc() => new() { Strength = 10, Perception = 8 };

    /// <summary>A made-up sword: normal damage 5..10, no other damage, magic or condition.</summary>
    public static CombatWeapon Sword() => new()
    {
        Kind = WeaponKind.Weapon,
        Damage = DamageRanges.None with { Normal = new DamageRange(5, 10) },
        Range = 1,
    };

    /// <summary>An NPC with melee 10 (effectiveness 75) attacking an NPC with the sword.</summary>
    public static AttackRequest Melee() => new()
    {
        Attacker = Npc(),
        Target = Npc(),
        Weapon = Sword(),
        Skill = CombatSkill.Melee,
        SkillLevel = 10,
    };

    /// <summary>The rolls before the attack check of an attack that is not aimed (torso, no aptitude events).</summary>
    public static (CombatRollPurpose, int)[] Opening(int location = 50) =>
        new[] { (LocationRoll, location), (TechnologyFailure, 100), (MagickCancel, 100) };

    /// <summary>A hit that is not critical and is not dodged by a target with dodge 0.</summary>
    public static (CombatRollPurpose, int)[] PlainHit() =>
        Opening().Concat(new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 100), (DodgeCritical, 100) }).ToArray();

    /// <summary>A critical hit (roll 1 against chance 3) that the target does not dodge.</summary>
    public static (CombatRollPurpose, int)[] CriticalHit() =>
        Opening().Concat(new[] { (AttackCheck, 1), (AttackCritical, 1), (DodgeCheck, 100), (DodgeCritical, 100) }).ToArray();

    public static (CombatRollPurpose, int)[] NoMultiplier() => new[] { (Triple, 100), (DoubleRoll, 100), (OneAndHalf, 100) };

    public static AttackResolution Run(
        AttackRequest request, IEnumerable<(CombatRollPurpose, int)> rolls, ICriticalEffects? effects = null,
        CombatAnomalyProfile? profile = null)
    {
        var script = new ScriptedRolls(rolls);
        var result = AttackResolver.Resolve(request, script, effects, profile);
        script.AssertConsumed();
        return result;
    }

    public static (CombatRollPurpose, int)[] Concat(params (CombatRollPurpose, int)[][] parts) =>
        parts.SelectMany(p => p).ToArray();
}
