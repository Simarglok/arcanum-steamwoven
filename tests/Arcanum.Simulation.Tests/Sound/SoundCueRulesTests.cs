using Arcanum.Simulation.Sound;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Sound;

/// <summary>ARC-WORLD-041: the event sound rules against the behaviour recorded in docs/research/audio-evidence.md SN23–SN27
/// and SN34 (the original's sound id functions); bases, materials and arts are invented.</summary>
[TestClass]
public sealed class SoundCueRulesTests
{
    // Materials: 0 stone, 1 brick, 2 wood, 3 plant, 4 flesh, 5 metal, 6 glass, 7 cloth, 8 liquid, 9 paper, 10 gas, 11 force, 12 fire, 13 powder.
    private static SoundItem Item(int material, int weight = 100, int sound = 0, int description = 0) => new(sound, material, weight, description);

    private static (int, int, int?) Of(SoundCue? cue) => cue is null ? (-1, 0, null) : (cue.Id, cue.Variants, cue.Unresolved?.Id);

    [TestMethod]
    public void Pickup_and_drop_follow_the_material_class_with_light_metal_up_to_2000_and_gold_apart()
    {
        int[] pickup = [5953, 5953, 5954, 5954, 5950, 5952, 5955, 5956, 5950, 5957, 5950, 5951, 5950, 5950];
        for (var material = 0; material < 14; material++)
        {
            Assert.AreEqual(pickup[material], SoundCueRules.Item(ItemSoundAction.Pickup, Item(material, 2001))!.Id, $"pickup {material}");
            Assert.AreEqual(pickup[material] + 10, SoundCueRules.Item(ItemSoundAction.Drop, Item(material, 2001))!.Id, $"drop {material}");
        }
        Assert.AreEqual(5951, SoundCueRules.Item(ItemSoundAction.Pickup, Item(5, 2000))!.Id, "heavy metal at 2000 sounds light");
        Assert.AreEqual(5961, SoundCueRules.Item(ItemSoundAction.Drop, Item(5, -1))!.Id);
        Assert.AreEqual(5958, SoundCueRules.Item(ItemSoundAction.Pickup, Item(99, description: 9056))!.Id, "gold before the material");
        Assert.AreEqual(5968, SoundCueRules.Item(ItemSoundAction.Drop, Item(5, description: 9056))!.Id);
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.Pickup, null));
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.Pickup, Item(14)), "a material past the table");
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.Drop, Item(-1)));
    }

    [TestMethod]
    public void Use_out_of_ammo_and_miss_are_offsets_of_the_base()
    {
        Assert.AreEqual((7300, 1, null), Of(SoundCueRules.Item(ItemSoundAction.Use, Item(5, sound: 7300))));
        Assert.AreEqual((7307, 1, null), Of(SoundCueRules.Item(ItemSoundAction.OutOfAmmo, Item(5, sound: 7300))));
        Assert.AreEqual(7303, SoundCueRules.Item(ItemSoundAction.Miss, Item(5, sound: 7300))!.Id);
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.Use, Item(5)), "no base: no use sound");
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.OutOfAmmo, null));
        Assert.AreEqual(4010, SoundCueRules.Item(ItemSoundAction.Miss, Item(5))!.Id, "a miss without a base");
        Assert.AreEqual(4010, SoundCueRules.Item(ItemSoundAction.Miss, null)!.Id, "an unarmed miss");
        Assert.AreEqual(unchecked(int.MaxValue + 7), SoundCueRules.Item(ItemSoundAction.OutOfAmmo, Item(5, sound: int.MaxValue))!.Id, "32-bit sum");
    }

    [TestMethod]
    public void A_hit_tries_the_weapon_sound_and_falls_back_to_the_materials()
    {
        var target = new SoundHitTarget(2);
        Assert.AreEqual((7304, 2, 7000 + 20 * 2 + 3 * 4), Of(SoundCueRules.Item(ItemSoundAction.Hit, Item(5, 3000, 7300), target)),
            "hit 1 or 2 with heavy metal on wood as the fallback");
        Assert.AreEqual((7306, 1, 7000 + 20 * 1 + 3 * 4), Of(SoundCueRules.Item(ItemSoundAction.CriticalHit, Item(5, 2000, 7300), target)),
            "a critical, light metal at 2000");
        Assert.AreEqual(2, SoundCueRules.Item(ItemSoundAction.Hit, Item(5, sound: 7300), target)!.Unresolved!.Variants, "the fallback draws 0 or 1 too");
        Assert.AreEqual((7000 + 3 * 1, 2, null), Of(SoundCueRules.Item(ItemSoundAction.Hit, null, new(6))), "unarmed is flesh; glass hits like light metal");
        Assert.AreEqual((7000 + 20 * 3 + 3 * 3, 2, null), Of(SoundCueRules.Item(ItemSoundAction.CriticalHit, Item(1), new(0))), "no base: the materials at once");
        Assert.AreEqual(7000 + 20 * 0 + 3 * 0, SoundCueRules.Item(ItemSoundAction.Hit, Item(9), new(7))!.Id, "paper and cloth hit as flesh");
        Assert.AreEqual(7000 + 20 * 2 + 3 * 1, SoundCueRules.Item(ItemSoundAction.Hit, Item(5, 5000), new(11))!.Id, "force hits like light metal");
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.Hit, Item(5, sound: 7300), null), "no target: nothing");
        Assert.AreEqual((7304, 2, null), Of(SoundCueRules.Item(ItemSoundAction.Hit, Item(5, sound: 7300), new(20))), "no fallback past the material table");
        Assert.IsNull(SoundCueRules.Item(ItemSoundAction.Hit, Item(20), new(2)));
    }

    [TestMethod]
    public void Critter_sounds_need_a_critter_and_the_local_pc_present_and_on()
    {
        var npc = new SoundCritter(16, 4500);
        for (var action = CritterSoundAction.CriticallyHit; action <= CritterSoundAction.Agitated; action++)
            Assert.AreEqual(4500 + (int)action, SoundCueRules.Critter(action, npc, SoundLocalPc.Present)!.Id);
        Assert.AreEqual(4501, SoundCueRules.Critter(CritterSoundAction.Dying, npc with { Type = 15 }, SoundLocalPc.Present)!.Id);
        Assert.IsNull(SoundCueRules.Critter(CritterSoundAction.Fidget, npc, SoundLocalPc.Off), "silent while the local PC is off");
        Assert.IsNull(SoundCueRules.Critter(CritterSoundAction.Fidget, npc, SoundLocalPc.Absent));
        Assert.IsNull(SoundCueRules.Critter(CritterSoundAction.Fidget, npc with { Type = 3 }, SoundLocalPc.Present));
        Assert.AreEqual(4, SoundCueRules.Critter(CritterSoundAction.CriticallyHit, npc with { SoundEffect = 4 }, SoundLocalPc.Present)!.Id, "a base of 0 still counts");
    }

    [TestMethod]
    public void Footsteps_follow_the_ground_and_the_armour_with_four_variants()
    {
        static uint Art(uint type, uint armor) => (type << 28) | (armor << 20);
        SoundCue? Step(uint art, int? tile) => SoundCueRules.Critter(CritterSoundAction.Footsteps, new(16, 4500, art, tile), SoundLocalPc.Present);
        int[] plain = [2904, 2912, 2916, 2920, 2928, 2932];
        int[] chain = [2908, 2912, 2916, 2924, 2928, 2936];
        for (var tile = 0; tile < 6; tile++)
        {
            Assert.AreEqual((plain[tile], 4, null), Of(Step(Art(2, 0), tile)), $"plain {tile}");
            Assert.AreEqual(chain[tile], Step(Art(2, 3), tile)!.Id, $"chain {tile}");
            Assert.AreEqual((2900, 4, null), Of(Step(Art(2, 4), tile)), $"plate {tile}");
            Assert.AreEqual(2900, Step(Art(2, 6), tile)!.Id, $"classic plate {tile}");
        }
        Assert.AreEqual(2900, Step(Art(2, 4), null)!.Id, "plate needs no ground");
        Assert.IsNull(Step(Art(2, 0), null), "no ground class");
        Assert.IsNull(Step(Art(2, 0), 6), "a class past the table");
        Assert.AreEqual(2900, Step(Art(12, 4), 0)!.Id, "a type-12 art reads three bits");
        Assert.AreEqual(2904, Step(Art(12, 8), 0)!.Id, "a type-12 art ignores bit 23");
        Assert.AreEqual(2900, Step(Art(12, 12), 0)!.Id, "12 reads as 4 there");
        Assert.AreEqual(2904, Step(Art(13, 4), 0)!.Id, "other art types wear nothing");
        Assert.IsNull(SoundCueRules.Critter(CritterSoundAction.Footsteps, new(16, 0, Art(2, 4), 0), SoundLocalPc.Off));
        Assert.IsTrue(SoundCueRules.FootstepFrame(3) && SoundCueRules.FootstepFrame(8));
        Assert.IsFalse(SoundCueRules.FootstepFrame(0) || SoundCueRules.FootstepFrame(4) || SoundCueRules.FootstepFrame(9));
    }

    [TestMethod]
    public void Containers_portals_and_misc_objects_add_their_offsets()
    {
        Assert.AreEqual(3102, SoundCueRules.Container(2, 3100, OpenableSoundAction.Locked)!.Id);
        Assert.AreEqual(1, SoundCueRules.Container(2, 0, OpenableSoundAction.Close)!.Id, "a base of 0 still plays");
        Assert.IsNull(SoundCueRules.Container(1, 3100, OpenableSoundAction.Open));
        Assert.AreEqual(3201, SoundCueRules.Portal(1, 3200, OpenableSoundAction.Close)!.Id);
        Assert.IsNull(SoundCueRules.Portal(2, 3200, OpenableSoundAction.Open));

        Assert.AreEqual(8000, SoundCueRules.Misc(3, 8000, MiscSoundAction.Busting)!.Id);
        Assert.AreEqual(8001, SoundCueRules.Misc(0, 8000, MiscSoundAction.Destroying)!.Id);
        Assert.AreEqual(8002, SoundCueRules.Misc(17, 8000, MiscSoundAction.Animating)!.Id);
        Assert.AreEqual(8001, SoundCueRules.Misc(5, 8000, MiscSoundAction.Busting)!.Id, "a weapon is busted at + 1");
        Assert.AreEqual(8002, SoundCueRules.Misc(5, 8000, MiscSoundAction.Destroying)!.Id);
        Assert.AreEqual(8002, SoundCueRules.Misc(5, 8000, MiscSoundAction.Animating)!.Id);
        Assert.AreEqual(8000, SoundCueRules.Misc(4, 8000, MiscSoundAction.Busting)!.Id, "projectiles sound");
        foreach (var type in new[] { 1, 2, 6, 7, 8, 14, 15, 16 })
            Assert.IsNull(SoundCueRules.Misc(type, 8000, MiscSoundAction.Busting), $"type {type}");
        Assert.IsNull(SoundCueRules.Misc(3, 0, MiscSoundAction.Animating), "no base");
    }

    [TestMethod]
    public void An_attack_plays_use_then_critical_hit_or_miss_on_attacker_and_target()
    {
        var sword = Item(5, 3000, 7300);
        Assert.AreEqual((7300, SoundCueSource.Attacker), (SoundCueRules.AttackStart(false, sword)!.Cue.Id, SoundCueRules.AttackStart(false, sword)!.Source));
        Assert.AreEqual(7307, SoundCueRules.AttackStart(true, sword)!.Cue.Id);
        Assert.IsNull(SoundCueRules.AttackStart(false, null), "unarmed has no use sound");

        var npc = new SoundCritter(16, 4500);
        var critical = SoundCueRules.AttackResult(AttackSoundOutcome.CriticalHit, sword, npc, new(4), SoundLocalPc.Present);
        CollectionAssert.AreEqual(new[] { (4500, SoundCueSource.Target), (7306, SoundCueSource.Attacker) }, critical.Select(c => (c.Cue.Id, c.Source)).ToArray());
        var off = SoundCueRules.AttackResult(AttackSoundOutcome.CriticalHit, sword, npc, new(4), SoundLocalPc.Off);
        CollectionAssert.AreEqual(new[] { 7306 }, off.Select(c => c.Cue.Id).ToArray(), "the bark is a critter sound; the weapon's is not");
        Assert.AreEqual(7304, SoundCueRules.AttackResult(AttackSoundOutcome.Hit, sword, npc, new(4), SoundLocalPc.Present).Single().Cue.Id);
        Assert.AreEqual(7303, SoundCueRules.AttackResult(AttackSoundOutcome.Miss, sword, npc, new(4), SoundLocalPc.Present).Single().Cue.Id);
        Assert.AreEqual(0, SoundCueRules.AttackResult(AttackSoundOutcome.Hit, sword, npc, null, SoundLocalPc.Present).Count, "a hit on nothing");
    }

    [TestMethod]
    public void Item_weight_for_sounds_counts_ammunition_quantity_and_weighs_gold_nothing()
    {
        Assert.AreEqual(0, SoundCueRules.ItemWeight(8, 500));
        Assert.AreEqual(120, SoundCueRules.ItemWeight(5, 100, 9, 20));
        Assert.AreEqual(7, SoundCueRules.ItemWeight(6, 3, 10), "3 × 10 / 4 truncated");
        Assert.AreEqual(-7, SoundCueRules.ItemWeight(6, -3, 10), "toward zero");
        Assert.AreEqual(unchecked(int.MaxValue * 2) / 4, SoundCueRules.ItemWeight(6, int.MaxValue, 2), "32-bit product");
        Assert.AreEqual(0, SoundCueRules.CritterArtArmor(0x60F00000), "item art");
        Assert.AreEqual(15, SoundCueRules.CritterArtArmor(0x20F00000));
        Assert.AreEqual(7, SoundCueRules.CritterArtArmor(0xC0F00000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SoundCue(1, 3));
    }

    [TestMethod]
    public void A_cue_draws_its_variant_and_falls_back_when_the_id_is_in_no_table()
    {
        var calls = new List<(int, int)>();
        int Draw(int min, int max) { calls.Add((min, max)); return max; }
        var states = new Dictionary<int, SoundIdState> { [7305] = SoundIdState.EmptyRecord, [7307] = SoundIdState.Named, [9001] = SoundIdState.Unknown };
        SoundIdState Lookup(int id) => states.GetValueOrDefault(id, SoundIdState.Unknown);
        Assert.AreEqual(2903, SoundCueRules.Choose(new(2900, 4), Lookup, Draw, false), "footsteps draw 0..3");
        CollectionAssert.AreEqual(new[] { (0, 3) }, calls.ToArray());
        calls.Clear();
        Assert.AreEqual(4010, SoundCueRules.Choose(new(4010), Lookup, Draw, false), "one variant: no draw");
        Assert.AreEqual(0, calls.Count);
        var hit = new SoundCue(7304, 2, new(7040, 2));
        Assert.AreEqual(7305, SoundCueRules.Choose(hit, Lookup, Draw, false), "an empty record counts as held (Original)");
        Assert.AreEqual(7041, SoundCueRules.Choose(hit, Lookup, Draw, true), "Steamwoven: an empty record falls back");
        Assert.AreEqual(7041, SoundCueRules.Choose(new(9000, 2, new(7040, 2)), Lookup, Draw, false), "an id no table holds falls back");
        Assert.AreEqual(7307, SoundCueRules.Choose(new(7307, 1, new(7040, 2)), Lookup, Draw, true), "a named id plays");
        Assert.ThrowsExactly<InvalidOperationException>(() => SoundCueRules.Choose(new(2900, 4), Lookup, (_, _) => 4, false));
    }

    [TestMethod]
    public void A_critter_with_the_undead_animal_monster_or_mechanical_flag_is_monstrous_and_its_footsteps_are_silent()
    {
        // ARC-RENDER-1126 (0x45F730, mask 0x2000800C): the footstep caller skips monstrous critters (SN26).
        foreach (var flag in new uint[] { 0x4, 0x8, 0x8000, 0x20000000 }) Assert.IsTrue(SoundCueRules.IsMonstrous(flag), $"0x{flag:X}");
        Assert.IsFalse(SoundCueRules.IsMonstrous(0));
        Assert.IsFalse(SoundCueRules.IsMonstrous(0x10000000), "other critter flags do not count");
        Assert.IsTrue(SoundCueRules.IsMonstrous(0x10000000 | 0x4));
    }
}
