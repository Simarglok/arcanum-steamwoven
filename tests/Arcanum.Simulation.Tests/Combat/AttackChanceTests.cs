using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Combat.CombatFixtures;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-UI-124 (ledger OH12): the combat panel's hit chance, 0x554830 over the shared check and its difficulty (0x4C8430) reasons.
[TestClass]
public sealed class AttackChanceTests
{
    private static AttackRequest Bow(int distance = 1, int range = 20) => Melee() with
    {
        Weapon = Sword() with { Range = range },
        Skill = CombatSkill.Bow,
        Distance = distance,
    };

    [TestMethod]
    public void A_plain_melee_attack_shows_the_effectiveness_and_no_reason()
    {
        // Melee 10: effectiveness 5 × 10 + 25 = 75, no difficulty, no icon.
        Assert.AreEqual(new AttackChance(75, SkillDifficultyReasons.None, false), AttackChanceRules.Preview(Melee()));
    }

    [TestMethod]
    public void Armour_class_takes_its_share_without_an_icon()
    {
        // The armour class term is effectiveness × (AC / 2) / 100 = 75 × 10 / 100 = 7: 68, and the panel draws no icon for it.
        var chance = AttackChanceRules.Preview(Melee() with { Target = Npc() with { ArmorClass = 20 } });
        Assert.AreEqual(new AttackChance(68, SkillDifficultyReasons.None, false), chance);
    }

    [TestMethod]
    public void The_chance_is_not_clamped_above_100()
    {
        // Melee 20: 5 × 20 + 25 = 125 (combat skills are not capped); the panel prints "%d%%".
        Assert.AreEqual(125, AttackChanceRules.Preview(Melee() with { SkillLevel = 20 }).Percent);
    }

    [TestMethod]
    public void A_weapon_heavier_than_the_wielders_strength_adds_five_a_point_and_its_icon()
    {
        // Strength 10 against a minimum of 12: +10 difficulty (flag 0x40).
        var chance = AttackChanceRules.Preview(Melee() with { Weapon = Sword() with { MinimumStrength = 12 } });
        Assert.AreEqual(new AttackChance(65, SkillDifficultyReasons.WeaponTooHeavy, false), chance);
        Assert.AreEqual(0x40, (int)chance.Reasons);
    }

    [TestMethod]
    public void A_weapons_to_hit_bonus_and_its_magic_adjustment_count_as_the_panels_invocation_has_no_cancel_flag()
    {
        // The panel's invocation flags are empty, so the weapon's magic to-hit adjustment (0x461590) counts: −(5 + 3) = −8.
        var chance = AttackChanceRules.Preview(Melee() with { Weapon = Sword() with { ToHitBonus = 5, MagicToHitAdjustment = 3 } });
        Assert.AreEqual(83, chance.Percent);
        Assert.AreEqual(SkillDifficultyReasons.None, chance.Reasons);
    }

    [TestMethod]
    public void A_target_beyond_the_range_is_zero_with_the_range_icon_and_the_distance_icon()
    {
        // Distance 11 against range 10 (flag 0x80, impossible); perception 8: 11 − 4 > 0 adds the distance penalty icon (0x100).
        var chance = AttackChanceRules.Preview(Bow(distance: 11, range: 10));
        Assert.AreEqual(new AttackChance(0, SkillDifficultyReasons.OutOfRange | SkillDifficultyReasons.DistancePenalty, false), chance);
    }

    [TestMethod]
    public void A_blocked_line_is_zero_with_the_blocked_icon_and_no_cover_icon()
    {
        var chance = AttackChanceRules.Preview(Bow(distance: 2) with { LineOfFireBlocked = true, CoverRating = 7 });
        Assert.AreEqual(new AttackChance(0, SkillDifficultyReasons.BlockedLine, false), chance);
    }

    [TestMethod]
    public void Distance_beyond_half_the_perception_and_cover_cost_five_a_cell_and_their_cost_and_show_two_icons()
    {
        // Perception 8: 10 − 4 = 6 cells × 5 = 30, and a cover cost of 4: 75 − 34 = 41 (flags 0x100 and 0x200).
        var chance = AttackChanceRules.Preview(Bow(distance: 10) with { CoverRating = 4 });
        Assert.AreEqual(new AttackChance(41, SkillDifficultyReasons.DistancePenalty | SkillDifficultyReasons.Cover, false), chance);
        Assert.AreEqual(0x300, (int)chance.Reasons);
    }

