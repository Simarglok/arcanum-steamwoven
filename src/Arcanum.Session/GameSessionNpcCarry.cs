using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>ARC-MAGIC-1506: what an NPC carries as the encumbrance level reads it (0x45F790): the total weight of its list (0x461410, the NPC as holder), its effective carry weight (stat 8) and the level they give.</summary>
public sealed record SessionNpcCarry(int Weight, int CarryWeight, int Level);

/// <summary>
/// ARC-MAGIC-1506 (the strength case of the base stat setter 0x4B0980, ledger RM3b): the setter reads the encumbrance level before it stores the new strength (0x45F790: the total weight of the NPC's list, 0x461410,
/// against its effective carry weight, stat 8, which the strength gives) and runs the recalculation 0x45F820 after it, which acts only when the level changed (WE5). The session knows an NPC's list when the map's inventory
/// seed holds it (the seed's items without the ones taken or dropped, then the made items the session added; a worn item is in it), so the level is readable for such an NPC, and the carry weight from the NPC's stored stats
/// with its effect list laid over them (the poison level and crippled legs of its life state under the getter 0x4B0490; no sector aptitude, as the hero's carry takes none).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The encumbrance of an NPC of the attached seed of <paramref name="map"/> if its base strength were <paramref name="strength"/> (the other stored stats as <paramref name="stored"/> gives them), or null when the session cannot
    /// say: no seed of the map, an NPC that is no holder of it, stats or a list that do not fit the character rules, an item the weight rules refuse.
    /// </summary>
    public SessionNpcCarry? QueryNpcCarry(SessionMapId map, SessionLifeTarget target, IReadOnlyList<int> stored, int strength)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stored);
        if (target.IsHero || !NpcEffectEntity(target.Entity) || !_inventory.Seeds.TryGetValue(map, out var seed)) return null;
        if (!seed.Holders.TryGetValue(target.Entity!, out var holder) || holder.Kind != SessionHolderKind.Npc) return null;
        var life = _life.Critters.TryGetValue(target.Entity!, out var record) ? record.State : target.Loaded;
        var context = new CharacterStatContext(0, Math.Clamp(life.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), life.Conditions.HasFlag(LifeConditions.CrippledLegsBoth));
        if (NpcStats(target, stored, CharacterStat.Strength, strength) is not { } owner || owner.Query(context).Snapshot is not { } snapshot) return null;
        var aptitude = Math.Clamp(snapshot.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        try
        {
            var items = _inventory.Current(seed, holder, item => DroppedCell(map, seed, item)).Select(item => Incoming(item.Entity, item.Facts));
            var weight = CarryRules.TotalWeight(items, CarryHolder.Critter, aptitude);
            var carry = snapshot.GetEffective(CharacterStat.CarryWeight);
            return new(weight, carry, CarryRules.Level(weight, carry));
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>
    /// The rule of effect 158 the recalculation adds (<see cref="CarryRules.EffectNumber"/>): the one the host attached for the hero's recalculation (<see cref="AttachEncumbranceEffect"/>, read from the effect table), else the
    /// effect rules of the NPC lists (<see cref="NpcEffectRules"/>), else null: without a rule no entry can be added.
    /// </summary>
    internal SessionEffectDefinition? EncumbranceRule() =>
        _encumbranceEffect ?? (NpcEffectRules?.Invoke(CarryRules.EffectNumber) is { Number: CarryRules.EffectNumber } rule && EffectDomain.IsValid(rule) ? rule : null);

    /// <summary>
    /// The encumbrance recalculation of an NPC for the level it reaches (<paramref name="level"/>): every entry of effect 158 leaves its list (whatever added it) and the effect with the item cause is added
    /// <see cref="CarryRules.EffectEntries"/> times (one a level, eight at the heaviest). Two revisions when both the removal and the add change the list. Not the NPC's encumbrance fatigue timer (a level of 4 or
    /// more) nor the animation speed: the caller refuses a level with a timer.
    /// </summary>
    /// <remarks>The recalculation 0x45F820 with the removal 0x4EA200, the add 0x4E9F70, the timer 0x45E820 (mode 1) and the animation speed 0x436FA0.</remarks>
    internal SessionEffectResult RecalculateNpcEncumbrance(SessionLifeTarget target, SessionEffectDefinition rule, int level)
    {
        var removed = RemoveAllNpcEffects(Version, target, CarryRules.EffectNumber);
        if (removed.Disposition == GameSessionDisposition.Rejected) return removed;
        var entries = CarryRules.EffectEntries(level);
        return entries == 0 ? removed : AddNpcEffect(Version, target, rule, EffectCause.Item, entries);
    }
}
