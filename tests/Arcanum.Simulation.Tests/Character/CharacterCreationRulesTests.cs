using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

// The new-character screen's race and gender buttons (ARC-UI-030, ledger NW2, NW4, NW5) as the retail 1.0.7.4 executable runs them.
[TestClass]
public sealed class CharacterCreationRulesTests
{
    private static readonly CharacterRace[] Women = [CharacterRace.Human, CharacterRace.Elf, CharacterRace.HalfElf, CharacterRace.HalfOrc];

    [TestMethod]
    public void The_race_table_offers_eight_races_and_women_of_four()
    {
        Assert.AreEqual(8, CharacterCreationRules.Races.Length);
        CollectionAssert.AreEqual(new[] { 0, 1, 4, 0, 2, 2, 0, 3 }, CharacterCreationRules.Races.Select(r => r.BodyType).ToArray(), "NW2 body types");
        CollectionAssert.AreEqual(Women, Enum.GetValues<CharacterRace>().Where(r => CharacterCreationRules.Allows(r, CharacterGender.Female)).ToArray());
        Assert.IsTrue(Enum.GetValues<CharacterRace>().Take(8).All(r => CharacterCreationRules.Allows(r, CharacterGender.Male)));
        Assert.IsFalse(CharacterCreationRules.Allows(CharacterRace.DarkElf, CharacterGender.Male), "races 8..10 are not offered");
    }

    [TestMethod]
    public void A_man_steps_through_every_race_and_wraps_both_ways()
    {
        var race = CharacterRace.Human;
        var seen = new List<CharacterRace>();
        for (var i = 0; i < 8; i++)
        {
            var press = CharacterCreationRules.NextRace(race, CharacterGender.Male);
            Assert.IsTrue(press.Changed && press.ClearsBackground && press.Gender == CharacterGender.Male);
            seen.Add(race = press.Race);
        }
        CollectionAssert.AreEqual(Enum.GetValues<CharacterRace>().Take(8).Skip(1).Append(CharacterRace.Human).ToArray(), seen.ToArray());
        Assert.AreEqual(CharacterRace.HalfOgre, CharacterCreationRules.PreviousRace(CharacterRace.Human, CharacterGender.Male).Race, "0 wraps to 7");
        Assert.AreEqual(CharacterRace.Human, CharacterCreationRules.NextRace(CharacterRace.HalfOgre, CharacterGender.Male).Race, "7 wraps to 0");
        Assert.AreEqual(CharacterRace.Dwarf, CharacterCreationRules.PreviousRace(CharacterRace.Elf, CharacterGender.Male).Race);
    }

    [TestMethod]
    public void A_woman_steps_only_through_the_races_that_allow_women()
    {
        var race = CharacterRace.Human;
        var seen = new List<CharacterRace>();
        for (var i = 0; i < 4; i++) seen.Add(race = CharacterCreationRules.NextRace(race, CharacterGender.Female).Race);
        CollectionAssert.AreEqual(new[] { CharacterRace.Elf, CharacterRace.HalfElf, CharacterRace.HalfOrc, CharacterRace.Human }, seen.ToArray());
        Assert.AreEqual(CharacterRace.HalfOrc, CharacterCreationRules.PreviousRace(CharacterRace.Human, CharacterGender.Female).Race, "wraps to the last woman's race");
        Assert.AreEqual(CharacterRace.Human, CharacterCreationRules.PreviousRace(CharacterRace.Elf, CharacterGender.Female).Race, "skips the dwarf");
        Assert.AreEqual(CharacterRace.HalfElf, CharacterCreationRules.PreviousRace(CharacterRace.HalfOrc, CharacterGender.Female).Race);
    }

    [TestMethod]
    public void A_gender_press_toggles_where_the_race_allows_it_and_the_refusal_clears_only_under_original()
    {
        foreach (var rules in CharacterCreationRuleProfile.All)
        {
            var woman = CharacterCreationRules.ToggleGender(CharacterRace.Elf, CharacterGender.Male, rules);
            Assert.AreEqual(new CreationPress(CharacterRace.Elf, CharacterGender.Female, true, true), woman);
            Assert.AreEqual(CharacterGender.Male, CharacterCreationRules.ToggleGender(CharacterRace.Elf, CharacterGender.Female, rules).Gender);
            var dwarf = CharacterCreationRules.ToggleGender(CharacterRace.Dwarf, CharacterGender.Male, rules);
            Assert.AreEqual(new CreationPress(CharacterRace.Dwarf, CharacterGender.Male, false, rules.RefusedGenderClearsBackground), dwarf, rules.Name);
        }
        Assert.IsTrue(CharacterCreationRuleProfile.Original.RefusedGenderClearsBackground, "NW4");
        Assert.IsFalse(CharacterCreationRuleProfile.Steamwoven.RefusedGenderClearsBackground);
        Assert.AreSame(CharacterCreationRuleProfile.Original, CharacterCreationRuleProfile.FromName("original"));
        Assert.IsNull(CharacterCreationRuleProfile.FromName("Original"));
    }

    [TestMethod]
    public void A_pair_the_screen_cannot_make_is_refused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CharacterCreationRules.NextRace(CharacterRace.Dwarf, CharacterGender.Female));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CharacterCreationRules.PreviousRace(CharacterRace.Orc, CharacterGender.Male));
        Assert.ThrowsExactly<ArgumentNullException>(() => CharacterCreationRules.ToggleGender(CharacterRace.Human, CharacterGender.Male, null!));
    }
}
