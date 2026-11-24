using System.Reflection;
using System.Text.RegularExpressions;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1881: the admission sets S1 .. S65 are one linear chain (each set holds every type and kind of the set it is built on), <see cref="ScrAdmissionSet.Latest"/> is its top, and the interpreter's maximum is that top
/// alone, so no test or host names a stale maximum when a later slice adds a set.</summary>
[TestClass]
public sealed class ScrAdmissionChainTests
{
    private static IReadOnlyList<ScrAdmissionSet> NumberedSets() => typeof(ScrAdmissionSet).GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(ScrAdmissionSet) && Regex.IsMatch(p.Name, "^S[0-9]+$"))
        .Select(p => (ScrAdmissionSet)p.GetValue(null)!).ToArray();

    [TestMethod]
    public void The_numbered_sets_are_one_linear_chain_and_the_latest_holds_them_all()
    {
        var sets = NumberedSets();
        Assert.IsTrue(sets.Count >= 50, "the chain is found by reflection over the S<n> properties");
        // Linear: any two sets are ordered by inclusion, so the sets sort by size into one chain in which each holds the one before it.
        var chain = sets.OrderBy(s => s.Conditions.Count + s.Actions.Count + s.ValueKinds.Count + s.FocusKinds.Count).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        for (var i = 1; i < chain.Length; i++)
            Assert.IsTrue(chain[i - 1].IsSubsetOf(chain[i]), $"{chain[i - 1].Id} must be inside {chain[i].Id}: the chain forked");
        Assert.AreSame(chain[^1], ScrAdmissionSet.Latest, "Latest is the top of the chain");
        foreach (var set in sets) Assert.IsTrue(set.IsSubsetOf(ScrAdmissionSet.Latest), set.Id);
        Assert.IsTrue(ScrAdmissionSet.SlideScripts.IsSubsetOf(ScrAdmissionSet.Latest));
    }

    [TestMethod]
    public void The_interpreter_runs_at_most_the_latest_set()
    {
        Assert.IsNotNull(new ScrRunOptions { Admission = ScrAdmissionSet.Latest });
        var outside = new ScrAdmissionSet("outside", ScrAdmissionSet.Latest.Conditions, ScrAdmissionSet.Latest.Actions.Concat(Enumerable.Range(0, 1000).Where(a => ScrCatalogue.IsKnownAction(a) && !ScrAdmissionSet.Latest.Actions.Contains(a)).Take(1)),
            ScrAdmissionSet.Latest.ValueKinds, ScrAdmissionSet.Latest.FocusKinds, counterIndexWraps: true);
        Assert.IsFalse(outside.IsSubsetOf(ScrAdmissionSet.Latest), "the probe needs one action the latest set lacks");
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), new FakeHost(), new() { Admission = outside }));
    }
}
