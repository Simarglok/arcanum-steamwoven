using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

/// <summary>
/// Opt-in check of the installed original against ARC-RES-027 (docs/research/starting-hero-evidence.md, SH-03/SH-05/SH-09):
/// DAT_ROOT is the game folder. Only numbers are compared; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class PremadeHeroRealDataTests
{
    // Basic prototype, race, gender, set, gold, items (basic prototypes), scheme: the "Pre-made heroes at the start" table.
    private static readonly (int Basic, int Race, int Gender, int Set, int Gold, int[] Items, int Scheme)[] Heroes =
    [
        (16067, 5, 1, 108, 385, [6029, 8168, 8065, 15178], 76),
        (16068, 4, 1, 24, 385, [6029, 8168, 8065], 77),
        (16069, 3, 1, 20, 385, [6029, 8168, 8049], 78),
        (16070, 7, 1, 25, 385, [6029, 8168, 8069], 79),
        (16071, 2, 1, 20, 385, [6029, 8168, 8049], 80),
        (16072, 0, 1, 22, 370, [8094, 6111, 8170], 81),
        (16073, 6, 1, 20, 385, [6029, 8168, 8049], 82),
        (16074, 1, 1, 24, 385, [6029, 8168, 8065], 83),
        (16075, 0, 0, 23, 300, [8200, 6051, 8171], 84),
        (16076, 2, 0, 22, 370, [8094, 6111, 8170], 85),
        (16077, 6, 0, 26, 385, [6029, 8168, 8142], 86),
        (16078, 3, 0, 26, 385, [6029, 8168, 8142], 87),
    ];

    [TestMethod, TestCategory("RealData")]
    public void Installed_pre_made_heroes_start_with_the_research_kit_and_innate_effects()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var sources = Sources(root);
        foreach (var expected in Heroes)
        {
            var read = PremadeHeroReader.Read(sources, expected.Basic);
            Assert.AreEqual(PremadeHeroFailure.None, read.Failure, $"{expected.Basic} {read.Detail} {read.EffectStatus} {read.SourceStatus}");
            var hero = read.Hero!;
            Assert.AreEqual((expected.Basic + 20, expected.Race, expected.Gender, expected.Set, expected.Gold, expected.Scheme, 0, 20),
                (hero.Prototype, hero.Race, hero.Gender, hero.InventorySource, hero.Gold, hero.AutoLevelScheme, hero.Alignment, hero.Age), $"{expected.Basic}");
            CollectionAssert.AreEqual(expected.Items, hero.Items.Select(item => item.BasicPrototype).ToArray(), $"{expected.Basic}");
            Assert.IsTrue(hero.Items.All(item => item.Type is 5 or 7 or 14 && item.Prototype == item.BasicPrototype + 20 && item.Name > 0 && item.Description > 0));
            Assert.AreEqual(64 + expected.Race, hero.RaceEffect.Number);
            Assert.AreEqual(expected.Gender == 0 ? 330 : (int?)null, hero.GenderEffect?.Number);
            Assert.IsTrue(hero.Background is >= 39 and <= 50, "a pre-made background, display data only");
        }

        // SH-09: human none; half-elf dx +1, be +1, cn −1, four tech skills −1, magick points +1, age +15; half-ogre st +4,
        // resist damage +10, be −1, in −4, prowling −2; the female effect st −1, cn +1. All add operators.
        var human = PremadeHeroReader.Read(sources, 16072).Hero!;
        Assert.AreEqual((true, 0), (human.RaceEffect.Defined, human.RaceEffect.Changes.Count));
        var halfElf = PremadeHeroReader.Read(sources, 16069).Hero!.RaceEffect.Changes;
        Assert.IsTrue(halfElf.All(change => change.Operator == EffectChangeOperator.Add));
        CollectionAssert.AreEquivalent(new[] { (1, 1), (3, 1), (2, -1), (22, 1), (25, 15) },
            halfElf.Where(change => change.Kind == EffectAttributeKind.Stat).Select(change => (change.Attribute, change.Value)).ToArray());
        Assert.AreEqual(4, halfElf.Count(change => change.Kind == EffectAttributeKind.TechSkill && change.Value == -1));
        var halfOgre = PremadeHeroReader.Read(sources, 16070).Hero!.RaceEffect.Changes;
        CollectionAssert.AreEquivalent(new[] { (0, 4), (3, -1), (4, -4) },
            halfOgre.Where(change => change.Kind == EffectAttributeKind.Stat).Select(change => (change.Attribute, change.Value)).ToArray());
        var female = PremadeHeroReader.Read(sources, 16075).Hero!.GenderEffect!.Changes;
        CollectionAssert.AreEquivalent(new[] { (EffectAttributeKind.Stat, 0, -1), (EffectAttributeKind.Stat, 2, 1) },
            female.Select(change => (change.Kind, change.Attribute, change.Value)).ToArray());

        // The new-character prototype has no inventory source: no coins, no items.
        var generic = PremadeHeroReader.Read(sources, 16066);
        Assert.AreEqual(PremadeHeroFailure.None, generic.Failure);
        Assert.AreEqual((0, 0, 0), (generic.Hero!.InventorySource, generic.Hero.Gold, generic.Hero.Items.Count));
        Assert.AreEqual(PremadeHeroFailure.NotPcPrototype, PremadeHeroReader.Read(sources, 6029).Failure);
        Assert.AreEqual(PremadeHeroFailure.PrototypeMissing, PremadeHeroReader.Read(sources, 16079).Failure);
    }

    /// <summary>Prototypes from the loose <c>data/proto</c> folder; the rule tables through the content stack (ARC-DATA-011).</summary>
    internal static PremadeHeroSources Sources(string root)
    {
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        MesFile? Table(string path, bool required)
        {
            var resolution = stack.Resolve(path);
            if (resolution.Status != ContentResolutionStatus.Resolved)
            {
                Assert.IsFalse(required, path);
                return null;
            }
            var bytes = stack.Read(resolution.Candidates[0]);
            Assert.AreEqual(ContentReadStatus.Read, bytes.Status, path);
            var parsed = MesFile.Parse(bytes.Bytes);
            Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"{path} line {parsed.Line}");
            return parsed.File;
        }
        return new(PremadeHeroReader.DirectoryPrototypes(Path.Combine(root, "data", "proto")), Table("rules/effect.mes", true)!,
            Table("rules/InvenSource.mes", true)!, Table("rules/InvenSourceBuy.mes", false));
    }
}
