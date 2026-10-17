using System.Globalization;
using System.Text.Json;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// Opt-in check of ARC-WORLD-028 on the installed original: every loose weapon, armour and generic prototype
/// (<c>ARC_AREA_PROTOTYPES</c>, the installation's <c>data/proto</c>) becomes wear values, and each weapon and armour item is
/// offered to a half-elf man (the session fixture hero, with the item as his only kit item). First observation 2026-10-17; only
/// numbers are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class EquipmentRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_items_become_wear_values_and_a_half_elf_wears_them()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_PROTOTYPES");
        if (string.IsNullOrEmpty(root)) Assert.Inconclusive("ARC_AREA_PROTOTYPES (the installation's data/proto) is required.");
        var lookup = PremadeHeroReader.DirectoryPrototypes(root);
        var numbers = Directory.EnumerateFiles(root, "*.pro").Select(Path.GetFileName).Where(n => n!.Length > 6 && n[..6].All(char.IsAsciiDigit))
            .Select(n => int.Parse(n![..6], CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        var items = numbers.Select(n => (Number: n, Fields: CombatFieldReader.ReadPrototype(lookup, n).Fields))
            .Where(r => r.Fields?.Weapon is not null || r.Fields?.Armor is not null || r.Fields?.Generic is not null).ToArray();
        var wear = items.Select(r => (r.Number, r.Fields!, Item: SessionEquipmentFacts.FromCombatFields(r.Fields!))).ToArray();
        var unconverted = wear.Where(r => r.Item is null).Select(r => r.Number).ToArray();
        Assert.AreEqual(0, unconverted.Length, "not converted: " + string.Join(", ", unconverted));

        var slots = wear.GroupBy(r => EquipmentRules.WearSlot(r.Item!)?.ToString() ?? "none").ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine(JsonSerializer.Serialize(slots.OrderBy(p => p.Key)));
        // 150 weapons, 251 armour items by coverage (13 shields), 148 generic items of which 2 use the shield location.
        Assert.AreEqual((150, 165, 13 + 2, 19, 15, 10, 19, 10, 146), (slots["Weapon"], slots["Armor"], slots["Shield"], slots["Helmet"],
            slots["Gauntlets"], slots["Boots"], slots["Ring1"], slots["Medallion"], slots["none"]));
        // Virgil's worn armour (ARC-DATA-021): body armour with AC +3.
        var virgil = wear.Single(r => r.Number == 8114).Item!;
        Assert.AreEqual((EquipmentSlot.Armor, 3), (EquipmentRules.WearSlot(virgil), virgil.Armor!.AcAdjustment));

        var outcomes = new Dictionary<string, int>();
        foreach (var (number, fields, item) in wear.Where(r => r.Item!.Kind != EquipmentItemKind.Generic || r.Item.UsesShieldLocation))
        {
            var type = (SessionItemType)fields.Object.Type;
            var kind = new SessionItemKind(type, number, fields.Object.Name, fields.Object.Description);
            using var owner = CreateWith(HalfElf() with { Kit = [new(kind)] });
            var slot = EquipmentRules.WearSlot(item!) ?? EquipmentSlot.Weapon;
            var spells = fields.Item!.Spells.Any(spell => spell != 10000);
            var result = owner.Equip(owner.Version, "kit:1", slot, new(kind, [], spells, item!), (_, _) => true, _ => SessionInsertVerdict.Accepted);
            var key = result.Failure == GameSessionFailure.WearRefused ? result.Refusal.ToString() : result.Failure.ToString();
            outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
            if (result.Disposition == GameSessionDisposition.Changed) _ = owner.QueryHeroEquipmentCombat(SkillTraining.Apprentice)!;
        }
        Console.WriteLine(JsonSerializer.Serialize(outcomes.OrderBy(p => p.Key)));
        // 403 items offered: 290 worn; 96 body armour of another size, 5 for women only, 12 with item spells.
        Assert.AreEqual((290, 96, 5, 12, 4), (outcomes["None"], outcomes["WrongSize"], outcomes["WrongGender"], outcomes["ItemEffectUnsupported"], outcomes.Count));
    }
}
