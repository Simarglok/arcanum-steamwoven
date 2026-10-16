using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder): every script the S2 set admits runs
/// from line 0 to a typed end in both modes with a synthetic host, and the others are refused before running.</summary>
[TestClass]
public sealed class ScrDispatcherRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_admitted_installed_script_runs_to_a_typed_end_in_both_modes()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in SCR dispatcher check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var host = new FakeHost();
        var number = 0; var admitted = 0; var stops = new Dictionary<ScrStop, int>();
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            number++;
            host.Slots.Clear(); host.Scripts.Clear();
            host.Attach(Npc, 19, number, program);
            var isAdmitted = ScrAdmission.Admit(program, ScrAdmissionSet.S2).IsAdmitted;
            var trap = (program.FileFlags & (ScrCatalogue.FileFlag.NonmagicalTrap | ScrCatalogue.FileFlag.MagicalTrap)) != 0;
            if (isAdmitted) admitted++;
            foreach (var rules in ScrRuleProfile.All)
            {
                var r = ScrDispatcher.RunObject(new(19, Npc, Pc, null), host, new() { Rules = rules });
                Assert.AreEqual(trap ? ScrStop.TrapScript : isAdmitted ? r.Stop : ScrStop.Refused, r.Stop, key);
                Assert.AreNotEqual(ScrStop.Refused, isAdmitted ? r.Stop : ScrStop.Budget, key);
                if (rules == ScrRuleProfile.Steamwoven) stops[r.Stop] = stops.GetValueOrDefault(r.Stop) + 1;
            }
        }
        Assert.AreEqual(2399, number);
        // Measured on the 1.0.7.4 GOG installation (ARC-SCRIPT-008): the 144 of S1 plus four that remove or change
        // themselves; of the 148, 116 end on a return, 25 end on an unreadable line 0 and the seven trap scripts go to
        // trap code without running.
        Assert.AreEqual(148, admitted);
        CollectionAssert.AreEquivalent(new Dictionary<ScrStop, int>
        {
            [ScrStop.TrapScript] = 7, [ScrStop.ReturnSkipDefault] = 29, [ScrStop.ReturnRunDefault] = 87, [ScrStop.LineOutOfRange] = 25, [ScrStop.Refused] = 2251,
        }, stops);
    }
}
