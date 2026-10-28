using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-021: the floating text rules against the behaviour recorded in docs/research/ledger/ui-audio.md CH10–CH12 (colours,
/// start offset and stacking, rise, fade, box, the attack's lines and their order); values invented, expected results worked by
/// hand.</summary>
[TestClass]
public sealed class FloatersTests
{
    private static string Message(int number) => "m" + number;

    [TestMethod]
    public void Colours_settings_and_sides_are_the_originals()
    {
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), FloaterRules.Rgb(FloaterColor.White));
        Assert.AreEqual(((byte)255, (byte)0, (byte)0), FloaterRules.Rgb(FloaterColor.Red));
        Assert.AreEqual(((byte)0, (byte)255, (byte)0), FloaterRules.Rgb(FloaterColor.Green));
        Assert.AreEqual(((byte)64, (byte)64, (byte)255), FloaterRules.Rgb(FloaterColor.Blue));
        Assert.AreEqual(((byte)255, (byte)255, (byte)0), FloaterRules.Rgb(FloaterColor.Yellow));
        Assert.AreEqual(FloaterColor.Red, FloaterRules.SideColor(FloaterSide.Pc));
        Assert.AreEqual(FloaterColor.Yellow, FloaterRules.SideColor(FloaterSide.PcFollower));
        Assert.AreEqual(FloaterColor.White, FloaterRules.SideColor(FloaterSide.Other));
        Assert.AreEqual(3, FloaterRules.Step(FloaterRules.DefaultSpeed), "speed 2 rises 3 px a step: 60 px a second");
        Assert.AreEqual(16, FloaterRules.LineHeight(14), "the text height + 2");
        Assert.Throws<ArgumentOutOfRangeException>(() => FloaterRules.Step(-1));
    }

    [TestMethod]
    public void A_new_line_starts_four_heights_down_or_stacks_under_a_line_that_has_just_started()
    {
        Assert.AreEqual(64, FloaterRules.StartOffset(null, 16));
        Assert.AreEqual(80, FloaterRules.StartOffset(64, 16), "the newest line has not risen: one height below it");
        Assert.AreEqual(65, FloaterRules.StartOffset(49, 16), "risen 15 px of 16: still below it");
        Assert.AreEqual(64, FloaterRules.StartOffset(48, 16), "risen a whole height (3h): the usual start");
    }

    [TestMethod]
    public void A_line_fades_in_from_four_heights_is_opaque_in_the_middle_and_fades_out_to_the_top()
    {
        Assert.AreEqual(0, FloaterRules.Alpha(64, 16), "at 4h: m = 72 → 80 − 72 = 8 = h / 2");
        Assert.AreEqual(42, FloaterRules.Alpha(60, 16), "(12 − 8) / 24 × 255 = 42.5");
        Assert.AreEqual(255, FloaterRules.Alpha(40, 16), "2.5h: m = 3h, opaque");
        Assert.AreEqual(255, FloaterRules.Alpha(24, 16), "1.5h: m = 2h, 24 / 24 × 255");
        Assert.AreEqual(127, FloaterRules.Alpha(12, 16), "12 / 24 × 255 = 127.5");
        Assert.AreEqual(10, FloaterRules.Alpha(1, 16), "1 / 24 × 255 = 10.6");
        Assert.AreEqual(0, FloaterRules.Alpha(0, 16), "at the top");
        Assert.AreEqual(0, FloaterRules.Alpha(100, 16), "a line stacked below 4h is invisible");
        Assert.AreEqual(166, FloaterRules.Alpha(17, 17), "odd height: h / 2 = 8, (25 − 8) / 26 × 255 = 166.7");
    }

    [TestMethod]
    public void The_box_stands_over_the_object_and_centres_its_lines()
    {
        Assert.AreEqual((200, 230), FloaterRules.Box(300, 400, 16), "100 left, 90 + 5h up");
        Assert.AreEqual(275, FloaterRules.LineLeft(200, 50));
        Assert.AreEqual(200, FloaterRules.LineLeft(200, 201), "(200 − 201) / 2 truncates to 0");
        Assert.AreEqual(199, FloaterRules.LineLeft(200, 203));
    }

    [TestMethod]
    public void The_field_rises_every_fifty_milliseconds_and_drops_lines_above_the_top()
    {
        var field = new FloaterField<string>(16);
        Assert.IsTrue(field.Add("a", "x", FloaterColor.Red));
        Assert.IsTrue(field.Advance(0, 2), "the first tick steps");
        Assert.AreEqual(61, field.Lines("a")[0].Offset);
        Assert.IsFalse(field.Advance(49, 2), "less than 50 ms");
        Assert.IsTrue(field.Advance(50, 2));
        Assert.AreEqual(58, field.Lines("a")[0].Offset);
        Assert.IsTrue(field.Add("a", "y", FloaterColor.White));
        Assert.AreEqual(74, field.Lines("a")[1].Offset, "stacked under the line that has risen 6 px");
        Assert.IsTrue(field.Add("b", "z", FloaterColor.Green));
        CollectionAssert.AreEqual(new[] { "a", "b" }, field.Owners.ToArray());
        Assert.IsFalse(field.Add("b", "", FloaterColor.Green), "an empty text");
        long now = 50;
        while (field.Lines("a").Count == 2) field.Advance(now += 50, 9);
        Assert.AreEqual(1, field.Lines("a").Count, "the older line went first");
        Assert.AreEqual("y", field.Lines("a")[0].Text);
        while (field.Count > 0) field.Advance(now += 50, 9);
        Assert.AreEqual(0, field.Owners.Count, "objects without lines are dropped");

        var edge = new FloaterField<int>(16);
        edge.Add(1, "x", FloaterColor.White);
        for (var t = 0; t < 21; t++) edge.Advance(t * 50L, 2);
        Assert.AreEqual(1, edge.Lines(1)[0].Offset, "64 − 21 × 3");
        edge.Advance(21 * 50L, 0);
        Assert.AreEqual(0, edge.Lines(1)[0].Offset, "a line at 0 stays");
        edge.Advance(22 * 50L, 0);
        Assert.AreEqual(0, edge.Count, "below 0 it is removed");

        var full = new FloaterField<int>(16);
        for (var i = 0; i < FloaterField<int>.MaxLines; i++) Assert.IsTrue(full.Add(i % 3, "l", FloaterColor.White));
        Assert.IsFalse(full.Add(0, "l", FloaterColor.White), "the project's limit");
        full.Clear();
        Assert.AreEqual(0, full.Count);
    }

    [TestMethod]
    public void A_critical_hit_floats_its_messages_in_order_and_the_numbers_only_at_level_two()
    {
        var hit = new CombatFloaterHit
        {
            CritterStep = true, Struck = FloaterSide.PcFollower, Critical = FloaterCritical.Hit, Multiplier = FloaterMultiplier.Double,
            Stunned = true, KnockedDown = true, Scarred = true, Blinded = true, CrippledArm = true, ArmDropsWeapon = true, ArmDropsShield = true,
            CrippledLegs = true, Armor = FloaterItem.Broken, HelmetDropped = true, Weapon = FloaterItem.Damaged, AmmunitionLost = true,
            WeaponDropped = true, HitPoints = 12, Fatigue = 4, Poison = 3, AttackerWeapon = FloaterItem.Damaged,
        };
        var minimal = CombatFloaters.Of(hit, 1, Message);
        CollectionAssert.AreEqual(new[] { "m12", "m14", "m16", "m18", "m3", "m4", "m5", "m19", "m20", "m6", "m24", "m20", "m8", "m9", "m19", "m8" },
            minimal.Select(f => f.Text).ToArray());
        Assert.IsTrue(minimal.Take(15).All(f => f.Over == FloaterOver.Struck && f.Color == FloaterColor.Yellow), "the follower's yellow");
        Assert.AreEqual(new CombatFloater(FloaterOver.Attacker, FloaterColor.Red, "m8"), minimal[^1], "the attacker's weapon wear in red over it");
        var verbose = CombatFloaters.Of(hit, 2, Message);
        Assert.AreEqual(minimal.Count + 2, verbose.Count);
        Assert.AreEqual(new CombatFloater(FloaterOver.Struck, FloaterColor.Yellow, "12 m1 4 m10"), verbose[15]);
        Assert.AreEqual(new CombatFloater(FloaterOver.Struck, FloaterColor.Green, "m0 +3"), verbose[16]);
        Assert.AreEqual(0, CombatFloaters.Of(hit, 0, Message).Count - minimal.Count, "level 0 makes the messages (the draw skips them)");
    }

    [TestMethod]
    public void The_damage_line_follows_the_originals_format()
    {
        string Line(int hitPoints, int fatigue) =>
            CombatFloaters.Of(new CombatFloaterHit { CritterStep = true, Struck = FloaterSide.Pc, HitPoints = hitPoints, Fatigue = fatigue }, 2, Message).Single().Text;
        Assert.AreEqual("7 m1", Line(7, 0));
        Assert.AreEqual("7 m1 2 m10", Line(7, 2));
        Assert.AreEqual(" 2 m10", Line(0, 2), "fatigue alone keeps the leading space");
        Assert.AreEqual("0 m1", Line(0, 0), "neither: the health part for 0");
        Assert.AreEqual("-2 m1", Line(-2, 0), "a heal through resistance prints its sign");
        var red = CombatFloaters.Of(new CombatFloaterHit { CritterStep = true, Struck = FloaterSide.Pc }, 2, Message).Single();
        Assert.AreEqual(FloaterColor.Red, red.Color, "the PC's red");
        Assert.AreEqual(0, CombatFloaters.Of(new CombatFloaterHit { CritterStep = true, Struck = FloaterSide.Pc, HitPoints = 5 }, 1, Message).Count, "no number at level 1");
    }

    [TestMethod]
    public void Dodges_objects_misses_and_a_destroyed_weapon_float_their_own_lines()
    {
        Assert.AreEqual(new CombatFloater(FloaterOver.Struck, FloaterColor.White, "m11"),
            CombatFloaters.Of(new CombatFloaterHit { Dodged = true, Struck = FloaterSide.Pc }, 1, Message).Single(), "a dodge is white at every level");
        Assert.AreEqual(new CombatFloater(FloaterOver.Struck, FloaterColor.White, "m1 9"),
            CombatFloaters.Of(new CombatFloaterHit { ObjectStep = true, HitPoints = 9 }, 2, Message).Single(), "an object: text first, then the number");
        Assert.AreEqual(0, CombatFloaters.Of(new CombatFloaterHit { ObjectStep = true, HitPoints = 9 }, 1, Message).Count);
        var miss = CombatFloaters.Of(new CombatFloaterHit { CritterStep = true, Struck = FloaterSide.Other, Critical = FloaterCritical.Miss, Multiplier = FloaterMultiplier.OneAndHalf }, 1, Message);
        CollectionAssert.AreEqual(new[] { "m2", "m13" }, miss.Select(f => f.Text).ToArray(), "a critical self-hit: the miss, then its multiplier");
        var destroyed = CombatFloaters.Of(new CombatFloaterHit
        {
            CritterStep = true, Struck = FloaterSide.Other, Weapon = FloaterItem.Broken, WeaponDestroyed = true, AmmunitionLost = true, WeaponDropped = true,
            KnockedOut = true, KnockedDown = true,
        }, 1, Message);
        CollectionAssert.AreEqual(new[] { "m17", "m23", "m22" }, destroyed.Select(f => f.Text).ToArray(), "unconscious wins; a destroyed weapon loses no ammunition and drops nothing");
        var hands = CombatFloaters.Of(new CombatFloaterHit { AttackerHands = 2, AttackerWeapon = FloaterItem.Broken }, 2, Message);
        CollectionAssert.AreEqual(new[] { new CombatFloater(FloaterOver.Attacker, FloaterColor.Red, "m23"), new CombatFloater(FloaterOver.Attacker, FloaterColor.Red, "2 m1") },
            hands.ToArray());
        Assert.AreEqual(0, CombatFloaters.Of(new CombatFloaterHit { AttackerHands = 2 }, 1, Message).Count, "the hands' loss only at level 2");
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatFloaters.Of(new CombatFloaterHit(), 3, Message));
    }
}
