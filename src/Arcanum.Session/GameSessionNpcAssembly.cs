using System.Collections.Immutable;
using Arcanum.Data.Technology;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Technology;

namespace Arcanum.Session;

/// <summary>
/// ARC-TECHNOLOGY-006: an NPC schematic crafter, as the dialog's <c>su</c> and script action 129 for an NPC with a PC leader make one
/// (ledger SW7, NC rows): the NPC's map data (<paramref name="Loaded"/>, its sheet and scheme, read only while the session keeps no
/// advancement record of it) and the stat changes of its effects (<paramref name="Modifiers"/>; the session keeps no durable effects
/// of NPCs, so the host passes what it knows, usually none).
/// </summary>
public sealed record SessionNpcCrafter(SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)
{
    public string Npc => Loaded.Entity;
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// An NPC's eight disciplines now (ARC-TECHNOLOGY-006, 0x4AFE60 and 0x4B00B0 for a critter): the stored degrees of its sheet (the
    /// session's advancement record when it levelled the NPC, else <see cref="SessionNpcCrafter.Loaded"/>), each lowered while its
    /// effective intelligence (<see cref="AutoLevelRules.EffectiveStat"/> with the crafter's modifiers) is below the degree's minimum, and
    /// the expertise of the counting degree. The "expertise in discipline" effect changes the original applies to every critter are
    /// not modelled for NPCs (the session keeps no NPC effects). Null for an invalid crafter.
    /// </summary>
    public ImmutableArray<SessionTechDiscipline>? QueryNpcTechnology(SessionNpcCrafter crafter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(crafter);
        if (!AdvancementDomain.Valid(crafter.Loaded) || !AutoLevelRules.IsValid(crafter.Modifiers)) return null;
        var sheet = (_advancement.Npcs.TryGetValue(crafter.Npc, out var saved) ? saved : crafter.Loaded).Sheet;
        var intelligence = AutoLevelRules.EffectiveStat(sheet, crafter.Modifiers, CharacterStat.Intelligence);
        var result = ImmutableArray.CreateBuilder<SessionTechDiscipline>(TechDegreeRules.Disciplines);
        for (var discipline = 0; discipline < TechDegreeRules.Disciplines; discipline++)
        {
            var degree = TechDegreeRules.Degree(sheet, discipline);
            result.Add(new(discipline, degree, TechDegreeRules.CountingDegree(degree, intelligence),
                TechDegreeRules.EffectiveExpertise(degree, intelligence, [])));
        }
        return result.MoveToImmutable();
    }

    /// <summary>
    /// The page of a schematic for an NPC crafter (the gate and the button over the crafter's own list, SW7): each component is
    /// searched in the NPC's current list (its seed items still there on every attached seed that lists it as an NPC, then the made
    /// items the session added to it, ARC-WORLD-055), at the seed's inventory location, broken by the session's durability record
    /// over <paramref name="items"/>' facts, with the NPC's expertise (<see cref="QueryNpcTechnology"/>). Null for an invalid crafter
    /// or an NPC the session does not know.
    /// </summary>
    public AssemblyPlan? QueryNpcAssembly(SessionNpcCrafter crafter, SchematicRecord schematic, SessionCraftLookup items, Func<int, ComponentFacts?> facts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(crafter);
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(facts);
        if (QueryNpcTechnology(crafter) is not { } technology || !KnownNpc(crafter.Npc)) return null;
        return NpcPlan(technology, schematic, NpcCraftInventory(crafter.Npc, items), facts);
    }