    [TestMethod]
    public void A_master_takes_no_distance_penalty_and_no_icon_for_it()
    {
        var chance = AttackChanceRules.Preview(Bow(distance: 10) with { Training = SkillTraining.Master });
        Assert.AreEqual(new AttackChance(75, SkillDifficultyReasons.None, false), chance);
    }

    [TestMethod]
    public void Darkness_and_an_injury_each_add_their_icon()
    {
        // A bow at full darkness (light 0): 30 × 255 / 255 = 30; a blinded shooter +30 more: 75 − 60 = 15 (flags 0x400, 0x800).
        var chance = AttackChanceRules.Preview(Bow() with
        {
            Target = Npc() with { LightLevel = 0 },
            Attacker = Npc() with { Conditions = CombatantConditions.Blinded },
        });
        Assert.AreEqual(new AttackChance(15, SkillDifficultyReasons.Darkness | SkillDifficultyReasons.Injury, false), chance);
    }

    [TestMethod]
    public void A_magick_cancel_chance_takes_its_share_and_above_twenty_shows_its_icon()
    {
        // A magickal weapon (complexity 5) against a target of aptitude −30, the shooter's 0: the chance is 30; 75 − 30 × 75 / 100 = 75 − 22 = 53.
        var weapon = Sword() with { MagickTechComplexity = 5 };
        var chance = AttackChanceRules.Preview(Melee() with { Weapon = weapon, Target = Npc() with { MagickTechAptitude = -30 } });
        Assert.AreEqual(new AttackChance(53, SkillDifficultyReasons.None, true), chance);
        // 20 is not above 20: the share is taken, no icon.
        var low = AttackChanceRules.Preview(Melee() with { Weapon = weapon, Target = Npc() with { MagickTechAptitude = -20 } });
        Assert.AreEqual(new AttackChance(60, SkillDifficultyReasons.None, false), low);
    }

    [TestMethod]
    public void A_thrown_skill_takes_no_cancel_chance()
    {
        var target = Npc() with { MagickTechAptitude = -50 };
        var thrown = AttackChanceRules.Preview(Melee() with
        {
            Weapon = Sword() with { MagickTechComplexity = 5 }, Target = target, Skill = CombatSkill.Throwing,
        });
        Assert.IsFalse(thrown.MagickTechPenalty);
        Assert.AreEqual(95, thrown.Percent, "7 × 10 + 25 = 95");
    }

    [TestMethod]
    public void The_cancel_share_of_a_negative_result_is_truncated_toward_zero_and_the_result_is_then_zero()
    {
        // 75 − difficulty 100 = −25; the cancel share is 30 × −25 / 100 = −7 (toward zero) taken off: −18, then 0.
        var chance = AttackChanceRules.Preview(Melee() with
        {
            Weapon = Sword() with { MinimumStrength = 30, MagickTechComplexity = 5 },
            Target = Npc() with { MagickTechAptitude = -30 },
        });
        Assert.AreEqual(0, chance.Percent);
        Assert.IsTrue(chance.MagickTechPenalty);
    }

    [TestMethod]
    public void The_panel_ignores_the_aim_the_backstab_and_the_bed_of_the_request()
    {
        var plain = AttackChanceRules.Preview(Melee());
        var other = AttackChanceRules.Preview(Melee() with { AimedLocation = HitLocation.Head, TargetWasInBed = true });
        Assert.AreEqual(plain, other);
    }

    [TestMethod]
    public void A_request_the_shared_check_refuses_throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AttackChanceRules.Preview(Melee() with { SkillLevel = 99 }));
        Assert.ThrowsExactly<ArgumentNullException>(() => AttackChanceRules.Preview(null!));
    }

    [TestMethod]
    public void The_reasons_carry_the_executables_own_bit_values()
    {
        Assert.AreEqual(0x40, (int)SkillDifficultyReasons.WeaponTooHeavy);
        Assert.AreEqual(0x80, (int)SkillDifficultyReasons.OutOfRange);
        Assert.AreEqual(0x100, (int)SkillDifficultyReasons.DistancePenalty);
        Assert.AreEqual(0x200, (int)SkillDifficultyReasons.Cover);
        Assert.AreEqual(0x400, (int)SkillDifficultyReasons.Darkness);
        Assert.AreEqual(0x800, (int)SkillDifficultyReasons.Injury);
        Assert.AreEqual(0x20000, (int)SkillDifficultyReasons.BlockedLine);
    }
}
