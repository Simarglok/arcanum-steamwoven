using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-020 opt-in census of the installed original (DAT_ROOT = the game folder): the item prototypes' spell slots
/// and the item triggers of the records they name (docs/world/item-spell-triggers.md). Counts only; no game text.</summary>
[TestClass]
public sealed class ItemSpellCensusRealDataTests
{
    private const int NoSpell = 10000;

    [TestMethod, TestCategory("RealData")]
    public void The_installed_item_spells_match_the_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in item spell census; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var spells = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        Assert.AreEqual(SpellDataStatus.Read, spells.Status, $"{spells.Refusal}");
        var records = spells.Data!.Records;
        var prototypes = ContentPrototypes.Open(stack);
        var slots = new List<(int Prototype, int Type, int Slot, int Spell, int Charges, uint Triggers)>();
        var scripts = new Dictionary<int, int>();
        foreach (var number in prototypes.Numbers)
        {
            if (CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is not { Item: { } item } fields) continue;
            scripts[number] = fields.Object.ScriptPoints.Count;
            for (var slot = 0; slot < item.Spells.Count; slot++)
            {
                if (item.Spells[slot] == NoSpell) continue;
                var spell = item.Spells[slot] & 0xFF;
                slots.Add((number, fields.Object.Type, slot, item.Spells[slot], item.SpellManaStore, spell < SpellRecord.Count ? records[spell].ItemTriggers : 0));
            }
        }
        string Triggers(uint mask) => mask == 0 ? "none" : string.Join("|", SpellNameTables.ItemTriggers.Select((n, i) => (n, v: SpellNameTables.ItemTriggerValues[i]))
            .Where(p => p.v != 0 && (mask & p.v) == p.v).Select(p => p.n));
        Console.WriteLine($"slots {slots.Count}, items {slots.Select(s => s.Prototype).Distinct().Count()}, records {slots.Select(s => s.Spell).Distinct().Count()}");
        Console.WriteLine($"by slot {string.Join(" ", slots.GroupBy(s => s.Slot).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}; " +
            $"after an empty slot {slots.Count(s => s.Slot > 0 && !slots.Any(o => o.Prototype == s.Prototype && o.Slot == s.Slot - 1))}");
        Console.WriteLine($"by type {string.Join(" ", slots.GroupBy(s => s.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}");
        foreach (var g in slots.GroupBy(s => s.Triggers).OrderBy(g => g.Key))
            Console.WriteLine($"triggers {Triggers(g.Key)}: slots {g.Count()}, charges {string.Join(" ", g.GroupBy(s => s.Charges).OrderBy(c => c.Key).Select(c => $"{c.Key}:{c.Count()}"))}, " +
                $"types {string.Join(" ", g.GroupBy(s => s.Type).OrderBy(c => c.Key).Select(c => $"{c.Key}:{c.Count()}"))}, records {string.Join(",", g.Select(s => s.Spell).Distinct().OrderBy(s => s))}");
        var componentTriggers = slots.Select(s => s.Spell & 0xFF).Distinct().Where(s => s < SpellRecord.Count)
            .SelectMany(s => records[s].Components.Where(c => c.ItemTriggers != 0).Select(c => (s, c.Action, c.Type, c.ItemTriggers))).ToArray();
        Console.WriteLine($"gated components {componentTriggers.Length}: {string.Join(" ", componentTriggers.GroupBy(c => (c.Action, Triggers(c.ItemTriggers))).Select(g => $"{g.Key.Action}/{g.Key.Item2}:{g.Count()}"))}");
        foreach (var w in slots.Where(s => (s.Triggers & 0x60000000) != 0))
            Console.WriteLine($"wear item {w.Prototype} t{w.Type} scripts {scripts[w.Prototype]} slot {w.Slot} record {w.Spell} charges {w.Charges} cells {records[w.Spell].ChargesFromCells} maintain {records[w.Spell].MaintainCost}/{records[w.Spell].MaintainPeriod} duration {records[w.Spell].DurationSeconds} areas {string.Join(",", records[w.Spell].Areas.Select(a => a.Targets.ToString("X")))}: " +
                string.Join(" ", records[w.Spell].Components.Select(c => $"{c.Action}/{c.Type}/{Triggers(c.ItemTriggers)}/{c.Area.Targets:X}")));
        Console.WriteLine($"out of range {slots.Count(s => s.Spell is < 0 or >= SpellRecord.Count)}");

        Assert.AreEqual((158, 155, 146, 0), (slots.Count, slots.Select(s => s.Prototype).Distinct().Count(), slots.Select(s => s.Spell).Distinct().Count(),
            slots.Count(s => s.Spell is < 0 or >= SpellRecord.Count)), "every slot names a record");
        Assert.AreEqual("none:94 Item_Used|Target_Attacker:42 Parent_Dmgs_Opponent_W_Item|Target_Attacker:2 Target_Hit|Target_Attacker:1 " +
            "Parent_Atks_Location:14 Parent_Hit|Target_Attacker|Target_Attacker_Weapon_Melee:1 Wear|Unwear:4",
            string.Join(" ", slots.GroupBy(s => s.Triggers).OrderBy(g => g.Key).Select(g => $"{Triggers(g.Key)}:{g.Count()}")));
        Assert.AreEqual(0, slots.Count(s => (s.Triggers & 0x81000000) != 0), "no installed record fires on pickup or drop");
        Assert.AreEqual("8168:168:-1:0 8169:168:-1:0 8170:168:-1:0 8207:182:-1:0", string.Join(" ", slots.Where(s => (s.Triggers & 0x60000000) != 0)
            .Select(s => $"{s.Prototype}:{s.Spell}:{s.Charges}:{scripts[s.Prototype]}")), "the wear items: unlimited charges, no move scripts, slot 0");
    }
}
