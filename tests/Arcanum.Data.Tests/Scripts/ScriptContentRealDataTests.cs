using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Scripts;

/// <summary>Opt-in check of the script-number table and the winning copies on the installed original (DAT_ROOT = the
/// game folder, module Arcanum); docs/scripting/script-content.md records the counts.</summary>
[TestClass]
public sealed class ScriptContentRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_script_number_has_one_name_and_reads_through_the_stack()
    {
        var plan = Plan();
        var opened = ScriptContent.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = opened.Content!;
        var names = content.Names;
        Assert.AreEqual(2399, names.ListedNames);
        Assert.AreEqual(2399, names.Names.Count);
        Assert.AreEqual(0, names.ReplacedNames);
        Assert.AreEqual(0, names.UnnumberedNames);
        Assert.AreEqual(0, names.ModuleOnlySystemNames);
        var dialogs = new Dictionary<ScriptContentStatus, int>();
        foreach (var name in names.Names)
        {
            var script = content.Script(name.Number);
            Assert.AreEqual(ScriptContentStatus.Read, script.Status, script.Detail);
            var status = content.Dialog(name.Number).Status;
            dialogs[status] = dialogs.GetValueOrDefault(status) + 1;
        }
        // ARC-SCRIPT-012: read as the game reads them, 697 dialogs open and 4 hold one record that lacks its last field, so
        // the game opens nothing from them; the rest have no DLG.
        Assert.AreEqual(697, dialogs[ScriptContentStatus.Read]);
        Assert.AreEqual(4, dialogs[ScriptContentStatus.Empty]);
        Assert.AreEqual(1698, dialogs[ScriptContentStatus.NotFound]);
        Assert.AreEqual(3, dialogs.Count);
        Assert.IsFalse(content.Changed);
        // The strict reader, on request, still refuses 38 of them (docs/formats/dlg-reader.md).
        using (var strict = ScriptContent.Open(plan, dialogs: new DlgReaderOptions()).Content!)
        {
            Assert.AreEqual(663, names.Names.Count(name => strict.Dialog(name.Number).Status == ScriptContentStatus.Read));
            Assert.AreEqual(38, names.Names.Count(name => strict.Dialog(name.Number).Status == ScriptContentStatus.Invalid));
        }

        using var stack = ContentStack.Open(plan).Stack!;
        Assert.AreEqual(3, names.Names.Count(name => !ScriptNameIndex.IsBase(stack.Resolve(name.ScriptPath).Winner!.Source.Role)), "module copies win");
        Assert.AreEqual(47, names.Names.Count(name => stack.Resolve(name.DialogPath).Candidates.Count > 1), "patched dialogs");
        // Eight DLG files are not the dialog path of their number's script (it has another name, or there is none): the
        // original never opens them for a script.
        var files = stack.Keys("dlg/").Where(key => key.EndsWith(".dlg", StringComparison.Ordinal) && key.Count(c => c == '/') == 1).ToArray();
        Assert.AreEqual(709, files.Length);
        Assert.AreEqual(8, files.Count(key => names.Find(ScriptNameIndex.Number(key[4..]) ?? 0)?.DialogPath != key));
    }

    [TestMethod, TestCategory("RealData")]
    public void Virgil_reads_the_patched_copies_the_original_opens()
    {
        var plan = Plan();
        using var content = ScriptContent.Open(plan).Content!;
        using var stack = ContentStack.Open(plan).Stack!;
        var script = content.Script(1324);
        Assert.AreEqual(ScriptContentStatus.Read, script.Status, script.Detail);
        Assert.AreEqual("scr/01324virgil.scr", script.Name!.ScriptPath);
        Assert.AreEqual("dlg/01324virgil.dlg", script.Name.DialogPath);
        // Arcanum4.dat, the last base archive, holds 49 lines over arcanum2.dat's 47.
        var winner = stack.Resolve(script.Name.ScriptPath);
        Assert.AreEqual(ContentSourceRole.BaseArchive, winner.Winner!.Source.Role);
        Assert.AreEqual("arcanum4.dat", Path.GetFileName(winner.Winner.Source.Path).ToLowerInvariant());
        Assert.AreEqual(2, winner.Candidates.Count);
        Assert.AreEqual(49, script.Program!.Count);
        // Its first 47 lines are the shadowed copy's, unchanged; two lines are added.
        var shadowed = stack.Read(winner.Candidates[1]).Bytes!;
        var patched = stack.Read(winner.Winner).Bytes!;
        Assert.AreEqual(64 + 47 * 132, shadowed.Length);
        var same = Enumerable.Range(0, 47).TakeWhile(i => shadowed.AsSpan(64 + i * 132, 132).SequenceEqual(patched.AsSpan(64 + i * 132, 132))).Count();
        Assert.AreEqual(47, same);
        var dialog = content.Dialog(1324);
        Assert.AreEqual(ScriptContentStatus.Read, dialog.Status, dialog.Detail);
        // His dialog has one copy, in arcanum3.dat.
        var dialogCopies = stack.Resolve(script.Name.DialogPath).Candidates;
        Assert.AreEqual(1, dialogCopies.Count);
        Assert.AreEqual("arcanum3.dat", Path.GetFileName(dialogCopies[0].Source.Path).ToLowerInvariant());
    }

    private static ContentMountPlan Plan()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in script content check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return discovery.Plan!;
    }
}
