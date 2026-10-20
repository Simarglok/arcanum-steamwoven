using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Technology.SchematicAssemblyRules;

namespace Arcanum.Simulation.Tests.Technology;

// ARC-TECHNOLOGY-004: the component gate, the button and the assembly plan (ledger SA1–SA6). Prototypes are invented: base 100
// (prototype 120) a technological part of discipline 3 and complexity −30, base 200 (220) a neutral part, base 300 (320) magickal.
[TestClass]
public sealed class SchematicAssemblyRulesTests
{
    private static readonly int[] NoExpertise = new int[8];
    private static int[] Expert(int discipline, int value) { var e = new int[8]; e[discipline] = value; return e; }

    private static ComponentFacts? Facts(int prototype) => prototype switch
    {
        120 => new(-30, 3), 220 => new(0, 3), 320 => new(40, 1), 420 => new(-10, 5), 520 => new(-5, 9), _ => null,
    };

    private static CraftItem Item(string entity, int prototype, int location = 0) => new(entity, prototype, location);

    [TestMethod]
    public void The_gate_finds_the_first_alternative_present_and_shows_the_first_when_none_is()
    {
        var gate = Gate([200, 100, 300], [Item("a", 120), Item("b", 320)], Expert(3, 30), Facts);
        Assert.AreEqual((true, 120, 1, "a"), (gate.Found, gate.Prototype, gate.Alternative, gate.Item), "alternatives in order: 220 absent, 120 present");
        var none = Gate([100, 200], [], Expert(3, 30), Facts);
        Assert.AreEqual((false, 120, -1, (string?)null, true), (none.Found, none.Prototype, none.Alternative, none.Item, none.ExpertiseOk));
    }

    [TestMethod]
    public void The_search_skips_worn_broken_and_redescribed_items_and_keeps_2000s_as_a_fallback()
    {
        Assert.IsNull(Find([Item("worn", 120, 1004), new("broken", 120, 0, Broken: true), new("odd", 120, 0, DescriptionChanged: true)], 120));
        Assert.AreEqual("bag", Find([Item("hot", 120, 2003), Item("bag", 120, 5)], 120)!.Entity, "a fallback slot item loses to any other");
        Assert.AreEqual("hot2", Find([Item("hot1", 120, 2000), Item("hot2", 120, 2009)], 120)!.Entity, "the last fallback");
        Assert.AreEqual("bag", Find([Item("bag", 120, 999), Item("ring", 120, 1000)], 120)!.Entity);
        Assert.IsNull(Find([new("empty", 120, 0, Ammunition: true, Units: 0)], 120), "ammunition counts by units");
        Assert.AreEqual("bullets", Find([new("bullets", 120, 0, Ammunition: true, Units: 1)], 120)!.Entity);
    }

    [TestMethod]
    public void Expertise_in_the_components_discipline_must_reach_minus_its_complexity()
    {
        Assert.IsTrue(Gate([100], [Item("a", 120)], Expert(3, 30), Facts).ExpertiseOk);
        Assert.IsFalse(Gate([100], [Item("a", 120)], Expert(3, 29), Facts).ExpertiseOk);
        Assert.IsFalse(Gate([100], [Item("a", 120)], Expert(2, 100), Facts).ExpertiseOk, "the component's discipline, not another");
        Assert.IsTrue(Gate([200], [Item("a", 220)], NoExpertise, Facts).ExpertiseOk, "neutral always passes");
        Assert.IsTrue(Gate([300], [Item("a", 320)], NoExpertise, Facts).ExpertiseOk, "magickal always passes (signed)");
        Assert.IsFalse(Gate([300], [Item("a", 320)], Expert(1, -50), Facts).ExpertiseOk, "an effect below 0 can fail even a magickal part");
        Assert.IsFalse(Gate([400], [Item("a", 420)], Expert(5, 9), Facts).ExpertiseOk);
        Assert.IsFalse(Gate([500], [Item("a", 520)], Expert(5, 100), Facts).ExpertiseOk, "a discipline outside 0..7 never passes");
        Assert.IsFalse(Gate([700], [Item("a", 720)], Expert(5, 100), Facts).ExpertiseOk, "a prototype without facts never passes");
    }

    [TestMethod]
    public void The_button_reports_lacking_expertise_first_then_a_missing_item_O7()
    {
        var ok = new ComponentGate(true, 120, 0, "a", true);
        var missing = ok with { Found = false, Item = null, Alternative = -1 };
        var unskilled = ok with { ExpertiseOk = false };
        Assert.AreEqual(AssemblyButton.CanAssemble, Button(ok, ok));
        Assert.AreEqual(AssemblyButton.ItemMissing, Button(ok, missing));
        Assert.AreEqual(AssemblyButton.ExpertiseLacking, Button(missing with { ExpertiseOk = false }, ok), "expertise is checked first");
        Assert.AreEqual(AssemblyButton.ExpertiseLacking, Button(ok, unskilled));
        Assert.AreEqual(((int?)0, (int?)1, (int?)null), (RefusalMessage(AssemblyButton.ItemMissing), RefusalMessage(AssemblyButton.ExpertiseLacking),
            RefusalMessage(AssemblyButton.CanAssemble)));
    }

    [TestMethod]
    public void The_plan_reserves_component_one_and_takes_the_product_of_the_first_nonzero_index_TC4()
    {
        // One part serves both lists: the plan needs two (TC4, both fixed).
        var one = Plan([100], [100], [900], 1, [Item("a", 120)], Expert(3, 30), Facts);
        Assert.AreEqual(AssemblyButton.ItemMissing, one.Button);
        var two = Plan([100], [100], [900], 2, [Item("a", 120), Item("b", 120)], Expert(3, 30), Facts);
        Assert.AreEqual((true, "a", "b", (int?)900, 2), (two.CanAssemble, two.First.Item, two.Second.Item, two.ProductBase, two.Quantity));
        // i1 = 1 → product alternative 1; i1 = 0 → the product of i2.
        var p1 = Plan([200, 100], [300], [900, 901, 902], 1, [Item("a", 120), Item("c", 320)], Expert(3, 30), Facts);
        Assert.AreEqual((int?)901, p1.ProductBase);
        var p2 = Plan([200], [400, 200], [900, 901, 902], 1, [Item("a", 220), Item("b", 220)], Expert(3, 30), Facts);
        Assert.AreEqual((int?)901, p2.ProductBase, "i1 = 0, i2 = 1");
        var p0 = Plan([200], [200], [900, 901, 902], 1, [Item("a", 220), Item("b", 220)], NoExpertise, Facts);
        Assert.AreEqual((int?)900, p0.ProductBase);
        Assert.AreEqual(AssemblyButton.ExpertiseLacking, Plan([100], [200], [900], 1, [Item("a", 120), Item("b", 220)], NoExpertise, Facts).Button,
            "TC5: the plan re-checks the gate");
    }

    [TestMethod]
    public void Ammunition_loses_one_unit_and_goes_at_none()
    {
        Assert.AreEqual((4, false), UseUnits(5, 1));
        Assert.AreEqual((0, true), UseUnits(1, 1));
        Assert.IsNull(UseUnits(0, 1));
        Assert.AreEqual((3, false), UseUnits(3, 0));
    }
}
