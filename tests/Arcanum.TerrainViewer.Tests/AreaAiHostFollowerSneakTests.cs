using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1560 (VBG7 for followers; ledger AW4, AW5; 0x45EDE0 with a follower as the prowler, 0x4AF260, 0x4AF470, 0x4AB460, 0x4AB990): a
/// concealed follower of the sneaking hero is hidden from the AI as the hero is. It carries the concealed and master-prowler facts, is
/// heard at the silent level, and lowers the observer's Perception by its own prowling margin (its own level, training, light and armour)
/// in sight and in hearing. Invented open map: an observer of Perception 8, and a follower (a human of every primary stat 20) with
/// 5 points of prowling joined to the hero's party. No original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostFollowerSneakTests
{
    private const int ProwlingSlot = 39, TrainingShift = 6;

    private sealed class OpenMap : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 200 && tile.Y < 200;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    // The follower's prowling skill: its points with a training tier, in the basic skills' array (field 39).
    private static AreaObjectRecord WithProwling(AreaObjectRecord row, int points, SkillTraining training) =>
        row with
        {
            EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot != ProwlingSlot ? f
                : f with { Elements = [.. f.Elements.Where(e => e.Key != (int)Skill.Prowling), new AreaSparseElement((int)Skill.Prowling, [points | ((int)training << TrainingShift)])] })],
        };

    // Critter flags 0x400000: the NPC gives off an extra large light (its own, as the prowling difficulty reads the prowler's carried light).
    private static AreaObjectRecord WithLight(AreaObjectRecord row) =>
        row with { EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot == 36 ? f with { Number = 0x400000 } : f)] };

    // The observer A (Perception 8) at (62, 10) and the follower B, a party member 7 tiles to its west; the hero stands out of the way.
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Setup(CellCoord follower, int points = SkillRules.MaxPoints, SkillTraining training = SkillTraining.Expert, bool lit = false)
    {
        var f = AreaAiHostRebindTests.Open(changeB: row => lit ? WithLight(WithProwling(row, points, training)) : WithProwling(row, points, training), perceptionA: 8);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ForceJoinParty(f.Session.Version, f.B).Disposition);
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new AreaAiNpc(f.B, f.RowB, follower)]) with
        {
            HeroCell = () => new CellCoord(62, 40),
            LineOfFire = () => new OpenMap(),
            NpcSheet = entity => f.Combat.Fields(entity) is { } fields ? AreaAdvancement.Npc(entity, fields, _ => null) : null,
        };
        return (f, f.Build(scene));
    }

    private static SessionNpcProwler Prowler(AreaAiHostRebindTests.Fixture f)
    {
        var sheet = AreaAdvancement.Npc(f.B, f.Combat.Fields(f.B)!, _ => null)!.Value;
        return new(f.Combat.Build(f.Session, f.B).Combat!, sheet.Loaded, sheet.Modifiers);
    }

    [TestMethod]
    public void A_sneaking_follower_is_not_seen_at_a_distance_an_ordinary_one_is()
    {
        var (f, host) = Setup(new CellCoord(62 - 7, 10));
        using var _ = f;
        Assert.IsTrue(host.SeeToward(f.A, f.B).Perceived, "an ordinary follower is seen 7 tiles away");
        Assert.IsFalse(host.Find(f.B)!.Concealed);

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ToggleHeroConcealment(f.Session.Version).Disposition);
        Assert.IsTrue(host.Find(f.B)!.Concealed, "the host's follower facts carry the concealment");
        var margin = f.Session.NpcProwlingMargin(Prowler(f), 8, new SkillDifficultyFacts { Light = 255 }, seeing: true);
        Assert.IsTrue(margin.Margin > 12, $"the test follower is a strong prowler (margin {margin.Margin})");
        var seen = host.SeeToward(f.A, f.B);
        Assert.IsFalse(seen.Perceived, "a sneaking follower is not seen at that distance");
        Assert.AreEqual(AwarenessReason.Range, seen.Reason);

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ToggleHeroConcealment(f.Session.Version).Disposition);
        Assert.IsTrue(host.SeeToward(f.A, f.B).Perceived, "an ordinary follower again");
    }

    [TestMethod]
    public void A_sneaking_follower_is_not_heard_at_a_distance_an_ordinary_one_is()
    {
        var (f, host) = Setup(new CellCoord(62 - 5, 10));
        using var _ = f;
        Assert.IsTrue(host.Hear(f.A, f.B, Loudness.Normal).Perceived, "an ordinary follower is heard 5 tiles away");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        var margin = f.Session.NpcProwlingMargin(Prowler(f), 8, new SkillDifficultyFacts { Light = 255 }, seeing: false).Margin;
        Assert.IsTrue(margin > 25, $"the test follower is a strong prowler (margin {margin})");
        Assert.IsFalse(host.Hear(f.A, f.B, Loudness.Normal).Perceived, "a sneaking follower is not heard 5 tiles away");
        Assert.IsFalse(host.Hear(f.A, f.B, Loudness.Silent).Perceived, "nor at the silent level the scan uses for it");
    }

    [TestMethod]
    public void The_margin_is_the_followers_own_skill_and_training()
    {
        var facts = new SkillDifficultyFacts { Light = 255 };
        int Margin(int points, SkillTraining training)
        {
            var (f, host) = Setup(new CellCoord(62 - 5, 10), points, training);
            using var _ = f;
            f.Session.ToggleHeroConcealment(f.Session.Version);
            return f.Session.NpcProwlingMargin(Prowler(f), 8, facts, seeing: false).Margin;
        }
        var untrained = Margin(0, SkillTraining.None);
        var skilled = Margin(SkillRules.MaxPoints, SkillTraining.Expert);
        Assert.IsTrue(skilled > untrained, $"the follower's own prowling counts: {skilled} against {untrained}");
    }

    [TestMethod]
    public void A_follower_carrying_a_light_is_easier_to_see()
    {
        var facts = new SkillDifficultyFacts { Light = 0 };
        int Difficulty(bool lit)
        {
            var (f, host) = Setup(new CellCoord(62 - 5, 10), points: 0, training: SkillTraining.None, lit: lit);
            using var _ = f;
            f.Session.ToggleHeroConcealment(f.Session.Version);
            return f.Session.NpcProwlingMargin(Prowler(f), 8, facts, seeing: true).Difficulty!.Total;
        }
        Assert.IsTrue(Difficulty(lit: true) > Difficulty(lit: false), "the follower's own light makes the prowling check harder");
    }

    [TestMethod]
    public void A_follower_that_is_not_concealed_has_no_margin()
    {
        var (f, host) = Setup(new CellCoord(62 - 5, 10));
        using var _ = f;
        var margin = f.Session.NpcProwlingMargin(Prowler(f), 8, new SkillDifficultyFacts { Light = 255 }, seeing: false);
        Assert.AreEqual(0, margin.Margin);
        Assert.IsNull(margin.Effectiveness);
    }

    [TestMethod]
    public void A_master_prowler_follower_is_marked_only_while_concealed()
    {
        var (f, host) = Setup(new CellCoord(62 - 2, 10), training: SkillTraining.Master);
        using var _ = f;
        Assert.IsFalse(host.Find(f.B)!.MasterProwler, "not concealed");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsTrue(host.Find(f.B)!.MasterProwler);
        Assert.IsFalse(host.Find(f.A)!.Concealed, "an NPC outside the party is not concealed");
    }

    [TestMethod]
    public void An_expert_follower_is_not_a_master_prowler()
    {
        var (f, host) = Setup(new CellCoord(62 - 2, 10));
        using var _ = f;
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsTrue(host.Find(f.B)!.Concealed);
        Assert.IsFalse(host.Find(f.B)!.MasterProwler);
    }
}
