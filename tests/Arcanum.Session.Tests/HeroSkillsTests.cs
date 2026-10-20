using System.Collections.Immutable;
using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's and followers' skills in play (ARC-SKILLS-002). The half-elf fixture hero has DX 9, PE 8, CH 8 (effects), and here
// bought melee 2 (apprentice), dodge 1, persuasion 3 (master bits), firearms 1, and two innate skill changes of its race and
// gender effects (firearms +2, haggle −1), invented in the shape of the pre-made heroes' (SH-09).
[TestClass]
public sealed class HeroSkillsTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;

    internal static readonly ImmutableArray<SessionInnateSkillChange> Innate =
    [
        new(EffectCause.Race, Skill.Firearms, StatModifierOperator.Add, 2),
        new(EffectCause.Gender, Skill.Haggle, StatModifierOperator.Add, -1),
    ];

    private static SessionHeroAdvancement Bought()
    {
        int[] basic = new int[12], tech = new int[4];
        basic[(int)Skill.Melee] = 2 | 1 << 6;
        basic[(int)Skill.Dodge] = 1;
        basic[(int)Skill.Persuasion] = 3 | 3 << 6;
        tech[SkillRules.FamilyIndex(Skill.Firearms)] = 1;
        return SessionHeroAdvancement.None with { BasicSkills = [.. basic], TechSkills = [.. tech] };
    }

    internal static SessionHeroDefinition Skilled() => HalfElf() with { Advancement = Bought(), InnateSkills = Innate };

    private static GameSessionOwner Owner(SessionHeroDefinition? hero = null)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ?? Skilled() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void TheHerosSkillsReadItsPointsTrainingKeyStatsAndInnateChanges()
    {
        using var owner = Owner();
        var skills = owner.QueryHeroSkills()!;
        Assert.AreEqual(SkillRules.Count, skills.All.Length);
        Assert.AreEqual(new SessionSkill(Skill.Melee, 2 | 1 << 6, 2, 8, SkillTraining.Apprentice), skills[Skill.Melee]);
        Assert.AreEqual(new SessionSkill(Skill.Dodge, 1, 1, 4, SkillTraining.None), skills[Skill.Dodge]);
        Assert.AreEqual((12, 7, SkillTraining.Master), (4 * skills[Skill.Persuasion].Points, skills[Skill.Persuasion].Level,
            skills[Skill.Persuasion].Training), "charisma 8 caps 12 at 7; the stored tier stays");
        Assert.AreEqual(6, skills[Skill.Firearms].Level, "4 + the race's 2, within perception 8's cap 7");
        Assert.AreEqual(0, skills[Skill.Haggle].Level, "0 − 1 clamped to 0");
        CollectionAssert.AreEqual(Innate.ToArray(), owner.QueryHero()!.InnateSkills.ToArray());
        Assert.AreEqual(1, owner.QueryHero()!.Advancement.TechSkills[1], "the innate change is never written into the points");

        using var plain = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.IsNull(plain.QueryHeroSkills());
    }

    [TestMethod]
    public void TheHerosAttackDodgeAndWeaponSpeedUseItsSkills()
    {
        using var owner = Owner();
        var bare = owner.QueryHeroAttack()!;
        Assert.AreEqual((CombatSkill.Melee, 8, SkillTraining.Apprentice, 10), (bare.Skill, bare.SkillLevel, bare.Training, bare.WeaponSpeed),
            "bare hands: melee 8, apprentice, the unarmed speed");
        Assert.AreEqual((4, SkillTraining.None, 0, SkillTraining.None), (bare.Combatant.DodgeLevel, bare.Combatant.DodgeTraining,
            bare.Combatant.BackstabLevel, bare.Combatant.BackstabTraining));

        var blade = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon
            {
                Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 15, MinimumStrength = 6,
                CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
            },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon,
            new(Kit[0].Kind, [], false, blade), Drawn, Accept).Failure);
        var armed = owner.QueryHeroAttack()!;
        Assert.AreEqual((8, SkillTraining.Apprentice, 20), (armed.SkillLevel, armed.Training, armed.WeaponSpeed), "15 + 5 for an apprentice");
        Assert.AreEqual(20, owner.QueryHeroEquipmentCombat()!.WeaponSpeed);
        Assert.AreEqual(15, owner.QueryHeroEquipmentCombat(SkillTraining.None)!.WeaponSpeed, "an explicit training still overrides");

        using var untrained = Owner(HalfElf());
        Assert.AreEqual((0, SkillTraining.None), (untrained.QueryHeroAttack()!.SkillLevel, untrained.QueryHeroAttack()!.Training));
    }

    [TestMethod]
    public void ADurableEffectsSkillChangeReachesTheLevels()
    {
        using var owner = Owner();
        // Effect 200: melee +2 (basic skill 2), repair +4 (tech skill 0), in level units.
        var effect = new SessionEffectDefinition(200, [new(EffectAttributeKind.BasicSkill, 2, EffectChangeOperator.Add, 2),
            new(EffectAttributeKind.TechSkill, 0, EffectChangeOperator.Add, 4)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AddHeroEffect(owner.Version, effect, EffectCause.Spell).Failure);
        var skills = owner.QueryHeroSkills()!;
        Assert.AreEqual((10, 4), (skills[Skill.Melee].Level, skills[Skill.Repair].Level), "melee 8 + 2; repair 0 + 4 within intelligence 8's cap 7");
        Assert.AreEqual(10, owner.QueryHeroAttack()!.SkillLevel);
    }

    [TestMethod]
    public void TheBestItemsChoiceScoresTheHerosWeaponSkillLevels()
    {
        using var owner = Owner();
        var chooser = owner.HeroChooser();
        Assert.AreEqual((8, 0, 0, 6), (chooser.MeleeLevel, chooser.BowLevel, chooser.ThrowingLevel, chooser.FirearmsLevel));
    }

    [TestMethod]
    public void TheFollowCheckReadsTheHerosPersuasion()
    {
        // A dislike that refuses an ordinary PC: a master of persuasion goes straight to the follower limit.
        var dislike = PartyStateTests.Facts(reaction: -100);
        using var master = Owner();
        var joined = master.JoinParty(master.Version, "virgil", dislike, false);
        Assert.AreEqual((GameSessionFailure.None, FollowConsent.Accepted), (joined.Failure, joined.Consent));
        using var plain = Owner(HalfElf());
        Assert.AreEqual(FollowConsent.Dislike, plain.JoinParty(plain.Version, "virgil", dislike, false).Consent);
        // Without a hero the caller's persuasion still decides.
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(FollowConsent.Accepted, heroless.JoinParty(heroless.Version, "virgil",
            dislike with { PcPersuasion = PersuasionTraining.Master }, false).Consent);
        // The master's expert bonus: charisma 8 / 4 + 1 = 3 followers.
        var limited = PartyStateTests.Facts(maxFollowers: 0);
        Assert.AreEqual(FollowConsent.Accepted, master.JoinParty(master.Version, "sogg", limited, false).Consent);
        Assert.AreEqual(FollowConsent.Accepted, master.JoinParty(master.Version, "gar", limited, false).Consent);
        Assert.AreEqual(FollowConsent.LimitReached, master.JoinParty(master.Version, "dog", limited, false).Consent);
    }

    [TestMethod]
    public void TheMaxFollowersStatEffectsCountForTheHero()
    {
        // Max followers −2 (stat 15): charisma 8 / 4 + 1 (master persuasion) − 2 = 1 follower.
        using var owner = Owner();
        var effect = new SessionEffectDefinition(201, [new(EffectAttributeKind.Stat, 15, EffectChangeOperator.Add, -2)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AddHeroEffect(owner.Version, effect, EffectCause.Curse).Failure);
        var facts = PartyStateTests.Facts(maxFollowers: 7);
        Assert.AreEqual(FollowConsent.Accepted, owner.JoinParty(owner.Version, "virgil", facts, false).Consent);
        Assert.AreEqual(FollowConsent.LimitReached, owner.JoinParty(owner.Version, "sogg", facts, false).Consent, "the caller's 7 does not count");
    }

    [TestMethod]
    public void TheInnateSkillChangesAreSavedAndRestored()
    {
        using var owner = Owner();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"innateSkills\":[{\"cause\":\"Race\",\"skill\":\"Firearms\",\"operator\":\"Add\",\"value\":2}," +
            "{\"cause\":\"Gender\",\"skill\":\"Haggle\",\"operator\":\"Add\",\"value\":-1}]");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(Innate.ToArray(), restored.QueryHero()!.InnateSkills.ToArray());
        Assert.AreEqual(owner.QueryHeroSkills()!.All.ToArray().Length, restored.QueryHeroSkills()!.All.Length);
        CollectionAssert.AreEqual(owner.QueryHeroSkills()!.All.ToArray(), restored.QueryHeroSkills()!.All.ToArray());

        // A shape error is a decode failure; a change no hero can have is the hero's import refusal.
        var bless = text.Replace("{\"cause\":\"Race\"", "{\"cause\":\"Bless\"");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(bless).Failure);
        var divide = text.Replace("\"operator\":\"Add\",\"value\":2}", "\"operator\":\"Divide\",\"value\":0}");
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Restore(Decode(divide), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void AHeroWithAnInnateChangeNoEffectCanHaveIsRefused()
    {
        SessionInnateSkillChange[][] refused =
        [
            [new(EffectCause.Bless, Skill.Melee, StatModifierOperator.Add, 1)],
            [new(EffectCause.Race, (Skill)16, StatModifierOperator.Add, 1)],
            [new(EffectCause.Race, Skill.Melee, (StatModifierOperator)9, 1)],
            [new(EffectCause.Race, Skill.Melee, StatModifierOperator.Divide, 0)],
            [new(EffectCause.Race, Skill.Melee, StatModifierOperator.Add, SessionInnateSkillChange.MaxMagnitude + 1)],
            [.. Enumerable.Repeat(new SessionInnateSkillChange(EffectCause.Race, Skill.Melee, StatModifierOperator.Add, 1), SessionInnateSkillChange.MaxPerEffect + 1)],
            [new(EffectCause.Gender, Skill.Melee, StatModifierOperator.Add, 1), new(EffectCause.Race, Skill.Melee, StatModifierOperator.Add, 1)],
            [null!],
        ];
        foreach (var changes in refused)
            Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { InnateSkills = changes } }).Failure);
        SessionInnateSkillChange[] full =
        [
            .. Enumerable.Repeat(new SessionInnateSkillChange(EffectCause.Race, Skill.Melee, StatModifierOperator.Add, 1), SessionInnateSkillChange.MaxPerEffect),
            .. Enumerable.Repeat(new SessionInnateSkillChange(EffectCause.Gender, Skill.Bow, StatModifierOperator.Add, 1), SessionInnateSkillChange.MaxPerEffect),
        ];
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { InnateSkills = full } }).Failure,
            "seven per effect, race first");
    }

    [TestMethod]
    public void TheCreationSchemeComparesLevelsWithTheInnateChanges()
    {
        // "melee 8": two points without an innate change, one with melee +4.
        var rules = SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme> { [65] = AutoLevelRules.Parse(Encoding.ASCII.GetBytes("melee 8,")).Scheme! },
            AutoLevelRules.DefaultSpellWillpower);
        GameSessionOwner Start(ImmutableArray<SessionInnateSkillChange> innate) => GameSessionOwner.Create(Descriptor() with
        {
            Hero = HalfElf() with { Advancement = SessionHeroAdvancement.WithScheme(65), InnateSkills = innate }, AutoLevel = rules,
        }).Owner!;
        using var plain = Start([]);
        using var helped = Start([new(EffectCause.Race, Skill.Melee, StatModifierOperator.Add, 4)]);
        Assert.AreEqual((2, 3), (plain.QueryHero()!.Advancement.BasicSkills[(int)Skill.Melee], plain.QueryHero()!.UnspentPoints));
        Assert.AreEqual((1, 4), (helped.QueryHero()!.Advancement.BasicSkills[(int)Skill.Melee], helped.QueryHero()!.UnspentPoints));
        Assert.AreEqual(8, helped.QueryHeroSkills()![Skill.Melee].Level);
    }

    [TestMethod]
    public void ALevelledFollowerDefendsWithItsSheetsDodge()
    {
        // The map data stores dodge 0; the session levelled the follower to dodge 2 points (8) under its sheet's dexterity 9.
        var rules = SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme>
        {
            [67] = AutoLevelRules.Parse(Encoding.ASCII.GetBytes("dodge 8,")).Scheme!,
            [70] = AutoLevelRules.Parse(Encoding.ASCII.GetBytes("cn 9,")).Scheme!,
        }, AutoLevelRules.DefaultSpellWillpower);
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules(), AutoLevel = rules }).Owner!;
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(dexterity: 4), critterFlags: 0);
        var map = SessionCritterCombat.From("follower", fields).Combat!;
        var follower = new SessionNpcAdvancement("follower", new(CharacterRace.Human, false, [8, 9, 8, 8, 8, 8, 8, 8], 0, 0, 1, 0,
            [.. new int[12]], [.. new int[4]], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 67);
        Assert.AreEqual(GameSessionFailure.None, owner.LevelFollower(owner.Version, follower, AdvancementModifiers.None, new(1, 4)).Failure);
        var sheet = owner.QueryNpcAdvancement("follower")!.Sheet;
        var defending = map.WithSheet(sheet);
        var state = new LifeState(0, 0, 0, LifeConditions.None, 0);
        Assert.AreEqual(0, map.Combatant(LifeRules.Evaluate(state, map.Facts), state, owner.Rules.Combat).DodgeLevel, "the map data: no dodge");
        Assert.AreEqual((8, sheet.Level), (defending.Combatant(LifeRules.Evaluate(state, defending.Facts), state, owner.Rules.Combat).DodgeLevel, defending.Level),
            "the sheet's points under the sheet's dexterity 9, not the map's 4");
        Assert.AreEqual(GameSessionFailure.None, owner.AttackCritter(owner.Version, new(map)).Failure, "the attack reads the record");
    }

    [TestMethod]
    public void ALevelledFollowersSkillsComeFromItsSavedSheet()
    {
        var rules = SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme>
        {
            [66] = AutoLevelRules.Parse(Encoding.ASCII.GetBytes("dodge 8, heal 4,")).Scheme!,
            [70] = AutoLevelRules.Parse(Encoding.ASCII.GetBytes("cn 9,")).Scheme!,
        }, AutoLevelRules.DefaultSpellWillpower);
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules(), AutoLevel = rules }).Owner!;
        var follower = new SessionNpcAdvancement("virgil", new(CharacterRace.Human, false, [.. Enumerable.Repeat(9, 8)], 0, 0, 1, 0,
            [.. new int[12]], [.. new int[4]], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 66);
        Assert.IsNull(owner.QueryNpcSkills("virgil", AdvancementModifiers.None));
        Assert.AreEqual(GameSessionFailure.None, owner.LevelFollower(owner.Version, follower, AdvancementModifiers.None, new(1, 4)).Failure);
        var skills = owner.QueryNpcSkills("virgil", AdvancementModifiers.None)!;
        Assert.AreEqual((8, 4), (skills[Skill.Dodge].Level, skills[Skill.Heal].Level));
        var boosted = AdvancementModifiers.None with { BasicSkills = [new((int)Skill.Heal, StatModifierOperator.Add, 3)] };
        Assert.AreEqual(7, owner.QueryNpcSkills("virgil", boosted)![Skill.Heal].Level, "its effects now, as the level update takes them");
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryNpcSkills("", AdvancementModifiers.None));
    }
}
