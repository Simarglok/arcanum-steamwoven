using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-085: the weight facts of one object as the original's object keeps them: its stored weight (field 0x59), its magic weight
/// adjustment (field 0x5A) and its magick/tech complexity (field 0x60), which scales the adjustment by the holder's power. A seed item's
/// come from its seed's facts when the hero takes it; an object the session creates (a made item, a new stack) has its prototype's
/// (<see cref="GameSessionOwner.AttachItemWeights"/>); a kit item's come with the hero definition.
/// </summary>
public readonly record struct SessionItemWeight(int Weight, int MagicWeight = 0, int Complexity = 0)
{
    /// <summary>Weight and adjustment within ±<see cref="CarryLimits.MaxWeight"/>; any 32-bit complexity.</summary>
    public bool IsValid => Weight is >= -CarryLimits.MaxWeight and <= CarryLimits.MaxWeight &&
        MagicWeight is >= -CarryLimits.MaxWeight and <= CarryLimits.MaxWeight;
}

/// <summary>ARC-WORLD-085: one saved weight row: an object the session tracks and its weight facts. Only rows with a non-zero weight or
/// adjustment are kept; an object without a row weighs 0 and has no adjustment.</summary>
public readonly record struct SessionItemWeightRecord(string Entity, SessionItemWeight Facts);

/// <summary>
/// ARC-WORLD-085: what the hero carries: the total weight of its list (<see cref="CarryRules.TotalWeight"/> with the hero as holder,
/// worn items included, its coins weightless), its effective carry weight (stat 8) and the encumbrance level these give
/// (<see cref="CarryRules.Level"/>, 0..6). The level's consequences (effect 158, the fatigue timer, the message) are not applied yet.
/// </summary>
public sealed record SessionHeroCarry(int Weight, int CarryWeight, int Level);

public sealed partial class GameSessionOwner
{
    private Func<int, SessionItemWeight?>? _itemWeights;

    /// <summary>
    /// ARC-WORLD-085: attaches the host's weight facts by prototype (its prototype data), which the session reads for an object it creates
    /// from a prototype: an assembly product, a new ammunition stack, a made item it adds to an NPC. Not state: it is never saved and is
    /// attached again after a restore; null detaches it. Without one, or for a prototype it answers null or invalid facts for, a created
    /// object weighs 0 (the original's object would have its prototype's weight).
    /// </summary>
    public void AttachItemWeights(Func<int, SessionItemWeight?>? weights)
    {
        ThrowIfDisposed();
        _itemWeights = weights;
    }

    /// <summary>ARC-WORLD-085: the hero's carried weight, carry weight and encumbrance level, or null without a hero. The session takes
    /// the stats without a sector's aptitude adjustment (it does not know the hero's sector).</summary>
    public SessionHeroCarry? QueryHeroCarry()
    {
        ThrowIfDisposed();
        if (HeroCarryFacts() is not { } facts) return null;
        return new(facts.Weight, facts.CarryWeight, CarryRules.Level(facts.Weight, facts.CarryWeight));
    }

    /// <summary>ARC-WORLD-085: the weight facts the session keeps for an object (its row), or null when it keeps none (it weighs 0).</summary>
    public SessionItemWeight? QueryItemWeight(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _inventory.Weights.TryGetValue(entity, out var row) ? row : null;
    }

    // The hero's total weight, carry weight and aptitude now: the stats under the hero's poison and crippled legs (the getter's
    // situational adjustments), the aptitude clamped to the equipment rules' range as the attack does.
    private (int Weight, int CarryWeight, int Aptitude)? HeroCarryFacts() => HeroCarryFacts(null);

    // The same with some held objects gone (null units) or holding fewer units (an assembly's spent components, consumed before the
    // products are made, TC3).
    private (int Weight, int CarryWeight, int Aptitude)? HeroCarryFacts(IReadOnlyDictionary<string, int?>? after)
    {
        if (_hero is null) return null;
        var life = _life.Hero?.State ?? LifeState.Unharmed;
        var stats = _hero.Stats.Query(new(0, Math.Clamp(life.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel),
            life.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot;
        if (stats is null) return null;
        var aptitude = Math.Clamp(stats.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var held = HeroHeld().Select(item => (Item: Carried(item.Entity, item.Kind.Type), Left: after is not null && after.TryGetValue(item.Entity, out var left) ? left : -1))
            .Where(pair => pair.Left is not null).Select(pair => pair.Left is >= 0 and var units ? pair.Item with { Quantity = units } : pair.Item);
        var weight = CarryRules.TotalWeight(held, CarryHolder.Critter, aptitude);
        return (weight, stats.GetEffective(CharacterStat.CarryWeight), aptitude);
    }

    // A tracked object as the weight reads it: its row (none: 0), its units when it is an ammunition stack.
    private CarriedItem Carried(string entity, SessionItemType type)
    {
        var facts = _inventory.Weights.TryGetValue(entity, out var row) ? row : default;
        var units = type == SessionItemType.Ammo && _inventory.Ammo.TryGetValue(entity, out var stack) ? stack.Units : 0;
        return new((int)type, facts.Weight, facts.MagicWeight, facts.Complexity, units);
    }

    /// <summary>
    /// The insert check's weight part for the hero (0x466510, ledger WE4): an item fits when the hero's total weight plus the item's weight
    /// with the hero as holder is at most its carry weight. Gold weighs nothing, but a hero already above its carry weight takes nothing.
    /// </summary>
    private bool HeroFits(CarriedItem item) => HeroCarryFacts() is not { } facts || HeroFits(facts, item, 0);

    // The same against given facts with a pending load (earlier units of one assembly) on top of the total.
    private static bool HeroFits((int Weight, int CarryWeight, int Aptitude) facts, CarriedItem item, int pending) =>
        CarryRules.Fits(unchecked(facts.Weight + pending), CarryRules.ItemWeight(item, CarryHolder.Critter, facts.Aptitude), facts.CarryWeight);

    private static int HeroItemWeight((int Weight, int CarryWeight, int Aptitude) facts, CarriedItem item) =>
        CarryRules.ItemWeight(item, CarryHolder.Critter, facts.Aptitude);

    // An item entering a list, as the weight reads it: an object with a row its row, any other the facts it brings.
    private CarriedItem Incoming(string entity, SessionItemFacts facts) =>
        _inventory.Weights.TryGetValue(entity, out var row)
            ? new((int)facts.Kind.Type, row.Weight, row.MagicWeight, row.Complexity, facts.Kind.Type == SessionItemType.Ammo ? facts.Quantity ?? 0 : 0)
            : new((int)facts.Kind.Type, facts.Weight, facts.MagicWeight, facts.Complexity, facts.Kind.Type == SessionItemType.Ammo ? facts.Quantity ?? 0 : 0);

    // The prototype's weight facts from the attached host source; an assembly product's complexity is the one the session keeps for the
    // made item (the host's facts for the product), any other created object weighs with the prototype's. A host answer the rules refuse
    // counts as none.
    private SessionItemWeight CreatedWeight(int prototype, int? complexity = null)
    {
        var facts = _itemWeights?.Invoke(prototype);
        ThrowIfDisposed();
        return facts is { IsValid: true } valid ? valid with { Complexity = complexity ?? valid.Complexity } : new(0, 0, complexity ?? 0);
    }
}
