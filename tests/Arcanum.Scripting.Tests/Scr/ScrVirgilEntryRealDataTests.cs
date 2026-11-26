using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// Opt-in check (<c>DAT_ROOT</c>, the game folder) of Virgil's talk entry on the installed script 1324: the dispatcher picks the DLG node
/// from the follower, local flag and jilted state, and every picked node is an NPC line of DLG 1324 (numbers only, never text).
/// </summary>
[TestClass]
public sealed class ScrVirgilEntryRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Virgils_installed_script_picks_each_entry_node_from_the_npc_state()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in Virgil entry check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var content = ScriptContent.Open(discovery.Plan!).Content!;
        var script = content.Script(1324);
        Assert.AreEqual(ScriptContentStatus.Read, script.Status, script.Detail);
        var dialog = content.Dialog(1324);
        Assert.AreEqual(ScriptContentStatus.Read, dialog.Status, dialog.Detail);

        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Dialog, 1324, script.Program!);
        int Entry(uint localFlags = 0)
        {
            host.Slots[(Npc, ScrPoint.Dialog)] = new(1324, localFlags, 0);
            var run = ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), host);
            Assert.AreEqual(ScrStop.Dialog, run.Stop, run.Refusal?.ToString());
            var start = run.Effects.Dialog!;
            Assert.AreEqual(new ScrDialogStart(Pc, Npc, 1324, start.Line, start.Node), start);
            Assert.IsTrue(dialog.Document!.TryGetRecord(start.Node, out var record), $"node {start.Node}");
            Assert.AreEqual(DlgRecordRole.Npc, record!.Role, $"node {start.Node}");
            return start.Node;
        }

        Assert.AreEqual(1, Entry(), "first meeting");
        Assert.AreEqual(527, Entry(1u << 31), "local flag 31");
        Assert.AreEqual(235, Entry(1u << 1), "local flag 1");
        host.Leaders[Npc] = Pc;
        Assert.AreEqual(385, Entry(), "following the PC");
        host.Leaders[Npc] = Chest;
        Assert.AreEqual(1, Entry(), "following someone else");
        host.Leaders.Clear();
        host.Jilted.Add(Npc);
        Assert.AreEqual(538, Entry(), "jilted");
    }
}
