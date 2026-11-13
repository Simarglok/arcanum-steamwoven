using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS1–NS8): the NPC state actions through the use-script host's heartbeat run over an invented content stack and map (no original data): critter disband lets a follower go through
/// the session's party, set and adjust reaction go to the AI host's delegate, the standpoints and the teleport map are the session's object fields that the AI host's route facts read, change script writes an
/// attachment slot as a map change, and change art number (ARC-SCRIPT-1735) writes the session's object art field of a scenery.</summary>
[TestClass]
public sealed class AreaNpcStateScriptTests
{
    private const byte Attachee = 1, Number = 3, Player = 21;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int script, int dialog = 0)
    {
        var slots = new List<AreaSparseElement> { new(ScrPoint.Heartbeat, [0, 0, script]) };
        if (dialog != 0) slots.Insert(0, new(ScrPoint.Dialog, [0, 0, dialog]));
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    // ARC-SCRIPT-1735: a scenery (type 3) of the shown map whose current art is <paramref name="art"/> and whose use script is <paramref name="script"/> (attachment point 1); an NPC row takes the same art through its own current art field (slot 0).
    private static AreaObjectRecord Scenery(int index, int script, uint art, uint flags = 0, int type = 3)
    {
        var fields = new List<AreaFieldValue>
        {
            new(0, AreaPresence.Value, AreaValueOrigin.Instance, art, [], null),
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(1, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, flags, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    // The art id of a scenery: class 4, kind bits 6..10, number bits 19..27, frame bits 14..18.
    private static uint SceneryArt(uint number, uint frame = 0) => (4u << 28) | (number << 19) | (frame << 14) | (3u << 6);

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

    private static readonly Clause Run = new(ScrCatalogue.Action.ReturnRunDefault);

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1163-");
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
            Write("scr/00820disband.scr", Program(Do(new(ScrCatalogue.Action.CritterDisband, (Attachee, -1))), Do(Run)));
            Write("scr/00821reaction.scr", Program(Do(new(ScrCatalogue.Action.SetReaction, (Attachee, -1), (Player, 0), N(60))),
                Do(new(ScrCatalogue.Action.AdjustReaction, (Attachee, -1), (Player, 0), N(-15))), Do(Run)));
            Write("scr/00822stand.scr", Program(Do(new(ScrCatalogue.Action.SetDayStandpoint, (Attachee, -1), N(480), N(521))),
                Do(new(ScrCatalogue.Action.SetNightStandpointEx, (Attachee, -1), N(-3), N(7), N(5012))), Do(Run)));
            Write("scr/00823own.scr", Program(Do(new(ScrCatalogue.Action.ChangeScript, (Attachee, -1), N(ScrPoint.Heartbeat), N(820))), Do(Run)));
            Write("scr/00824dialog.scr", Program(Do(new(ScrCatalogue.Action.ChangeScript, (Attachee, -1), N(ScrPoint.Dialog), N(1200))), Do(Run)));
            Write("scr/00826lowmap.scr", Program(Do(new(ScrCatalogue.Action.SetDayStandpointEx, (Attachee, -1), N(10), N(20), N(0))), Do(Run)));
            Write("scr/00825art.scr", Program(Do(new(ScrCatalogue.Action.ChangeArtNumber, (Attachee, -1), N(4))), Do(Run)));
            Write("scr/00828standart.scr", Program(Do(new(ScrCatalogue.Action.SetDayStandpoint, (Attachee, -1), N(480), N(521))),
                Do(new(ScrCatalogue.Action.ChangeArtNumber, (Attachee, -1), N(4))), Do(Run)));
            Write("scr/00827art9.scr", Program(Do(new(ScrCatalogue.Action.ChangeArtNumber, (Attachee, -1), N(9))), Do(Run)));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, AreaScriptWorld? world, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, world ?? new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    [TestMethod]
    public void A_disband_lets_a_follower_go_and_leaves_a_free_npc_as_it_is()
    {
        // NS1 (0x45DFC0, force 1): the party's leave; the NPC is free afterwards, a second disband changes nothing.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 820);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
            Assert.IsTrue(session.QueryParty().Followers.Any(f => f.Entity == entity));
            var host = Host(session, content, null, npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsFalse(session.QueryParty().Followers.Any(f => f.Entity == entity), "no longer a follower");
            var version = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(version, session.Version, "a free NPC: nothing changes");
        });
    }

    [TestMethod]
    public void Set_and_adjust_reaction_go_to_the_ai_hosts_delegate_in_order_and_without_it_the_run_is_left_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 821);
            var asked = new List<(string Npc, int Value, bool Set)>();
            var host = Host(session, content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ScriptReaction: (who, value, set) => { asked.Add((who, value, set)); return "ok"; }), npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            var entity = AreaUseScriptHost.EntityId(npc);
            CollectionAssert.AreEqual(new[] { (entity, 60, true), (entity, -15, false) }, asked);
            Assert.AreEqual("ok", host.LastReaction);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no AI host");
        });
    }

    [TestMethod]
    public void The_standpoints_are_the_sessions_fields_and_the_extended_one_writes_the_teleport_map()
    {
        // NS3: the day standpoint (480, 521); the night one (−3, 7) with the sign extension in its high word (y | x >> 31 = −1); the extended action's map 5012 is teleport map 13 (5012 − 4999).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 822);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.IsNull(AreaUseScriptHost.Standpoint(session, 1, entity, day: true));
            Assert.IsNull(session.QueryTeleportMap(entity));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(new AreaRouteLocation(480, 521), AreaUseScriptHost.Standpoint(session, 1, entity, day: true));
            Assert.AreEqual(new AreaRouteLocation(unchecked((uint)-3), uint.MaxValue), AreaUseScriptHost.Standpoint(session, 1, entity, day: false));
            Assert.AreEqual(13, session.QueryTeleportMap(entity));
        });
    }

    [TestMethod]
    public void An_extended_standpoint_with_a_map_the_session_cannot_keep_is_refused_before_anything_applies()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 826);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsNull(AreaUseScriptHost.Standpoint(session, 1, AreaUseScriptHost.EntityId(npc), day: true), "the standpoint was not written either");
        });
    }

    [TestMethod]
    public void Change_script_of_its_own_slot_takes_effect_when_the_run_ends()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 823);
            var host = Host(session, content, null, npc);
            var obj = new ScrObjectRef(1, AreaUseScriptHost.EntityId(npc));
            Assert.AreEqual(823, host.Slot(obj, ScrPoint.Heartbeat)?.Script);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(820, host.Slot(obj, ScrPoint.Heartbeat)?.Script, "the next heartbeat runs script 820");
        });
    }

    [TestMethod]
    public void Change_script_of_another_slot_is_kept_only_where_the_object_has_the_element()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var plain = Row(1, 824);
            var talker = Row(2, 824, dialog: 1100);
            var rules = session.Rules.Scripts;
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, plain).RunHeartbeat(plain, ScrPoint.Heartbeat, rules).Outcome, "no dialog element: left unapplied before anything applies");
            var host = Host(session, content, null, talker);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(talker, ScrPoint.Heartbeat, rules).Outcome);
            Assert.AreEqual(1200, host.Slot(new ScrObjectRef(1, AreaUseScriptHost.EntityId(talker)), ScrPoint.Dialog)?.Script);
        });
    }

    [TestMethod]
    public void Change_art_number_writes_the_new_art_id_of_a_scenery_with_frame_zero_and_a_return_to_the_data_art_clears_it()
    {
        // NS5, NS9: the number bits (19..27) are replaced and the frame bits (14..18) cleared, only when the ART file exists; the id the map data gives is the baseline (the record goes).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var framed = SceneryArt(9, 3);
            var scenery = Scenery(1, 825, framed);
            var entity = AreaUseScriptHost.EntityId(scenery);
            var map = new SessionMapId(1);
            var asked = new List<uint>();
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: aid => { asked.Add(aid); return true; });
            Assert.IsNull(session.QueryObjectArt(map, entity));
            { var first = Host(session, content, world, scenery).Run(scenery, session.Rules.Scripts); Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, first.Outcome, first.Detail); }
            Assert.AreEqual(SceneryArt(4), session.QueryObjectArt(map, entity), "number 4, frame 0");
            CollectionAssert.AreEqual(new[] { SceneryArt(4) }, asked.Distinct().ToArray(), "the test of the file is for the new id (the refusal pass and the apply pass both ask)");
            CollectionAssert.AreEqual(new[] { (entity, SceneryArt(4)) }, session.QueryObjectArts(map).ToArray());
            // Returning to the art id the map data gives clears the record.
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetObjectArt(session.Version, map, entity, framed, framed).Disposition);
            Assert.IsNull(session.QueryObjectArt(map, entity), "the id of the map data is the baseline");
            var plain = Scenery(2, 827, SceneryArt(9));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, world, plain).Run(plain, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(plain)), "the same art id writes nothing");
        });
    }

    [TestMethod]
    public void Change_art_number_changes_nothing_for_a_missing_art_file_a_destroyed_scenery_and_a_critter_art_and_refuses_what_it_cannot_draw()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var map = new SessionMapId(1);
            var none = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => false);
            var any = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true);
            // 0x502220: no ART file for the new id, nothing is written (the run goes on).
            var scenery = Scenery(1, 825, SceneryArt(9));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, none, scenery).Run(scenery, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(scenery)));
            // Without the module's art index the run is left unapplied.
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, scenery).Run(scenery, session.Rules.Scripts).Outcome);
            // 0x43EB30: an object destroyed in its map data (object flag 0x1) is left as it is.
            var destroyed = Scenery(2, 825, SceneryArt(9), flags: 1);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, destroyed).Run(destroyed, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(destroyed)));
            // A destroyed container (not drawable with another art) is left alone, not refused: the destroyed test comes before the type test.
            var destroyedContainer = Scenery(8, 825, (7u << 28) | (5u << 19), flags: 1, type: 2);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, destroyedContainer).Run(destroyedContainer, session.Rules.Scripts).Outcome);
            // A critter class (the NPC's body art is class 2) answers the id 0 in the number setter: nothing changes; a unique NPC's art (class 13) and a container are not drawn with another art: the run is refused before anything applies.
            var critter = Scenery(3, 825, (2u << 28) | 5u, type: 16);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, critter).Run(critter, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(critter)));
            var unique = Scenery(4, 825, (13u << 28) | (5u << 20), type: 16);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, any, unique).Run(unique, session.Rules.Scripts).Outcome);
            var container = Scenery(5, 825, (7u << 28) | (5u << 19), type: 2);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, any, container).Run(container, session.Rules.Scripts).Outcome);
            // A refused change art number leaves the run's other effects unapplied too: the same script (a day standpoint, then the art change) writes the standpoint for a critter art (no change) and not for a unique NPC's art (refused).
            var okNpc = Scenery(6, 828, (2u << 28) | 5u, type: 16);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, okNpc).Run(okNpc, session.Rules.Scripts).Outcome);
            Assert.AreEqual(new AreaRouteLocation(480, 521), AreaUseScriptHost.Standpoint(session, 1, AreaUseScriptHost.EntityId(okNpc), day: true), "the control: the standpoint is written when the art change is a no-op");
            var refusedNpc = Scenery(7, 828, (13u << 28) | (5u << 20), type: 16);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, any, refusedNpc).Run(refusedNpc, session.Rules.Scripts).Outcome);
            Assert.IsNull(AreaUseScriptHost.Standpoint(session, 1, AreaUseScriptHost.EntityId(refusedNpc), day: true), "nothing applied before the refused art change");
        });
    }

    // ARC-SCRIPT-1778: the art id of a door (class 3: number bits 19..27, door bit 10, frame bits 14..18) and of a container (class 7: number bits 19..27, frame bits 14..18).
    private static uint DoorArt(uint number, uint frame = 0) => (3u << 28) | (number << 19) | (frame << 14) | (1u << 10);
    private static uint ContainerArt(uint number, uint frame = 0) => (7u << 28) | (number << 19) | (frame << 14);

    [TestMethod]
    public void Change_art_number_gives_a_door_and_a_container_the_new_art_closed_with_their_lock_fields_kept()
    {
        // AN10: the open state of a door or window and of a container is the frame of its art id; the action writes the new id with frame 0, so the object is closed after it, and its lock fields are not touched.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var map = new SessionMapId(1);
            var any = (SessionOpenable openable, SessionOpenableBaseline baseline) => new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true,
                Openable: _ => new AreaScriptOpenable(openable, baseline, null, 3));
            foreach (var (type, art, kind, opened) in new[] { (1, DoorArt(9, 2), OpenableKind.Portal, 2), (2, ContainerArt(9, 1), OpenableKind.Container, 1) })
            {
                var row = Scenery(type * 10, 825, art, type: type);
                var entity = AreaUseScriptHost.EntityId(row);
                var openable = new SessionOpenable(map, entity);
                var baseline = new SessionOpenableBaseline(kind, LockRules.LockedBit | LockRules.JammedBit, 0, opened);
                Assert.AreEqual(opened, session.QueryOpenable(openable, baseline).Frame, "the T0 frame is the open state");
                var ran = Host(session, content, any(openable, baseline), row).Run(row, session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                var after = session.QueryOpenable(openable, baseline);
                Assert.AreEqual(0, after.Frame, $"type {type}: the new art is on frame 0, closed");
                Assert.AreEqual(LockRules.LockedBit | LockRules.JammedBit, after.Flags, "the lock fields are kept");
                var written = session.QueryObjectArt(map, entity);
                Assert.AreEqual(type == 1 ? DoorArt(4) : ContainerArt(4), written, "number 4, frame 0, the other bits kept");
            }
        });
    }

    [TestMethod]
    public void Change_art_number_with_the_same_number_still_closes_an_open_door()
    {
        // 0x502C40 runs after the number setter whatever the number was: the id is written with frame 0 (AN3), so an open door of the same art closes.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var map = new SessionMapId(1);
            var row = Scenery(1, 825, DoorArt(4, 2), type: 1);
            var entity = AreaUseScriptHost.EntityId(row);
            var openable = new SessionOpenable(map, entity); var baseline = new SessionOpenableBaseline(OpenableKind.Portal, 0, 0, 2);
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true, Openable: _ => new AreaScriptOpenable(openable, baseline, null, 3));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, world, row).Run(row, session.Rules.Scripts).Outcome);
            Assert.AreEqual(0, session.QueryOpenable(openable, baseline).Frame);
            Assert.AreEqual(DoorArt(4), session.QueryObjectArt(map, entity), "the id with frame 0 differs from the data's open id, so it is recorded");
        });
    }

    [TestMethod]
    public void Change_art_number_of_a_door_or_container_without_an_openable_or_a_file_is_refused_or_changes_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var map = new SessionMapId(1);
            var door = Scenery(1, 825, DoorArt(9, 2), type: 1);
            // No openable (the object is not on the shown map's portals): the run is left unapplied before anything applies.
            var none = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, none, door).Run(door, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(door)));
            // No ART file for the new id: nothing changes, the door stays open (0x502220 before the write).
            var openable = new SessionOpenable(map, AreaUseScriptHost.EntityId(door)); var baseline = new SessionOpenableBaseline(OpenableKind.Portal, 0, 0, 2);
            var missing = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => false, Openable: _ => new AreaScriptOpenable(openable, baseline, null, 3));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, missing, door).Run(door, session.Rules.Scripts).Outcome);
            Assert.AreEqual(2, session.QueryOpenable(openable, baseline).Frame);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(door)));
            // A door whose art id is not a door's class is refused.
            var odd = Scenery(2, 825, ContainerArt(9), type: 1);
            var any = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true, Openable: _ => new AreaScriptOpenable(openable, baseline, null, 3));
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, any, odd).Run(odd, session.Rules.Scripts).Outcome);
        });
    }
}
