using Arcanum.Data.Content;
using Arcanum.Data.Fixes;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Fixes;

/// <summary>Opt-in check of the installed fix catalogue (DAT_ROOT = the game folder, module Arcanum): every fix matches
/// its file and applies in Steamwoven, nothing changes in Original. Asserts structure only, no game text.</summary>
[TestClass]
public sealed class DataFixRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_fix_matches_its_file_and_applies_only_in_Steamwoven()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in data fix check; this is not a synthetic pass.");
        var plan = ContentMountPlan.Discover(root).Plan!;
        using var content = ScriptContent.Open(plan).Content!;
        using var stack = ContentStack.Open(plan).Stack!;
        foreach (var path in DlgDataFixCatalogue.Installed.Fixes.Select(fix => fix.Path).Distinct())
        {
            var number = content.Names.Names.Single(name => name.DialogPath == path).Number;
            var original = content.Dialog(number, DataFixProfile.Original);
            var steamwoven = content.Dialog(number, DataFixProfile.Steamwoven);
            var expected = DlgDataFixCatalogue.Installed.Fixes.Where(fix => fix.Path == path).ToArray();
            Assert.AreEqual(0, original.Fixes.Count, path);
            CollectionAssert.AreEqual(expected, steamwoven.Fixes.Where(a => a.Outcome == DlgFixOutcome.Applied).Select(a => a.Fix).ToArray(), path);
            Assert.AreEqual(1, stack.Resolve(path).Candidates.Count, $"{path}: one copy");
            Assert.AreEqual(ScriptContentStatus.Read, steamwoven.Status, steamwoven.Detail);
            if (expected[0].Operation == DlgFixOperation.CompleteLastRecord)
            {
                // DF1: the retail game opens nothing; the fix gives the one record its print line shows.
                Assert.AreEqual(ScriptContentStatus.Empty, original.Status, path);
                Assert.AreEqual(1, steamwoven.Document!.Records.Count, path);
                Assert.IsTrue(steamwoven.Document.Records[0].Text.Length > 0, path);
                continue;
            }
            Assert.AreEqual(ScriptContentStatus.Read, original.Status, path);
            Assert.AreEqual(original.Document!.Records.Count, steamwoven.Document!.Records.Count, path);
            foreach (var fix in expected)
            {
                DlgRecord Record(DlgDocument document) => document.Records.Single(r => r.Id == fix.RecordId);
                var before = Record(original.Document); var after = Record(steamwoven.Document);
                Assert.AreEqual(0, after.Response.Length, $"{path} {fix.RecordId}");
                var separator = before.Action.Length > 0 ? 2 : 0;
                Assert.AreEqual(before.Action.Length + separator + before.Response.Length, after.Action.Length, $"{path} {fix.RecordId}");
                CollectionAssert.AreEqual(before.Response.Bytes.ToArray(), after.Action.Bytes.Skip(before.Action.Length + separator).ToArray());
                // DF2 edits an NPC line, DF3 a player reply.
                Assert.AreEqual(fix.LedgerId == "DF2" ? DlgRecordRole.Npc : DlgRecordRole.Player, after.Role, $"{path} {fix.RecordId}");
            }
        }
    }
}
