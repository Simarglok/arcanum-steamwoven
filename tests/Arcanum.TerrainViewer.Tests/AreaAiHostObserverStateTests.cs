using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1519 (VBG8; ledger VB3-16, VB3-17; 0x4AF260, 0x4AF470, 0x4B0EE0): the observer's state in sight and hearing. A blinded
/// observer (critter flag 0x80) sees nothing but still hears; a paralysed one (0x40) sees and hears (the checks refuse 0x10A0 and 0x20);
/// a stunned one (0x20) does neither; Perception 20 or more sees an invisible target. Invented open map, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostObserverStateTests
{
    private static readonly string Pc = AiSchedulerRules.Pc;
    private const int InvisibleSpellFlag = 0x1, StunnedCritterFlag = 0x20;

    private sealed class OpenMap : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 200 && tile.Y < 200;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    // The hero two tiles from the NPC; the NPC has Perception 8 unless asked.
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Setup(int perception = 8)
    {
        var f = AreaAiHostRebindTests.Open(perceptionA: perception);
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]) with { HeroCell = () => new CellCoord(62 - 2, 10), LineOfFire = () => new OpenMap() };
        return (f, f.Build(scene));
    }

    private static void Write(AreaAiHostRebindTests.Fixture f, string who, SessionObjectFlagField field, int mask) =>
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteObjectFlag(f.Session.Version, who, field, mask, true).Disposition);

    [TestMethod]
    public void A_blinded_observer_does_not_see_but_hears()
    {
        var (f, host) = Setup();
        using var _ = f;
        Assert.IsTrue(host.SeeToward(f.A, Pc).Perceived, "the NPC sees the hero two tiles away");
        var target = f.Combat.Build(f.Session, f.A).Combat?.Target ?? throw new AssertFailedException("the NPC has no combat build");
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetBlinded(f.Session.Version, target, true).Disposition);
        Assert.IsTrue(host.Blinded(f.A));
        Assert.AreEqual(new AwarenessResult(AwarenessRules.NotPerceived, AwarenessReason.State), host.SeeToward(f.A, Pc), "a blinded NPC sees nothing");
        Assert.IsTrue(host.Hear(f.A, Pc, Loudness.Normal).Perceived, "but hears");
    }

    [TestMethod]
    public void A_paralysed_observer_still_sees_and_hears()
    {
        var (f, host) = Setup();
        using var _ = f;
        Write(f, f.A, SessionObjectFlagField.Critter, SessionSpellCaster.ParalysedCritterFlag);
        Assert.IsTrue(host.Paralysed(f.A));
        Assert.IsTrue(host.SeeToward(f.A, Pc).Perceived, "paralysis 0x40 is not in the sight refusal mask 0x10A0");
        Assert.IsTrue(host.Hear(f.A, Pc, Loudness.Normal).Perceived, "nor in hearing's 0x20");
    }

    [TestMethod]
    public void A_stunned_observer_neither_sees_nor_hears()
    {
        var (f, host) = Setup();
        using var _ = f;
        Write(f, f.A, SessionObjectFlagField.Critter, StunnedCritterFlag);
        Assert.AreEqual(AwarenessRules.NotPerceived, host.SeeToward(f.A, Pc).Excess);
        Assert.AreEqual(AwarenessRules.NotPerceived, host.Hear(f.A, Pc, Loudness.Normal).Excess);
    }

    [TestMethod]
    public void Perception_20_sees_an_invisible_hero_and_Perception_19_does_not()
    {
        foreach (var (perception, seen) in new[] { (19, false), (20, true) })
        {
            var (f, host) = Setup(perception);
            using var _ = f;
            Write(f, Pc, SessionObjectFlagField.Spell, InvisibleSpellFlag);
            Assert.IsTrue(host.Find(Pc)!.InvisibleSpell);
            var result = host.SeeToward(f.A, Pc);
            Assert.AreEqual(seen, result.Perceived, $"Perception {perception}: {result}");
            if (!seen) Assert.AreEqual(AwarenessReason.Invisible, result.Reason);
        }
    }
    // ARC-COMBAT-1832 (exe 0x4AB990, 0x4AB460, 0x4AABE0, 0x4A9650): the object flag 0x400000 a script's toggle wrote on the hero or an NPC is what the hostility facts read, over the map data's.
    [TestMethod]
    public void A_written_invulnerable_flag_reaches_the_hostility_facts_of_the_hero_and_an_NPC()
    {
        var (f, host) = Setup();
        using var _ = f;
        Assert.IsFalse(host.Find(Pc)!.Invulnerable);
        Assert.IsFalse(host.Find(f.A)!.Invulnerable);
        Write(f, Pc, SessionObjectFlagField.Object, 0x400000);
        Write(f, f.A, SessionObjectFlagField.Object, 0x400000);
        Assert.IsTrue(host.Find(Pc)!.Invulnerable);
        Assert.IsTrue(host.Find(f.A)!.Invulnerable);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteObjectFlag(f.Session.Version, f.A, SessionObjectFlagField.Object, 0x400000, false).Disposition);
        Assert.IsFalse(host.Find(f.A)!.Invulnerable, "toggled back off");
    }
}
