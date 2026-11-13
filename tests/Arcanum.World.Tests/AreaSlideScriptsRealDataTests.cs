using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-RENDER-1171 opt-in (<c>DAT_ROOT</c>, the game folder; ledger SS1, SS2): the installed slide scripts 997 (end game), 998 (death) and 999 (credits) and the eight ending scripts
/// they call (2921–2928) are all admitted by <see cref="ScrAdmissionSet.SlideScripts"/>, and a run against a game with no flags set queues what the scripts' lines say: the credits slides 45
/// to 55, the default death slide 0 and, for the end game, the last ending script's chain. Slide numbers only; no picture, voice or text is read or printed.
/// </summary>
[TestClass]
public sealed class AreaSlideScriptsRealDataTests
{
    private static ScriptContent Open(out ContentStack stack)
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        stack = ContentStack.Open(discovery.Plan!).Stack!;
        return ScriptContent.Over(stack);
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_installed_slide_and_ending_script_is_admitted_by_the_slide_set()
    {
        var content = Open(out var stack);
        using var _ = stack;
        foreach (var number in new[] { 997, 998, 999, 2921, 2922, 2923, 2924, 2925, 2926, 2927, 2928 })
        {
            var read = content.Script(number);
            Assert.AreEqual(ScriptContentStatus.Read, read.Status, $"script {number} {read.Detail}");
            var admission = ScrAdmission.Admit(read.Program!, ScrAdmissionSet.SlideScripts);
            Assert.IsTrue(admission.IsAdmitted, $"script {number}: {admission.Refusal}");
        }
    }

    [TestMethod, TestCategory("RealData")]
    public void The_credits_script_queues_slides_45_to_55_and_the_death_script_slide_0_without_flags()
    {
        var content = Open(out var stack);
        using var _ = stack;
        var credits = AreaSlideScripts.Run(AreaSlideKind.Credits, content, null);
        CollectionAssert.AreEqual(Enumerable.Range(45, 11).ToArray(), credits.Slides.ToArray(), credits.Detail);
        var death = AreaSlideScripts.Run(AreaSlideKind.Death, content, null);
        CollectionAssert.AreEqual(new[] { 0 }, death.Slides.ToArray(), death.Detail);
        var end = AreaSlideScripts.Run(AreaSlideKind.EndGame, content, null);
        Assert.IsNotNull(end.Stop, end.Detail);
        Assert.IsTrue(end.Slides.Count <= AreaSlideScripts.MaxQueued);
        Console.WriteLine($"end game with no flags queued {string.Join(",", end.Slides)} ({end.Detail})");
    }
}
