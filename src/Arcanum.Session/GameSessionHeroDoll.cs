using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-UI-1145 (the resistance function, ledger EQ8): the five resistance lines of the hero's character sheet: <paramref name="worn"/> (the worn armour's adjustments, the hero stores none) and the
    /// poison bonus of <paramref name="constitution"/> under the durable effects' resistance changes, clamped 0..95; the Magick line is the worn magic resistance under the
    /// <c>resistmagic</c> changes (the same values as the doll's armour rating, <see cref="QueryHeroDollRatings"/>). All zero without a hero.
    /// </summary>
    public (DamageResistances Damage, int Magic) QueryHeroSheetResistances(EquipmentResistances worn, int constitution)
    {
        ThrowIfDisposed();
        if (_hero is null) return (DamageResistances.None, 0);
        var effects = EffectDomain.Resistances(_effects.Entries, _effects.Definitions);
        return (CombatantRules.Resistances(DamageResistances.None, worn, constitution, false, effects), CombatantRules.MagicResistance(0, worn.Magic, false, effects.Magic));
    }

    /// <summary>
    /// ARC-UI-1267 (ledger IW25, IW41, IW43): the numbers the hero's own inventory window prints under its doll and the overlay it draws, read from the hero's session
    /// values as <see cref="SessionCritterCombat.DollValues"/> reads an NPC's: the displayed damage rating (0x464630, <see cref="DollRatings.Damage"/>) over the weapon the hero strikes
    /// with (<see cref="QueryHeroAttack"/>) with the weapon skill's effectiveness under the game's difficulty (<see cref="AttackRules.AttackEffectiveness"/>, 0x4C62E0 reads
    /// the player character's difficulty adjustment), the displayed armour rating (0x464700, <see cref="DollRatings.Armour"/>) over the display form of the armour class and the
    /// resistances (0x43D880 and 0x43D6D0 with their identified-only argument: a worn magickal item that is not <paramref name="identified"/> (by wear slot, 0x464370) gives none of
    /// its magic adjustments; the hero stores no resistance, so the magic resistance is its worn items' and its durable effects'), and whether the weapon in the weapon slot needs
    /// more strength than the hero has now (0x465E90). Null without a hero.
    /// </summary>
    public SessionDollRatings? QueryHeroDollRatings(Func<EquipmentSlot, bool> identified)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(identified);
        if (QueryHeroAttack() is not { } hero) return null;
        var state = _life.Hero!.Value.State;
        var snapshot = _hero!.Stats.Query(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth), SpeedShift: HeroTempusFugitShift())).Snapshot!;
        var effectiveness = AttackRules.AttackEffectiveness(hero.Skill, hero.SkillLevel, hero.Combatant.ExtraordinaryIntelligence, isPc: true, _difficulty,
            targetMagneticInversion: false);
        var damage = DollRatings.Damage(hero.Weapon, hero.Combatant.DamageBonus, hero.Skill, effectiveness);
        // A magickal item that is not identified shows none of its magic adjustments (an item of a neutral or technological complexity always shows them).
        var slots = _equipment.Worn.Keys.ToHashSet();
        var shown = _equipment.Items().ToDictionary(pair => pair.Key, pair => pair.Value is { MagickTechComplexity: > 0, Armor: { } armor } item && !identified(pair.Key)
            ? item with { Armor = armor with { MagicAcAdjustment = 0, MagicResistances = EquipmentResistances.None } } : pair.Value);
        var equipment = EquipmentRules.Evaluate(shown, HeroAptitude(), HeroWeaponTraining(HeroSkills()));
        var armorClass = AttackRules.ArmorClass(0, equipment.ArmorClassAdjustment, snapshot.GetEffective(CharacterStat.AcAdjustment), value => value, _rules.Combat);
        var effects = EffectDomain.Resistances(_effects.Entries, _effects.Definitions);
        var resistances = CombatantRules.Resistances(DamageResistances.None, equipment.Resistances, snapshot.GetEffective(CharacterStat.Constitution), false, effects);
        var magic = CombatantRules.MagicResistance(0, equipment.Resistances.Magic, false, effects.Magic);
        var armour = DollRatings.Armour(armorClass, resistances.Normal, resistances.Fire, resistances.Electrical, resistances.Poison, magic);
        return new(damage, armour, slots.Contains(EquipmentSlot.Weapon) && DollRatings.TooHeavy(hero.Weapon.MinimumStrength, hero.Combatant.Strength));
    }
}
