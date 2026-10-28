using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

/// <summary>ARC-MAGIC-018: a spell's missile (goals 28 and 78) and the casting goal's facing turn (0x42C850), ledger SM rows, over a
/// synthetic map.</summary>
[TestClass]
public sealed class SpellMissileRulesTests
{
    private static bool Inside(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 64 && tile.Y < 64;

    [TestMethod]
    public void A_leg_flies_the_projectile_line_is_at_its_destination_on_the_same_tile_and_is_refused_past_100_steps()
    {
        var leg = SpellMissileRules.Leg(new(10, 10), new(10, 16));
        Assert.AreEqual(SpellMissileLegKind.Flies, leg.Kind);
        Assert.AreEqual(ProjectileRules.Launch(new(10, 10), new(10, 16))!.Samples.Count, leg.Flight!.Samples.Count, "the combat projectile's line");
        Assert.AreEqual(new SpellMissileLeg(SpellMissileLegKind.AtDestination, null), SpellMissileRules.Leg(new(3, 4), new(3, 4)));
        // 30 tiles along x: 1200 pixels on the screen's x axis (40 a tile), past the line's 100 steps of 10 pixels.
        Assert.AreEqual(new SpellMissileLeg(SpellMissileLegKind.Refused, null), SpellMissileRules.Leg(new(0, 30), new(30, 30)));
    }

    [TestMethod]
    public void A_tick_takes_its_steps_and_stops_where_the_leg_runs_out_testing_nothing_on_the_way()
    {
        var flight = SpellMissileRules.Leg(new(20, 20), new(14, 23)).Flight!;
        var count = flight.Samples.Count;
        var ticks = 0;
        var tick = new SpellMissileTick(flight, false, false);
        while (!tick.LegEnded)
        {
            tick = SpellMissileRules.Tick(tick.Flight, 4, Inside);
            Assert.IsFalse(tick.OffMap);
            ticks++;
        }
        Assert.AreEqual((count + 3) / 4, ticks, "four steps a tick, the last tick short");
        Assert.AreEqual((count, new CellCoord(14, 23), 0L, 0L), (tick.Flight.Next, tick.Flight.Tile, tick.Flight.X - SumX(flight), tick.Flight.Y - SumY(flight)),
            "the leg ends on its destination's centre");
        // A tick after the leg's end takes nothing (the goal has popped).
        Assert.AreEqual(tick.Flight, SpellMissileRules.Tick(tick.Flight, 4, Inside).Flight);
        // A map that ends under the line fails the tick.
        Assert.IsTrue(SpellMissileRules.Tick(flight, count, t => t == new CellCoord(20, 20)).OffMap);
    }

    private static long SumX(ProjectileFlight f) => f.Samples.Sum(s => (long)s.Dx);
    private static long SumY(ProjectileFlight f) => f.Samples.Sum(s => (long)s.Dy);

    [TestMethod]
    public void The_speed_and_the_pace_take_their_defaults()
    {
        Assert.AreEqual((4, 7, 0), (SpellMissileRules.StepsPerTick(0), SpellMissileRules.StepsPerTick(7), SpellMissileRules.StepsPerTick(-2)));
        Assert.AreEqual((100, 100, 66, 1000), (SpellMissileRules.PaceMs(null), SpellMissileRules.PaceMs(0), SpellMissileRules.PaceMs(15), SpellMissileRules.PaceMs(1)));
    }

    [TestMethod]
    public void After_a_leg_the_missile_arrives_follows_its_moved_target_or_flies_its_line_again()
    {
        CellCoord here = new(7, 7), there = new(9, 7);
        Assert.AreEqual(SpellMissileLegEnd.Arrive, SpellMissileRules.LegEnd(here, here, false, null), "a tile target");
        Assert.AreEqual(SpellMissileLegEnd.Arrive, SpellMissileRules.LegEnd(here, here, true, here), "the target stands there");
        Assert.AreEqual(SpellMissileLegEnd.Retarget, SpellMissileRules.LegEnd(here, here, true, there), "the target moved");
        Assert.AreEqual(SpellMissileLegEnd.Arrive, SpellMissileRules.LegEnd(here, here, true, null), "a target gone from the map");
        Assert.AreEqual(SpellMissileLegEnd.Replay, SpellMissileRules.LegEnd(there, here, true, there), "off its destination");
        var flight = SpellMissileRules.Leg(new(10, 10), new(10, 13)).Flight!;
        var ended = SpellMissileRules.Tick(flight, 1000, Inside).Flight;
        var replay = SpellMissileRules.Replay(ended);
        Assert.AreEqual((0, ended.X, ended.Y, ended.Tile), (replay.Next, replay.X, replay.Y, replay.Tile), "the line again from where it is");
    }

    [TestMethod]
    public void An_eye_candy_missile_faces_like_a_projectile_with_its_rotation_in_bits_9_to_11()
    {
        // Eye-candy number 40, frame 0, foreground, rotation 3 (the parent's facing, replaced).
        var art = 0xE0000000U | (40U << 19) | (3U << 9);
        Assert.AreEqual(0xE0000000U | (40U << 19) | (5U << 9), ProjectileRules.FacedArt(art, 5));
        Assert.AreEqual(0xE0000000U | (43U << 19) | (7U << 9), ProjectileRules.FacedArt(art, 31));
        Assert.AreEqual(0xEFF80000U | (1U << 9), ProjectileRules.FacedArt(0xEFF80000U, 9), "a number past 511 stays");
    }

    [TestMethod]
    public void The_facing_turn_refuses_a_stoned_stunned_or_paralysed_caster_and_turns_only_toward_another_object()
    {
        CellCoord caster = new(10, 10);
        Assert.AreEqual(new SpellFacing(SpellFacingKind.Turned, 5), SpellFacingRules.Turn(0, true, 0, caster, false, new(12, 10)));
        Assert.AreEqual(new SpellFacing(SpellFacingKind.Turned, LineOfFireRules.Rotation(caster, new(9, 13))), SpellFacingRules.Turn(0, true, 0, caster, false, new(9, 13)));
        Assert.AreEqual(SpellFacingKind.Unturned, SpellFacingRules.Turn(SpellFacingRules.Stoned, true, 0x60, caster, false, null).Kind, "a tile cast turns nothing");
        Assert.AreEqual(SpellFacingKind.Unturned, SpellFacingRules.Turn(0, true, 0, caster, true, caster).Kind, "its own target");
        Assert.AreEqual(SpellFacingKind.Refused, SpellFacingRules.Turn(SpellFacingRules.Stoned, false, 0, caster, false, new(12, 10)).Kind);
        Assert.AreEqual(SpellFacingKind.Refused, SpellFacingRules.Turn(0, true, 0x20, caster, false, new(12, 10)).Kind, "stunned");
        Assert.AreEqual(SpellFacingKind.Refused, SpellFacingRules.Turn(0, true, 0x40, caster, false, new(12, 10)).Kind, "paralysed");
        Assert.AreEqual(SpellFacingKind.Turned, SpellFacingRules.Turn(0, false, 0x60, caster, false, new(12, 10)).Kind, "critter flags count for a PC or NPC only");
        Assert.AreEqual(new SpellFacing(SpellFacingKind.Turned, 5), SpellFacingRules.Turn(0, true, 0x80, caster, false, caster), "another object on the caster's tile: rotation 5");
    }
}
