using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1189 (ledger SP2, TT2, TT3): the item modifier of the spot trap roll (the powered shield, name 2866, at location 1005, with charges of ammunition type 2
// and an aptitude at or below 0) and the NPC's own spot roll. Entities, prototypes, scripts and numbers are invented; the shield's name number is the installed one.
[TestClass]
public sealed class SpotTrapItemAndNpcTests
{
    private static readonly SessionTrapped Chest = new(Exterior, "oid:chest");
    private static readonly SessionTrapBaseline TrappedChest = new(TrapCarrier.TrappedObject, 30001, TrapRules.NonmagicalFlag, 0, 0);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;
    private const int OtherShieldName = 3000;

    private static SessionItemFacts Cells(int units) => new(new(SessionItemType.Ammo, 7060, 100, 200), 0, 0, 0, units, []) { AmmoType = TrapRules.ChargeAmmoType };

    private static SessionItemFacts Shield(int name, int location) => new(new(SessionItemType.Armor, 8214, name, 200), 0, 0, location, null, []);

    // A hero with Spot Trap points (Perception 20) and `tech` tech points (aptitude below 0 from 1) or `magick` magick points (above 0); with a shield on the ground, picked up
    // and worn when `wear`, and `cells` charges picked up when above 0.
    private static GameSessionOwner Hero(int tech, int magick, int shieldName, bool wear, int cells, SkillTraining training = SkillTraining.Master, ulong seed = 0)
    {
        var stats = Defaults();
        stats[5] = 20;
        stats[8] = magick;
        stats[9] = tech;
        int[] basic = new int[12];
        basic[(int)Skill.SpotTrap] = 5 | (int)training << 6;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var created = GameSessionOwner.Create(Descriptor() with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        var owner = created.Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, new([],
        [
            new("shield", Shield(shieldName, 0), new CellCoord(101, 200)),
            new("cells", Cells(Math.Max(cells, 1)), new CellCoord(102, 200)),
        ])).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "shield", Accept).Disposition);
        if (cells > 0) Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cells", Accept).Disposition);
        if (wear)
        {
            var item = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Shield } };
            var worn = owner.Equip(owner.Version, "shield", EquipmentSlot.Shield, new SessionWearableItem(new(SessionItemType.Armor, 8214, shieldName, 200), [], false, item), Drawn, Accept);
            Assert.AreEqual(GameSessionFailure.None, worn.Failure, worn.Refusal.ToString());
        }
        return owner;
    }

    private static int Aptitude(GameSessionOwner owner) => owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.MagickTechAptitude);

    private static int Total(GameSessionOwner owner)
    {
        var roll = owner.RollHeroSpotTrap(owner.Version, Chest, TrappedChest);
        Assert.AreEqual(GameSessionDisposition.Changed, roll.Disposition);
        return roll.Check!.Check.Difficulty;
    }

    [TestMethod]
    public void The_powered_shield_with_charges_gives_a_technologist_a_bonus_on_the_spot_roll()
    {
        using var plain = Hero(tech: 5, magick: 0, OtherShieldName, wear: true, cells: 10);
        using var powered = Hero(tech: 5, magick: 0, TrapRules.PoweredShieldName, wear: true, cells: 10);
        var aptitude = Aptitude(powered);
        Assert.IsTrue(aptitude < 0, "tech points move the aptitude toward technology: " + aptitude);
        Assert.AreEqual(Total(plain) + aptitude, Total(powered), "the modifier is the aptitude itself, negative: a bonus (SP2)");
    }

    [TestMethod]
    public void The_modifier_needs_the_name_the_slot_the_charges_and_an_aptitude_at_or_below_zero()
    {
        using var baseline = Hero(tech: 5, magick: 0, OtherShieldName, wear: true, cells: 10);
        var expected = Total(baseline);
        // The shield carried and not worn, no charges, and a magickal hero (aptitude above 0: the modifier is cleared to 0) all leave the total unchanged.
        foreach (var (label, owner) in new (string, GameSessionOwner)[]
        {
            ("not worn", Hero(5, 0, TrapRules.PoweredShieldName, wear: false, cells: 10)),
            ("no charges", Hero(5, 0, TrapRules.PoweredShieldName, wear: true, cells: 0)),
        })
        {
            using (owner) Assert.AreEqual(expected, Total(owner), label);
        }
        using var magickal = Hero(tech: 0, magick: 5, TrapRules.PoweredShieldName, wear: true, cells: 10);
        using var magickalPlain = Hero(tech: 0, magick: 5, OtherShieldName, wear: true, cells: 10);
        Assert.IsTrue(Aptitude(magickal) > 0);
        Assert.AreEqual(Total(magickalPlain), Total(magickal), "a positive aptitude clears the modifier");
    }

    // ---- the NPC's own roll ----

    private static GameSessionOwner NpcOwner(bool poweredShield, bool charges, ulong seed = 0)
    {
        var created = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        var owner = created.Owner!;
        var items = new List<string>();
        var seeds = new List<SessionSeedItem>();
        if (poweredShield) { items.Add("guard-shield"); seeds.Add(new("guard-shield", Shield(TrapRules.PoweredShieldName, TrapRules.ShieldLocation), null)); }
        if (charges) { items.Add("guard-cells"); seeds.Add(new("guard-cells", Cells(6), null)); }
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, new([new("guard", SessionHolderKind.Npc, false, false, [], items)], seeds)).Disposition);
        return owner;
    }

    private static SessionNpcSpot Spotter(int points, SkillTraining training, int tech = 0, bool led = false, string entity = "guard")
    {
        int[] basic = new int[12];
        basic[(int)Skill.SpotTrap] = points | (int)training << 6;
        var sheet = new SessionNpcAdvancement(entity, new(CharacterRace.Human, false, [8, 8, 8, 8, 20, 20, 8, 8], 0, tech, 1, 0,
            [.. basic], [.. new int[4]], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0);
        return new(SessionLifeTarget.Critter(entity, new LifeFacts(), new LifeState(0, 0, 0, LifeConditions.None, 0)), sheet, AdvancementModifiers.None, led);
    }

    [TestMethod]
    public void A_companion_rolls_its_own_spot_trap_skill_and_a_master_spots_far_more_often_than_an_untrained_one()
    {
        var master = 0;
        var untrained = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            using var owner = NpcOwner(false, false, seed);
            var a = owner.RollNpcSpotTrap(owner.Version, Spotter(20, SkillTraining.Master, led: true), Chest, TrappedChest, light: SessionPickLockTarget.FullLight);
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (a.Disposition, a.Failure));
            master += a.Spotted ? 1 : 0;
            var b = owner.RollNpcSpotTrap(owner.Version, Spotter(0, SkillTraining.None, led: true), Chest, TrappedChest);
            untrained += b.Spotted ? 1 : 0;
            Assert.IsFalse(b.SecondRoll, "no second roll below master");
        }
        Assert.IsTrue(master > 150, "a master's two rolls: " + master);
        Assert.IsTrue(untrained < master, $"{untrained} against {master}");
    }

    [TestMethod]
    public void The_darkness_penalises_an_untrained_npc_and_the_roll_writes_only_the_draws()
    {
        using var owner = NpcOwner(false, false);
        var draws = owner.QueryRandom().Draws;
        var revision = owner.Version.Revision;
        var lit = owner.RollNpcSpotTrap(owner.Version, Spotter(0, SkillTraining.None, led: true), Chest, TrappedChest, light: SessionPickLockTarget.FullLight);
        var dark = owner.RollNpcSpotTrap(owner.Version, Spotter(0, SkillTraining.None, led: true), Chest, TrappedChest, light: 0);
        Assert.IsTrue(dark.Check!.Check.Difficulty > lit.Check!.Check.Difficulty, "darkness adds to an untrained source's difficulty (SC9)");
        Assert.AreEqual(revision + 2, owner.Version.Revision, "one revision per roll");
        Assert.IsTrue(owner.QueryRandom().Draws > draws);
        Assert.IsFalse(owner.QueryTrap(Chest, TrappedChest).Spotted, "the roll marks nothing: the caller does");
    }

    [TestMethod]
    public void An_npc_with_the_powered_shield_and_charges_gets_its_aptitude_as_the_modifier()
    {
        using var powered = NpcOwner(true, true);
        using var bare = NpcOwner(false, false);
        using var uncharged = NpcOwner(true, false);
        var spotter = Spotter(5, SkillTraining.Expert, tech: 5, led: true);
        var withShield = powered.RollNpcSpotTrap(powered.Version, spotter, Chest, TrappedChest).Check!.Check.Difficulty;
        var without = bare.RollNpcSpotTrap(bare.Version, spotter, Chest, TrappedChest).Check!.Check.Difficulty;
        var noCharges = uncharged.RollNpcSpotTrap(uncharged.Version, spotter, Chest, TrappedChest).Check!.Check.Difficulty;
        var aptitude = AutoLevelRules.DerivedStat(spotter.Loaded.Sheet, spotter.Modifiers, CharacterStat.MagickTechAptitude);
        Assert.IsTrue(aptitude < 0);
        Assert.AreEqual(without + aptitude, withShield);
        Assert.AreEqual(without, noCharges, "a shield without charges gives nothing");
    }

    [TestMethod]
    public void The_npc_gate_follows_who_knows_the_trap_and_the_players_detect_flag()
    {
        using var owner = NpcOwner(false, false);
        var draws = owner.QueryRandom().Draws;
        var free = Spotter(20, SkillTraining.Master);
        var led = free with { LedByPc = true };
        // A free NPC knows every trap the PC did not place; a led one knows the spotted ones; the player's detect flag spots for any observer: no roll, nothing drawn.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RollNpcSpotTrap(owner.Version, free, Chest, TrappedChest).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RollNpcSpotTrap(owner.Version, led, Chest, TrappedChest with { ObjectFlags = TrapRules.SpottedFlag }).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RollNpcSpotTrap(owner.Version, led, Chest, TrappedChest, TrapRules.DetectTrapsSpellFlag).Disposition);
        Assert.AreEqual(draws, owner.QueryRandom().Draws);
        // A trap the PC placed is unknown to a free NPC, which rolls for it.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RollNpcSpotTrap(owner.Version, free, Chest, TrappedChest with { ObjectFlags = TrapRules.PlacedByPcFlag }).Disposition);
        Assert.AreEqual(TrapObserver.LedNpc, GameSessionOwner.NpcSpotObserver(led));
        Assert.AreEqual(TrapObserver.FreeNpc, GameSessionOwner.NpcSpotObserver(free));
    }

    [TestMethod]
    public void A_companion_that_spots_marks_the_trap_for_the_player_and_a_free_npc_marks_nothing()
    {
        using var owner = NpcOwner(false, false);
        var free = owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest with { ObjectFlags = TrapRules.PlacedByPcFlag }, GameSessionOwner.NpcSpotObserver(Spotter(5, SkillTraining.None)));
        Assert.AreEqual(GameSessionDisposition.NoChange, free.Disposition);
        var led = owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest, GameSessionOwner.NpcSpotObserver(Spotter(5, SkillTraining.None, led: true)));
        Assert.AreEqual(GameSessionDisposition.Changed, led.Disposition);
        Assert.IsTrue(owner.TrapKnownTo(Chest, TrappedChest, TrapObserver.Player), "the player knows what a companion spotted");
    }

    [TestMethod]
    public void An_npc_spot_roll_refuses_the_hero_a_foreign_sheet_an_invalid_object_or_light_and_a_stale_version()
    {
        using var owner = NpcOwner(false, false);
        var spotter = Spotter(5, SkillTraining.Expert, led: true);
        void Refused(SessionNpcSpot spot, GameSessionFailure failure, SessionTrapped? trapped = null, int light = SessionPickLockTarget.FullLight, GameSessionVersion? version = null)
        {
            var roll = owner.RollNpcSpotTrap(version ?? owner.Version, spot, trapped ?? Chest, TrappedChest, light: light);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (roll.Disposition, roll.Failure));
        }
        Refused(spotter with { Spotter = SessionLifeTarget.Hero }, GameSessionFailure.SkillsUnavailable);
        Refused(spotter with { Loaded = Spotter(5, SkillTraining.Expert, entity: "other").Loaded }, GameSessionFailure.SkillsUnavailable);
        Refused(spotter, GameSessionFailure.InvalidTrap, Chest with { Entity = "" });
        Refused(spotter, GameSessionFailure.InvalidTrap, light: SessionPickLockTarget.FullLight + 1);
        Refused(spotter, GameSessionFailure.StaleVersion, version: new(owner.Version.Owner, owner.Version.Revision + 1));
        Assert.AreEqual(0, owner.QueryRandom().Draws, "a refusal draws nothing");
    }
}
