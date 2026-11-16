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
    private const byte Attachee = 1, Number = 3, Looped = 10, LocalObject = 11, EveryScenery = 15, EveryPortal = 19;
    private const int LoopScript = 830, AssignScript = 831, ReactionScript = 832, ToggleScript = 835, PortalToggleScript = 836, MixedToggleScript = 837, LoopFlag = 2360, AssignFlag = 2361;

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
            // ARC-SCRIPT-2248: 835 toggles its attachee (a use click's object); 836 has the shape of the ground rune tile script 2221 at line 15 to 20: for every portal in the vicinity, toggle the looped portal.
            Write("scr/00835toggle.scr", Program(Do(new(ScrCatalogue.Action.ToggleState, (Attachee, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00836portals.scr", Program(
                Do(new(ScrCatalogue.Action.LoopFor, (EveryPortal, -1))),
                Do(new(ScrCatalogue.Action.ToggleState, (Looped, -1))),
                Do(new(ScrCatalogue.Action.LoopEnd)),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 837: toggle the attachee, then every portal around it: a run whose first toggle the host could apply and whose second it cannot.
            Write("scr/00837mixed.scr", Program(
                Do(new(ScrCatalogue.Action.ToggleState, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.LoopFor, (EveryPortal, -1))),
                Do(new(ScrCatalogue.Action.ToggleState, (Looped, -1))),
                Do(new(ScrCatalogue.Action.LoopEnd)),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaObjectRecord Npc(int script) => AreaHeartbeatScriptTests.Row(1, 16, script);
    private static AreaObjectRecord Scenery(int index, long? worldX = null) => AreaHeartbeatScriptTests.Row(index, 3, 0) with { WorldX = worldX ?? 10 + index };

    private static AreaObjectRecord Container(int index, long? worldX = null) => AreaHeartbeatScriptTests.Row(index, 2, 0) with { WorldX = worldX ?? 10 + index };

    private static AreaObjectRecord Portal(int index, long? worldX = null) => AreaHeartbeatScriptTests.Row(index, 1, 0) with { WorldX = worldX ?? 10 + index };

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, Func<AreaObjectRecord, bool> gone, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => new CellCoord(12, 21), gone, _ => null, () => null));

    [TestMethod]
    public void The_heartbeat_set_admits_the_scenery_focus_assign_object_and_float_line_but_not_get_reaction()
    {
        var set = AreaUseScriptHost.HeartbeatAdmission;
        Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EverySceneryInVicinity) && set.FocusKinds.Contains(ScrFocusKind.LocalObject));
        Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.AssignObject) && set.Actions.Contains(ScrCatalogue.Action.FloatLine));
        Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.GetReaction), "the host has no reaction answer");
        Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65));
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

    // ARC-SCRIPT-2321 (AS3): the focus kind any scenery in the vicinity (16), the list of kind 15 read as "one is enough", for the tile script 2561 and the bust script 1734.
    [TestMethod]
    public void Every_script_host_set_that_admits_the_scenery_focus_admits_the_any_scenery_focus_too()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission, AreaUseScriptHost.CombatPointAdmission })
        {
            Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EverySceneryInVicinity), set.Id);
            Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.AnySceneryInVicinity), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        }
    }

    // ARC-SCRIPT-2041 (PV1–PV4): the focus kind every portal in the vicinity (19) of the four ground rune tile scripts.
    [TestMethod]
    public void The_use_placed_dying_and_combat_point_sets_admit_the_portal_focus_and_the_heartbeat_set_does_not()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.DyingAdmission, AreaUseScriptHost.CombatPointAdmission })
        {
            Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EveryPortalInVicinity), set.Id);
            Assert.IsFalse(set.FocusKinds.Contains(ScrFocusKind.AnyPortalInVicinity), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        }
        Assert.IsFalse(AreaUseScriptHost.HeartbeatAdmission.FocusKinds.Contains(ScrFocusKind.EveryPortalInVicinity));
    }

    [TestMethod]
    public void The_portals_around_an_object_are_the_near_portals_that_are_not_gone_whatever_their_state()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Npc(LoopScript);
            var near = Portal(2);
            var nearToo = Portal(3, 14);
            var gone = Portal(4);
            var far = Portal(5, 60);
            var scenery = Scenery(6);
            var host = Host(session, content, row => row.Key == gone.Key, npc, near, nearToo, gone, far, scenery);
            var around = host.PortalsInVicinity(new(1, AreaUseScriptHost.EntityId(npc)));
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(near), AreaUseScriptHost.EntityId(nearToo) }, around.Select(o => o.Code).ToArray(), "snapshot order; no scenery, NPC, gone or far portal");
            Assert.AreEqual(0, host.PortalsInVicinity(new(2, AreaUseScriptHost.EntityId(npc))).Count, "an object of another map has no place here");
        });
    }

    // ARC-SCRIPT-2322 (ledger CV1–CV5): the focus kinds every container (17) and any container (18) in the vicinity, in the use, placed, dying, combat-point and heartbeat sets.
    [TestMethod]
    public void Every_set_that_runs_a_script_on_an_object_admits_the_container_focus_kinds()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.DyingAdmission, AreaUseScriptHost.CombatPointAdmission, AreaUseScriptHost.HeartbeatAdmission })
        {
            Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EveryContainerInVicinity), set.Id);
            Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.AnyContainerInVicinity), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        }
    }

    [TestMethod]
    public void The_containers_around_an_object_are_the_near_containers_that_are_not_gone()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Npc(LoopScript);
            var near = Container(2);
            var nearToo = Container(3, 14);
            var gone = Container(4);
            var far = Container(5, 60);
            var portal = Portal(6);
            var host = Host(session, content, row => row.Key == gone.Key, npc, near, nearToo, gone, far, portal);
            var around = host.ContainersInVicinity(new(1, AreaUseScriptHost.EntityId(npc)));
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(near), AreaUseScriptHost.EntityId(nearToo) }, around.Select(o => o.Code).ToArray(), "snapshot order; no portal, NPC, gone or far container");
            Assert.AreEqual(0, host.ContainersInVicinity(new(2, AreaUseScriptHost.EntityId(npc))).Count, "an object of another map has no place here");
        });
    }

    // ARC-SCRIPT-2335 (ledger IV2, IV5): an insert-item script's vicinity is the scan around the item's own tile (a held item keeps the tile its map data gave it), not around its holder; an item without a row, or of
    // another map, has no known tile (the session then refuses the move), and a change art number of an object the host holds no row for is refused (no plan), the hero's is an empty plan.
    [TestMethod]
    public void The_containers_around_an_item_are_the_near_containers_of_its_own_tile_and_a_made_item_has_none()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var item = AreaHeartbeatScriptTests.Row(9, 11, 0) with { WorldX = 12 };
            var near = Container(2);
            var far = Container(5, 60);
            var host = Host(session, content, _ => false, item, near, far);
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(near) }, host.ContainersNearItem(new(1, AreaUseScriptHost.EntityId(item)))!.Select(o => o.Code).ToArray());
            Assert.IsNull(host.ContainersNearItem(new(1, "made:3")), "a made item has no row and so no known tile");
            Assert.IsNull(host.ContainersNearItem(new(2, AreaUseScriptHost.EntityId(item))), "another map");
            Assert.IsNull(host.PlanObjectArt(new(1, "made:3"), 4), "no row, no plan");
            Assert.IsNull(host.PlanObjectArt(host.Pc, 4)!.Art, "the hero's art is a critter class: a plan that changes nothing is still a plan");
            Assert.IsNull(host.PlanObjectArt(new(1, AreaUseScriptHost.EntityId(far)), 4), "no art index in this host: the plan is refused");
        });
    }

    // ARC-SCRIPT-2376 (ledger IV25, PG3, TG4): the scenery and the critters of an insert-item script's vicinity are the scans around the item's own tile as well; an item without a known tile gets no answer (the session then refuses the move).
    [TestMethod]
    public void The_scenery_and_the_critters_around_an_item_are_those_near_its_own_tile()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var item = AreaHeartbeatScriptTests.Row(9, 11, 0) with { WorldX = 12 };
            var near = Scenery(2);
            var far = Scenery(5, 60);
            var gone = Scenery(4);
            var container = Container(3);
            var critter = Npc(0);
            var farCritter = AreaHeartbeatScriptTests.Row(7, 16, 0) with { WorldX = 60 };
            var host = Host(session, content, row => row.Key == gone.Key, item, near, far, gone, container, critter, farCritter);
            var at = new ScrObjectRef(1, AreaUseScriptHost.EntityId(item));
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(near) }, host.SceneryNearItem(at)!.Select(o => o.Code).ToArray(), "no container, far or gone scenery");
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(critter), host.Pc.Code }, host.CrittersNearItem(at)!.Select(o => o.Code).ToArray(), "the near NPC, then the hero (the host's world puts it at (12, 21), next to the item); no far critter, no scenery");
            foreach (var around in new Func<ScrObjectRef, IReadOnlyList<ScrObjectRef>?>[] { host.SceneryNearItem, host.CrittersNearItem, host.ContainersNearItem })
            {
                Assert.IsNull(around(new(1, AreaUseScriptHost.EntityId(critter))), "an NPC is no item: no centre from its map row");
                Assert.IsNull(around(new(1, "made:3")), "a made item has no row and so no known tile");
                Assert.IsNull(around(new(2, AreaUseScriptHost.EntityId(item))), "another map");
            }
        });
    }

    // ARC-SCRIPT-2248 (ledger OT1–OT5): toggle state (56), which only the heartbeat set held, is in the use set and so in the placed, dying and combat-point sets built on it.
    [TestMethod]
    public void Every_set_that_runs_a_script_on_an_object_admits_toggle_state()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.DyingAdmission, AreaUseScriptHost.CombatPointAdmission, AreaUseScriptHost.HeartbeatAdmission })
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.ToggleState), set.Id);
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.CombatPointAdmission })
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        Assert.IsFalse(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.Kill), "kill stays with the heartbeat and dying sets");
        Assert.IsTrue(ScrAdmission.Admit(ProgramOf(ToggleScript), AreaUseScriptHost.Admission).IsAdmitted);
        Assert.IsTrue(ScrAdmission.Admit(ProgramOf(PortalToggleScript), AreaUseScriptHost.PlacedAdmission).IsAdmitted);
    }

    private static ScrProgram ProgramOf(int number)
    {
        ScrProgram? read = null;
        WithContent(content => read = content.Script(number).Program);
        return read!;
    }

    // The use click's script toggles its object: an NPC of the shown map switches off, a second run switches it back on (the heartbeat host's answer, shared).
    [TestMethod]
    public void A_use_script_toggles_an_NPC_of_the_shown_map_off_and_back_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = AreaHeartbeatScriptTests.Row(1, 16, ToggleScript, point: ScrPoint.Use);
            var host = Host(session, content, _ => false, npc);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, entity));
            var first = host.Run(npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, first.Outcome, first.Detail);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, entity), "switched off by the toggle");
            var second = host.Run(npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, second.Outcome, second.Detail);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, entity), "and on again");
        });
    }

    // The shape of 2221: a loop over the portals around the attachee. With none around nothing is toggled; with one the run is refused whole (a portal's off flag is not modelled), so nothing applies.
    [TestMethod]
    public void A_use_script_that_toggles_the_portals_around_it_runs_over_none_and_is_refused_over_one()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = AreaHeartbeatScriptTests.Row(1, 16, PortalToggleScript, point: ScrPoint.Use);
            var alone = Host(session, content, _ => false, npc);
            var none = alone.Run(npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, none.Outcome, none.Detail);
            var portal = Portal(2);
            var host = Host(session, content, _ => false, npc, portal);
            var refused = host.Run(npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
            StringAssert.Contains(refused.Detail, "toggle state of " + AreaUseScriptHost.EntityId(portal) + " is not modelled");
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, AreaUseScriptHost.EntityId(portal)));
        });
    }

    // The refusal is whole: an NPC toggle that comes before a portal toggle in the same run is not applied either.
    [TestMethod]
    public void A_run_that_toggles_an_NPC_and_then_a_portal_is_refused_whole_and_the_NPC_stays_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = AreaHeartbeatScriptTests.Row(1, 16, MixedToggleScript, point: ScrPoint.Use);
            var host = Host(session, content, _ => false, npc, Portal(2));
            var run = host.Run(npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, run.Outcome, run.Detail);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, AreaUseScriptHost.EntityId(npc)), "nothing applied");
        });
    }

    // An NPC the map data stores off (object flag 0x2) is switched on by a use script's toggle, through the session's flag change, and off again by a second one.
    [TestMethod]
    public void A_use_script_switches_on_an_NPC_the_map_data_stores_off()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = AreaHeartbeatScriptTests.Row(1, 16, ToggleScript, objectFlags: AreaUseScriptHost.OffObjectFlagBit, point: ScrPoint.Use);
            var host = Host(session, content, _ => false, npc);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.IsTrue(AreaUseScriptHost.NpcOff(session, 1, entity, AreaUseScriptHost.OffObjectFlagBit), "off in the data");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(npc, session.Rules.Scripts).Outcome);
            Assert.IsFalse(AreaUseScriptHost.NpcOff(session, 1, entity, AreaUseScriptHost.OffObjectFlagBit), "switched on");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(npc, session.Rules.Scripts).Outcome);
            Assert.IsTrue(AreaUseScriptHost.NpcOff(session, 1, entity, AreaUseScriptHost.OffObjectFlagBit), "and off again");
        });
    }
}
