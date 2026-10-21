using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder): the S1 set admits
/// the scripts the ARC-RES-045 census counts (docs/research/scr-census.md, rule P7).</summary>
[TestClass]
public sealed class ScrAdmissionRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void S1_admits_the_census_count_and_no_installed_script_uses_an_unknown_type()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in SCR admission check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var admitted = 0; var codes = new Dictionary<ScrRefusalCode, int>(); var total = 0; var s6 = 0; var s7 = 0; var sounding = 0;
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var read = stack.Read(stack.Resolve(key).Winner!);
            Assert.AreEqual(ContentReadStatus.Read, read.Status, key);
            var program = ScrProgram.Read(read.Bytes!).Program;
            Assert.IsNotNull(program, key);
            total++;
            var result = ScrAdmission.Admit(program, ScrAdmissionSet.S1);
            // ARC-SCRIPT-040: scripts the sound set admits whole, and those of them with a sound action.
            if (ScrAdmission.Admit(program, ScrAdmissionSet.S6).IsAdmitted) s6++;
            if (ScrAdmission.Admit(program, ScrAdmissionSet.S7).IsAdmitted)
            {
                s7++;
                if (program.Lines.Any(l => new[] { l.OnTrue.Type, l.OnFalse.Type }.Any(t => t is ScrCatalogue.Action.PlaySound or ScrCatalogue.Action.PlaySoundOn or ScrCatalogue.Action.PlaySoundScheme))) sounding++;
            }
            if (result.IsAdmitted) admitted++;
            else codes[result.Refusal!.Code] = codes.GetValueOrDefault(result.Refusal.Code) + 1;
        }
        Assert.AreEqual(2399, total);
        Assert.AreEqual(144, admitted);
        Assert.AreEqual((336, 342, 6), (s6, s7, sounding), "the sound set admits six more installed scripts whole, each with a sound action");
        foreach (var code in new[] { ScrRefusalCode.UnknownCondition, ScrRefusalCode.UnknownAction, ScrRefusalCode.UnknownValueKind, ScrRefusalCode.UnknownFocusKind, ScrRefusalCode.IndexOutOfRange, ScrRefusalCode.WriteToNumber })
            Assert.IsFalse(codes.ContainsKey(code), code.ToString());
    }

    [TestMethod, TestCategory("RealData")]
    public void The_reading_actions_of_the_installed_scripts_are_counted()
    {
        // ARC-UI-049: how many installed scripts use the reading actions (64 book, 65 picture, 114 plaque) and the set-start-line
        // action 94, and how many more scripts S10 admits whole than S9.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in SCR reading census; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var uses = new Dictionary<int, int> { [ScrCatalogue.Action.ShowBook] = 0, [ScrCatalogue.Action.ShowPicture] = 0, [ScrCatalogue.Action.ShowPlaque] = 0, [94] = 0 };
        int s9 = 0, s10 = 0;
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            foreach (var action in uses.Keys.ToArray())
                if (program.Lines.Any(l => l.OnTrue.Type == action || l.OnFalse.Type == action)) uses[action]++;
            if (ScrAdmission.Admit(program, ScrAdmissionSet.S9).IsAdmitted) s9++;
            if (ScrAdmission.Admit(program, ScrAdmissionSet.S10).IsAdmitted) s10++;
        }
        Console.WriteLine($"scripts with 64 {uses[64]}, 65 {uses[65]}, 114 {uses[114]}, 94 {uses[94]}; S9 admits {s9}, S10 {s10}");
        Assert.AreEqual((10, 3, 13, 2), (uses[64], uses[65], uses[114], uses[94]));
        Assert.AreEqual((657, 681), (s9, s10), "the reading actions admit 24 more installed scripts whole");
    }

    [TestMethod, TestCategory("RealData")]
    public void The_skill_actions_of_the_installed_scripts_are_counted()
    {
        // ARC-SKILLS-009: how many installed scripts use get skill (47), use item with skill (79) and try to steal 100 coins (115), and
        // how many more scripts S11 admits whole than S10.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in SCR skill census; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var uses = new Dictionary<int, int> { [ScrCatalogue.Action.GetSkill] = 0, [ScrCatalogue.Action.UseItemWithSkill] = 0, [115] = 0 };
        int s10 = 0, s11 = 0;
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            foreach (var action in uses.Keys.ToArray())
                if (program.Lines.Any(l => l.OnTrue.Type == action || l.OnFalse.Type == action)) uses[action]++;
            if (ScrAdmission.Admit(program, ScrAdmissionSet.S10).IsAdmitted) s10++;
            if (ScrAdmission.Admit(program, ScrAdmissionSet.S11).IsAdmitted) s11++;
        }
        Console.WriteLine($"scripts with 47 {uses[47]}, 79 {uses[79]}, 115 {uses[115]}; S10 admits {s10}, S11 {s11}");
        Assert.AreEqual((17, 2, 1), (uses[47], uses[79], uses[115]));
        Assert.AreEqual((681, 683), (s10, s11), "the skill actions admit 2 more installed scripts whole");
    }
}
