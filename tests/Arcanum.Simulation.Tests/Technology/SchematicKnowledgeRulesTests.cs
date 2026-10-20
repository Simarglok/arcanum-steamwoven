using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Technology.SchematicKnowledgeRules;

namespace Arcanum.Simulation.Tests.Technology;

// ARC-TECHNOLOGY-003: the found-schematic registration (ledger SK1–SK3). Numbers are invented.
[TestClass]
public sealed class SchematicKnowledgeRulesTests
{
    [TestMethod]
    public void A_new_number_is_appended_once_and_a_known_one_is_already_known()
    {
        var first = Register([], 4150, 10, TechnologyRuleProfile.Steamwoven);
        Assert.AreEqual(SchematicReading.Registered, first.Reading);
        var second = Register(first.Found, 4010, 10, TechnologyRuleProfile.Steamwoven);
        CollectionAssert.AreEqual(new[] { 4150, 4010 }, second.Found.ToArray(), "appended at the end");
        var again = Register(second.Found, 4150, 10, TechnologyRuleProfile.Steamwoven);
        Assert.AreEqual(SchematicReading.AlreadyKnown, again.Reading);
        Assert.AreEqual(second.Found, again.Found);
        Assert.AreEqual(SchematicReading.ListFull, Register(second.Found, 4020, 2, TechnologyRuleProfile.Steamwoven).Reading);
        Assert.AreEqual(SchematicReading.AlreadyKnown, Register(second.Found, 4010, 2, TechnologyRuleProfile.Steamwoven).Reading, "a full list still knows");
    }

    [TestMethod]
    public void Number_zero_is_refused_in_steamwoven_and_registered_in_the_original_TC8()
    {
        Assert.AreEqual(SchematicReading.RefusedZero, Register([], 0, 10, TechnologyRuleProfile.Steamwoven).Reading);
        var original = Register([], 0, 10, TechnologyRuleProfile.Original);
        Assert.AreEqual(SchematicReading.Registered, original.Reading);
        CollectionAssert.AreEqual(new[] { 0 }, original.Found.ToArray());
        Assert.IsTrue(TechnologyRuleProfile.Original.RegistersSchematicZero);
        Assert.IsFalse(TechnologyRuleProfile.Steamwoven.RegistersSchematicZero);
        Assert.AreSame(TechnologyRuleProfile.Steamwoven, TechnologyRuleProfile.FromName("steamwoven"));
    }

    [TestMethod]
    public void Only_a_schematic_subtype_registers()
    {
        Assert.AreEqual(SchematicReading.NotSchematic, Read([], 1, 4150, 10, TechnologyRuleProfile.Steamwoven).Reading);
        Assert.AreEqual(SchematicReading.Registered, Read([], SchematicSubtype, 4150, 10, TechnologyRuleProfile.Steamwoven).Reading);
        Assert.ThrowsExactly<ArgumentException>(() => Register(default, 1, 1, TechnologyRuleProfile.Steamwoven));
    }
}