    /// <summary>
    /// An NPC crafter assembles a schematic (0x56E720 with the crafter the NPC, ledger SW7; the owner's TC3, TC4 and TC5 as for the
    /// hero): the gate over the NPC's list and expertise is checked again, component 1 is reserved, every product unit must be a kind
    /// the lists admit and pass the host's insert check for the NPC (as if the components were already gone and the earlier units
    /// inserted) before anything is consumed; then both components leave the NPC with the durability deletion (as
    /// <see cref="RemoveItemFromNpc"/>) and the accepted units enter its list (as <see cref="AddItemToNpc"/>). One revision.
    /// ARC-WORLD-070: the original creates each product unit on the crafter's tile and leaves it there when the NPC cannot take it
    /// (ledger SA3–SA5); so does the session: a refused unit lies on <paramref name="crafterCell"/> (the NPC's tile on the hero's map,
    /// which the host knows) as a made item in the ground store (<see cref="SessionAssemblyResult.OnGround"/>), which the hero can
    /// pick up. The ground store is the hero's, so without a hero, or without a crafter tile that fits, a refused unit refuses the
    /// assembly (<see cref="GameSessionFailure.InsertRefused"/>, nothing consumed).
    /// Refused before any effect: an invalid crafter (<see cref="GameSessionFailure.InvalidAdvancement"/>), the version, an NPC the
    /// session does not know (<see cref="GameSessionFailure.UnknownHolder"/>), a dead NPC (<see cref="GameSessionFailure.InvalidLifeTarget"/>:
    /// the original's open refuses a dead crafter), the gate (<see cref="GameSessionFailure.AssemblyRefused"/> with the plan), a record
    /// without a product or with a quantity below 1 (<see cref="GameSessionFailure.InvalidSchematics"/>), a product the lists do not
    /// admit (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), the NPC's list bound, the store, the ground store (the hero's item
    /// bound), the made numbers or the durability records (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, a refused unit
    /// that can go nowhere (<see cref="GameSessionFailure.InsertRefused"/>; a call back into the owner from a host callback, a seed attach
    /// included, is <see cref="GameSessionFailure.StaleVersion"/>).
    /// ARC-WORLD-066: as for the hero, an ammunition component loses one unit and an ammunition product (<paramref name="productAmmo"/>)
    /// merges into the NPC's stack of its prototype, a new <c>made:n</c> stack only when it holds none; a refused unit is a stack of its
    /// own on the ground.
    /// </summary>
    public SessionAssemblyResult AssembleSchematicForNpc(GameSessionVersion expected, SessionNpcCrafter crafter, SchematicRecord schematic,
        SessionCraftLookup items, Func<int, ComponentFacts?> facts, Func<int, SessionItemKind?> productKind, SessionInsertCheck insertCheck,
        Func<int, (int AmmoType, int Units)?>? productAmmo = null, Arcanum.Simulation.Movement.CellCoord? crafterCell = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(crafter);
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(productKind);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (QueryNpcTechnology(crafter) is not { } technology) return RejectAssembly(GameSessionFailure.InvalidAdvancement);
        if (!IsCurrent(expected)) return RejectAssembly(GameSessionFailure.StaleVersion);
        var npc = crafter.Npc;
        if (!KnownNpc(npc)) return RejectAssembly(GameSessionFailure.UnknownHolder);
        if (NpcDead(npc)) return RejectAssembly(GameSessionFailure.InvalidLifeTarget);
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        bool Stale() => _revision != revision || _inventory.SeedGeneration != seeds;
        var plan = NpcPlan(technology, schematic, NpcCraftInventory(npc, items), facts);
        ThrowIfDisposed();
        if (Stale()) return RejectAssembly(GameSessionFailure.StaleVersion);
        if (plan.Button != AssemblyButton.CanAssemble) return RejectAssembly(GameSessionFailure.AssemblyRefused, plan);
        var quantity = schematic.Quantity;
        if (plan.ProductBase is not { } productBase || quantity < 1) return RejectAssembly(GameSessionFailure.InvalidSchematics, plan);
        var prototype = productBase + SchematicAssemblyRules.PrototypeOffset;
        var product = productKind(prototype);
        ThrowIfDisposed();
        if (Stale()) return RejectAssembly(GameSessionFailure.StaleVersion);
        if (product is not { } kind || kind.Prototype != prototype || !Enum.IsDefined(kind.Type) || !InventoryDomain.Admitted(kind.Type))
            return RejectAssembly(GameSessionFailure.ItemTypeUnsupported, plan);
        (int AmmoType, int Units)? ammo = null;
        if (kind.Type == SessionItemType.Ammo)
        {
            ammo = productAmmo?.Invoke(prototype);
            ThrowIfDisposed();
            if (Stale()) return RejectAssembly(GameSessionFailure.StaleVersion);
            if (ammo is not { } a || a.AmmoType is < 0 or >= SessionAmmoRecord.AmmoTypes || a.Units < 0) return RejectAssembly(GameSessionFailure.ItemTypeUnsupported, plan);
        }
        var consumed = new[] { plan.First.Item!, plan.Second.Item! };
        // ARC-WORLD-066: an ammunition component keeps its stack unless its last unit goes.
        var stacks = NpcStacks(npc).ToDictionary(pair => pair.Stack.Entity, pair => pair.Stack, StringComparer.Ordinal);
        var deleted = consumed.Where(entity => !stacks.TryGetValue(entity, out var s) || s.Units <= 1).ToArray();
        SessionAmmoRecord? into = ammo is null ? null : NpcStacks(npc).Where(pair => pair.Prototype == prototype && !deleted.Contains(pair.Stack.Entity))
            .Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault();
        // ARC-WORLD-070: one insert check a product unit for the NPC, for the object it is (the stack it merges into); a refused unit
        // lies on the crafter's tile.
        var units = PlanUnits(quantity, ammo is not null, into?.Entity,
            unitEntity => insertCheck(new SessionInventoryItem(unitEntity, InventoryDomain.MadeFacts(kind) with { Quantity = ammo?.Units, AmmoType = ammo?.AmmoType })),
            Stale, out var firstRefusal, out var unitFailure);
        if (units is null) return unitFailure == GameSessionFailure.StaleVersion ? RejectAssembly(unitFailure) : RejectAssembly(unitFailure, plan);
        var ground = units.Count(u => u.Ground);
        if (ground > 0 && (_hero is null || crafterCell is not { } tile || !DropCellFits(tile)))
            return RejectAssembly(GameSessionFailure.InsertRefused, plan) with { Verdict = firstRefusal };
        var inList = units.Count(u => u.NewObject && !u.Ground);
        var addedConsumed = deleted.Count(entity => _inventory.NpcOf(entity) == npc);
        if (_inventory.AddedTo(npc).Count + SeedItemCount(npc) - deleted.Length + (long)inList > SessionMapInventorySeed.MaxListLength ||
            _inventory.NpcHeld - addedConsumed + (long)inList > _limits.MaxNpcItems || _inventory.Held + (long)ground > _limits.MaxPcItems)
            return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var keptStacks = consumed.Except(deleted).Count(entity => !_inventory.Ammo.ContainsKey(entity));
        if (ammo is not null && _inventory.Ammo.Count + keptStacks + (long)units.Count(u => u.NewObject) +
            (into is { } i && !_inventory.Ammo.ContainsKey(i.Entity) ? 1 : 0) > _limits.MaxAmmoRecords)
            return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        if (ammo is { } added && (into is { } t ? t.Units - (consumed.Contains(t.Entity) ? 1 : 0) : 0L) + (long)added.Units * units.Count(u => !u.Ground) > int.MaxValue)
            return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        var newRecords = deleted.Count(entity => !_durability.Records.ContainsKey(entity));
        if (_durability.Count + newRecords > _limits.MaxDurabilityRecords) return RejectAssembly(GameSessionFailure.BudgetExceeded, plan);
        if (_revision == long.MaxValue) return RejectAssembly(GameSessionFailure.RevisionExhausted, plan);
        foreach (var entity in consumed)
        {
            if (deleted.Contains(entity)) Delete(entity);
            else _inventory.Ammo[entity] = stacks[entity] with { Units = stacks[entity].Units - 1 };
        }
        // A seed stack the NPC holds keeps its seed units until the session tracks it.
        if (into is { } seedStack && !_inventory.Ammo.ContainsKey(seedStack.Entity) && units.Any(u => !u.Ground)) _inventory.Ammo[seedStack.Entity] = seedStack;
        foreach (var unit in units)
        {
            if (unit.Ground) PlaceMade(unit.Entity, kind, _actor.Map, crafterCell!.Value);
            else if (unit.NewObject)
            {
                _inventory.AddToNpc(npc, new SessionMadeItem(unit.Entity, kind));
                _inventory.Taken.Add(unit.Entity);
            }
            if (ammo is { } a) AddAmmoUnits(unit.Entity, a.AmmoType, a.Units);
        }
        _revision++;
        return Assembled(plan, units, consumed, firstRefusal);
    }

