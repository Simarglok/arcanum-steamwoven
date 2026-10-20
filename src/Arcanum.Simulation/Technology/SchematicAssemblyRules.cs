using System.Collections.Immutable;

namespace Arcanum.Simulation.Technology;

/// <summary>
/// One item of a crafter's own inventory as the component gate sees it (ARC-TECHNOLOGY-004): its entity, its prototype number
/// (the base number + 20), its inventory location, whether its current art carries the destroyed bit, whether its description
/// differs from its prototype's, and for ammunition its units.
/// </summary>
public sealed record CraftItem(string Entity, int Prototype, int Location, bool Broken = false, bool DescriptionChanged = false,
    bool Ammunition = false, int Units = 0);

/// <summary>A component prototype's technology facts: its magick/tech complexity (negative technological) and its item
/// discipline (0..7; field 0x61).</summary>
public readonly record struct ComponentFacts(int Complexity, int Discipline);

/// <summary>One component slot of a schematic page: whether an item was found, the prototype shown (the found alternative's, else
/// the first alternative's), the alternative index found (−1 for none), the item found, and whether the crafter's expertise in
/// that prototype's discipline reaches its complexity.</summary>
public sealed record ComponentGate(bool Found, int Prototype, int Alternative, string? Item, bool ExpertiseOk);

/// <summary>The assemble button's state (the page draw 0x56DDC0; ledger O7 settled by a third reading and the installed texts).</summary>
public enum AssemblyButton
{
    /// <summary>Both components are present and the expertise reaches both: the button assembles.</summary>
    CanAssemble = 0,
    /// <summary>A component is missing (expertise reaching both): the click shows <c>schematic_text.mes</c> 0.</summary>
    ItemMissing = 1,
    /// <summary>The expertise does not reach a component (checked first): the click shows <c>schematic_text.mes</c> 1.</summary>
    ExpertiseLacking = 2,
}

/// <summary>What an assembly plan found: the two items, the product prototype and the quantity, or why it cannot assemble. A plan
/// whose button assembles but whose product list has no alternative at the found index has no product.</summary>
public sealed record AssemblyPlan(AssemblyButton Button, ComponentGate First, ComponentGate Second, int? ProductBase, int Quantity)
{
    public bool CanAssemble => Button == AssemblyButton.CanAssemble && ProductBase is not null;
}

/// <summary>
/// ARC-TECHNOLOGY-004: the schematic component gate, the assemble button and the assembly plan (contract
/// docs/world/schematic-assembly-runtime.md, ledger "Schematic assembly"). Pure over the crafter's own inventory, its effective
/// expertise per discipline and the component prototypes' facts; the session (or a host for an NPC crafter) applies the plan.
/// </summary>
public static class SchematicAssemblyRules
{
    public const int Disciplines = 8, AlternativeCount = 3, PrototypeOffset = 20;
    public const int FirstWornLocation = 1000, LastWornLocation = 1008, FirstFallbackLocation = 2000, LastFallbackLocation = 2009;
    /// <summary>The page texts of <c>schematic_text.mes</c>: missing items, lacking expertise, the completion, and the button hover.</summary>
    public const int MissingMessage = 0, ExpertiseMessage = 1, CompletedMessage = 2, AssembleHoverMessage = 3;
    /// <summary>The completion sound is this plus the current discipline (0..7: 3018..3025; ARC-TECHNOLOGY-005, ledger SW6).</summary>
    public const int CompletedSoundBase = 3018;

    /// <summary>
    /// The gate of one component (0x56E190): its alternatives (base numbers) are tried in order, each as a prototype (base + 20)
    /// searched in the crafter's own inventory only (<see cref="Find"/>); the first found sets the prototype, otherwise the first
    /// alternative is shown. The expertise test: the crafter's effective expertise in that prototype's discipline must be at
    /// least −complexity (signed, so neutral and magickal components always pass). <paramref name="reserved"/> are items another
    /// component already took (TC4, both fixed: one item never serves both).
    /// </summary>
    [OriginalFunction(0x56E190, OriginalFunctionScope.Partial)]
    public static ComponentGate Gate(IReadOnlyList<int> alternatives, IReadOnlyList<CraftItem> inventory, IReadOnlyList<int> expertise,
        Func<int, ComponentFacts?> facts, IReadOnlySet<string>? reserved = null)
    {
        Check(alternatives, inventory, expertise, facts);
        for (var i = 0; i < alternatives.Count; i++)
        {
            var prototype = alternatives[i] + PrototypeOffset;
            if (Find(inventory, prototype, reserved) is { } item)
                return new(true, prototype, i, item.Entity, ExpertiseOk(prototype, expertise, facts));
        }
        var shown = alternatives.Count > 0 ? alternatives[0] + PrototypeOffset : 0;
        return new(false, shown, -1, null, alternatives.Count > 0 && ExpertiseOk(shown, expertise, facts));
    }

