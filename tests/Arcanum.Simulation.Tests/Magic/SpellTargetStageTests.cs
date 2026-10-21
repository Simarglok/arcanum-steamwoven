using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-011: the per-target stage, the no-stack test, the cancels and the environment test (ledger PT rows, MG15, MG30, MG32,
// MG33). Objects, records and costs are invented.
[TestClass]
public sealed class SpellTargetStageTests
{
    private static readonly SpellRunObject Hero = new("hero", SpellObjectFacts.PcType, 10);
    private static readonly SpellRunObject Orc = new("orc", SpellObjectFacts.NpcType);
    private static readonly SpellRunObject Mage = new("mage", SpellObjectFacts.NpcType);
    private const int Shell = SpellTargetStage.AntiMagicShell, Reflect = SpellTargetStage.FullReflection;

    private static SpellRun Run(int id = 0, int spell = 7, SpellRunObject? source = null, SpellRunObject? target = null, SpellRunFlags flags = SpellRunFlags.Active,
        SpellRunAction action = SpellRunAction.Begin) =>
        new(id, spell, action, source ?? Hero, Hero, SpellLocation.None, target, SpellLocation.None, null, flags);

    private static readonly SpellRunTarget AtOrc = new(Orc, new(3, 3));

    [TestMethod]
    public void A_shelled_target_is_skipped_unless_the_run_set_the_shell()
    {
        var shelled = SpellTargetStage.Decide(Run(), new(), AtOrc, new(Shell, ParentLocal: true), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, (int?)SpellTargetStage.MessageShelledTarget, true), (shelled.Passes, shelled.Message, shelled.MaintainBar));
        Assert.AreEqual((false, (int?)null), (SpellTargetStage.Decide(Run(), new(), AtOrc, new(Shell), MagicAnomalyProfile.Steamwoven) is var quiet ? quiet.Passes : true, quiet.Message),
            "anyone but the local player is skipped silently");
        Assert.IsTrue(SpellTargetStage.Decide(Run() with { SetSpellFlags = Shell }, new(), AtOrc, new(Shell), MagicAnomalyProfile.Steamwoven).Passes, "the shell's own run");
        Assert.IsTrue(SpellTargetStage.Decide(Run(), new(), new(Hero, SpellLocation.None), new(Shell), MagicAnomalyProfile.Steamwoven).Passes, "the parent is never stopped");
        Assert.IsTrue(SpellTargetStage.Decide(Run(), new(), new(null, new(1, 1)), new(Shell), MagicAnomalyProfile.Steamwoven).Passes, "a tile");
    }

    [TestMethod]
    public void Full_reflection_turns_the_run_and_its_caster_pays_Mg15()
    {
        var facts = new SpellTargetStageFacts(Reflect, ReflectionCaster: Mage, LastCostCharged: 13, ReflectedCost: 6);
        var original = SpellTargetStage.Decide(Run(), new(), AtOrc, facts, MagicAnomalyProfile.Original);
        Assert.AreEqual((true, "orc", (string?)"hero"), (original.Passes, original.Run.Source?.Id, original.Target.Object?.Id), "source and target swap");
        Assert.AreNotEqual(SpellRunFlags.None, original.Run.Flags & SpellRunFlags.Reflected);
        Assert.AreEqual(new SpellReflectionPayment(Mage, 13), original.Payment, "MG15: the last cost charged");
        Assert.AreEqual(new SpellReflectionPayment(Mage, 6), SpellTargetStage.Decide(Run(), new(), AtOrc, facts, MagicAnomalyProfile.Steamwoven).Payment,
            "the reflected run's own cost");
        Assert.AreEqual((true, "hero", (SpellReflectionPayment?)null), SpellTargetStage.Decide(Run(), new() { NoReflect = true }, AtOrc, facts,
            MagicAnomalyProfile.Steamwoven) is var noReflect ? (noReflect.Passes, noReflect.Run.Source!.Id, noReflect.Payment) : default, "No_Reflect:");
        var back = SpellTargetStage.Decide(Run(), new(), AtOrc, facts with { SourceSpellFlags = Reflect, ParentLocal = true }, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, (int?)SpellTargetStage.MessageReflectedBack), (back.Passes, back.Message), "the caster reflects too: the spell fizzles");
        var own = SpellTargetStage.Decide(Run() with { SetSpellFlags = Reflect }, new(), AtOrc, facts, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, "hero", (SpellReflectionPayment?)null), (own.Passes, own.Run.Source!.Id, own.Payment), "the reflection's own run: no pay, no swap");
        // A run already reflected turns at a later target that does not reflect: no payment, but the swap.
        var later = SpellTargetStage.Decide(Run(flags: SpellRunFlags.Active | SpellRunFlags.Reflected), new(), AtOrc, new(0), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual(("orc", (SpellReflectionPayment?)null), (later.Run.Source!.Id, later.Payment));
        Assert.AreEqual("orc", SpellTargetStage.Decide(Run(source: Orc), new(), AtOrc, facts, MagicAnomalyProfile.Steamwoven).Run.Source!.Id,
            "the source as its own target: no swap");
    }

    [TestMethod]
    public void No_stack_finds_the_same_spell_on_the_object_or_a_self_spell_of_its_parent()
    {
        var table = SpellRunTable.Empty.With(Run(1, target: Orc)).With(Run(2, spell: 8, target: Orc)).With(Run(3, spell: 9));
        SpellRunRecord Records(int spell) => spell == 9 ? new() { SelfSpell = true } : new();
        Assert.AreEqual(1, SpellTargetStage.Blocking(table, Run(0, target: Orc), Orc, Records));
        Assert.IsNull(SpellTargetStage.Blocking(table, Run(1, target: Orc), Orc, Records), "itself is not a block");
        Assert.AreEqual(3, SpellTargetStage.Blocking(table, Run(0, spell: 9), Hero, Records), "a self spell without a target, by its parent");
        Assert.IsNull(SpellTargetStage.Blocking(table.With(Run(3, spell: 9, flags: SpellRunFlags.None)), Run(0, spell: 9), Hero, Records), "an inactive run");
    }

    [TestMethod]
    public void The_cancels_end_runs_of_the_same_source_and_the_environment_loop_compares_its_own_record_Mg33()
    {
        const int Air = 0x4, Earth = 0x8;
        var records = new Dictionary<int, SpellRunRecord> { [7] = new() { CancelsSpellFlags = Air | Earth }, [8] = new() { CancelsSpellFlags = Earth },
            [9] = new(), [10] = new() { CancelsSpellFlags = Air, CancelsEnvironmentSpellFlags = Air } };
        SpellRunRecord Records(int spell) => records[spell];
        var table = SpellRunTable.Empty.With(Run(1, spell: 8)).With(Run(2, spell: 9)).With(Run(3, spell: 8, source: Orc));
        CollectionAssert.AreEqual(new[] { 1 }, SpellTargetStage.Cancelled(table, Run(0), Records, _ => true).ToArray(), "a shared bit, the same source");
        CollectionAssert.AreEqual(new[] { 1, 2 }, SpellTargetStage.Cancelled(table, Run(0, spell: 10), Records, _ => true).ToArray(),
            "MG33: the environment loop ends every run of the source");
        Assert.AreEqual(0, SpellTargetStage.Cancelled(table, Run(0, spell: 10), Records, _ => false).Count);
        Assert.IsTrue(SpellTargetStage.EnvironmentAllows(Air, Air | Earth, false));
        Assert.IsFalse(SpellTargetStage.EnvironmentAllows(Air | Earth, Air, false));
        Assert.IsTrue(SpellTargetStage.EnvironmentAllows(SpellTargetStage.DetectingInvisible, 0, true), "extraordinary perception");
        Assert.IsFalse(SpellTargetStage.EnvironmentAllows(Air, null, true), "no local player");
        var reflections = SpellRunTable.Empty.With(Run(4, target: Orc) with { SetSpellFlags = Reflect, Parent = Mage });
        Assert.AreEqual(Mage, SpellTargetStage.FlagCaster(reflections, Orc, Reflect, Reflect));
        Assert.IsNull(SpellTargetStage.FlagCaster(reflections, Orc, 0, Reflect), "the target must carry the flag");
    }
}
