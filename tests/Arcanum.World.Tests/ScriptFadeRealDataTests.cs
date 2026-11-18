using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-SCRIPT-1241 (docs/research/bink-movies.md section 2): the installed scripts that play a movie through script action 103 (DAT_ROOT = the game folder, opt-in): which movie, audio
/// track, seconds and flag each line names, and which sets admit each script whole (the attachment points of the scripts are in the research note).</summary>
[TestClass]
public sealed class ScriptFadeRealDataTests
{
    private static ScrProgram Load(ContentStack stack, int number)
    {
        var key = stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1
            && int.TryParse(k.AsSpan(4, 5), out var n) && n == number).Order(StringComparer.Ordinal).Last();
        return ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
    }

    [TestMethod, TestCategory("RealData")]
    public void The_six_movie_scripts_name_their_movies_and_the_sets_admit_what_they_can()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in script fade check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        // Movie, audio track (the sound operand), seconds and flag of every fade line that names a movie, in line order.
        var expected = new Dictionary<int, (int Movie, int Track, int Seconds, int Flag)[]>
        {
            [1093] = [(4, 1, 0, 0)],
            [1105] = [(2, 0, 5, 2), (3, 0, 5, 2), (6, 0, 10800, 2)],
            [1109] = [(4, 0, 0, 0)],
            [2862] = [(3, 0, 0, 0), (5, 0, 0, 0)],
            [2863] = [(2, 0, 0, 0), (5, 0, 0, 0)],
            [3061] = [(2, 0, 0, 0), (5, 0, 0, 0)],
        };
        // First refusal of the widest set with the fade (S35): change script (99) in Kerghan's dialog, end-game slides (132) in the three scripts that end the game; the two small scripts are admitted whole.
        var firstRefusal = new Dictionary<int, int?> { [1093] = null, [1105] = 99, [1109] = null, [2862] = 132, [2863] = 132, [3061] = 132 };
        foreach (var (number, lines) in expected)
        {
            var program = Load(stack, number);
            var movies = program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Where(c => c.Type == ScrCatalogue.Action.Fade && c.Values[2] != 0
                    && Enumerable.Range(0, 4).All(i => c.Kinds[i] == 3))
                .Select(c => (Movie: c.Values[2], Track: c.Values[1], Seconds: c.Values[0], Flag: c.Values[3])).ToArray();
            CollectionAssert.AreEqual(lines, movies, $"script {number}");
            var whole = ScrAdmission.Admit(program, ScrAdmissionSet.S35);
            Assert.AreEqual(firstRefusal[number], whole.Refusal?.Type, $"script {number}: first refusal of S35 {whole.Refusal?.Code}");
        }
        // 1093 and 1109 only call back into the dialog script that called them (call script, 7): the use and dying sets admit them, and since ARC-SCRIPT-2198 so does the dialog set (the callee meets the dialog gates of its attachee,
        // as the original's call-script runner sends it through the event runner at the caller's point).
        foreach (var number in new[] { 1093, 1109 })
        {
            var program = Load(stack, number);
            Assert.IsTrue(ScrAdmission.Admit(program, AreaUseScriptHost.Admission).IsAdmitted && ScrAdmission.Admit(program, AreaUseScriptHost.DyingAdmission).IsAdmitted, $"script {number}");
            Assert.IsTrue(ScrAdmission.Admit(program, AreaScriptHost.TalkAdmission).IsAdmitted, $"script {number}: the dialog set admits it whole");
        }
        // ARC-SCRIPT-2198: Kerghan's dialog script 1105 is admitted whole by the dialog sets (loops, change script, disband, create item, transform, call script and call-ex, play eye candy) while a use host answers.
        var kerghan = Load(stack, 1105);
        foreach (var set in new[] { AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
        {
            var admitted = ScrAdmission.Admit(kerghan, set);
            Assert.IsTrue(admitted.IsAdmitted, $"script 1105 under {set.Id}: {admitted.Refusal?.Code} type {admitted.Refusal?.Type} line {admitted.Refusal?.Line}");
        }
    }
}
