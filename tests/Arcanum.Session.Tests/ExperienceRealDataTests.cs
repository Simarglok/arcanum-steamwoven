using System.Text.Json;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// Opt-in check of ARC-WORLD-030 on the installed original (<c>DAT_ROOT</c>, the game folder): the four experience tables are
/// read through the content stack, the level table is admitted, and the session fixture hero (a half-elf man) is taken from
/// level 1 to the last level with quest experience. Only counts and levels are printed, never table values.
/// </summary>
[TestClass]
public sealed class ExperienceRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_experience_tables_level_the_hero_to_fifty()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        MesFile Table(string path)
        {
            var resolution = stack.Resolve(path);
            Assert.AreEqual(ContentResolutionStatus.Resolved, resolution.Status, path);
            var bytes = stack.Read(resolution.Candidates[0]);
            Assert.AreEqual(ContentReadStatus.Read, bytes.Status, path);
            var parsed = MesFile.Parse(bytes.Bytes);
            Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, path);
            return parsed.File!;
        }
        var levels = ExperienceTables.ReadLevels(Table("rules/xp_level.mes"));
        var quest = ExperienceTables.ReadValues(Table("rules/xp_quest.mes"));
        var critter = ExperienceTables.ReadValues(Table("rules/xp_critter.mes"));
        var questLevels = ExperienceTables.ReadQuestLevels(Table("rules/gamequest.mes"));
        Assert.AreEqual((ExperienceTableStatus.Read, ExperienceTableStatus.Read, ExperienceTableStatus.Read, ExperienceTableStatus.Read),
            (levels.Status, quest.Status, critter.Status, questLevels.Status));
        var table = ExperienceTable.Create(levels.Thresholds);
        Assert.IsNotNull(table, "the installed level table is strictly increasing from 0");
        Assert.AreEqual((50, 51), (quest.Values!.Count, critter.Values!.Count));
        Assert.IsTrue(critter.Values.Keys.Order().SequenceEqual(Enumerable.Range(0, 51)));
        Assert.IsTrue(questLevels.Values!.Values.All(level => quest.Values.ContainsKey(level)), "every quest level has quest experience");
        var rules = SessionExperienceRules.Create(table, quest.Values, questLevels.Values);
        Assert.IsNotNull(rules);
        Console.WriteLine(JsonSerializer.Serialize(new { quests = questLevels.Values.Count }));

        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = rules }).Owner!;
        var awards = 0;
        while (owner.QueryHeroExperience()!.Level < 50 && awards < 10_000)
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.AwardQuestExperience(owner.Version, 50).Disposition);
            awards++;
        }
        var view = owner.QueryHeroExperience()!;
        Assert.AreEqual((50, 56), (view.Level, view.UnspentPoints));
        Console.WriteLine(JsonSerializer.Serialize(new { awards }));
        // The last level's experience falls back to level 50's: the hero never passes it.
        for (var i = 0; i < 20; i++) owner.AwardQuestExperience(owner.Version, 50);
        Assert.AreEqual(50, owner.QueryHeroExperience()!.Level);
        Assert.IsTrue(owner.QueryHeroExperience()!.Experience < table!.Threshold(51));
    }
}
