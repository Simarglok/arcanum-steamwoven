using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

// ARC-WORLD-035: the blessing and curse tables (the effect numbers 0x4C4420 / 0x4C3F70, checked in the original executable).
// Entries are invented.
[TestClass]
public sealed class BlessingRulesTests
{
    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    [TestMethod]
    public void TheEffectIsTheAtoiOfTheNumbersEffectEntry()
    {
        var table = Mes("{500}{A}{501}{120}{502}{text}{511}{ 7}{521}{-3}{531}{12abc}{541}{XXX}{551}{}{561}{99999999999}{40}{x}{401}{9}{1000}{got}");
        Assert.AreEqual(new BlessingEffect(120, BlessingEffectSource.Number), BlessingRules.EffectOf(table, 50));
        Assert.AreEqual(new BlessingEffect(7, BlessingEffectSource.Number), BlessingRules.EffectOf(table, 51), "leading white space");
        Assert.AreEqual(new BlessingEffect(-3, BlessingEffectSource.Number), BlessingRules.EffectOf(table, 52));
        Assert.AreEqual(new BlessingEffect(12, BlessingEffectSource.Prefix), BlessingRules.EffectOf(table, 53));
        // A placeholder text, an empty entry and a missing one all give the module's effect 0.
        Assert.AreEqual(new BlessingEffect(0, BlessingEffectSource.NotANumber), BlessingRules.EffectOf(table, 54));
        Assert.AreEqual(new BlessingEffect(0, BlessingEffectSource.NotANumber), BlessingRules.EffectOf(table, 55));
        Assert.AreEqual(new BlessingEffect(0, BlessingEffectSource.Missing), BlessingRules.EffectOf(table, 57));
        Assert.AreEqual(new BlessingEffect(null, BlessingEffectSource.Overflow), BlessingRules.EffectOf(table, 56));
        // Below 50 no entry is read, though entry 401 exists.
        Assert.AreEqual(new BlessingEffect(0, BlessingEffectSource.BelowRange), BlessingRules.EffectOf(table, 40));
        Assert.AreEqual(new BlessingEffect(0, BlessingEffectSource.Missing), BlessingRules.EffectOf(table, int.MaxValue));
        CollectionAssert.AreEqual(new[] { 50, 51, 52, 53, 54, 55, 56 }, BlessingRules.Numbers(table).ToArray());
        // Numbers from 100 read entries above the received message 1000, as the original does.
        CollectionAssert.AreEqual(new[] { 100 }, BlessingRules.Numbers(Mes("{1000}{got}{1001}{5}")).ToArray());
        Assert.AreEqual(new BlessingEffect(5, BlessingEffectSource.Number), BlessingRules.EffectOf(Mes("{1000}{got}{1001}{5}"), 100));
    }

    [TestMethod]
    public void AnyEffectNumberReadsFromTheModuleBelowFifty()
    {
        var effects = Mes("{50}{st +1}");
        var module = Mes("{0}{st +6 be -6}");
        Assert.AreEqual(2, EffectRules.ReadAny(effects, module, 0).Rule!.Changes.Count);
        var empty = EffectRules.ReadAny(effects, module, 4).Rule!;
        Assert.AreEqual((4, false, 0), (empty.Number, empty.Defined, empty.Changes.Count));
        Assert.AreEqual((0, false), (EffectRules.ReadAny(effects, null, 0).Rule!.Number, EffectRules.ReadAny(effects, null, 0).Rule!.Defined));
        Assert.AreEqual(1, EffectRules.ReadAny(effects, module, 50).Rule!.Changes.Count);
        Assert.AreEqual(EffectRuleStatus.OutOfRange, EffectRules.ReadAny(effects, module, 400).Status);
        Assert.AreEqual(EffectRuleStatus.OutOfRange, EffectRules.ReadAny(effects, module, -1).Status);
    }
}
