using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1756 (VB2-2 gap d): an NPC's attack on an object. The attack animation's node (0x42B9C0) reads the target's art and object flags, an object's included; the attack record's first step (0x4B3170) replaces a scenery
/// target that holds an occupant (a bed's sleeper) by the occupant, pulls it out of the bed and runs the check on it with the modifier -30. Invented NPCs and an invented bed, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiBedAttackTests
{
    private static readonly CellCoord BedCell = new(61, 14), Attacker = new(61, 13), Sleeper = new(62, 14), HeroBeside = new(60, 13);
    private const string Bed = "oid:bed";

    private static readonly AreaObjectRecord BedRow = new(new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 5, 500L, new string('f', 64), 100), 0, 3, 9999, "p", "p", "r", "s", AreaScope.Core, null,
        BedCell.X, BedCell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static (AreaAiHostRebindTests.Fixture F, AreaAiHost Host, AreaBedOccupancy Occupancy, List<string> Struck) Open(Func<AreaBedOccupancy, string?> sleeper, AreaAiObjectFacts? facts = null, bool asleep = true, CellCoord? heroAt = null)
    {
        var f = AreaAiHostRebindTests.Open();
        var occupancy = new AreaBedOccupancy();
        var struck = new List<string>();
        var target = new AreaObjectAttackTarget(Bed, BedRow, DamageableObjectKind.Scenery,
            new SessionObjectTarget(new SessionMapId(1), Bed, new SessionObjectBaseline { Kind = DamageableObjectKind.Scenery, Points = 6, Material = ObjectMaterial.Cloth, Cell = BedCell }), null, null, null);
        var plain = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [new(f.A, f.RowA, Sleeper), new(f.B, f.RowB, Attacker)]);
        var scene = plain
            with
            {
                HeroCell = heroAt is { } near ? () => near : plain.HeroCell,
                ObjectFacts = id => id == Bed ? facts ?? new AreaAiObjectFacts(3, BedCell) : null,
                ObjectAttackTarget = id => id == Bed ? target : null,
                ObjectStruck = (_, _, hit, _) => struck.Add(hit!.Entity),
                Beds = new AreaAiBeds(occupancy, () => [(BedRow.Key, BedCell)]),
            };
        var host = f.Build(scene);
        if (sleeper(occupancy) is { } who)
        {
            Assert.IsTrue(occupancy.Seat(BedRow.Key, who == "A" ? f.A : who));
            // A sleeper holds the critter flag 0x1000 and the object flag 0x100 (what the seat step writes); one without them is a stale seat the host's first bed question takes out.
            if (who == "A" && asleep) Assert.AreEqual(GameSessionFailure.None, f.Session.WriteObjectFlag(f.Session.Version, f.A, SessionObjectFlagField.Critter, 0x1000, true, false).Failure);
        }
        return (f, host, occupancy, struck);
    }

    [TestMethod]
    public void A_blow_on_an_occupied_bed_pulls_the_sleeper_out_and_strikes_it_with_minus_30()
    {
        var (f, host, occupancy, struck) = Open(_ => "A");
        using var _ = f;
        var hp = f.Session.QueryHeroLife()!.Status.HitPoints;

        host.Hit(f.B, Bed);

        Assert.IsNull(occupancy.Occupant(BedRow.Key), "the unseat 0x45F3A0: the bed is empty: " + host.TraceOf(f.B)?.Item);
        Assert.AreEqual(0, struck.Count, "the bed itself took nothing: the record's target is the occupant");
        var notice = host.DrainNotices().FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Hit) ?? throw new AssertFailedException("no hit notice: " + host.TraceOf(f.B)?.Hit);
        Assert.AreEqual((f.B, f.A), (notice.Npc, notice.Target), "the occupant is the target of the notice");
        Assert.AreEqual(-30, notice.Result?.Resolution?.Difficulty?.Invocation, "SC16: the check's invocation modifier is -30: " + host.TraceOf(f.B)?.Hit + " / " + notice.Result?.Resolution?.Refusal);
        Assert.AreEqual(hp, f.Session.QueryHeroLife()!.Status.HitPoints, "the hero is not struck");
        StringAssert.Contains(host.TraceOf(f.B)?.Item, "pulled out");
    }

    [TestMethod]
    public void A_seat_of_an_npc_that_does_not_sleep_is_taken_out_first_and_the_blow_strikes_the_bed()
    {
        // A load leaves the shared occupancy seating an NPC whose saved state is awake: the host's first bed question (this blow) syncs it before the occupant is read.
        var (f, host, occupancy, struck) = Open(_ => "A", asleep: false);
        using var _ = f;

        host.Hit(f.B, Bed);

        CollectionAssert.AreEqual(new[] { Bed }, struck, "the bed took the blow: " + host.TraceOf(f.B)?.Hit);
        Assert.IsNull(occupancy.Occupant(BedRow.Key), "the stale seat is gone");
        Assert.IsFalse(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Hit), "no critter was struck");
    }

    [TestMethod]
    public void A_blow_on_an_empty_bed_strikes_the_bed_as_any_scenery_with_no_modifier()
    {
        var (f, host, occupancy, struck) = Open(_ => null);
        using var _ = f;

        host.Hit(f.B, Bed);

        CollectionAssert.AreEqual(new[] { Bed }, struck, "the session's object attack: " + host.TraceOf(f.B)?.Hit);
        Assert.AreEqual(0, occupancy.Count);
        Assert.IsFalse(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Hit), "no critter was struck");
    }

    [TestMethod]
    public void A_blow_on_a_bed_the_hero_rests_in_pulls_the_hero_out_and_strikes_him_with_minus_30()
    {
        // ARC-AI-1939 (0x4B3170, 0x45F3A0): the unseat takes a PC out too; the record's target becomes the hero, the check carries -30 and the event names the hero.
        var (f, host, occupancy, struck) = Open(_ => AreaScriptHost.PcCode, heroAt: HeroBeside);
        using var _ = f;

        host.Hit(f.B, Bed);

        Assert.IsNull(occupancy.Occupant(BedRow.Key), "the hero's seat is taken out: " + host.TraceOf(f.B)?.Item);
        Assert.AreEqual(0, struck.Count, "the bed itself took nothing: the record's target is the hero");
        var notice = host.DrainNotices().FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Hit) ?? throw new AssertFailedException("no hit notice: " + host.TraceOf(f.B)?.Hit);
        Assert.AreEqual((f.B, AreaScriptHost.PcCode), (notice.Npc, notice.Target), "the hero is the target of the notice");
        Assert.AreEqual(-30, notice.Result?.Resolution?.Difficulty?.Invocation, "SC16: the check's invocation modifier is -30: " + host.TraceOf(f.B)?.Hit);
        StringAssert.Contains(host.TraceOf(f.B)?.Item, "hero pulled out");
    }

    [TestMethod]
    public void The_next_blow_after_the_hero_was_pulled_out_strikes_the_empty_bed_and_the_windows_close_unseat_changes_nothing()
    {
        var (f, host, occupancy, struck) = Open(_ => AreaScriptHost.PcCode, heroAt: HeroBeside);
        using var _ = f;
        host.Hit(f.B, Bed);
        host.DrainNotices();

        host.Hit(f.B, Bed);

        CollectionAssert.AreEqual(new[] { Bed }, struck, "an empty bed is struck as a scenery");
        // SWB12: the rest window's close unseats the hero from the bed it stored; the bed no longer holds him (the original logs an error and changes nothing).
        Assert.IsFalse(occupancy.Unseat(BedRow.Key, AreaScriptHost.PcCode));
        Assert.IsTrue(occupancy.Seat(BedRow.Key, f.A), "a sleeper that took the emptied bed");
        Assert.IsFalse(occupancy.Unseat(BedRow.Key, AreaScriptHost.PcCode), "the hero's close leaves the new occupant in");
        Assert.AreEqual(f.A, occupancy.Occupant(BedRow.Key));
    }

    [TestMethod]
    public void The_attack_animation_reads_the_objects_flag_0x80_and_art()
    {
        var plain = Open(_ => null);
        using var a = plain.F;
        Assert.AreEqual(WeaponArtRules.AttackAnimation, plain.Host.AttackRequest(plain.F.B, Bed).Animation, "a scenery art is no body: its animation reads 0 and its height 1");

        var shrunk = Open(_ => null, new AreaAiObjectFacts(3, BedCell) { Flags = 0x80 });
        using var b = shrunk.F;
        Assert.AreEqual(WeaponArtRules.LowAttackAnimation, shrunk.Host.AttackRequest(shrunk.F.B, Bed).Animation, "object flag 0x80 (shrunk): the low attack");

        var lying = Open(_ => null, new AreaAiObjectFacts(3, BedCell) { Art = (BodyArmorArtRules.CritterArt << 28) | (7u << 6) });
        using var c = lying.F;
        Assert.AreEqual(WeaponArtRules.LowAttackAnimation, lying.Host.AttackRequest(lying.F.B, Bed).Animation, "a target art whose animation is 7..11: the low attack");
    }
}
