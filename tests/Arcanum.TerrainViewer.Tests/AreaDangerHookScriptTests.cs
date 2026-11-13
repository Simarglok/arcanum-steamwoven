using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-AI-1530 (ledger VB2-6): the enter-combat (13, 0x4AAE29) and exit-combat (14, 0x4AAF32) scripts of the danger setter through the use-script host (<see cref="AreaUseScriptHost.RunDangerPoint"/>) over an invented content stack and map
/// (no original data): the setter's target is the triggerer, the script's global flag write reaches the session, and the session's danger setter asks the host when an NPC enters and when it leaves a fight, never when it stays in one.</summary>
[TestClass]
public sealed class AreaDangerHookScriptTests
{
    private const byte Number = 3, GlobalFlag = 4, Local = 2, Triggerer = 0;
    private const int EnterFlag = 2401, ExitFlag = 2402, PcTargetFlag = 2403, PcType = 15;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with script slots at the given points.
    private static AreaObjectRecord Row(int index, params (int Point, int Script)[] attached)
    {
        var slots = attached.Select(a => new AreaSparseElement(a.Point, [0, 0, a.Script])).ToList();
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) If(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);
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
        var root = Directory.CreateTempSubdirectory("arc-ai-1530-");
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
            // 911 (an enter-combat script): sets global flag 2401, and flag 2403 only when the triggerer is a PC (object type 15), then runs the default.
            Write("scr/00911enter.scr", Program(
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, EnterFlag), N(1))),
                Do(new(ScrCatalogue.Action.GetObjectType, (Triggerer, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(4))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PcTargetFlag), N(1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 912 (an exit-combat script): sets global flag 2402.
            Write("scr/00912exit.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, ExitFlag), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    [TestMethod]
    public void The_enter_combat_script_runs_with_the_target_as_triggerer_and_the_exit_script_at_its_own_point()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, (ScrPoint.EnterCombat, 911), (ScrPoint.ExitCombat, 912));
            var other = Row(2, (ScrPoint.Heartbeat, 912));
            var host = Host(session, content, npc, other);
            var rules = session.Rules.Scripts;
            var id = AreaUseScriptHost.EntityId(npc);

            var enter = host.RunDangerPoint(ScrPoint.EnterCombat, id, new ScrObjectRef(1, AreaUseScriptHost.EntityId(other)), rules);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, enter.Outcome);
            Assert.AreEqual((ScrOutcome.RunDefault, 911), (enter.Run!.Outcome, enter.Run.Script));
            Assert.IsTrue(Flag(session, EnterFlag));
            Assert.IsFalse(Flag(session, PcTargetFlag), "an NPC target is no PC");
            Assert.IsFalse(Flag(session, ExitFlag), "the exit point is another slot");

            host.RunDangerPoint(ScrPoint.EnterCombat, id, host.Pc, rules);
            Assert.IsTrue(Flag(session, PcTargetFlag), "the hero is the triggerer");
            host.RunDangerPoint(ScrPoint.ExitCombat, id, null, rules);
            Assert.IsTrue(Flag(session, ExitFlag));
        });
    }

    [TestMethod]
    public void An_npc_without_a_script_at_the_point_runs_nothing_and_another_point_is_refused()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var none = Row(1);
            var heartbeatOnly = Row(2, (ScrPoint.Heartbeat, 911));
            var host = Host(session, content, none, heartbeatOnly);
            var rules = session.Rules.Scripts;
            foreach (var victim in new[] { AreaUseScriptHost.EntityId(none), AreaUseScriptHost.EntityId(heartbeatOnly), "oid:nobody" })
                Assert.IsNull(host.RunDangerPoint(ScrPoint.EnterCombat, victim, host.Pc, rules).Run, victim);
            Assert.IsFalse(Flag(session, EnterFlag));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => host.RunDangerPoint(ScrPoint.Heartbeat, AreaUseScriptHost.EntityId(heartbeatOnly), null, rules));
        });
    }

    [TestMethod]
    public void The_sessions_danger_setter_runs_the_enter_script_on_entering_a_fight_and_the_exit_script_on_leaving_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, (ScrPoint.EnterCombat, 911), (ScrPoint.ExitCombat, 912));
            var host = Host(session, content, npc);
            var id = AreaUseScriptHost.EntityId(npc);
            session.DangerHookScript = hook =>
            {
                ScrObjectRef? triggerer = hook.Triggerer is null ? null : hook.Triggerer.IsHero ? host.Pc : new ScrObjectRef(1, hook.Triggerer.Entity!);
                host.RunDangerPoint(hook.Point == DangerScriptPoint.EnterCombat ? ScrPoint.EnterCombat : ScrPoint.ExitCombat, hook.Attachee, triggerer, session.Rules.Scripts);
            };

            var target = SessionCombatant.Critter("oid:target");
            var fight = session.SetDangerState(session.Version, new() { Entity = id, State = DangerState.CombatFocus, Target = target });
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (fight.Disposition, fight.Failure));
            Assert.IsTrue(Flag(session, EnterFlag), "entering the fight runs slot 13");
            Assert.IsFalse(Flag(session, PcTargetFlag), "the target was an NPC");
            Assert.IsFalse(Flag(session, ExitFlag));
            Assert.AreEqual(target, session.QueryDanger(id).Focus, "rule 9 wrote the focus after the script");

            var calm = session.SetDangerState(session.Version, new() { Entity = id, State = DangerState.None });
            Assert.AreEqual(GameSessionDisposition.Changed, calm.Disposition);
            Assert.IsTrue(Flag(session, ExitFlag), "leaving the fight runs slot 14");
        });
    }
}
