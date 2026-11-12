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
    /// <summary>The insert check's answer that refused a product: the host's, or <see cref="SessionInsertVerdict.TechnologyRefused"/>
    /// for a Technophobia hero (ARC-WORLD-069). On a refusal (<see cref="GameSessionFailure.InsertRefused"/>) the product could go
    /// nowhere; on success (ARC-WORLD-070) the first refused unit's answer, whose message the host shows for a PC, the product lying
    /// on the crafter's tile (<see cref="OnGround"/>). Null when every unit entered the inventory.</summary>
    public SessionInsertVerdict? Verdict { get; init; }

    /// <summary>ARC-WORLD-070: the product units the inventory refused, made items lying on the crafter's tile in the session's
    /// ground store (<see cref="GameSessionOwner.QueryPlacedItems"/>), in unit order; empty when every unit entered.</summary>
    public IReadOnlyList<string> OnGround { get; init; } = [];
}

/// <summary>ARC-WORLD-070: one product unit of an assembly: the made item it is (or the held stack an ammunition unit merges into),
/// whether it is a new object, and whether it lies on the crafter's tile because the insert check refused it.</summary>
internal readonly record struct AssemblyUnit(string Entity, bool NewObject, bool Ground);

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
    /// units already inserted (the session asks before it changes anything). ARC-WORLD-070: the original creates each product unit on
    /// the crafter's tile (0x4ED8B0) and transfers it into the inventory without checking the result (0x4617F0): a refused insert
    /// check shows its message for a PC and leaves the unit on the tile (ledger SA3–SA5). So does the session: a unit the check
    /// refuses lies on the hero's tile as a made item in the ground store (<see cref="SessionAssemblyResult.OnGround"/>, the first
    /// refusal in <see cref="SessionAssemblyResult.Verdict"/>), which the hero can pick up again. Then component 1 and component 2 are
    /// deleted (the durability deletion), and the accepted units enter the hero's inventory. One revision. No roll, skill, action
    /// points, time or experience.
    /// Refused before any effect: no hero or a dead one (<see cref="GameSessionFailure.InvalidLifeTarget"/>), the version, the gate
    /// (<see cref="GameSessionFailure.AssemblyRefused"/> with the plan), a product the session cannot hold
    /// (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), a record without a product at the found index or with a quantity
    /// below 1 (<see cref="GameSessionFailure.InvalidSchematics"/>), a refused unit when the hero's tile cannot hold a ground item
    /// (<see cref="GameSessionFailure.InsertRefused"/>), the item (inventory and ground together, <see cref="GameSessionLimits.MaxPcItems"/>),
    /// ammunition, made-number and durability budgets (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision.
    /// ARC-WORLD-066: an ammunition component loses one unit (0x4658E0; the stack goes at its last unit) instead of being deleted, and
    /// an ammunition product (<paramref name="productAmmo"/>: the product prototype's ammunition type and units) merges each accepted
    /// unit into the hero's stack of its prototype (one an earlier unit started included), a new <c>made:n</c> stack only when the hero
    /// holds none (<see cref="SessionAssemblyResult.Made"/> then lists the stacks the products entered); a refused unit is a stack of
    /// its own on the ground. An ammunition product without its facts is <see cref="GameSessionFailure.ItemTypeUnsupported"/>.
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
        // ARC-WORLD-069: the product's magick/tech complexity for the insert check's background part (a Technophobia hero cannot take
        // a technological product); a product the host gives no facts for counts as neutral (0).
        var complexity = facts(prototype)?.Complexity ?? 0;
        ThrowIfDisposed();
        // ARC-WORLD-085: each new unit has the product prototype's weight.
        var weight = CreatedWeight(prototype, complexity);
        if (_revision != revision) return RejectAssembly(GameSessionFailure.StaleVersion);
        // ARC-WORLD-085: the components are gone (a stack one unit lighter) before the products are weighed (TC3), and each accepted unit
        // adds its weight for the next one, as the original inserts them one by one (WE4).
        var carry = HeroCarryFacts(spent.ToDictionary(c => c.Entity, c => c.Stack is { Units: > 1 } s ? s.Units - 1 : (int?)null, StringComparer.Ordinal));
        var pending = 0;
        // ARC-DEV-333 (B48): each new unit also needs a free grid place (the insert check's room, WE4): the components' cells are free by then and the
        // places of the units accepted before count taken; an ammunition unit joining a stack needs none.
        var freedCells = deleted.ToHashSet(StringComparer.Ordinal);
        var unitCells = new Dictionary<string, Arcanum.Simulation.Items.InventoryGridItem>(StringComparer.Ordinal);
        // ARC-WORLD-1133: items waiting outside the grid take the freed cells first (they precede the products in the list).
        var waitingCells = WaitingItemCells(freedCells);
        // ARC-WORLD-070: the host answers one insert check a product unit, for the object it is (the stack it merges into); a refused
        // unit lies on the hero's tile.
        var units = PlanUnits(quantity, ammo is not null, into?.Entity, unitEntity =>
        {
            var unitFacts = new SessionItemFacts(kind, weight.Weight, complexity, -1, ammo?.Units, []) { AmmoType = ammo?.AmmoType, MagicWeight = weight.MagicWeight };
            var unit = new Arcanum.Simulation.Items.CarriedItem((int)kind.Type, weight.Weight, weight.MagicWeight, complexity, ammo?.Units ?? 0);
            var heavy = carry is { } c && !HeroFits(c, unit, pending);
            var merges = unitEntity == into?.Entity || unitCells.ContainsKey(unitEntity);
            var place = merges ? null : NewItemPlace(unitEntity, kind, freedCells, [.. waitingCells, .. unitCells.Values]);
            var verdict = HeroInsertVerdict(heavy ? SessionInsertVerdict.TooHeavy : insertCheck(new SessionInventoryItem(unitEntity, unitFacts)), complexity, room: merges || place is not null);
            if (verdict == SessionInsertVerdict.Accepted && carry is { } accepted) pending = unchecked(pending + HeroItemWeight(accepted, unit));
            if (verdict == SessionInsertVerdict.Accepted && place is { } taken) unitCells[unitEntity] = taken;
            return verdict;
        }, () => _revision != revision, out var firstRefusal, out var unitFailure);
        if (units is null) return unitFailure == GameSessionFailure.StaleVersion ? RejectAssembly(unitFailure) : RejectAssembly(unitFailure, plan);
        var (map, cell) = (_actor.Map, _actor.Cell);
        var ground = units.Count(u => u.Ground);
        if (ground > 0 && !DropCellFits(cell)) return RejectAssembly(GameSessionFailure.InsertRefused, plan) with { Verdict = firstRefusal };
        var newItems = units.Count(u => u.NewObject);
        if (_inventory.Held - deleted.Length + (long)newItems > _limits.MaxPcItems) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        if (ammo is { } added)
        {
            var accepted = units.Count(u => !u.Ground);
            var before = into is { } target ? target.Units - (components.Contains(target.Entity) ? 1 : 0) : 0L;
            if (before + (long)added.Units * accepted > int.MaxValue) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
            if (_inventory.Ammo.Count + (long)units.Count(u => u.NewObject) > _limits.MaxAmmoRecords) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        }
        var newRecords = deleted.Count(entity => !_durability.Records.ContainsKey(entity));
        if (_durability.Count + newRecords > _limits.MaxDurabilityRecords) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        if (_revision == long.MaxValue) return RejectAssembly(GameSessionFailure.RevisionExhausted, plan);
        foreach (var (entity, stack) in spent)
        {
            if (stack is { Units: > 1 } s) _inventory.Ammo[entity] = s with { Units = s.Units - 1 };
            else DestroyObject(entity);
        }
        foreach (var unit in units)
        {
            // ARC-WORLD-083: each new unit keeps the product's complexity, in the list and on the ground.
            if (unit.Ground) CreateMadeItem(new(unit.Entity, kind, SessionCreatePlace.Ground, weight, complexity, Map: map, Cell: cell, Source: SessionPlacedSource.Made));
            else if (unit.NewObject) CreateMadeItem(new(unit.Entity, kind, SessionCreatePlace.HeroList, weight, complexity));
            if (ammo is { } a) AddAmmoUnits(unit.Entity, a.AmmoType, a.Units);
        }
        Revise();
        return Assembled(plan, units, components, firstRefusal);
    }

    /// <summary>
    /// ARC-WORLD-070: the product units of an assembly in order, each asked of the insert check (<paramref name="check"/>, given the
    /// object it is). A unit is a new made item (the next made number) unless it is ammunition and the crafter holds a stack of its
    /// prototype (<paramref name="stack"/>, or the stack an earlier accepted unit started), which it merges into. A refused unit is a
    /// new made item on the ground (a merge candidate gets its own number). Null, with <paramref name="failure"/>, when a callback
    /// changed the session (<paramref name="stale"/>: <see cref="GameSessionFailure.StaleVersion"/>) or the made numbers run out
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>).
    /// </summary>
    private List<AssemblyUnit>? PlanUnits(int quantity, bool ammo, string? stack, Func<string, SessionInsertVerdict> check, Func<bool> stale,
        out SessionInsertVerdict? firstRefusal, out GameSessionFailure failure)
    {
        (firstRefusal, failure) = (null, GameSessionFailure.None);
        var next = NextMadeNumber();
        var units = new List<AssemblyUnit>(quantity);
        for (var unit = 0; unit < quantity; unit++)
        {
            var merges = ammo && stack is not null;
            if (!merges && next > int.MaxValue) { failure = GameSessionFailure.BudgetExceeded; return null; }
            var entity = merges ? stack! : SessionMadeItem.EntityOf((int)next);
            var verdict = check(entity);
            ThrowIfDisposed();
            if (stale()) { failure = GameSessionFailure.StaleVersion; return null; }
            if (verdict == SessionInsertVerdict.Accepted)
            {
                units.Add(new(entity, !merges, false));
                if (!merges) { next++; if (ammo) stack = entity; }
                continue;
            }
            firstRefusal ??= verdict;
            if (next > int.MaxValue) { failure = GameSessionFailure.BudgetExceeded; return null; }
            units.Add(new(SessionMadeItem.EntityOf((int)next++), true, true));
        }
        return units;
    }

    /// <summary>An ammunition product unit's units into its stack (a new stack starts at 0).</summary>
    private void AddAmmoUnits(string entity, int ammoType, int units)
    {
        var current = _inventory.Ammo.TryGetValue(entity, out var stack) ? stack : new SessionAmmoRecord(entity, ammoType, 0);
        _inventory.Ammo[entity] = current with { Units = current.Units + units };
    }

    private SessionAssemblyResult Assembled(AssemblyPlan plan, List<AssemblyUnit> units, string[] consumed, SessionInsertVerdict? firstRefusal) =>
        new(GameSessionDisposition.Changed, GameSessionFailure.None, plan,
            Array.AsReadOnly(units.Where(u => !u.Ground).Select(u => u.Entity).Distinct(StringComparer.Ordinal).ToArray()), Array.AsReadOnly(consumed), Version)
        {
            Verdict = firstRefusal,
            OnGround = Array.AsReadOnly(units.Where(u => u.Ground).Select(u => u.Entity).ToArray()),
        };

    private AssemblyPlan Plan(SchematicRecord schematic, IReadOnlyList<CraftItem> inventory, Func<int, ComponentFacts?> facts)
    {
        var expertise = QueryHeroTechnology()!.Value.Select(d => d.Expertise).ToArray();
        return SchematicAssemblyRules.Plan(schematic.Component1.Bases, schematic.Component2.Bases, schematic.Product.Bases, schematic.Quantity,
            inventory, expertise, facts);
    }

    /// <summary>The hero's own items as the gate sees them: kit, taken and made items in that order, worn ones at their slot.</summary>
    private IReadOnlyList<CraftItem> CraftInventory(SessionCraftLookup items)
    {
        var held = _inventory.Hero.PackKinds.ToArray();
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
        var numbers = _inventory.Made.Select(item => item.Entity).Concat(_inventory.Lists.All.Select(item => item.Entity))
            .Concat(_inventory.Placed.Select(item => item.Entity)).Concat(_durability.Destroyed).Concat(_durability.Records.Keys)
            .Select(entity => (long)(SessionMadeItem.Number(entity) ?? 0));
        // ARC-WORLD-094: and never a number a regeneration discarded.
        return Math.Max(numbers.DefaultIfEmpty(0).Max(), _inventory.LastDiscardedMade) + 1;
    }

    private SessionAssemblyResult RejectAssembly(GameSessionFailure failure, AssemblyPlan? plan = null) =>
        new(GameSessionDisposition.Rejected, failure, plan, Array.Empty<string>(), Array.Empty<string>(), Version);
}