    /// <summary>
    /// The inventory search the gate and assembly use (0x462540 with flags 7): the crafter's own items, no nested containers or
    /// party; an exact prototype; worn items (locations 1000..1008), broken items and items whose description differs from their
    /// prototype's skipped; an item in 2000..2009 kept only as a fallback (the last one), any other match returned at once.
    /// An ammunition item with no units is not a component.
    /// </summary>
    public static CraftItem? Find(IReadOnlyList<CraftItem> inventory, int prototype, IReadOnlySet<string>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        CraftItem? fallback = null;
        foreach (var item in inventory)
        {
            if (item is null || item.Prototype != prototype || reserved?.Contains(item.Entity) == true) continue;
            if (item.Location is >= FirstWornLocation and <= LastWornLocation || item.Broken || item.DescriptionChanged) continue;
            if (item.Ammunition && item.Units < 1) continue;
            if (item.Location is >= FirstFallbackLocation and <= LastFallbackLocation) { fallback = item; continue; }
            return item;
        }
        return fallback;
    }

    /// <summary>The button state of a page (0x56DDC0): expertise lacking for either component first, then an item missing,
    /// otherwise the button assembles.</summary>
    [OriginalFunction(0x56DDC0, OriginalFunctionScope.Partial)]
    public static AssemblyButton Button(ComponentGate first, ComponentGate second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!first.ExpertiseOk || !second.ExpertiseOk) return AssemblyButton.ExpertiseLacking;
        return first.Found && second.Found ? AssemblyButton.CanAssemble : AssemblyButton.ItemMissing;
    }

    /// <summary>The message the assemble button's click shows for a refusal: 0 for a missing item, 1 for lacking expertise.</summary>
    public static int? RefusalMessage(AssemblyButton button) => button switch
    {
        AssemblyButton.ItemMissing => MissingMessage,
        AssemblyButton.ExpertiseLacking => ExpertiseMessage,
        _ => null,
    };

    /// <summary>
    /// The plan of one assembly (0x56E720 with the owner's decisions): the gate is checked again (TC5, both modes); component 1
    /// is found first and reserved, then component 2 among the other items (TC4); the product is alternative i1 when i1 ≠ 0,
    /// else alternative i2; <paramref name="quantity"/> products. The caller checks the products can be made before consuming
    /// anything (TC3) and consumes component 1 then 2 (one unit of ammunition, any other item destroyed).
    /// </summary>
    [OriginalFunction(0x56E720, OriginalFunctionScope.Partial)]
    public static AssemblyPlan Plan(IReadOnlyList<int> component1, IReadOnlyList<int> component2, IReadOnlyList<int> product, int quantity,
        IReadOnlyList<CraftItem> inventory, IReadOnlyList<int> expertise, Func<int, ComponentFacts?> facts)
    {
        ArgumentNullException.ThrowIfNull(product);
        var first = Gate(component1, inventory, expertise, facts);
        var reserved = first.Item is { } taken ? new HashSet<string>(StringComparer.Ordinal) { taken } : null;
        var second = Gate(component2, inventory, expertise, facts, reserved);
        var button = Button(first, second);
        if (button != AssemblyButton.CanAssemble) return new(button, first, second, null, quantity);
        var index = first.Alternative != 0 ? first.Alternative : second.Alternative;
        int? productBase = index >= 0 && index < product.Count ? product[index] : null;
        // A product list without an alternative at that index leaves the plan without a product (a data defect, not a missing item).
        return new(AssemblyButton.CanAssemble, first, second, productBase, quantity);
    }

    /// <summary>
    /// The use of ammunition units the assembly makes of an ammunition component (0x4658E0, its source side with no target): a
    /// count of 0 changes nothing; fewer units than the count refuses (null); otherwise the units fall by the count and the item
    /// is destroyed when none are left (<c>Destroyed</c>).
    /// </summary>
    [OriginalFunction(0x4658E0, OriginalFunctionScope.Partial)]
    public static (int Units, bool Destroyed)? UseUnits(int units, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return (units, false);
        if (units < count) return null;
        return (units - count, units == count);
    }

    private static bool ExpertiseOk(int prototype, IReadOnlyList<int> expertise, Func<int, ComponentFacts?> facts)
    {
        if (facts(prototype) is not { } component || component.Discipline is < 0 or >= Disciplines) return false;
        return expertise[component.Discipline] >= -(long)component.Complexity;
    }

    private static void Check(IReadOnlyList<int> alternatives, IReadOnlyList<CraftItem> inventory, IReadOnlyList<int> expertise,
        Func<int, ComponentFacts?> facts)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(expertise);
        ArgumentNullException.ThrowIfNull(facts);
        if (alternatives.Count > AlternativeCount) throw new ArgumentException("At most three alternatives.", nameof(alternatives));
        if (expertise.Count != Disciplines) throw new ArgumentException("Eight disciplines' expertise are required.", nameof(expertise));
    }
}
