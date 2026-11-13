using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1518 (VBG7; ledger AW4, AW5, ST5; 0x4AF260, 0x4AF470, 0x4AB460, 0x4AB990): sneaking has an effect on the AI. A concealed hero
/// lowers the observer's Perception by the prowling margin in sight and in hearing, is heard at the silent level and carries the
/// concealed and master-prowler facts the target scan and the target validity read. Invented open map and an NPC of Perception 8, no
/// original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostSneakTests
{
    private static readonly string Pc = AiSchedulerRules.Pc;

    // An open map of any size: nothing blocks, nothing is soundproof.
    private sealed class OpenMap : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 200 && tile.Y < 200;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    // A strong prowler: Perception 20 (prowling's key stat) and 5 points of prowling (level 20), so its effectiveness beats an observer of Perception 8.
    private static SessionHeroDefinition Prowler(SessionHeroDefinition hero)
    {
        var stats = hero.Stats.ToArray();
        stats[(int)CharacterStat.Perception] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.Prowling] = SkillRules.MaxPoints;
        return hero with { Stats = stats, Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
    }

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Setup(CellCoord hero, SkillTraining training = SkillTraining.Expert)
    {
        var f = AreaAiHostRebindTests.Open(heroEdit: Prowler, perceptionA: 8);
        for (var tier = SkillTraining.Apprentice; tier <= training; tier++)
        {
            var set = f.Session.SetHeroSkillTraining(f.Session.Version, Skill.Prowling, tier);
            Assert.AreEqual(GameSessionDisposition.Changed, set.Disposition, $"the test hero can train to {tier}: {set.Change?.Status}, level {f.Session.QueryHeroSkills()![Skill.Prowling].Level}");
        }
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]) with { HeroCell = () => hero, LineOfFire = () => new OpenMap() };
        return (f, f.Build(scene));
    }

    // Sight at a distance an ordinary hero is seen at: the NPC's Perception 8 reaches 8 tiles.
    [TestMethod]
    public void A_sneaking_hero_is_not_seen_at_a_distance_an_ordinary_hero_is()
    {
        var (f, host) = Setup(new CellCoord(62 - 7, 10));
        using var _ = f;
        Assert.IsTrue(host.SeeToward(f.A, Pc).Perceived, "an ordinary hero is seen 7 tiles away");
        Assert.IsFalse(host.Find(Pc)!.Concealed);

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ToggleHeroConcealment(f.Session.Version).Disposition);
        Assert.IsTrue(host.Find(Pc)!.Concealed, "the host's hero facts carry the concealment");
        var margin = f.Session.HeroProwlingMargin(8, new SkillDifficultyFacts { Light = 255 }, seeing: true).Margin;
        Assert.IsTrue(margin > 12, $"the test hero is a strong prowler (margin {margin})");
        var seen = host.SeeToward(f.A, Pc);
        Assert.IsFalse(seen.Perceived, "a sneaking hero is not seen at that distance");
        Assert.AreEqual(AwarenessReason.Range, seen.Reason);
        Assert.AreEqual(7 - Math.Clamp(8 - 8 * margin / 100, 1, 10), seen.Excess, "the observer's Perception fell by the margin");

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ToggleHeroConcealment(f.Session.Version).Disposition);
        Assert.IsTrue(host.SeeToward(f.A, Pc).Perceived, "an ordinary hero again");
    }

    [TestMethod]
    public void A_sneaking_hero_is_not_heard_at_a_distance_an_ordinary_hero_is()
    {
        var (f, host) = Setup(new CellCoord(62 - 5, 10));
        using var _ = f;
        // Perception 8, normal noise: heard within (4 + 8 - 2) / 2 = 5 tiles.
        Assert.IsTrue(host.Hear(f.A, Pc, Loudness.Normal).Perceived, "an ordinary hero is heard 5 tiles away");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        var margin = f.Session.HeroProwlingMargin(8, new SkillDifficultyFacts { Light = 255 }, seeing: false).Margin;
        Assert.IsTrue(margin > 25, $"the test hero is a strong prowler (margin {margin})");
        Assert.IsFalse(host.Hear(f.A, Pc, Loudness.Normal).Perceived, "a sneaking hero is not heard 5 tiles away");
        Assert.IsFalse(host.Hear(f.A, Pc, Loudness.Silent).Perceived, "nor at the silent level the scan uses for it");
    }

    [TestMethod]
    public void The_target_scan_finds_an_ordinary_hero_a_sneaking_one_hides_from()
    {
        var (f, host) = Setup(new CellCoord(62 - 5, 10));
        using var _ = f;
        var ordinary = HostilityRules.FindTarget(host, f.A, f.Session.Rules.Combat);
        Assert.AreEqual(Pc, ordinary.Target, $"the NPC is hostile to the hero ({ordinary.Reason})");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        var hidden = HostilityRules.FindTarget(host, f.A, f.Session.Rules.Combat);
        Assert.IsNull(hidden.Target, "the sneaking hero is not heard from 5 tiles (the NPC faces away)");
        Assert.IsNull(hidden.TurnToward, "and not perceived, so nothing to turn toward");
    }

    [TestMethod]
    public void A_master_prowler_is_marked_in_the_hostility_facts_only_while_concealed()
    {
        var (f, host) = Setup(new CellCoord(62 - 2, 10), SkillTraining.Master);
        using var _ = f;
        Assert.IsFalse(host.Find(Pc)!.MasterProwler, "not concealed");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsTrue(host.Find(Pc)!.MasterProwler);
    }

    [TestMethod]
    public void An_expert_is_not_a_master_prowler()
    {
        var (f, host) = Setup(new CellCoord(62 - 2, 10));
        using var _ = f;
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsFalse(host.Find(Pc)!.MasterProwler);
    }
}
