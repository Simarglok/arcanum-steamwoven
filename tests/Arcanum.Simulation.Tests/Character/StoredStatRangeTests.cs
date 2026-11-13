using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>ARC-MAGIC-1476 (exe read 0x4B0FC0, 0x4B0FD0 and their tables at 0x5B5194 and 0x5B5204): the range the stat setter clamps a stored stat to, by the source's stat index.</summary>
[TestClass]
public sealed class StoredStatRangeTests
{
    [TestMethod]
    public void The_primary_stats_are_the_races_range()
    {
        for (var index = 0; index < 8; index++)
            foreach (var race in Enum.GetValues<CharacterRace>())
                Assert.AreEqual(CharacterStatRules.Range(race, (CharacterStat)index), CharacterStatRules.StoredRange(race, index));
        Assert.AreEqual(new StatRange(1, 26), CharacterStatRules.StoredRange(CharacterRace.Ogre, 0));
    }

    [TestMethod]
    public void The_stats_above_the_derived_ones_have_the_tables_range()
    {
        Assert.AreEqual(new StatRange(0, 51), CharacterStatRules.StoredRange(CharacterRace.Human, 17), "level");
        Assert.AreEqual(new StatRange(0, 2_000_000_000), CharacterStatRules.StoredRange(CharacterRace.Human, 18), "experience");
        Assert.AreEqual(new StatRange(-1000, 1000), CharacterStatRules.StoredRange(CharacterRace.Human, 19), "alignment");
        Assert.AreEqual(new StatRange(0, 100), CharacterStatRules.StoredRange(CharacterRace.Human, 20), "fate points");
        Assert.AreEqual(new StatRange(0, 56), CharacterStatRules.StoredRange(CharacterRace.Human, 21), "unspent points");
        Assert.AreEqual(new StatRange(0, 210), CharacterStatRules.StoredRange(CharacterRace.Human, 22), "magick points");
        Assert.AreEqual(new StatRange(0, 210), CharacterStatRules.StoredRange(CharacterRace.Human, 23), "tech points");
        Assert.AreEqual(new StatRange(0, 1000), CharacterStatRules.StoredRange(CharacterRace.Human, 24), "poison");
        Assert.AreEqual(new StatRange(0, 1000), CharacterStatRules.StoredRange(CharacterRace.Human, 25));
        Assert.AreEqual(new StatRange(0, 1), CharacterStatRules.StoredRange(CharacterRace.Human, 26), "gender");
        Assert.AreEqual(new StatRange(0, 11), CharacterStatRules.StoredRange(CharacterRace.Human, 27), "race");
    }

    [TestMethod]
    public void A_derived_stat_and_an_index_outside_the_array_have_none()
    {
        for (var index = 8; index <= 16; index++) Assert.IsNull(CharacterStatRules.StoredRange(CharacterRace.Human, index), $"stat {index}");
        Assert.IsNull(CharacterStatRules.StoredRange(CharacterRace.Human, -1));
        Assert.IsNull(CharacterStatRules.StoredRange(CharacterRace.Human, 28));
    }
}
