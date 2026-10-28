using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Traps;

/// <summary>What a native trap script does to one victim (ledger TT11–TT13).</summary>
public enum TrapEffectKind
{
    /// <summary>Script 30000: a spell cast at the victim with no caster and no source.</summary>
    Spell,
    /// <summary>Scripts 30001 and 30003..30006: a damage step of one damage type, with no attack roll.</summary>
    Damage,
    /// <summary>Script 30002: a shot at the victim by a shooter object near the triggerer (script 30007 with the trap's tag).</summary>
    Shot,
}

/// <summary>
/// The effect of a native trap script on each victim (ledger TT11), decoded from the script's counters: the kind, the damage type and
/// range (the counters' low byte and second byte, drawn by the original's random range per victim), the spell (the top byte: always
/// cast by 30000, cast after a damage or a shot only when not 0) and the shooter's tag of a shot (the third byte).
/// </summary>
public readonly record struct TrapEffect(int Script, TrapEffectKind Kind, DamageType DamageType, int Minimum, int Maximum, int Spell, int ShooterTag)
{
    /// <summary>Whether a spell is cast at the victim: always for 30000, otherwise when the top byte is not 0.</summary>
    public bool CastsSpell => Kind == TrapEffectKind.Spell || Spell != 0;
}

/// <summary>One object near the triggerer that may shoot for a 30002 trap (ledger TT12): its use script (slot 1) and counters.</summary>
public readonly record struct TrapShooterCandidate(string Entity, int UseScript, int Counters);

/// <summary>
/// ARC-WORLD-107: the effects of the native trap scripts 30000..30006 of the retail 1.0.7.4 executable (ledger TT11–TT14), which the
/// native run (<see cref="TrapRules.NativeRun"/>, 0x4BCB70) applies to each victim: the decoding of the counters, the shooter search
/// of a shot, the damage source and the amount. Pure functions; applying them (the damage step, the spell, the shot) is the host's.
/// </summary>
public static class TrapEffectRules
{
    /// <summary>The line of <c>mes\trap.mes</c> the run floats over the triggerer (ledger TT8).</summary>
    public const int FiredLine = 1;
    /// <summary>The script number a shooter's use script carries; the native run takes it and does nothing (ledger TT8, TT12).</summary>
    public const int ShooterScript = TrapRules.LastNativeScript;
    /// <summary>The object types the run hits (PC and NPC, the mask 0x18000 of 0x4AE4E0) and the shooter search lists (wall,
    /// portal, scenery and trap object, the mask 0x2000B of 0x440FC0).</summary>
    public const int VictimTypeMask = (1 << 15) | (1 << 16), ShooterTypeMask = 0x2000B;
    /// <summary>The combat context flags of a damage (automatic hit 0x100, ranged 0x200, and 0x80000, with which the damage step
    /// marks the hit and runs the attack end itself) and of a shot (0x100 and 0x200 only); recorded, not read by the rules.</summary>
    public const int DamageContextFlags = 0x80300, ShotContextFlags = 0x300;

    private static readonly DamageType?[] DamageByScript =
        [null, DamageType.Normal, DamageType.Normal, DamageType.Normal, DamageType.Fire, DamageType.Electrical, DamageType.Poison];

    /// <summary>
    /// The effect of <paramref name="script"/> with <paramref name="counters"/> (the jump table of 0x4BCDC0), or null for a number
    /// outside 30000..30006. 30000 casts the top byte's spell; 30001 and 30003 deal normal damage, 30004 fire, 30005 electrical and
    /// 30006 poison, each in the range of the low byte and the second byte; 30002 shoots normal damage in the same range from the
    /// shooter whose tag is the third byte; after a damage or a shot the top byte's spell is cast when it is not 0.
    /// </summary>
    [OriginalFunction(0x4BCDC0, OriginalFunctionScope.Partial)]
    public static TrapEffect? Of(int script, int counters)
    {
        var index = script - TrapRules.FirstNativeScript;
        if (index is < 0 or > 6) return null;
        var bits = unchecked((uint)counters);
        int low = (int)(bits & 0xFF), second = (int)((bits >> 8) & 0xFF), tag = (int)((bits >> 16) & 0xFF), spell = (int)(bits >> 24);
        if (index == 0) return new(script, TrapEffectKind.Spell, DamageType.Normal, 0, 0, spell, 0);
        return new(script, index == 2 ? TrapEffectKind.Shot : TrapEffectKind.Damage, DamageByScript[index]!.Value, low, second, spell,
            index == 2 ? tag : 0);
    }

    /// <summary>
    /// The shooter of a 30002 trap (0x4BD150): the first of the objects listed around the triggerer (types wall, portal, scenery and
    /// trap object in the screen-sized rectangle the original lists, in its order; the host's) whose use script is 30007 and whose
    /// counters' third byte is <paramref name="tag"/>, or null. No shooter: the shot and its spell do nothing.
    /// </summary>
    [OriginalFunction(0x4BD150, OriginalFunctionScope.Full)]
    public static string? Shooter(IEnumerable<TrapShooterCandidate> candidates, int tag)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var candidate in candidates)
            if (candidate.UseScript == ShooterScript && ((unchecked((uint)candidate.Counters) >> 16) & 0xFF) == tag) return candidate.Entity;
        return null;
    }

    /// <summary>Whether the trap's damage counts as the local player's (0x4BD950): a trap the PC placed (object flag 0x1000000)
    /// makes the player the context's damage source (the kill credit); any other trap has none.</summary>
    [OriginalFunction(0x4BD950, OriginalFunctionScope.Full)]
    public static bool CreditsPlayer(int trapObjectFlags) => (trapObjectFlags & TrapRules.PlacedByPcFlag) != 0;

    /// <summary>The amount of one victim's damage or shot: the original's random range (0x4CEF00) of the low byte and the second
    /// byte, which draws nothing for an empty range and swaps a reversed one; one draw per victim.</summary>
    public static int Amount(TrapEffect effect, ICombatRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        if (effect.Kind == TrapEffectKind.Spell) return 0;
        if (effect.Minimum == effect.Maximum) return effect.Minimum;
        return rolls.Roll(Purpose(effect.DamageType), Math.Min(effect.Minimum, effect.Maximum), Math.Max(effect.Minimum, effect.Maximum));
    }

    /// <summary>The context's damage slots: <paramref name="amount"/> in the effect's type, every other slot 0.</summary>
    public static DamageByType Damage(TrapEffect effect, int amount) => DamageByType.Zero.With(effect.DamageType, amount);

    private static CombatRollPurpose Purpose(DamageType type) => type switch
    {
        DamageType.Poison => CombatRollPurpose.DamagePoison,
        DamageType.Electrical => CombatRollPurpose.DamageElectrical,
        DamageType.Fire => CombatRollPurpose.DamageFire,
        _ => CombatRollPurpose.DamageNormal,
    };
}
