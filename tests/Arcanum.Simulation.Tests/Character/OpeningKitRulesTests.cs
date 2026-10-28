using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>ARC-UI-036: the opening shop's kit (0x460FF0, ledger OS3, OS4): clothes and jackets by gender and armour size, and the
/// order of the kit's items.</summary>
[TestClass]
public sealed class OpeningKitRulesTests
{
    [TestMethod]
    public void The_clothes_go_by_gender_and_armour_size_and_the_jacket_by_size_only()
    {
        Assert.AreEqual((8065, 8049, 8069), (OpeningKitRules.Clothes(CharacterGender.Male, ArmorSizes.Small), OpeningKitRules.Clothes(CharacterGender.Male, ArmorSizes.Medium),
            OpeningKitRules.Clothes(CharacterGender.Male, ArmorSizes.Large)));
        Assert.AreEqual((8134, 8133, 8135), (OpeningKitRules.Clothes(CharacterGender.Female, ArmorSizes.Small), OpeningKitRules.Clothes(CharacterGender.Female, ArmorSizes.Medium),
            OpeningKitRules.Clothes(CharacterGender.Female, ArmorSizes.Large)));
        Assert.AreEqual((8118, 8110, 8126), (OpeningKitRules.Jacket(ArmorSizes.Small), OpeningKitRules.Jacket(ArmorSizes.Medium), OpeningKitRules.Jacket(ArmorSizes.Large)));
        Assert.IsNull(OpeningKitRules.Clothes(CharacterGender.Male, ArmorSizes.None));
        Assert.IsNull(OpeningKitRules.Jacket(ArmorSizes.Small | ArmorSizes.Medium));
        // Every race the creation screen offers has a size the table answers (dwarf small, half-ogre large, elf medium).
        foreach (var race in Enum.GetValues<CharacterRace>().Where(race => CharacterCreationRules.Allows(race, CharacterGender.Male)))
            Assert.IsNotNull(OpeningKitRules.Clothes(CharacterGender.Male, EquipmentRules.ArmorSize(race)), race.ToString());
    }

    [TestMethod]
    public void The_kit_is_the_background_items_then_the_clothes_then_lockpicks_and_bandages_for_any_points()
    {
        CollectionAssert.AreEqual(new[] { 6049, 7001, 8049 }, OpeningKitRules.Items([6049, 7001], 8049, 0, 0).ToArray());
        CollectionAssert.AreEqual(new[] { 8110, OpeningKitRules.Lockpicks, OpeningKitRules.Bandages }, OpeningKitRules.Items([], 8110, 1, 1).ToArray());
        CollectionAssert.AreEqual(new[] { OpeningKitRules.Bandages }, OpeningKitRules.Items([], null, 0, 3).ToArray(), "no clothes for a size the table lacks");
        Assert.AreEqual((15178, 15179), (OpeningKitRules.Lockpicks, OpeningKitRules.Bandages));
    }
}