    private static AssemblyPlan NpcPlan(ImmutableArray<SessionTechDiscipline> technology, SchematicRecord schematic, IReadOnlyList<CraftItem> inventory,
        Func<int, ComponentFacts?> facts) =>
        SchematicAssemblyRules.Plan(schematic.Component1.Bases, schematic.Component2.Bases, schematic.Product.Bases, schematic.Quantity,
            inventory, technology.Select(d => d.Expertise).ToArray(), facts);

    /// <summary>The NPC's current list as the gate sees it: its seed items still there (every attached seed that lists it as an NPC,
    /// in list order, at the seed's location), then its added made items (in the bag); an item the host gives no facts for is no
    /// component, as for the hero.</summary>
    private IReadOnlyList<CraftItem> NpcCraftInventory(string npc, SessionCraftLookup items)
    {
        var listed = new List<(string Entity, int Prototype, int Location)>();
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc)
                foreach (var entity in holder.Items)
                    if (!_inventory.Taken.Contains(entity) && !Dropped(entity))
                        listed.Add((entity, seed.Items[entity].Facts.Kind.Prototype, seed.Items[entity].Facts.InventoryLocation));
        foreach (var item in _inventory.AddedTo(npc)) listed.Add((item.Entity, item.Kind.Prototype, -1));
        var result = new List<CraftItem>(listed.Count);
        var stacks = NpcStacks(npc).ToDictionary(pair => pair.Stack.Entity, pair => pair.Stack, StringComparer.Ordinal);
        foreach (var (entity, prototype, location) in listed)
        {
            if (items(entity) is not { } host) continue;
            var broken = _durability.Records.TryGetValue(entity, out var condition) ? condition.Broken : host.Broken;
            // ARC-WORLD-066: an ammunition stack counts by its units.
            var stack = stacks.TryGetValue(entity, out var ammo) ? ammo : (SessionAmmoRecord?)null;
            result.Add(new CraftItem(entity, prototype, location, broken, host.DescriptionChanged, stack is not null, stack?.Units ?? 0));
        }
        return result.AsReadOnly();
    }

    /// <summary>Whether an NPC is dead: an attached seed marks it dead, or the session killed it (its life records). An invalid entity
    /// id throws.</summary>
    public bool QueryNpcDead(string npc)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) throw new ArgumentException("Invalid entity id.", nameof(npc));
        return NpcDead(npc);
    }

    /// <summary>Whether the session knows an NPC: an NPC holder of an attached seed, a party record, or an NPC holding added items
    /// (the NPCs whose list the inventory and crafter calls can read). An invalid entity id throws.</summary>
    public bool QueryNpcKnown(string npc)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) throw new ArgumentException("Invalid entity id.", nameof(npc));
        return KnownNpc(npc);
    }

    private bool NpcDead(string npc) => KilledNpc(npc) ||
        _inventory.Seeds.Values.Any(seed => seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc && holder.Dead);
}
