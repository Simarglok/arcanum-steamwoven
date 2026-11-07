using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-114: an NPC's heartbeat script through the use-script host (<see cref="AreaUseScriptHost.RunHeartbeat"/>) over an
/// invented content stack and map (no original data): kill (58) goes to the world, toggle state (56) switches the NPC off as a map change
/// that the map's gone set reads, the heartbeat slot is written back, and what the viewer cannot apply leaves the run unapplied. The
/// opt-in check runs the installed Crash Site pre-made heroes' scripts.</summary>
[TestClass]
public sealed class AreaHeartbeatScriptTests
{
    private const byte Attachee = 1, Local = 2, Number = 3, GlobalFlag = 4, Everyone = 8, Looped = 10, Player = 21;
    private const int ChosenFlag = 2350;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static ScrObjectRef Ref(int index) => new(1, Entity(index));

    // A row with a heartbeat slot (point 19), optionally a dialog slot (point 9), and its object flags.
    private static AreaObjectRecord Row(int index, int type, int script, uint objectFlags = 0, int dialog = 0, int? faction = null)
    {
        var slots = new List<AreaSparseElement> { new(ScrPoint.Heartbeat, [0, 0, script]) };
        if (dialog != 0) slots.Insert(0, new(ScrPoint.Dialog, [0, 0, dialog]));
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, objectFlags, [], null),
        };
        if (faction is { } f) fields.Add(new(81, AreaPresence.Value, AreaValueOrigin.Instance, f, [], null));
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
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
        var root = Directory.CreateTempSubdirectory("arc-world-114-");
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
            // 781: the pre-made heroes' shape: chosen (flag 2350) switches itself off, removes the script and skips the default; else it
            // kills itself and runs the default.
            Write("scr/00781hero.scr", Program(
                If(new(ScrCatalogue.Condition.Equal, (GlobalFlag, ChosenFlag), N(1)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(4))),
                Do(new(ScrCatalogue.Action.ToggleState, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.RemoveThisScript)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault)),
                Do(new(ScrCatalogue.Action.Kill, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 782: toggles its attachee and runs the default (twice switches it back on).
            Write("scr/00782toggle.scr", Program(Do(new(ScrCatalogue.Action.ToggleState, (Attachee, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 783: a dialog, outside the heartbeat set.
            Write("scr/00783talk.scr", Program(Do(new(ScrCatalogue.Action.Dialog, (Attachee, -1), N(1))), Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // ARC-SCRIPT-047: 784 is the opening talk's heartbeat (1100's shape): for everyone in the vicinity, a looped PC gets a call-ex of
            // the attachee's dialog point at line 1; then it removes itself and skips the default. 785 is the dialog script it calls: line 1
            // sets local flag 2 and asks for node 3.
            Write("scr/00784opening.scr", Program(
                Do(new(ScrCatalogue.Action.LoopFor, (Everyone, 0))),
                Do(new(ScrCatalogue.Action.GetObjectType, (Looped, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(15)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(6))),
                Do(new(ScrCatalogue.Action.CallScriptEx, (Attachee, -1), N(ScrPoint.Dialog), N(1), (Looped, 0))),
                Do(new(ScrCatalogue.Action.RemoveThisScript)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault)),
                Do(new(ScrCatalogue.Action.LoopEnd))));
            Write("scr/00785talk.scr", Program(
                Do(new(ScrCatalogue.Action.ReturnSkipDefault)),
                Do(new(ScrCatalogue.Action.SetLocalFlag, N(2))),
                Do(new(ScrCatalogue.Action.Dialog, N(3)))));
            // ARC-SCRIPT-049: 786 sets its attachee's faction to 11 (2756's shape) and runs the default. 787 skips the default when its
            // attachee is switched off or dead, or (2700's shape) when the player is within 5 tiles; 788 asks for 785 in 2 seconds (1914's).
            Write("scr/00786faction.scr", Program(Do(new(ScrCatalogue.Action.SetFaction, (Attachee, -1), N(11))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00787near.scr", Program(
                If(new(ScrCatalogue.Condition.IsSwitchedOff, (Attachee, -1)), new(ScrCatalogue.Action.ReturnSkipDefault), new(ScrCatalogue.Action.DoNothing)),
                If(new(ScrCatalogue.Condition.IsDead, (Attachee, -1)), new(ScrCatalogue.Action.ReturnSkipDefault), new(ScrCatalogue.Action.DoNothing)),
                Do(new(ScrCatalogue.Action.DistanceBetween, (Attachee, -1), (Player, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.LessOrEqual, (Local, 0), N(5)), new(ScrCatalogue.Action.ReturnSkipDefault), new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00788later.scr", Program(
                Do(new(ScrCatalogue.Action.CallScriptIn, N(785), N(0), (Attachee, -1), (Attachee, -1), N(2))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-DEV-231 (S14a, GT158–GT161): 789 walks its attachee to a tile and 790 runs it to a tile (script 1717's shape: a literal tile), then both run the default.
            Write("scr/00789walk.scr", Program(Do(new(ScrCatalogue.Action.WalkTo, (Attachee, -1), N(40), N(50))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00790run.scr", Program(Do(new(ScrCatalogue.Action.RunTo, (Attachee, -1), N(41), N(51))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00792heal.scr", Program(Do(new(ScrCatalogue.Action.HealFatigue, (Attachee, -1), N(8))), Do(new(ScrCatalogue.Action.HealFatigue, (Attachee, -1), N(-5))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00793animate.scr", Program(Do(new(ScrCatalogue.Action.PerformAnimation, (Attachee, -1), N(13))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00794steal.scr", Program(Do(new(ScrCatalogue.Action.StealCoins, (Attachee, -1), (Attachee, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00795prone.scr", Program(Do(new(ScrCatalogue.Action.BecomeProne, (Attachee, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-DEV-241: 796 has its attachee cast spell 7 free and unresistable at the player (122), 797 casts spell 8 at the player with no caster, unresistable (136).
            Write("scr/00796cast.scr", Program(Do(new(ScrCatalogue.Action.CastFreeUnresistableSpell, (Attachee, -1), N(7), (Player, 0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-DEV-243: 798 deals 9 fire damage (type 3, apply unresistable 138) to its attachee and then 4 normal damage (60) to the player, 799 heals the attachee 6 hit points (75).
            Write("scr/00798damage.scr", Program(Do(new(ScrCatalogue.Action.DamageUnresistable, (Attachee, -1), N(9), N(3))), Do(new(ScrCatalogue.Action.Damage, (Player, 0), N(4), N(0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00799heal.scr", Program(Do(new(ScrCatalogue.Action.HealHitPoints, (Attachee, -1), N(6))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-DEV-244: 800 has the player cast spell 9 (48) at its attachee.
            Write("scr/00800pccast.scr", Program(Do(new(ScrCatalogue.Action.CastSpell, (Player, 0), N(9), (Attachee, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00797caston.scr", Program(Do(new(ScrCatalogue.Action.CastUnresistableSpellOn, N(8), (Player, 0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00791offmap.scr", Program(Do(new(ScrCatalogue.Action.WalkTo, (Attachee, -1), N(-3), N(5))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, List<ScrObjectRef>? kills, params AreaObjectRecord[] shown) =>
        HostAt(session, content, kills, null, shown);

    private static AreaUseScriptHost HostAt(GameSessionOwner session, ScriptContent content, List<ScrObjectRef>? kills, Arcanum.Simulation.Movement.CellCoord? pc,
        params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => pc, _ => false, _ => null, () => null,
            kills is null ? null : obj => { kills.Add(obj); return true; }));

    private static void SetFlag(GameSessionOwner session, int flag)
    {
        var flags = session.Flags;
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied,
            flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(flag), true)).Disposition);
    }

    [TestMethod]
    public void An_unchosen_hero_is_killed_through_the_world_and_keeps_its_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 781);
            var kills = new List<ScrObjectRef>();
            var ran = Host(session, content, kills, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.RunDefault, ran.Run!.Outcome);
            CollectionAssert.AreEqual(new[] { Ref(1) }, kills);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            Assert.IsNull(session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.SlotProperty(ScrPoint.Heartbeat, "script"))));
        });
    }

    [TestMethod]
    public void The_chosen_hero_is_switched_off_its_script_removed_and_the_map_no_longer_has_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            SetFlag(session, ChosenFlag);
            var npc = Row(1, 16, 781);
            var kills = new List<ScrObjectRef>();
            var host = Host(session, content, kills, npc);
            var targets = new AreaObjectTargets(1, [npc], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null);
            Assert.AreEqual(0, targets.Gone(session).Count);
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.SkipDefault, ran.Run!.Outcome);
            Assert.AreEqual(0, kills.Count);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            CollectionAssert.AreEqual(new[] { npc.Key }, targets.Gone(session).ToArray());
            // The heartbeat slot holds no script now: the next tick runs nothing.
            Assert.AreEqual(new ScrSlot(0, 0, 0), host.Slot(Ref(1), ScrPoint.Heartbeat));
            var again = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.IsNull(again.Run?.Script, again.Detail);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)), "still off");
        });
    }

    [TestMethod]
    public void A_second_toggle_switches_the_npc_back_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 782);
            var host = Host(session, content, null, npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            Assert.IsNull(session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.OffProperty)), "back to its map data");
        });
    }

    [TestMethod]
    public void What_the_viewer_cannot_apply_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // A kill without the world's kill.
            var npc = Row(1, 16, 781);
            var noKill = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, noKill.Outcome, noKill.Detail);
            // A kill the world says it cannot apply (no combat values) is refused before anything applies.
            var kills = new List<ScrObjectRef>();
            var cannot = new AreaUseScriptHost(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                obj => { kills.Add(obj); return true; }, _ => false)).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, cannot.Outcome, cannot.Detail);
            Assert.AreEqual(0, kills.Count);
            // Switching on an NPC its map data has off.
            var off = Row(2, 16, 782, objectFlags: 0x2);
            var on = Host(session, content, null, off).RunHeartbeat(off, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, on.Outcome, on.Detail);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, Entity(2)));
            // A script outside the heartbeat set, and a row that is no NPC.
            var talker = Row(3, 16, 783);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, Host(session, content, null, talker).RunHeartbeat(talker, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            var scenery = Row(4, 3, 782);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, Host(session, content, null, scenery).RunHeartbeat(scenery, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.Throws<ArgumentOutOfRangeException>(() => Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Use, session.Rules.Scripts));
            Assert.AreEqual(0, new AreaObjectTargets(1, [npc, off, talker], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null).Gone(session).Count,
                "nothing was switched off");
        });
    }

    [TestMethod]
    public void An_opening_heartbeat_asks_for_the_npc_s_talk_once_the_pc_is_near_and_keeps_the_dialog_slot()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // The NPC stands at (11, 20); the vicinity reaches 10 tiles on both axes (PG13).
            var npc = Row(1, 16, 784, dialog: 785);
            var far = HostAt(session, content, null, new(22, 20), npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, far.Outcome, far.Detail);
            Assert.IsNull(far.Dialog, "the PC is 11 tiles away");
            Assert.AreEqual((ScrOutcome.SkipDefault, 784), (far.Run!.Outcome, Host(session, content, null, npc).Slot(Ref(1), ScrPoint.Heartbeat)!.Value.Script));

            var host = HostAt(session, content, null, new(21, 30), npc);
            var near = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, near.Outcome, near.Detail);
            Assert.AreEqual(new ScrDialogStart(host.Pc, Ref(1), 785, 2, 3), near.Dialog, "at the dialog point");
            // The heartbeat removed itself; the dialog slot keeps the flag its run set, under the keys the dialog host reads.
            Assert.AreEqual(new ScrSlot(0, 0, 0), host.Slot(Ref(1), ScrPoint.Heartbeat));
            Assert.AreEqual(new ScrSlot(785, 1u << 2, 0), host.Slot(Ref(1), ScrPoint.Dialog));
            Assert.AreEqual(1L << 2, session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaConversationState.LocalFlagsProperty)));
        });
    }

    [TestMethod]
    public void Set_faction_keeps_the_npc_s_new_faction_which_its_readers_take()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 786, faction: 4);
            var ran = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(11L, session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.FactionProperty)));
            Assert.AreEqual(11, AreaUseScriptHost.Faction(session, npc, 4));
            Assert.AreEqual((0, 11), AreaConversationHost.Scope(npc, session));
            Assert.AreEqual((0, 4), AreaConversationHost.Scope(npc), "the map data's");
            // A faction equal to the map data's clears the key.
            var member = Row(2, 16, 786, faction: 11);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, member).RunHeartbeat(member, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(2), AreaUseScriptHost.FactionProperty)));
        });
    }

    [TestMethod]
    public void Switched_off_dead_and_the_distance_to_the_player_steer_the_heartbeat()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // The NPC stands at (11, 20).
            var npc = Row(1, 16, 787);
            var near = HostAt(session, content, null, new(14, 22), npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual((ScrOutcome.SkipDefault, 3), (near.Run!.Outcome, near.Run.StopLine), "3 tiles away");
            var far = HostAt(session, content, null, new(17, 20), npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(ScrOutcome.RunDefault, far.Run!.Outcome, "6 tiles away");
            // Off in its map data, or switched off by a script.
            var off = Row(2, 16, 787, objectFlags: 0x2);
            Assert.AreEqual(0, HostAt(session, content, null, new(30, 20), off).RunHeartbeat(off, ScrPoint.Heartbeat, session.Rules.Scripts).Run!.StopLine);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
                [new(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.OffProperty), 1)]).Disposition);
            Assert.AreEqual(0, HostAt(session, content, null, new(30, 20), npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Run!.StopLine);
            var host = HostAt(session, content, null, new(30, 20), npc);
            Assert.AreEqual((false, false), (host.IsSwitchedOff(host.Pc), host.IsDead(host.Pc)), "the PC is neither");
            Assert.IsNull(host.IsDead(new ScrObjectRef(1, "oid:unknown")), "no loaded row");
            // Dead as the session holds it (a kill).
            var dead = Row(3, 16, 787);
            Assert.AreEqual(GameSessionDisposition.Changed,
                session.Kill(session.Version, SessionLifeTarget.Critter(Entity(3), new Arcanum.Simulation.Combat.LifeFacts(), default)).Disposition);
            var killed = HostAt(session, content, null, new(30, 20), dead);
            Assert.IsTrue(killed.IsDead(Ref(3)));
            Assert.AreEqual(1, killed.RunHeartbeat(dead, ScrPoint.Heartbeat, session.Rules.Scripts).Run!.StopLine);
        });
    }

    [TestMethod]
    public void Call_script_in_from_a_heartbeat_goes_to_the_session_queue()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 788);
            var ran = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            var call = session.QueryScriptCalls().Single();
            Assert.AreEqual((785, 0, 16000L), (call.Call.Script, call.Call.Line, call.DeadlineMs - session.QueryClockSnapshot().GameTimestampMs));
        });
    }

    [TestMethod]
    public void A_heartbeat_s_walk_to_and_run_to_start_the_goal_in_the_ai_host_and_the_script_goes_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var walkers = new List<(string Npc, Arcanum.Simulation.Movement.CellCoord Cell, bool Run)>();
            var walk = Row(1, 16, 789);
            var run = Row(2, 16, 790);
            var host = new AreaUseScriptHost(session, 1, [walk, run], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptWalk: (npc, cell, running) => { walkers.Add((npc, cell, running)); return "started"; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(walk, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(run, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(walk), new Arcanum.Simulation.Movement.CellCoord(40, 50), false),
                (AreaUseScriptHost.EntityId(run), new Arcanum.Simulation.Movement.CellCoord(41, 51), true) }, walkers);
            Assert.AreEqual("started", host.LastWalk);
        });
    }

    [TestMethod]
    public void A_heartbeat_s_heal_fatigue_goes_to_the_life_step_with_a_positive_amount_only()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var healed = new List<(string Npc, int Amount)>();
            var npc = Row(1, 16, 792);
            var host = new AreaUseScriptHost(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                HealFatigue: (obj, amount) => { healed.Add((obj.Code, amount)); return true; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(npc), 8) }, healed, "GT162: a negative amount (it raises the damage in the original) is not modelled");
            var without = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, without.Outcome, "no life step: the run is left unapplied");
        });
    }

    [TestMethod]
    public void A_heartbeat_s_perform_animation_goes_to_the_ai_host()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var asked = new List<(string Npc, int Animation)>();
            var npc = Row(1, 16, 793);
            var host = new AreaUseScriptHost(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptAnimate: (who, animation) => { asked.Add((who, animation)); return "started"; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(npc), 13) }, asked);
            Assert.AreEqual("started", host.LastAnimation);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no AI host: left unapplied");
        });
    }

    [TestMethod]
    public void A_heartbeat_s_steal_goes_to_the_ai_host_for_an_npc_pair_only()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var asked = new List<(string Thief, string Victim)>();
            var npc = Row(1, 16, 794);
            var host = new AreaUseScriptHost(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptSteal: (thief, victim) => { asked.Add((thief, victim)); return "started"; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(npc), AreaUseScriptHost.EntityId(npc)) }, asked);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no AI host: left unapplied");
        });
    }

    [TestMethod]
    public void A_heartbeat_s_casts_go_to_the_ai_host_with_the_caster_the_target_the_spell_and_the_flags()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var asked = new List<(string? Caster, string Target, int Spell, bool Free, bool Unresistable)>();
            var by = Row(1, 16, 796);
            var none = Row(2, 16, 797);
            var host = new AreaUseScriptHost(session, 1, [by, none], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptCast: (caster, target, spell, free, unresistable) => { asked.Add((caster, target, spell, free, unresistable)); return "cast"; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(by, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(none, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[]
            {
                ((string?)AreaUseScriptHost.EntityId(by), Arcanum.Simulation.Ai.AiSchedulerRules.Pc, 7, true, true),
                ((string?)null, Arcanum.Simulation.Ai.AiSchedulerRules.Pc, 8, false, true),
            }, asked);
            Assert.AreEqual("cast", host.LastCast);
            // ARC-DEV-244: the PC as caster goes to the viewer with the scheduler id of the PC.
            var byPc = Row(3, 16, 800);
            var pcHost = new AreaUseScriptHost(session, 1, [byPc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptCast: (caster, target, spell, free, unresistable) => { asked.Add((caster, target, spell, free, unresistable)); return "cast"; }));
            asked.Clear();
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, pcHost.RunHeartbeat(byPc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { ((string?)Arcanum.Simulation.Ai.AiSchedulerRules.Pc, AreaUseScriptHost.EntityId(byPc), 9, false, false) }, asked);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, by).RunHeartbeat(by, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no AI host: left unapplied");
        });
    }

    [TestMethod]
    public void A_heartbeat_s_damage_and_heal_go_to_the_viewer_with_the_victim_the_amount_the_type_and_the_flag()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var damaged = new List<(string Victim, int Amount, int Type, bool Unresistable)>();
            var healed = new List<(string Victim, int Amount)>();
            var hurt = Row(1, 16, 798);
            var heals = Row(2, 16, 799);
            var host = new AreaUseScriptHost(session, 1, [hurt, heals], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptDamage: (victim, amount, type, unresistable) => { damaged.Add((victim, amount, type, unresistable)); return "damaged"; },
                ScriptHeal: (victim, amount) => { healed.Add((victim, amount)); return "healed"; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(hurt, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(heals, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(hurt), 9, 3, true), (Arcanum.Simulation.Ai.AiSchedulerRules.Pc, 4, 0, false) }, damaged);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(heals), 6) }, healed);
            Assert.AreEqual("healed", host.LastHeal);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, hurt).RunHeartbeat(hurt, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no viewer delegate: left unapplied");
        });
    }

    [TestMethod]
    public void A_heartbeat_s_become_prone_goes_to_the_ai_host()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var asked = new List<string>();
            var npc = Row(1, 16, 795);
            var host = new AreaUseScriptHost(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptProne: who => { asked.Add(who); return "lying"; }));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(npc) }, asked);
            Assert.AreEqual("lying", host.LastProne);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no AI host: left unapplied");
        });
    }

    [TestMethod]
    public void A_walk_to_a_negative_tile_starts_nothing_and_a_host_without_the_ai_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var walkers = new List<string>();
            var offMap = Row(1, 16, 791);
            var host = new AreaUseScriptHost(session, 1, [offMap], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                ScriptWalk: (npc, _, _) => { walkers.Add(npc); return "started"; }));
            // GT158: a negative X is an off-map tile in the original (its Y reads -1); the project starts no walk to it.
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(offMap, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(0, walkers.Count);
            var plain = Row(1, 16, 789);
            var without = Host(session, content, null, plain).RunHeartbeat(plain, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, without.Outcome, "no AI host: the walk is not modelled and the run is left unapplied");
        });
    }

    // ---- opt-in: the installed Crash Site ----

    [TestMethod, TestCategory("RealData")]
    public void The_installed_crash_site_kills_the_eleven_unchosen_heroes_and_switches_the_chosen_one_off()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in heartbeat check; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed"]), default);
        var rows = result.Area!.Snapshot.Objects;
        // The twelve NPCs whose heartbeat (point 19) is 1781 + i, the pre-made hero i's script (character-creation-evidence.md).
        var heroes = rows.Where(r => r.Type == 16 && AreaUseScriptHost.LoadedSlot(r, ScrPoint.Heartbeat) is { Script: >= 1781 and <= 1792 })
            .GroupBy(r => AreaUseScriptHost.EntityId(r)).Select(g => g.First()).OrderBy(r => AreaUseScriptHost.LoadedSlot(r, ScrPoint.Heartbeat)!.Value.Script).ToArray();
        Assert.AreEqual(12, heroes.Length);
        using var session = ConversationSessionFixture.Create(result.Area.Snapshot.RuntimeMap);
        // Solomon Doone (index 5, flag 2355) chosen, as in the owner's report.
        SetFlag(session, 2355);
        var kills = new List<ScrObjectRef>();
        var host = new AreaUseScriptHost(session, result.Area.Snapshot.RuntimeMap, rows, [], content,
            new AreaScriptWorld(() => null, _ => false, _ => null, () => null, obj => { kills.Add(obj); return true; }));
        foreach (var hero in heroes)
        {
            var ran = host.RunHeartbeat(hero, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
        }
        var solomon = AreaUseScriptHost.EntityId(heroes[5]);
        CollectionAssert.AreEquivalent(heroes.Where((_, i) => i != 5).Select(h => AreaUseScriptHost.EntityId(h)).ToArray(), kills.Select(k => k.Code).ToArray());
        Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, result.Area.Snapshot.RuntimeMap, solomon));
        Assert.AreEqual(1, heroes.Count(h => AreaUseScriptHost.SwitchedOff(session, result.Area.Snapshot.RuntimeMap, AreaUseScriptHost.EntityId(h))));
    }

    // ARC-SCRIPT-047: Virgil's heartbeat 1100 at the Crash Site opens his talk with the PC as soon as the PC is within the vicinity (10
    // tiles on both axes): a call-ex of his dialog point 1324 at line 4, whose dialog action asks for node 1; the heartbeat then removes itself.
    [TestMethod, TestCategory("RealData")]
    public void Virgil_s_heartbeat_at_the_crash_site_opens_his_talk_once_the_pc_is_near()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in heartbeat check; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed"]), default);
        var snapshot = result.Area!.Snapshot;
        var virgil = snapshot.Objects.Single(r => r.Type == 16 && AreaUseScriptHost.LoadedSlot(r, ScrPoint.Dialog) is { Script: 1324 });
        Assert.AreEqual(1100, AreaUseScriptHost.LoadedSlot(virgil, ScrPoint.Heartbeat)!.Value.Script);
        var at = new Arcanum.Simulation.Movement.CellCoord(checked((int)virgil.WorldX!.Value), checked((int)virgil.WorldY!.Value));
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        AreaUseScriptHost HostWithPc(Arcanum.Simulation.Movement.CellCoord pc) => new(session, snapshot.RuntimeMap, snapshot.Objects, [], content,
            new AreaScriptWorld(() => pc, _ => false, _ => null, () => null));
        var entity = new ScrObjectRef(snapshot.RuntimeMap, AreaUseScriptHost.EntityId(virgil));

        var far = HostWithPc(new(at.X + 11, at.Y)).RunHeartbeat(virgil, ScrPoint.Heartbeat, session.Rules.Scripts);
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, far.Outcome, far.Detail);
        Assert.IsNull(far.Dialog, "11 tiles away");

        var host = HostWithPc(new(at.X + 3, at.Y - 2));
        var near = host.RunHeartbeat(virgil, ScrPoint.Heartbeat, session.Rules.Scripts);
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, near.Outcome, near.Detail);
        Assert.AreEqual(new ScrDialogStart(host.Pc, entity, 1324, 4, 1), near.Dialog);
        Assert.AreEqual(ScrOutcome.SkipDefault, near.Run!.Outcome);
        Assert.AreEqual(0, host.Slot(entity, ScrPoint.Heartbeat)!.Value.Script, "the heartbeat removed itself");
        Assert.AreEqual(1324, host.Slot(entity, ScrPoint.Dialog)!.Value.Script);
        Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 1324, 1, session.Rules.Data).Status);
        Assert.IsNull(host.RunHeartbeat(virgil, ScrPoint.Heartbeat, session.Rules.Scripts).Dialog, "once only");
    }
}
