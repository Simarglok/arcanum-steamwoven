using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-1229 (ledger IW2–IW5): the live light of a lit eye candy (Illuminate) is answered by the overlay host while its run is shown, in the first free of the owner's four overlay light slots.</summary>
[TestClass]
public sealed class AreaEyeCandyLightTests
{
    private const string Hero = SessionSpellCaster.Hero;
    private const string Npc = "npc:1";
    private const SpellEyeCandyFlags Fore = SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Animates;

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; }
        public AreaEyeCandyHost Host { get; }
        public FrameStep Step { get; }
        public Dictionary<string, int> Saved = [];
        public bool SlotsUnknown;
        public Dictionary<int, SpellEyeCandyEntry> Entries = new()
        {
            // Illuminate's entry: loops, light art 1 in (226, 60, 236).
            [20] = new(0x2000, Fore | SpellEyeCandyFlags.Forever, -1, 1, (226, 60, 236), 0),
            // A lit entry that ends on its own, a plain one and a lit one with another colour.
            [21] = new(0x2100, Fore, -1, 1, (10, 20, 30), 0),
            [22] = new(0x2200, Fore | SpellEyeCandyFlags.Forever, -1, null, (0, 0, 0), 0),
            [23] = new(0x2300, Fore | SpellEyeCandyFlags.Forever, -1, 4, (1, 2, 3), 0),
        };

        public Fixture(GameSessionOwner? session = null)
        {
            Session = session ?? ConversationSessionFixture.CreateWithHero();
            Host = new AreaEyeCandyHost(Session, i => Entries.GetValueOrDefault(i), (art, parts) => new EyeCandyFacts(4, 2, 100, 0, true, true), (low, high) => low,
                savedLightSlots: owner => SlotsUnknown ? -1 : Saved.GetValueOrDefault(owner));
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, null, () => Host) };
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void A_lit_looping_eye_candy_keeps_a_light_in_a_free_overlay_slot_while_its_run_is_shown()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 20, 0, 9));
        Assert.AreEqual(0, f.Host.Lights().Count(), "no light before the run's begin (IW3)");
        f.Run(60);
        var light = f.Host.Lights().Single();
        Assert.AreEqual((Hero, 0, 1, (226, 60, 236)), (light.Owner, light.Slot, light.ArtNumber, light.Color), "light art 1 in Illuminate's colour, the first overlay slot");
        f.Run(2_000);
        Assert.AreEqual(1, f.Host.Lights().Count(), "a looping eye candy keeps its light");
        Assert.AreEqual(1, f.Host.Remove(Hero, 20, 9, false), "the END Remove interrupts the run");
        f.Run(100);
        Assert.AreEqual(0, f.Host.Lights().Count(), "the end node writes the slot back as no light (IW4)");
        Assert.AreEqual(0, f.Host.Shown().Count());
    }

    [TestMethod]
    public void A_lit_eye_candy_that_ends_on_its_own_takes_its_light_with_it_and_the_slot_frees()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Npc, 21, 0, 1));
        f.Run(60);
        Assert.AreEqual(1, f.Host.Lights().Count());
        f.Run(1_000);
        Assert.AreEqual(0, f.Host.Lights().Count(), "the finish wrote the slot back");
        Assert.IsTrue(f.Host.LightSlotFree(Npc));
    }

    [TestMethod]
    public void An_entry_without_a_light_and_other_owners_have_their_own_lights()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 22, 0, 1));
        Assert.IsTrue(f.Host.Play(Npc, 20, 0, 2));
        Assert.IsTrue(f.Host.Play(Hero, 23, 0, 3));
        f.Run(60);
        var lights = f.Host.Lights().ToArray();
        Assert.AreEqual(2, lights.Length, "the unlit entry gives none");
        var hero = lights.Single(l => l.Owner == Hero);
        var npc = lights.Single(l => l.Owner == Npc);
        Assert.AreEqual((0, 4), (hero.Slot, hero.ArtNumber));
        Assert.AreEqual((0, 1), (npc.Slot, npc.ArtNumber), "the slots are the owner's, not the host's");
    }

    [TestMethod]
    public void A_light_takes_the_first_slot_its_own_record_and_the_other_lights_leave_free()
    {
        using var f = new Fixture();
        f.Saved[Npc] = 0b0001;
        Assert.IsTrue(f.Host.Play(Npc, 20, 0, 1));
        f.Run(60);
        Assert.AreEqual(1, f.Host.Lights().Single().Slot, "slot 0 holds a saved light");
        f.Saved[Npc] = 0b0101;
        Assert.IsTrue(f.Host.Play(Npc, 23, 0, 2));
        f.Run(60);
        Assert.AreSequenceEqual(new[] { 1, 3 }, f.Host.Lights().Select(l => l.Slot).Order().ToArray(), "slots 0 and 2 are saved, 1 is held: the next is 3");
        Assert.AreEqual(1, f.Host.Lights().First().Slot, "a slot a run took stays with it while it runs");
    }

    [TestMethod]
    public void A_critter_with_all_four_overlay_light_slots_in_use_gets_no_eye_candy_at_all()
    {
        using var f = new Fixture();
        f.Saved[Npc] = 0b1111;
        Assert.IsFalse(f.Host.LightSlotFree(Npc));
        Assert.IsFalse(f.Host.Play(Npc, 20, 0, 1), "IW2: the add refuses like a full part list");
        Assert.AreEqual(1, f.Host.Skipped);
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(Npc).Count(r => !r.Done), "no run is built");
        f.Run(60);
        Assert.AreEqual(0, f.Host.Shown().Count());
        Assert.IsTrue(f.Host.Play(Npc, 22, 0, 2), "an entry without a light still plays");
        // Three saved slots and a live light fill the four; the next lit add refuses, the first one is kept.
        f.Saved[Npc] = 0b0111;
        Assert.IsTrue(f.Host.Play(Npc, 20, 0, 3));
        f.Run(60);
        Assert.AreEqual(3, f.Host.Lights().Single().Slot);
        Assert.IsFalse(f.Host.Play(Npc, 23, 0, 4), "all four are in use now");
        Assert.AreEqual(1, f.Host.Lights().Count());
        // The other owners are not affected.
        Assert.IsTrue(f.Host.Play(Hero, 23, 0, 5));
    }

    [TestMethod]
    public void A_newer_run_in_a_lower_table_slot_never_shares_a_slot_and_pending_adds_hold_theirs()
    {
        using var f = new Fixture();
        // An unlit run ends and frees a low table slot; the second lit run then lands below the first one.
        Assert.IsTrue(f.Host.Play(Npc, 21, 0, 1));
        Assert.IsTrue(f.Host.Play(Npc, 20, 0, 2));
        Assert.IsTrue(f.Host.Play(Npc, 20, 0, 3));
        f.Run(60);
        var slots = f.Host.Lights().Select(l => l.Slot).ToArray();
        Assert.AreEqual(slots.Length, slots.Distinct().Count(), "one slot a light: " + string.Join(",", slots));
        f.Run(1_000);
        Assert.AreEqual(2, f.Host.Lights().Count(), "the one that ended on its own freed its slot");
        Assert.IsTrue(f.Host.Play(Npc, 23, 0, 4));
        f.Run(60);
        var again = f.Host.Lights().Select(l => l.Slot).ToArray();
        Assert.AreEqual(again.Length, again.Distinct().Count(), "still one slot a light: " + string.Join(",", again));
        // Adds before any begin hold their slots: with three of four taken by the record only one of two adds plays.
        using var g = new Fixture();
        g.Saved[Npc] = 0b0111;
        Assert.IsTrue(g.Host.Play(Npc, 20, 0, 1));
        Assert.IsFalse(g.Host.Play(Npc, 23, 0, 2), "the first add already holds the last slot (IW2)");
        g.Run(60);
        Assert.AreEqual(3, g.Host.Lights().Single().Slot);
    }

    // ARC-MAGIC-1280: the overlay runs of a game that was saved and loaded are the new host's own: the maintained glow returns with no second cast, in a slot of its own, and the four-slot rule of the add holds.
    [TestMethod]
    public void A_loaded_game_shows_the_lights_of_its_restored_runs_again_and_keeps_the_four_slot_rule()
    {
        using var before = new Fixture();
        Assert.IsTrue(before.Host.Play(Hero, 20, 0, -1));
        Assert.IsTrue(before.Host.Play(Npc, 20, 0, -1));
        Assert.IsTrue(before.Host.Play(Npc, 23, 0, -1));
        before.Run(60);
        Assert.AreEqual(3, before.Host.Lights().Count());
        using var session = ConversationSessionFixture.SaveAndLoad(before.Session);
        Assert.AreEqual(3, session.QueryGoals().Runs.Count(r => !r.Done), "the three overlay runs are in the loaded table");
        using var after = new Fixture(session);
        after.Saved[Npc] = 0b0100; // the owner's own record holds slot 2
        Assert.AreEqual(3, after.Host.AdoptRestored(), "the three lit runs take their slots again");
        after.Run(60); // the restored runs step on at their saved due time
        var lights = after.Host.Lights().ToArray();
        Assert.AreEqual(3, lights.Length, "the glow of each run is back with no second cast");
        var hero = lights.Single(l => l.Owner == Hero);
        Assert.AreEqual((1, (226, 60, 236)), (hero.ArtNumber, hero.Color));
        var npc = lights.Where(l => l.Owner == Npc).ToArray();
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, npc.Select(l => l.Slot).ToArray(), "slot 2 is the record's");
        Assert.IsTrue(after.Host.LightSlotFree(Npc), "slot 3 is free");
        Assert.IsTrue(after.Host.Play(Npc, 20, 0, -1), "a lit add takes slot 3");
        Assert.IsFalse(after.Host.Play(Npc, 20, 0, -1), "and the fifth light is refused, as in a game that never saved");
        // The maintained spell's END after the load takes the restored light off: the Remove finds the run by its tag.
        Assert.AreEqual(2, after.Host.Remove(Npc, 20, -1, false), "the restored run and the new one are removed (the END's Remove)");
        Assert.AreEqual(1, after.Host.Remove(Hero, 20, -1, false), "the hero's restored run is removable too");
        after.Run(200);
        Assert.AreEqual(1, after.Host.Lights().Count(), "only the NPC's unlit-by-Remove entry 23 light is left");
    }

    // ARC-MAGIC-1280: the overlay host of a loaded session is built before the viewer knows the owners' own light slots (its cast host comes later): no slot is claimed then, and the record's lights are never shared.
    [TestMethod]
    public void A_restored_run_claims_no_slot_until_the_owners_own_slots_are_known()
    {
        using var before = new Fixture();
        Assert.IsTrue(before.Host.Play(Npc, 20, 0, -1));
        before.Run(60);
        using var session = ConversationSessionFixture.SaveAndLoad(before.Session);
        using var after = new Fixture(session);
        after.SlotsUnknown = true;
        after.Saved[Npc] = 0b0001; // the record holds slot 0
        Assert.AreEqual(0, after.Host.AdoptRestored(), "nothing is claimed while the slots are unknown");
        Assert.AreEqual(0, after.Host.Lights().Count(), "and no light is answered yet");
        after.SlotsUnknown = false;
        Assert.AreEqual(1, after.Host.AdoptRestored());
        Assert.AreEqual(1, after.Host.Lights().Single().Slot, "slot 0 is the record's");
    }

    [TestMethod]
    public void The_slot_helpers_follow_the_first_free_rule_of_the_add()
    {
        Assert.AreEqual(0, AreaEyeCandyLights.Mask([]));
        Assert.AreEqual(0b1010, AreaEyeCandyLights.Mask([1, 3, 3, 9, -1]), "slots outside 0..3 are no slot");
        Assert.AreEqual(0, AreaEyeCandyLights.FirstFree(0));
        Assert.AreEqual(2, AreaEyeCandyLights.FirstFree(0b0011));
        Assert.AreEqual(-1, AreaEyeCandyLights.FirstFree(0b1111));
        var light = AreaEyeCandyLights.ToLight(new(Hero, new GoalHandle(1, 1), 2, 1, (226, 60, 236)), 92929, 82560, 3, -4, null);
        Assert.AreEqual((9u << 28 | 1u << 19, 1, 0), (light.ArtId, light.ArtNumber, light.Frame), "light art class 9, number 1, frame 0");
        Assert.AreEqual(StationaryLightFlags.NoAnimation, light.Flags, "flag 0x20: no light animation");
        Assert.AreEqual((92929L, 82560L, 3, -4, new AmbientColor(226, 60, 236), 2), (light.X, light.Y, light.OffsetX, light.OffsetY, light.Color, light.Slot));
        Assert.IsNull(light.Owner);
    }

    [TestMethod]
    public void A_critters_stand_follows_the_pose_like_its_own_light()
    {
        Assert.AreEqual((92928L, 82560L, 0, 0), AreaCritterLights.Stand(92928, 82560, 0, 0, null));
        var shift = Arcanum.Data.Terrain.TerrainGeometry.Project(2, 0);
        Assert.AreEqual((92930L, 82560L, 10, 5), AreaCritterLights.Stand(92928, 82560, 0, 0, new AreaCritterPose(default, 92930, 82560, shift.X + 10, shift.Y + 5)));
    }
}
