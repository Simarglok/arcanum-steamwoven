using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1815 (ledger SC21–SC23): the heartbeat set admits the focus kinds every scenery in the vicinity (15) and object variable (11), assign object (15) and float line (23). The main map's script 2231 (21 heartbeats of point 19) loops over the scenery
/// around its NPC and stops a script eye candy on each; the scenery focus was the one thing the set lacked for it. Synthetic scripts of its shape run through the use-script host (no original data); the opt-in check
/// (<c>AreaScriptSpellTests</c>) counts the installed scripts.
/// </summary>
[TestClass]
public sealed class AreaHeartbeatSceneryLoopTests
{
    private const byte Attachee = 1, Number = 3, Looped = 10, LocalObject = 11, EveryScenery = 15;
    private const int LoopScript = 830, AssignScript = 831, ReactionScript = 832, LoopFlag = 2360, AssignFlag = 2361;

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));

    private static byte[] Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        return bytes;
    }

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1815-");
        try
        {
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Write("tig.dat", AreaOwnerFixture.Dat());
            Write("modules/Syn.dat", AreaOwnerFixture.Dat());
            // 830 has the shape of the main map's heartbeat 2231: loop over every scenery in the vicinity, set a flag and stop script eye candy 2 on each, then run the default.
            Write("scr/00830loop.scr", Program(
                Do(new(ScrCatalogue.Action.LoopFor, (EveryScenery, -1))),
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(LoopFlag))),
                Do(new(ScrCatalogue.Action.StopScriptEyeCandy, N(2), (Looped, -1))),
                Do(new(ScrCatalogue.Action.LoopEnd)),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 831: assign the attachee to object variable 0, float DLG line 1 over it (the PC stands near), then set a flag.
            Write("scr/00831assign.scr", Program(
                Do(new(ScrCatalogue.Action.AssignObject, (LocalObject, 0), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.FloatLine, N(1), (LocalObject, 0))),
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AssignFlag))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("dlg/00831assign.dlg", Encoding.Latin1.GetBytes("{1}{Mind the gears.}{}{}{}{}{}"));
            // 832: get reaction, which the heartbeat host cannot answer.
            Write("scr/00832reaction.scr", Program(
                Do(new(ScrCatalogue.Action.GetReaction, (Attachee, -1), (Attachee, -1), (4, 0))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaObjectRecord Npc(int script) => AreaHeartbeatScriptTests.Row(1, 16, script);
    private static AreaObjectRecord Scenery(int index, long? worldX = null) => AreaHeartbeatScriptTests.Row(index, 3, 0) with { WorldX = worldX ?? 10 + index };

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, Func<AreaObjectRecord, bool> gone, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => new CellCoord(12, 21), gone, _ => null, () => null));

    [TestMethod]
    public void The_heartbeat_set_admits_the_scenery_focus_assign_object_and_float_line_but_not_get_reaction()
    {
        var set = AreaUseScriptHost.HeartbeatAdmission;
        Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EverySceneryInVicinity) && set.FocusKinds.Contains(ScrFocusKind.LocalObject));
        Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.AssignObject) && set.Actions.Contains(ScrCatalogue.Action.FloatLine));
        Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.GetReaction), "the host has no reaction answer");
        Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S59));
        // The dying set is built on it and the placed set already held all three; the use click's set is unchanged.
        Assert.IsTrue(AreaUseScriptHost.DyingAdmission.FocusKinds.Contains(ScrFocusKind.EverySceneryInVicinity));
        Assert.IsFalse(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.FloatLine));
    }

    [TestMethod]
    public void A_heartbeat_loops_over_the_scenery_around_its_npc_and_a_far_or_gone_one_is_left_out()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Npc(LoopScript);
            var near = Scenery(2);
            var gone = Scenery(3);
            var far = Scenery(4, 60);
            var host = Host(session, content, row => row.Key == gone.Key, npc, near, gone, far);
            Assert.AreEqual(1, host.SceneryInVicinity(new(1, AreaUseScriptHost.EntityId(npc))).Count, "only the near one counts");
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(LoopFlag), "the loop body ran for the near scenery");
            // The loop's stop eye candy (124) is asked once, for the near scenery only: not for the gone or the far one, nor for the NPC.
            CollectionAssert.AreEqual(new[] { (2, ScrEyeCandyKind.StopScript, AreaUseScriptHost.EntityId(near)) }, ran.Run!.Effects.EyeCandies.Select(e => (e.Number, e.Kind, e.Object.Code)).ToArray());
            // ARC-SCRIPT-1828: the host hands the stop on to the viewer as a stop request of the same entry and object.
            CollectionAssert.AreEqual(new[] { new AreaScriptEyeCandy(new(1, AreaUseScriptHost.EntityId(near)), 2, Stop: true) }, host.TakeEyeCandies().ToArray());
        });
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Npc(LoopScript);
            var host = Host(session, content, _ => false, npc, Scenery(4, 60));
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsFalse(host.GlobalFlag(LoopFlag), "no scenery near: the loop body did not run");
        });
    }

    [TestMethod]
    public void A_heartbeat_assigns_an_object_variable_and_floats_a_line_over_it_once()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Npc(AssignScript);
            var host = Host(session, content, _ => false, npc);
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(AssignFlag));
            CollectionAssert.AreEqual(new[] { new AreaScriptFloat(new(1, AreaUseScriptHost.EntityId(npc)), "Mind the gears.", host.Pc) }, host.TakeFloats().ToArray());
            Assert.AreEqual(0, host.TakeFloats().Count, "taken once");
        });
    }

    [TestMethod]
    public void A_heartbeat_with_get_reaction_is_still_refused_and_the_tick_goes_on_as_without_a_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Npc(ReactionScript);
            var host = Host(session, content, _ => false, npc);
            Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(host.Script(ReactionScript).Program!, AreaUseScriptHost.HeartbeatAdmission).Refusal!.Code);
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, ran.Outcome, ran.Detail);
        });
    }
}
