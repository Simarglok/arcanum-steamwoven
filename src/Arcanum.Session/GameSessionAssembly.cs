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
/// current tab; a refusal of the gate (<see cref="GameSessionFailure.AssemblyRefused"/>) shows the message of
/// <see cref="SchematicAssemblyRules.RefusalMessage"/>.
/// </summary>
public sealed record SessionAssemblyResult(GameSessionDisposition Disposition, GameSessionFailure Failure, AssemblyPlan? Plan,
    IReadOnlyList<string> Made, IReadOnlyList<string> Consumed, GameSessionVersion Version);

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
    /// anything is consumed. The original creates each product on the crafter's tile and leaves it there when the inventory
    /// cannot take it; the session keeps no new ground items, so a refused insert check refuses the assembly instead (nothing is
    /// lost). Then component 1 and component 2 are deleted (the durability deletion), and <c>quantity</c> made items enter the
    /// hero's inventory. One revision. No roll, skill, action points, time or experience.
    /// Refused before any effect: no hero or a dead one (<see cref="GameSessionFailure.InvalidLifeTarget"/>), the version, the gate
    /// (<see cref="GameSessionFailure.AssemblyRefused"/> with the plan), a product the session cannot hold
    /// (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), the insert check (<see cref="GameSessionFailure.InsertRefused"/>),
    /// the item and durability budgets (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4ED8B0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionAssemblyResult AssembleSchematic(GameSessionVersion expected, SchematicRecord schematic, SessionCraftLookup items,
        Func<int, ComponentFacts?> facts, Func<int, SessionItemKind?> productKind, SessionInsertCheck insertCheck)
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
        if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
        if (!plan.CanAssemble) return RejectAssembly(GameSessionFailure.AssemblyRefused, plan);
        var quantity = schematic.Quantity;
        if (quantity < 1) return RejectAssembly(GameSessionFailure.AssemblyRefused, plan);
        var prototype = plan.ProductBase!.Value + SchematicAssemblyRules.PrototypeOffset;
        if (productKind(prototype) is not { } kind || kind.Prototype != prototype || !Enum.IsDefined(kind.Type) || !InventoryDomain.Admitted(kind.Type))
            return RejectAssembly(GameSessionFailure.ItemTypeUnsupported, plan);
        if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
        if (_inventory.Held - 2 + (long)quantity > _limits.MaxPcItems) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var newRecords = new[] { plan.First.Item!, plan.Second.Item! }.Count(entity => !_durability.Records.ContainsKey(entity));
        if (_durability.Count + newRecords > _limits.MaxDurabilityRecords) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var next = NextMadeNumber();
        if (next > int.MaxValue - quantity) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var made = Enumerable.Range(next, quantity).Select(SessionMadeItem.EntityOf).ToArray();
        foreach (var entity in made)
        {
            var verdict = insertCheck(new SessionInventoryItem(entity, new SessionItemFacts(kind, 0, 0, -1, null, [])));
            ThrowIfDisposed();
            if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
            if (verdict != SessionInsertVerdict.Accepted) return RejectAssembly(GameSessionFailure.InsertRefused, plan);
        }
        if (_revision == long.MaxValue) return RejectAssembly(GameSessionFailure.RevisionExhausted, plan);
        Delete(plan.First.Item!);
        Delete(plan.Second.Item!);
        foreach (var entity in made) _inventory.Add(new SessionMadeItem(entity, kind));
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, plan, Array.AsReadOnly(made),
            Array.AsReadOnly(new[] { plan.First.Item!, plan.Second.Item! }), Version);
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
            // Gold and ammunition are not admitted to the hero's lists (ARC-WORLD-014), so no held item counts by units.
            result.Add(new CraftItem(entity, kind.Prototype, location, broken, facts.DescriptionChanged));
        }
        return result.AsReadOnly();
    }

    /// <summary>The next made number: one past every made item the hero holds or the session deleted.</summary>
    private int NextMadeNumber()
    {
        var numbers = _inventory.Made.Select(item => SessionMadeItem.Number(item.Entity) ?? 0)
            .Concat(_durability.Destroyed.Select(entity => SessionMadeItem.Number(entity) ?? 0));
        return numbers.DefaultIfEmpty(0).Max() + 1;
    }

    private SessionAssemblyResult RejectAssembly(GameSessionFailure failure, AssemblyPlan? plan = null) =>
        new(GameSessionDisposition.Rejected, failure, plan, Array.Empty<string>(), Array.Empty<string>(), Version);
}
