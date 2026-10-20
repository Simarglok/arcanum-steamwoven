using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-016: set auto-level scheme (139) as a dispatcher effect (ledger OL30: one request per object of the object
/// operand, the number a value, nothing bought now, the run goes on). Programs are invented.</summary>
[TestClass]
public sealed class ScrSchemeTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, EveryFollower = 2, Extra = 12, Player = 21;
    // S8 without the guard conditions of S4, which need a host with object queries (the fake host has none).
    private static readonly ScrRunOptions S8 = new()
    {
        Admission = new("S7-scheme", ScrAdmissionSet.S7.Conditions, ScrAdmissionSet.S7.Actions.Append(ScrCatalogue.Action.SetAutoLevelScheme),
            ScrAdmissionSet.S7.ValueKinds, ScrAdmissionSet.S7.FocusKinds),
    };

    private static ScrProgram Schemes() => Program(
        Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (Triggerer, 0), N(101))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(91))),
        Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (Attachee, 0), (Local, 0))),
        Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (Extra, 0), N(70))),
        Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (Player, 0), N(-5))),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)));

    [TestMethod]
    public void Set_scheme_is_admitted_with_s8_and_returned_per_object_in_action_order()
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, Schemes());
        var refused = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, new() { Admission = ScrAdmissionSet.S7 });
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.SetAutoLevelScheme),
            (refused.Outcome, refused.Refusal!.Code, refused.Refusal.Type));

        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S8);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the run goes on after each request");
        CollectionAssert.AreEqual(new[]
        {
            new ScrSchemeRequest(Pc, 101),
            new ScrSchemeRequest(Npc, 91),
            new ScrSchemeRequest(Pc, -5),
        }, run.Effects.Schemes.ToArray(), "a value operand is read; an absent object (no extra object) names none; the number is not range-checked");
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_run_with_only_a_scheme_is_not_empty_and_s4_stays_inside_s8()
    {
        var host = new FakeHost();
        host.Attach(Chest, Use, 600, Program(Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (Triggerer, 0), N(0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = ScrDispatcher.RunObject(new(Use, Chest, Npc, null), host, S8);
        CollectionAssert.AreEqual(new[] { new ScrSchemeRequest(Npc, 0) }, run.Effects.Schemes.ToArray(), "the dispatcher does not decide what the object is");
        Assert.IsFalse(run.Effects.IsEmpty);
        Assert.IsTrue(ScrAdmissionSet.S4.IsSubsetOf(ScrAdmissionSet.S8));
        Assert.IsTrue(S8.Admission.IsSubsetOf(ScrAdmissionSet.S8));
        Assert.IsTrue(ScrAdmissionSet.S8.Actions.Contains(ScrCatalogue.Action.SetAutoLevelScheme));
        Assert.IsFalse(ScrAdmissionSet.S8.IsSubsetOf(ScrAdmissionSet.S4));
    }

    [TestMethod]
    public void A_focus_group_operand_is_not_admitted_yet()
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 601, Program(Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (EveryFollower, 0), N(101))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S8);
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.FocusKindNotAdmitted), (run.Outcome, run.Refusal!.Code));
        Assert.AreEqual(0, run.Effects.Schemes.Count);
    }

    [TestMethod]
    public void A_deferred_call_returns_its_scheme_requests()
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, Schemes());
        var run = ScrDispatcher.RunCall(new(2023, 0, Pc, Npc), host, S8);
        CollectionAssert.AreEqual(new[] { new ScrSchemeRequest(Pc, 101), new ScrSchemeRequest(Npc, 91), new ScrSchemeRequest(Pc, -5) },
            run.Effects.Schemes.ToArray());
    }

    // Opt-in (DAT_ROOT = the game folder): the installed set-scheme actions. Only the measured counts and numbers are checked.
    [TestMethod, TestCategory("RealData")]
    public void The_installed_set_scheme_action_is_admitted_by_s8()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in set-scheme check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var found = new List<(int Script, int Line, byte Focus, int Number)>();
        ScrProgram? script2023 = null;
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            var number = int.Parse(key[4..].TakeWhile(char.IsAsciiDigit).ToArray(), System.Globalization.CultureInfo.InvariantCulture);
            if (number == 2023) script2023 = program;
            for (var line = 0; line < program.Count; line++)
                foreach (var action in new[] { program.Lines[line].OnTrue, program.Lines[line].OnFalse })
                    if (action.Type == ScrCatalogue.Action.SetAutoLevelScheme) found.Add((number, line, action.Kind(0), action.Value(1)));
        }
        // Measured on the 1.0.7.4 GOG installation: one set-scheme action, line 5 of the winning script 2023, on the triggerer, 101.
        CollectionAssert.AreEqual(new[] { (2023, 5, Triggerer, 101) }, found.ToArray());
        // From that line S8 admits it; the next line (action 99) is the first refusal, so the action itself is not what refuses.
        var refusal = ScrAdmission.AdmitFrom(script2023!, ScrAdmissionSet.S8, 5).Refusal!;
        Assert.AreEqual((6, ScrRefusalCode.ActionNotAdmitted), (refusal.Line, refusal.Code));
    }
}
