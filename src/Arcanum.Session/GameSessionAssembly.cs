using System.Collections.Immutable;
using Arcanum.Data.Technology;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Technology;

namespace Arcanum.Session;

/// <summary>What the gate needs of an item the hero holds that the session does not keep (ARC-TECHNOLOGY-004): its inventory
/// location (a worn item's is its slot, which the session knows), whether its description differs from its prototype's, and the
/// destroyed bit of its data (the session's durability record wins).</summary>
public readonly record struct SessionCraftFacts(int Location, bool DescriptionChanged = false, bool Broken = false);

/// <summary>The host's facts of a held item for the gate, or null when it has none (then the item is not a component).</summary>
public delegate SessionCraftFacts? SessionCraftLookup(string entity);

/// <summary>
/// The outcome of an assembly (ARC-TECHNOLOGY-004): the plan (the gate's state of both components and the product), the items
/// made (<c>made:n</c>) and the consumed components. On success the host shows <c>schematic_text.mes</c>
/// <see cref="SchematicAssemblyRules.CompletedMessage"/> and plays <see cref="SchematicAssemblyRules.CompletedSoundBase"/> + the
/// current discipline; a refusal of the gate (<see cref="GameSessionFailure.AssemblyRefused"/>) shows the message of
/// <see cref="SchematicAssemblyRules.RefusalMessage"/>.
/// </summary>
public sealed record SessionAssemblyResult(GameSessionDisposition Disposition, GameSessionFailure Failure, AssemblyPlan? Plan,
    IReadOnlyList<string> Made, IReadOnlyList<string> Consumed, GameSessionVersion Version)
{
    /// <summary>The insert check's answer that refused a product (<see cref="GameSessionFailure.InsertRefused"/>): the host's, or
    /// <see cref="SessionInsertVerdict.TechnologyRefused"/> for a Technophobia hero (ARC-WORLD-069); null otherwise.</summary>
    public SessionInsertVerdict? Verdict { get; init; }
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The page of a schematic for the hero (the gate and the button, 0x56E190 and 0x56DDC0): each component searched in the hero's
    /// own items (kit, taken and made; a deleted one is gone), worn items by their slot, broken items by the session's durability
    /// record over <paramref name="items"/>' facts, and the hero's effective expertise (<see cref="QueryHeroTechnology"/>). Null
    /// without a hero.
    /// </summary>
    public AssemblyPlan? QueryHeroAssembly(SchematicRecord schematic, SessionCraftLookup items, Func<int, ComponentFacts?> facts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(facts);
        if (_hero is null) return null;
        return Plan(schematic, CraftInventory(items), facts);
    }

    /// <summary>
    /// The hero assembles a schematic (0x56E720 with the owner's decisions TC3, TC4 and TC5, both modes): the gate is checked again;
    /// component 1 is reserved before component 2 is searched; every product unit must be an item kind the session admits
    /// (<paramref name="productKind"/>, the product prototype's type, name and description) and pass the insert check before
    /// anything is consumed. The host answers each unit's insert check as if both components were already gone and the earlier
    /// units already inserted (the session asks before it changes anything). The original creates each product on the crafter's tile and leaves it there when the inventory
    /// cannot take it; the session keeps no new ground items, so a refused insert check refuses the assembly instead (nothing is
    /// lost). Then component 1 and component 2 are deleted (the durability deletion), and <c>quantity</c> made items enter the
    /// hero's inventory. One revision. No roll, skill, action points, time or experience.
    /// Refused before any effect: no hero or a dead one (<see cref="GameSessionFailure.InvalidLifeTarget"/>), the version, the gate
    /// (<see cref="GameSessionFailure.AssemblyRefused"/> with the plan), a product the session cannot hold
    /// (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), a record without a product at the found index or with a quantity
    /// below 1 (<see cref="GameSessionFailure.InvalidSchematics"/>), the insert check (<see cref="GameSessionFailure.InsertRefused"/>),
    /// the item and durability budgets (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision.
    /// ARC-WORLD-066: an ammunition component loses one unit (0x4658E0; the stack goes at its last unit) instead of being deleted, and
    /// an ammunition product (<paramref name="productAmmo"/>: the product prototype's ammunition type and units) merges each unit into
    /// the hero's stack of its prototype, a new <c>made:n</c> stack only when the hero holds none (<see cref="SessionAssemblyResult.Made"/>
    /// then lists the stacks the products entered). An ammunition product without its facts is <see cref="GameSessionFailure.ItemTypeUnsupported"/>.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4ED8B0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionAssemblyResult AssembleSchematic(GameSessionVersion expected, SchematicRecord schematic, SessionCraftLookup items,
        Func<int, ComponentFacts?> facts, Func<int, SessionItemKind?> productKind, SessionInsertCheck insertCheck,
        Func<int, (int AmmoType, int Units)?>? productAmmo = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(productKind);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (_hero is null || HeroDead()) return RejectAssembly(GameSessionFailure.InvalidLifeTarget);
        if (!IsCurrent(expected)) return RejectAssembly(GameSessionFailure.StaleVersion);
        var revision = _revision;
        var plan = Plan(schematic, CraftInventory(items), facts);
        ThrowIfDisposed();
        if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
        if (plan.Button != AssemblyButton.CanAssemble) return RejectAssembly(GameSessionFailure.AssemblyRefused, plan);
        // A record whose product list has no alternative at the found index, or a quantity below 1, cannot be assembled.
        var quantity = schematic.Quantity;
        if (plan.ProductBase is not { } productBase || quantity < 1) return RejectAssembly(GameSessionFailure.InvalidSchematics, plan);
        var prototype = productBase + SchematicAssemblyRules.PrototypeOffset;
        var product = productKind(prototype);
        ThrowIfDisposed();
        if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
        if (product is not { } kind || kind.Prototype != prototype || !Enum.IsDefined(kind.Type) || !InventoryDomain.Admitted(kind.Type))
            return RejectAssembly(GameSessionFailure.ItemTypeUnsupported, plan);
        (int AmmoType, int Units)? ammo = null;
        if (kind.Type == SessionItemType.Ammo)
        {
            ammo = productAmmo?.Invoke(prototype);
            ThrowIfDisposed();
            if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
            if (ammo is not { } a || a.AmmoType is < 0 or >= SessionAmmoRecord.AmmoTypes || a.Units < 0) return RejectAssembly(GameSessionFailure.ItemTypeUnsupported, plan);
        }
        var components = new[] { plan.First.Item!, plan.Second.Item! };
        // An ammunition component keeps its stack unless its last unit goes; every other component is deleted.
        var spent = new List<(string Entity, SessionAmmoRecord? Stack)>();
        foreach (var entity in components) spent.Add((entity, _inventory.Ammo.TryGetValue(entity, out var stack) ? stack : null));
        var deleted = spent.Where(c => c.Stack is not { } s || s.Units <= 1).Select(c => c.Entity).ToArray();
        // Ammunition products: a held stack of the prototype that outlives the components takes every unit, else one new stack.
        SessionAmmoRecord? into = ammo is null ? null : HeroHeld().Where(item => item.Kind.Type == SessionItemType.Ammo && item.Kind.Prototype == prototype &&
            !deleted.Contains(item.Entity)).Select(item => _inventory.Ammo[item.Entity]).Cast<SessionAmmoRecord?>().FirstOrDefault();
        var newItems = ammo is null ? quantity : into is null ? 1 : 0;
        if (_inventory.Held - deleted.Length + (long)newItems > _limits.MaxPcItems) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        if (ammo is { } added)
        {
            var before = into is { } target ? target.Units - (components.Contains(target.Entity) ? 1 : 0) : 0L;
            if (before + (long)added.Units * quantity > int.MaxValue) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
            if (into is null && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        }
        var newRecords = deleted.Count(entity => !_durability.Records.ContainsKey(entity));
        if (_durability.Count + newRecords > _limits.MaxDurabilityRecords) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var next = NextMadeNumber();
        if (newItems > 0 && next > int.MaxValue - (long)newItems + 1) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var made = Enumerable.Range((int)next, newItems).Select(SessionMadeItem.EntityOf).ToArray();
        // ARC-WORLD-069: the product's magick/tech complexity for the insert check's background part (a Technophobia hero cannot take
        // a technological product); a product the host gives no facts for counts as neutral (0).
        var complexity = facts(prototype)?.Complexity ?? 0;
        ThrowIfDisposed();
        if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
        // The host answers one insert check a product unit, for the stack it enters.
        for (var unit = 0; unit < quantity; unit++)
        {
            var entity = into?.Entity ?? (made.Length > 0 ? made[ammo is null ? unit : 0] : SessionMadeItem.EntityOf((int)next));
            var unitFacts = new SessionItemFacts(kind, 0, complexity, -1, ammo?.Units, []) { AmmoType = ammo?.AmmoType };
            var verdict = HeroInsertVerdict(insertCheck(new SessionInventoryItem(entity, unitFacts)), complexity);
            ThrowIfDisposed();
            if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
            if (verdict != SessionInsertVerdict.Accepted) return RejectAssembly(GameSessionFailure.InsertRefused, plan) with { Verdict = verdict };
        }
        if (_revision == long.MaxValue) return RejectAssembly(GameSessionFailure.RevisionExhausted, plan);
        foreach (var (entity, stack) in spent)
        {
            if (stack is { Units: > 1 } s) _inventory.Ammo[entity] = s with { Units = s.Units - 1 };
            else Delete(entity);
        }
        if (ammo is { } product2)
        {
            var target = into is { } t ? _inventory.Ammo[t.Entity] : new SessionAmmoRecord(made[0], product2.AmmoType, 0);
            if (into is null) _inventory.Add(new SessionMadeItem(made[0], kind));
            _inventory.Ammo[target.Entity] = target with { Units = target.Units + product2.Units * quantity };
        }
        else foreach (var entity in made) _inventory.Add(new SessionMadeItem(entity, kind));
        _revision++;
        var entered = ammo is null ? made : [into?.Entity ?? made[0]];
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, plan, Array.AsReadOnly(entered), Array.AsReadOnly(components), Version);
    }

    private AssemblyPlan Plan(SchematicRecord schematic, IReadOnlyList<CraftItem> inventory, Func<int, ComponentFacts?> facts)
    {
        var expertise = QueryHeroTechnology()!.Value.Select(d => d.Expertise).ToArray();
        return SchematicAssemblyRules.Plan(schematic.Component1.Bases, schematic.Component2.Bases, schematic.Product.Bases, schematic.Quantity,
            inventory, expertise, facts);
    }

    /// <summary>The hero's own items as the gate sees them: kit, taken and made items in that order, worn ones at their slot.</summary>
    private IReadOnlyList<CraftItem> CraftInventory(SessionCraftLookup items)
    {
        var held = _inventory.Kit.Select(item => (item.Entity, item.Kind)).Concat(_inventory.Pc.Select(item => (item.Entity, item.Kind)))
            .Concat(_inventory.Made.Select(item => (item.Entity, item.Kind))).ToArray();
        var result = new List<CraftItem>(held.Length);
        foreach (var (entity, kind) in held)
        {
            if (items(entity) is not { } facts) continue;
            var location = _equipment.SlotOf(entity) is { } slot ? (int)slot : facts.Location;
            var broken = _durability.Records.TryGetValue(entity, out var condition) ? condition.Broken : facts.Broken;
            // ARC-WORLD-066: an ammunition stack counts by its units (a stack at 0 is no component).
            var stack = _inventory.Ammo.TryGetValue(entity, out var ammo) ? ammo : (SessionAmmoRecord?)null;
            result.Add(new CraftItem(entity, kind.Prototype, location, broken, facts.DescriptionChanged, stack is not null, stack?.Units ?? 0));
        }
        return result.AsReadOnly();
    }

    /// <summary>The next made number: one past every made entity the hero or an NPC (ARC-WORLD-055) holds, the hero placed on the
    /// ground (ARC-WORLD-059), the session deleted or keeps a durability record of (no seed or taken item can carry the prefix); past
    /// int.MaxValue there is none.</summary>
    private long NextMadeNumber()
    {
        var numbers = _inventory.Made.Select(item => item.Entity).Concat(_inventory.Npcs.Values.SelectMany(list => list).Select(item => item.Entity))
            .Concat(_inventory.Placed.Select(item => item.Entity)).Concat(_durability.Destroyed).Concat(_durability.Records.Keys)
            .Select(entity => (long)(SessionMadeItem.Number(entity) ?? 0));
        return numbers.DefaultIfEmpty(0).Max() + 1;
    }

    private SessionAssemblyResult RejectAssembly(GameSessionFailure failure, AssemblyPlan? plan = null) =>
        new(GameSessionDisposition.Rejected, failure, plan, Array.Empty<string>(), Array.Empty<string>(), Version);
}
