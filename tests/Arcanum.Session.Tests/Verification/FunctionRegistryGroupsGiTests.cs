using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests.Verification;

/// <summary>
/// ARC-DEV-389 (T1 of the wave 3 audit, ARC-RES-121): the 31 functions of groups G and I that carry a game rule have their registry rows, and the
/// triage record (<c>docs/verification/triage/groups-g-i.csv</c>) and the registry agree on them. The 1,011 not relevant functions stay in the triage
/// record only (a task that reads one more closely may register it later, so no row is required to be absent).
/// </summary>
public sealed partial class FunctionRegistryTests
{
    private const string GroupsGiTriage = "docs/verification/triage/groups-g-i.csv";
    private const string GroupsGiLedger = "docs/research/ledger/census-triage-gi.md";
    private static readonly string[] TriageStates = ["not relevant", "not implemented", "implemented"];

    private sealed record Triage(uint Address, string Group, string State, string Scope, string Level, string Ours);

    [TestMethod]
    public void GroupsGiTriageRecordIsWellFormed()
    {
        var triage = GroupsGiRecord();
        Assert.AreEqual(1042, triage.Count, "wave 3 read 1,042 functions");
        Assert.AreEqual(triage.Count, triage.Select(t => t.Address).Distinct().Count(), "one triage row per address");
        CollectionAssert.AreEqual(triage.Select(t => t.Address).Order().ToList(), triage.Select(t => t.Address).ToList(), "triage rows sorted by address");
        foreach (var t in triage)
        {
            CollectionAssert.Contains(TriageStates, t.State, $"0x{t.Address:X}: state");
            CollectionAssert.Contains(new[] { "G", "I" }, t.Group, $"0x{t.Address:X}: group");
            Assert.AreEqual(t.State == "implemented", t.Scope.Length > 0, $"0x{t.Address:X}: a scope exactly for an implemented row");
            if (t.State != "not relevant") Assert.AreEqual(t.State == "implemented", t.Ours.Length > 0, $"0x{t.Address:X}: a method of ours exactly for an implemented row");
        }
        Assert.AreEqual(1011, triage.Count(t => t.State == "not relevant"));
        Assert.AreEqual(18, triage.Count(t => t.State == "not implemented"), "17 read not implemented, and the maintain bar reset whose only counterpart is a property");
        Assert.AreEqual(13, triage.Count(t => t.State == "implemented"));
    }

    [TestMethod]
    public void EveryRuleStateFunctionOfGroupsGiHasItsRegistryRow()
    {
        var rows = Rows().ToDictionary(r => r.Address);
        var ruleState = GroupsGiRecord().Where(t => t.State != "not relevant").ToList();
        Assert.AreEqual(31, ruleState.Count, "18 not implemented and 13 implemented functions carry a rule");
        foreach (var t in ruleState)
        {
            var name = $"0x{t.Address:X}";
            Assert.IsTrue(rows.TryGetValue(t.Address, out var row), $"{name} carries a rule and has no registry row");
            // Later work may implement a function or promote its level, so only what cannot legitimately fall back is pinned.
            if (t.State == "implemented") Assert.IsTrue(row.Implemented, $"{name}: the triage read it as implemented, the registry says {row.Status}");
            Assert.IsTrue(row.Evidence.StartsWith(GroupsGiLedger + "#", StringComparison.Ordinal), $"{name}: evidence is the wave 3 ledger section");
            StringAssert.Matches(row.Level, new(@"^F[1-4]$"), $"{name}: a read function starts at F1");
        }
    }

    [TestMethod]
    public void TheImplementedGroupsGiFunctionsStayMarked()
    {
        var marked = MarkedMethods().ToDictionary(m => m.Address);
        foreach (var t in GroupsGiRecord().Where(t => t.State == "implemented"))
        {
            var name = $"0x{t.Address:X}";
            Assert.IsTrue(marked.TryGetValue(t.Address, out var mark), $"{name}: no method carries OriginalFunction({name})");
            Assert.IsNotNull(MethodIlHash.Name(mark.Method), $"{name}: marked method");
        }
    }

    private static List<Triage> GroupsGiRecord()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), GroupsGiTriage));
        Assert.AreEqual("address,group,state,class,scope,level,name,domain,ours,direct_callers,pointer_holders,export_origin", lines[0]);
        return lines.Skip(1).Where(l => l.Length > 0).Select(Csv)
            .Select(f => new Triage(Convert.ToUInt32(f[0], 16), f[1], f[2], f[4], f[5], f[8])).ToList();
    }
}
