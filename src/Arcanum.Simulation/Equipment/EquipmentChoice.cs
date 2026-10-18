using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Equipment;

/// <summary>
/// One item of a wearer's inventory as the best-item choice sees it (ARC-WORLD-034): its wear values, where it is worn now (null in
/// the pack), its stored worth and identified flag (an unidentified magickal item is worth 300), whether it is a throwable weapon and
/// its weight (the damage of a thrown item that is not a throwable weapon comes from its weight).
/// </summary>
public sealed record EquipmentCandidate(EquipmentItem Item)
{
    public EquipmentSlot? WornIn { get; init; }
    public int Worth { get; init; }
    public bool IdentifiedFlag { get; init; }
    public bool Throwable { get; init; }
    public int Weight { get; init; }
}

/// <summary>
/// The wearer facts the weapon score reads: its effective DamageBonus, magick/tech aptitude, the levels of the four weapon skills
/// (firearms is a technological skill), extraordinary intelligence, whether it is the local PC (difficulty applies) and the game
/// difficulty, and the ammunition it carries per ammunition type (none when absent).
/// </summary>
public sealed record EquipmentChooser
{
    public int DamageBonus { get; init; }
    public int MagickTechAptitude { get; init; }
    public int MeleeLevel { get; init; }
    public int BowLevel { get; init; }
    public int ThrowingLevel { get; init; }
    public int FirearmsLevel { get; init; }
    public bool ExtraordinaryIntelligence { get; init; }
    public bool IsPc { get; init; }
    public GameDifficulty Difficulty { get; init; } = GameDifficulty.Normal;
    public IReadOnlyDictionary<int, int> Ammunition { get; init; } = new Dictionary<int, int>();
}

/// <summary>
/// ARC-WORLD-034: which item a critter puts into a slot when it wields its best items (the retail 1.0.7.4 function at 0x465170,
/// read in the executable by two readers, verification ledger "Starting kit (ARC-WORLD-034)"). Pure functions; the session runs the
/// steps around them (taking the current item off, wielding the choice).
/// </summary>
public static class EquipmentChoice
{
    /// <summary>Unidentified items are scored as worth this.</summary>
    public const int UnidentifiedWorth = 300;
    public const int MinimumWorth = 2;
    public const int MaxWeight = 1_000_000;

