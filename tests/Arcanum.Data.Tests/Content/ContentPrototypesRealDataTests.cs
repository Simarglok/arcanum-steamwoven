using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Tests.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Content;

/// <summary>
/// ARC-DATA-026 opt-in check of the installed original (DAT_ROOT = the game folder): on an installation whose prototypes
/// are all loose in <c>data/proto</c>, the stack-backed source reads the same bytes as the old one-folder lookup, so the
/// pre-made heroes and every combat prototype read unchanged. Only numbers and hashes are compared, never original text.
/// </summary>
[TestClass]
public sealed class ContentPrototypesRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_stack_source_reads_what_the_data_folder_holds()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        var folder = PremadeHeroReader.DirectoryPrototypes(Path.Combine(root, "data", "proto"));

        Assert.AreEqual(1423, prototypes.Numbers.Count);
        Assert.AreEqual(0, prototypes.AmbiguousNumbers.Count + prototypes.UnrecognisedKeys.Count + prototypes.IgnoredModuleKeys.Count);
        Assert.IsTrue(prototypes.Numbers.All(n => prototypes.Candidate(n)!.Source.Role == ContentSourceRole.DataDirectory));
        foreach (var number in prototypes.Numbers)
        {
            var (stacked, loose) = (prototypes.Read(number), folder(number));
            Assert.AreEqual((PrototypeFileStatus.Found, PrototypeFileStatus.Found), (stacked.Status, loose.Status), $"{number}");
            CollectionAssert.AreEqual(loose.Bytes, stacked.Bytes, $"{number}");
            var (a, b) = (CombatFieldReader.ReadPrototype(prototypes.Read, number), CombatFieldReader.ReadPrototype(folder, number));
            Assert.AreEqual((b.Failure, b.Detail, b.Fields is null), (a.Failure, a.Detail, a.Fields is null), $"{number}");
        }

        var tables = PremadeHeroRealDataTests.Sources(root);
        var stackedSources = tables with { Prototype = prototypes.Read };
        for (var basic = 16066; basic <= 16078; basic++)
        {
            var (a, b) = (PremadeHeroReader.Read(stackedSources, basic), PremadeHeroReader.Read(tables, basic));
            Assert.AreEqual(PremadeHeroFailure.None, a.Failure, $"{basic} {a.Detail}");
            Assert.AreEqual((b.Hero!.Prototype, b.Hero.Race, b.Hero.Gender, b.Hero.Gold, b.Hero.InventorySource),
                (a.Hero!.Prototype, a.Hero.Race, a.Hero.Gender, a.Hero.Gold, a.Hero.InventorySource), $"{basic}");
            CollectionAssert.AreEqual(b.Hero.Items.ToArray(), a.Hero.Items.ToArray(), $"{basic}");
        }
    }

    /// <summary>ARC-DATA-027: the game-start tables the universal readers take from the base sources have no module copy on
    /// the installed game, so reading them by phase changes nothing there; the module-load tables are the module's own.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Game_start_tables_have_no_module_copy_on_the_installed_game()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        foreach (var path in new[] { "rules/effect.mes", "rules/invensource.mes", "rules/invensourcebuy.mes", "mes/mainmenu.mes", "rules/xp_level.mes",
            "rules/xp_quest.mes", "art/light/light.mes" })
        {
            var resolution = stack.Resolve(path);
            Assert.IsNotNull(ContentPhase.Candidate(resolution, ContentLoadPhase.GameStart), path);
            Assert.AreSame(resolution.Winner, ContentPhase.Candidate(resolution, ContentLoadPhase.GameStart), path);
        }
        foreach (var path in new[] { "rules/gameinit.mes", "rules/maplist.mes", "rules/lighting schemes.mes", "rules/gamequest.mes" })
            Assert.IsTrue(ContentPhase.IsModule(stack.Resolve(path).Winner!.Source.Role), path);
    }
}
