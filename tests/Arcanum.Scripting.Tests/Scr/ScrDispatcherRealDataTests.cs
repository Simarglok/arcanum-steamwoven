using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder): every script the S2 set admits runs
/// from line 0 to a typed end in both modes with a synthetic host (the trap scripts go to trap code), and the others are refused before running.</summary>
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
        var total = 0; var admitted = 0; var stops = new Dictionary<ScrStop, int>();
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            total++;
            // The script number is the file name's leading digits, as the original names its files.
            var number = int.Parse(key[4..].TakeWhile(char.IsAsciiDigit).ToArray(), System.Globalization.CultureInfo.InvariantCulture);
            host.Slots.Clear(); host.Scripts.Clear();
            host.Attach(Npc, 19, number, program);
            var isAdmitted = ScrAdmission.Admit(program, ScrAdmissionSet.S2).IsAdmitted;
            var trap = number is >= ScrDispatcher.FirstTrapScript and <= ScrDispatcher.LastTrapScript &&
                (program.FileFlags & (ScrCatalogue.FileFlag.NonmagicalTrap | ScrCatalogue.FileFlag.MagicalTrap)) != 0;
            if (isAdmitted) admitted++;
            var steamwoven = ScrDispatcher.RunObject(new(19, Npc, Pc, null), host);
            var original = ScrDispatcher.RunObject(new(19, Npc, Pc, null), host, new() { Rules = ScrRuleProfile.Original });
            if (trap) Assert.AreEqual(ScrStop.TrapScript, steamwoven.Stop, key);
            else if (!isAdmitted) Assert.AreEqual(ScrStop.Refused, steamwoven.Stop, key);
            else Assert.IsTrue(steamwoven.Stop is ScrStop.ReturnSkipDefault or ScrStop.ReturnRunDefault or ScrStop.FellOffEnd or ScrStop.LineOutOfRange or ScrStop.Budget or ScrStop.IndexOutOfRange, key);
            // With a host holding no flags, the modes differ only in effects, never in how a run ends.
            Assert.AreEqual((steamwoven.Outcome, steamwoven.Stop, steamwoven.StopLine), (original.Outcome, original.Stop, original.StopLine), key);
            stops[steamwoven.Stop] = stops.GetValueOrDefault(steamwoven.Stop) + 1;
        }
        Assert.AreEqual(2399, total);
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