    /// <summary>Whether an item counts as identified: a neutral or technological item always, a magickal one with its flag.</summary>
    [OriginalFunction(0x464370, OriginalFunctionScope.Full)]
    public static bool Identified(EquipmentItem item, bool identifiedFlag)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.MagickTechComplexity <= 0 || identifiedFlag;
    }

    /// <summary>The worth the choice compares: 300 for an unidentified item, else the stored worth, at least 2.</summary>
    [OriginalFunction(0x461F20, OriginalFunctionScope.Full)]
    public static int Worth(EquipmentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Identified(candidate.Item, candidate.IdentifiedFlag) ? Math.Max(MinimumWorth, candidate.Worth) : UnidentifiedWorth;
    }

    /// <summary>The base damage of a thrown item: a throwable weapon's own ranges; anything else normal damage
    /// <c>(weight + 99) / 100 .. (weight + 49) / 50</c> from its weight (the caller's effective weight) and no other type.</summary>
    [OriginalFunction(0x462330, OriginalFunctionScope.Partial)]
    public static DamageRanges ThrownDamage(EquipmentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Item.Weapon is { } weapon && candidate.Throwable) return weapon.Damage;
        if (candidate.Weight is < 0 or > MaxWeight) throw new ArgumentOutOfRangeException(nameof(candidate));
        return DamageRanges.None with { Normal = new((candidate.Weight + 99) / 100, (candidate.Weight + 49) / 50) };
    }

    /// <summary>
    /// A weapon's score for this wielder, or null when it lacks ammunition (an ammunition type and less carried than one use). The
    /// average of each damage type's range for the wielder (melee DamageBonus, the magic adjustments, the source's damage caps;
    /// a throwing weapon's base from <see cref="ThrownDamage"/>), summed over the five types, times the skill's effectiveness / 100.
    /// The effectiveness is the attack effectiveness of the weapon's skill without a target modifier but magnetic inversion; a skill
    /// at level 0 counts −1 except melee, and an effectiveness of 0 counts −1 too. Integer division truncates toward zero.
    /// </summary>
    public static int? WeaponScore(EquipmentCandidate candidate, EquipmentChooser wielder, bool targetMagneticInversion = false)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(wielder);
        var weapon = candidate.Item.Weapon ?? throw new ArgumentException("not a weapon", nameof(candidate));
        if (weapon.AmmoType is { } ammo && (wielder.Ammunition.TryGetValue(ammo, out var carried) ? carried : 0) < weapon.AmmoConsumption)
            return null;
        var combat = EquipmentRules.Weapon(new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = candidate.Item },
            wielder.MagickTechAptitude);
        if (weapon.Skill == CombatSkill.Throwing) combat = combat with { Damage = ThrownDamage(candidate) };
        long total = 0;
        foreach (var type in Enum.GetValues<DamageType>())
        {
            var range = AttackRules.WeaponDamageRange(combat, wielder.DamageBonus, weapon.Skill, type, magicCancelled: false);
            total += (range.Min + range.Max) / 2;
        }
        var effectiveness = Effectiveness(weapon.Skill, wielder, targetMagneticInversion);
        return (int)(total * effectiveness / 100);
    }

    /// <summary>
    /// The candidate a critter wields in <paramref name="slot"/>, by index, or null to keep what the slot holds (or none). The weapon
    /// slot takes the eligible weapon with the highest <see cref="WeaponScore"/>: the first one scored wins over nothing, a higher score
    /// wins, and on a tie the original keeps the item with the larger object handle, which the session cannot know: here the later
    /// candidate wins (candidates come in the order the items were created). With a target at <paramref name="targetDistance"/> an
    /// eligible weapon that reaches it overrides the best overall the same way. Any other slot takes the eligible item not worn
    /// anywhere whose <see cref="Worth"/> is strictly higher than <paramref name="keptWorth"/> (the worth of an item the slot keeps;
    /// null for none) and than every earlier one. <paramref name="eligible"/> is the wear check of candidate i against the current
    /// equipment, the art lookup included.
    /// </summary>
    public static int? ChooseBest(EquipmentSlot slot, IReadOnlyList<EquipmentCandidate> candidates, Func<int, bool> eligible, int? keptWorth,
        EquipmentChooser wielder, int? targetDistance = null, bool targetMagneticInversion = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(wielder);
        if (!Enum.IsDefined(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        if (targetDistance is < 0) throw new ArgumentOutOfRangeException(nameof(targetDistance));
        if (slot == EquipmentSlot.Weapon && keptWorth is not null) throw new ArgumentException("the weapon slot keeps nothing", nameof(keptWorth));
        int? best = null, bestScore = keptWorth, ranged = null, rangedScore = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!eligible(i)) continue;
            if (slot == EquipmentSlot.Weapon)
            {
                if (WeaponScore(candidate, wielder, targetMagneticInversion) is not { } score) continue;
                // Later candidates stand for larger handles, so a tie goes to the later one.
                if (bestScore is null || score >= bestScore) (best, bestScore) = (i, score);
                if (targetDistance is { } distance && (rangedScore is null || score >= rangedScore) && Range(candidate, wielder) >= distance)
                    (ranged, rangedScore) = (i, score);
            }
            else if (candidate.WornIn is null)
            {
                var worth = Worth(candidate);
                if (bestScore is null || worth > bestScore) (best, bestScore) = (i, worth);
            }
        }
        return ranged ?? best;
    }

    private static int Range(EquipmentCandidate candidate, EquipmentChooser wielder)
    {
        var weapon = candidate.Item.Weapon!;
        return weapon.Range + EquipmentRules.AdjustMagic(weapon.MagicRange, candidate.Item.MagickTechComplexity, wielder.MagickTechAptitude);
    }

    private static int Effectiveness(CombatSkill skill, EquipmentChooser wielder, bool targetMagneticInversion)
    {
        var level = skill switch
        {
            CombatSkill.Melee => wielder.MeleeLevel,
            CombatSkill.Bow => wielder.BowLevel,
            CombatSkill.Throwing => wielder.ThrowingLevel,
            CombatSkill.Firearms => wielder.FirearmsLevel,
            _ => throw new ArgumentOutOfRangeException(nameof(skill)),
        };
        if (skill != CombatSkill.Melee && level == 0) return -1;
        var value = AttackRules.AttackEffectiveness(skill, level, wielder.ExtraordinaryIntelligence, wielder.IsPc, wielder.Difficulty,
            targetMagneticInversion);
        return value == 0 ? -1 : value;
    }
}
