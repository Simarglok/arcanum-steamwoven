using Arcanum.Data.Magic;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-014: the spell data in the rules' types. Records are invented.
[TestClass]
public sealed class SessionSpellRecordsTests
{
    internal static SpellRecord Record(int number = 55, IReadOnlyList<SpellComponent>? components = null, SpellInfo info = SpellInfo.Aggressive,
        ulong beginTargets = 0x40, bool technology = false) => new()
    {
        Number = number, MinimumWillpower = 6, Cost = 7, MaintainCost = 2, MaintainPeriod = 0, DurationSeconds = 30, DurationStat = 4,
        DurationStatLevel = 10, DurationStatModifier = 3, DurationTriggerCount = 2, ResistStat = 6, ResistModifier = -2, Range = 12,
        IsTechnology = technology, ChargesFromCells = false, SkipsBeginCost = false, Info = info, Enabled = true, NoResist = false, NoReflect = true,
        CastingAnimation = -1, Missile = -1, CasterEyeCandy = [-1, -1, -1, -1, -1], TargetEyeCandy = [-1, -1, -1, -1, -1], NoStack = 1,
        CancelsSpellFlags = 0x4, CancelsEnvironmentSpellFlags = 0, DisallowedCasterSpellFlags = 0x800, DisallowedTargetSpellFlags = 0x1000,
        DisallowedTargetCritterFlags = 0x20, ItemTriggers = 0, AiPriorities = Enumerable.Repeat(-1, 10).ToArray(), AiDefensiveSecond = 0,
        LinesPresent = [true, true, false, false, false], KeysRead = [],
        Areas = [new(beginTargets, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)],
        Components = components ?? [Damage(SpellAction.Begin, 0, 3, 40, 0x800000), Damage(SpellAction.End, 0, 0, 0, 0x40), Damage(SpellAction.Begin, 4, 2, 2, 0)],
        HasDamage = true, SpellFlagsSet = 0, AreaLine = null, ComponentLines = [],
    };

    internal static SpellComponent Damage(SpellAction action, int type, int min, int max, uint flags, SpellComponentType kind = SpellComponentType.Damage) =>
        new(action, kind, new(0x40, 0, 0, 0, -1), 0, 0, new DamageData(type, min, max, flags));

    [TestMethod]
    public void A_record_translates_field_by_field_with_its_components_numbered_per_action()
    {
        var spell = SessionSpellRecords.Translate(Record());
        var run = spell.Run;
        Assert.AreEqual((7, 2, 0, 30, 4, 10, 3, 2, 6), (run.Cost, run.MaintainCost, run.MaintainPeriod, run.Duration.Seconds, run.Duration.Stat,
            run.Duration.StatLevel, run.Duration.PerPoint, run.TriggerCount, run.WillpowerMinimum));
        Assert.AreEqual((true, true, 0x4, false), (run.StackingRules, run.NoReflect, run.CancelsSpellFlags, run.SelfSpell));
        Assert.AreEqual(new SpellInvocationRecord(false, 12, 0x800, 0x1000, 0x20), run.Invocation);
        CollectionAssert.AreEqual(new[] { 2, 0, 1, 0, 0 }, run.ActionComponents.ToArray());
        Assert.AreEqual(new SpellResistRecord(55, false, true, true, 0, 6, -2), spell.Resist);
        Assert.AreEqual((SpellComponentType.Damage, 1, new SpellDamageComponent(4, 2, 2, 0)),
            spell.Component(SpellRunAction.Begin, 1) is { } second ? (second.Type, second.Index, second.Damage) : default);
        Assert.AreEqual(new SpellDamageComponent(0, 0, 0, 0x40), spell.Component(SpellRunAction.End, 0)!.Damage);
        Assert.IsNull(spell.Component(SpellRunAction.End, 1));
        Assert.AreEqual(0x40UL, spell.Areas[0].Targets);
    }

    [TestMethod]
    public void The_self_bit_and_the_info_word_feed_the_no_stack_test_and_the_resistance()
    {
        Assert.IsTrue(SessionSpellRecords.Translate(Record(beginTargets: 0x41)).Run.SelfSpell);
        Assert.IsTrue(SessionSpellRecords.Translate(Record(info: SpellInfo.MayBeAggressive)).Resist.Aggressive, "may-be-aggressive has the 0x2 bit");
        Assert.IsFalse(SessionSpellRecords.Translate(Record(info: SpellInfo.Friendly)).Resist.Aggressive);
        var heal = SessionSpellRecords.Translate(Record(components: [Damage(SpellAction.Begin, 1, 50, 50, 0, SpellComponentType.Heal)]));
        Assert.AreEqual(new SpellDamageComponent(1, 50, 50, 0), heal.Components.Single().Damage);
        var noData = SessionSpellRecords.Translate(Record(components: [new(SpellAction.Begin, SpellComponentType.EyeCandy, new(0, 0, 0, 0, -1), 0, 0, null)]));
        Assert.IsNull(noData.Components.Single().Damage);
    }
}
