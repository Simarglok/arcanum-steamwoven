using System.Globalization;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>
/// Opt-in check of ARC-WORLD-053 on the installed original (<c>DAT_ROOT</c>, the game folder, whose <c>data/proto</c> holds the
/// loose prototypes): every item prototype becomes a durability baseline with a maximum of at least 1, no stored damage and no
/// destroyed bit, as the data census of the technology research counts (docs/research/technology-evidence.md#data-census). Only
/// numbers are compared, never original text.
/// </summary>
[TestClass]
public sealed class ItemDurabilityRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_item_prototype_starts_whole()
    {
        var game = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(game)) Assert.Inconclusive("DAT_ROOT is required for the opt-in durability check; this is not a synthetic pass.");
        var root = Path.Combine(game, "data", "proto");
        var lookup = PremadeHeroReader.DirectoryPrototypes(root);
        var numbers = Directory.EnumerateFiles(root, "*.pro").Select(Path.GetFileName).Where(n => n!.Length > 6 && n[..6].All(char.IsAsciiDigit))
            .Select(n => int.Parse(n![..6], CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        var items = numbers.Select(n => (Number: n, Fields: CombatFieldReader.ReadPrototype(lookup, n).Fields)).Where(r => r.Fields?.Item is not null).ToArray();
        Assert.AreEqual(798, items.Length, "item prototypes");
        var conditions = items.Select(r => (r.Number, Condition: SessionItemConditions.FromCombatFields(r.Fields!))).ToArray();
        Assert.AreEqual(0, conditions.Count(r => r.Condition is null), "every item has bounded values");
        Assert.AreEqual(0, conditions.Count(r => r.Condition!.Value.MaximumHitPoints < 1), "every maximum is at least 1");
        Assert.AreEqual(0, conditions.Count(r => r.Condition!.Value.Damage != 0 || r.Condition.Value.Broken || r.Condition.Value.InventoryArtBroken),
            "no stored damage, none broken");
    }
}
