using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// Opt-in ARC-WORLD-036 check on the installed original (DAT_ROOT = the game folder): every pre-made hero spends its starting
/// points on its scheme and levels to 50 with it, and Virgil, read from the Crash Site's object file, follows the hero's level
/// steps from 1 to 50 with his own scheme. Since ARC-SKILLS-002 the pre-made heroes also start with the skill levels their scheme
/// and their race and gender effects give, which their attack reads. Only counts and levels are printed, never original text.
/// </summary>
[TestClass]
public sealed class AreaAdvancementOriginalTests
{
    private static string Root() => Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root ? root
        : throw new AssertInconclusiveException("DAT_ROOT is required for the opt-in auto-level check; this is not a synthetic pass.");

    [TestMethod, TestCategory("RealData")]
    public void Pre_made_heroes_spend_their_points_and_level_to_fifty()
    {
        var module = Path.Combine(Root(), "modules", "Arcanum.dat");
        var rows = new List<object>();
        foreach (var premade in AreaStartingHero.Premade)
        {
            var load = AreaStartingHero.Load(module, premade);
            Assert.AreEqual(AreaHeroStatus.Ready, load.Status, $"{premade.Name}: {load.Detail}");
            Assert.IsNotNull(load.AutoLevel);
            Assert.IsNotNull(load.AutoLevel.Scheme(load.Definition!.Advancement!.AutoLevelScheme), premade.Name);
            var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
                AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
            using var session = created.Owner ?? throw new AssertFailedException($"{premade.Name}: {created.Failure}");
            var start = session.QueryHero()!;
            Assert.IsTrue(start.UnspentPoints < load.Definition.UnspentPoints, $"{premade.Name} spends at the start");
            var purchases = 0;
            for (var award = 0; award < 2000 && session.QueryHero()!.Level < 50; award++)
                purchases += session.AwardQuestExperience(session.Version, 50).Award?.Purchases?.Count ?? 0;
            var hero = session.QueryHero()!;
            Assert.AreEqual(50, hero.Level, premade.Name);
            rows.Add(new { premade.Name, Scheme = hero.Advancement.AutoLevelScheme, StartUnspent = start.UnspentPoints, purchases,
                hero.UnspentPoints, Hp = session.QueryHeroLife()!.Status.MaxHitPoints, Spells = hero.Advancement.Colleges.Sum() });
        }
        Console.WriteLine(JsonSerializer.Serialize(rows));
    }

    [TestMethod, TestCategory("RealData")]
    public void Pre_made_heroes_start_with_the_skill_levels_their_scheme_and_race_give()
    {
        var module = Path.Combine(Root(), "modules", "Arcanum.dat");
        var rows = new List<object>();
        int innate = 0, trainedOrLevelled = 0, attackers = 0;
        foreach (var premade in AreaStartingHero.Premade)
        {
            var load = AreaStartingHero.Load(module, premade);
            Assert.AreEqual(AreaHeroStatus.Ready, load.Status, $"{premade.Name}: {load.Detail}");
            var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
                AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
            using var session = created.Owner ?? throw new AssertFailedException($"{premade.Name}: {created.Failure}");
            var hero = session.QueryHero()!;
            var skills = session.QueryHeroSkills()!;
            innate += hero.InnateSkills.Length;
            // Every level is the rule's: points, the innate and durable changes, the key stat's cap.
            foreach (var skill in skills.All)
                Assert.IsTrue(skill.Level is >= 0 and <= SkillRules.MaxLevel, $"{premade.Name} {skill.Skill}");
            var levels = skills.All.Where(s => s.Level > 0).ToArray();
            if (levels.Length > 0) trainedOrLevelled++;
            // The innate changes reach the levels without touching the points.
            foreach (var change in hero.InnateSkills.Where(c => c.Operator == StatModifierOperator.Add && c.Value > 0))
                Assert.IsTrue(skills[change.Skill].Level >= Math.Min(skills[change.Skill].Points * 4 + change.Value,
                    SkillRules.KeyStatCap(session.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(SkillRules.KeyStat(change.Skill)))),
                    $"{premade.Name} {change.Skill}");
            var attack = session.QueryHeroAttack()!;
            Assert.AreEqual(skills[SkillRules.Of(attack.Skill)].Level, attack.SkillLevel, premade.Name);
            if (attack.SkillLevel > 0) attackers++;
            rows.Add(new { premade.Name, Innate = hero.InnateSkills.Length, Levels = levels.ToDictionary(s => s.Skill.ToString(), s => s.Level),
                Attack = $"{attack.Skill} {attack.SkillLevel} {attack.Training}" });
        }
        Console.WriteLine(JsonSerializer.Serialize(rows));
        // First observation 2026-10-20: 28 innate skill changes, eight heroes attack with a melee level above 0.
        Assert.AreEqual(28, innate, "the race and gender effects change skills (SH-09)");
        Assert.AreEqual(8, attackers, "pre-made heroes attack with a non-zero skill level");
        Assert.AreEqual(AreaStartingHero.Premade.Count, trainedOrLevelled, "every pre-made hero starts with a skill level");
    }

    [TestMethod, TestCategory("RealData")]
    public void Virgil_follows_the_hero_from_level_one_to_fifty()
    {
        var root = Root();
        string P(string relative) => Path.Combine(root, relative);
        var module = P("modules/Arcanum.dat");
        var load = AreaStartingHero.Load(module, AreaStartingHero.Premade[0]);
        Assert.AreEqual(AreaHeroStatus.Ready, load.Status, load.Detail);
        using var data = AreaOriginalValidator.OpenFiles(module, P("arcanum1.dat"), P("arcanum2.dat"), P("modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), P("data/proto"));
        var read = CombatFieldReader.ReadArea(data.LoadStrict());
        Assert.AreEqual(CombatFieldFailure.None, read.Failure, $"{read.SourceIndex} {read.Detail}");
        var virgil = read.Objects!.Single(o => o.Fields.Object.Prototype == 17102 && o.Fields.Npc?.AiData == 5).Fields;
        Assert.AreEqual(2, virgil.Critter!.Effects!.Count, "Virgil's two effects");
        var input = AreaAdvancement.Npc("virgil", virgil, load.Effects!);
        Assert.IsNotNull(input, "Virgil's sheet is admitted");
        var (loaded, modifiers) = input.Value;
        Assert.AreEqual(1, loaded.Sheet.Level);
        Assert.AreEqual(91, loaded.AutoLevelScheme, "Virgil's scheme (ARC-RES-031)");
        Assert.IsTrue(modifiers.Stats.Length > 0, "his effects change stats");

        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
        using var session = created.Owner!;
        var completed = 0;
        for (var level = 1; level < 50; level++)
        {
            var step = session.LevelFollower(session.Version, loaded, modifiers, new(level, level + 1));
            Assert.AreEqual(GameSessionDisposition.Changed, step.Disposition, $"level {level + 1}: {step.Failure}");
            if (step.Scheme!.Completed) completed++;
        }
        var sheet = session.QueryNpcAdvancement("virgil")!.Sheet;
        Assert.AreEqual(50, sheet.Level);
        Assert.IsTrue(AutoLevelRules.IsValid(sheet));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sheet.Level, sheet.UnspentPoints, Completed = completed, Colleges = sheet.Colleges.Sum(), Skills = sheet.BasicSkills.Sum(s => s & 63),
            sheet.HitPointPoints, Stats = sheet.Stats.Sum(),
        }));
    }
}
