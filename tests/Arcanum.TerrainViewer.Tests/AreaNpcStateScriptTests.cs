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
    private const byte Attachee = 1, Number = 3, Player = 21, Extra = 12;

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

    // ARC-SCRIPT-1779: the art id of an item: class 6, the number in bits 17..27, the armour coverage in bits 14..16, the subtype in bits 6..9 and the item type in the low four bits (place bits 12..13 are 0, the ground art).
    private static uint ItemArt(uint number, uint type, uint coverage = 0, uint subtype = 2) => (6u << 28) | (number << 17) | (coverage << 14) | (subtype << 6) | type;

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
            Write("scr/00829art25.scr", Program(Do(new(ScrCatalogue.Action.ChangeArtNumber, (Attachee, -1), N(25))), Do(Run)));
            Write("scr/00830extra.scr", Program(Do(new(ScrCatalogue.Action.ChangeArtNumber, (Extra, -1), N(25))), Do(Run)));
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
    public void Change_art_number_writes_the_ground_art_of_an_item_by_the_number_range_of_its_type_and_leaves_the_rest_as_it_is()
    {
        // AN10 (0x502830): types 0 and 1 and an armour without coverage allow 20 numbers (the id is kept past them, so nothing changes), every other type 1,000; the type, subtype, coverage and place bits stay and the file test applies as for a scenery.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var map = new SessionMapId(1);
            var any = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true);
            var none = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => false);
            // A weapon art (type 0, 20 numbers): number 4 is taken.
            var weapon = Scenery(1, 825, ItemArt(9, 0), type: 5);
            var weaponEntity = AreaUseScriptHost.EntityId(weapon);
            { var run = Host(session, content, any, weapon).Run(weapon, session.Rules.Scripts); Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, run.Outcome, run.Detail); }
            Assert.AreEqual(ItemArt(4, 0), session.QueryObjectArt(map, weaponEntity));
            // Number 25 is past 20: the id is kept (the original logs "Range exceeded"), so the written id is the old one and the record is the same as the data's: none.
            var narrow = Scenery(2, 829, ItemArt(9, 1), type: 14);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, narrow).Run(narrow, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(narrow)), "25 is out of the range of type 1");
            var coverless = Scenery(3, 829, ItemArt(9, 2), type: 7);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, coverless).Run(coverless, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(coverless)), "an armour without coverage: 20 numbers");
            // Type 4 (range 1,000) and an armour with coverage take 25; the coverage and subtype bits stay.
            var wide = Scenery(4, 829, ItemArt(9, 4, subtype: 3), type: 9);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, wide).Run(wide, session.Rules.Scripts).Outcome);
            Assert.AreEqual(ItemArt(25, 4, subtype: 3), session.QueryObjectArt(map, AreaUseScriptHost.EntityId(wide)));
            var ring = Scenery(5, 829, ItemArt(9, 2, coverage: 5), type: 7);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, ring).Run(ring, session.Rules.Scripts).Outcome);
            Assert.AreEqual(ItemArt(25, 2, coverage: 5), session.QueryObjectArt(map, AreaUseScriptHost.EntityId(ring)));
            // No ART file for the new id: nothing is written. Without the art index the run is left unapplied. A destroyed item (object flag 0x1) is left as it is.
            var missing = Scenery(6, 825, ItemArt(9, 0), type: 5);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, none, missing).Run(missing, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(missing)));
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, missing).Run(missing, session.Rules.Scripts).Outcome);
            var gone = Scenery(7, 825, ItemArt(9, 0), flags: 1, type: 5);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, gone).Run(gone, session.Rules.Scripts).Outcome);
            Assert.IsNull(session.QueryObjectArt(map, AreaUseScriptHost.EntityId(gone)));
            // An item row whose art is not of the item class is refused (the viewer cannot draw it), like any object of no modelled kind.
            var odd = Scenery(8, 825, SceneryArt(9), type: 14);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, any, odd).Run(odd, session.Rules.Scripts).Outcome);
            // A second run on the changed item works from its current art (number 4 again is the same id: no record change); the data's id returns it to the baseline.
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, any, weapon).Run(weapon, session.Rules.Scripts).Outcome);
            Assert.AreEqual(ItemArt(4, 0), session.QueryObjectArt(map, weaponEntity));
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetObjectArt(session.Version, map, weaponEntity, ItemArt(9, 0), ItemArt(9, 0)).Disposition);
            Assert.IsNull(session.QueryObjectArt(map, weaponEntity));
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
        // AD1: the open state of a door or window and of a container is the frame of its art id; the action writes the new id with frame 0, so the object is closed after it, and its lock fields are not touched.
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

    // ARC-SCRIPT-1826 (AN18): a map-data item carried from another map, and a made or script-created item, as the extra object of a use script of a scenery on the shown map (map 2): the item is named on the shown map and owns no row there.
    private sealed class OpenMap : Arcanum.Simulation.Items.IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public Arcanum.Simulation.Movement.CellCoord? Step(Arcanum.Simulation.Movement.CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public Arcanum.Simulation.Items.DropPath Path(Arcanum.Simulation.Movement.CellCoord from, Arcanum.Simulation.Movement.CellCoord to, Arcanum.Simulation.Items.DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to; at = Step(at, steps[^1])!.Value) steps.Add(Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y))));
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(Arcanum.Simulation.Movement.CellCoord cell) => false;
    }

    private static readonly SessionItemKind GenericKind = new(SessionItemType.Generic, 2120, 1, 2);

    private static AreaScriptedUse RunOnExtra(AreaUseScriptHost host, GameSessionOwner session, AreaObjectRecord scenery, string extraEntity) =>
        host.RunUseScript(AreaUseScriptHost.EntityId(scenery), null, new ScrObjectRef(2, extraEntity), session.Rules.Scripts);

    [TestMethod]
    public void Change_art_number_changes_the_ground_art_of_a_made_or_script_created_item_from_its_prototype_art_and_the_record_follows_the_item()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var prototype = new AreaPrototypeItem(GenericKind, ItemArt(9, 4, subtype: 3), new(1));
            var world = new AreaScriptWorld(() => null, _ => false, number => number == GenericKind.Prototype - AreaPrototypeItems.PrototypeOffset ? prototype : null, () => null, ArtExists: _ => true);
            var created = session.CreateGroundItem(session.Version, GenericKind, new SessionMapId(2), new(10, 10), new OpenMap());
            Assert.AreEqual(GameSessionDisposition.Changed, created.Disposition, created.Failure.ToString());
            var made = created.Item!;
            var user = Scenery(1, 830, SceneryArt(9));
            AreaUseScriptHost Host2(AreaScriptWorld? w) => new(session, 2, [user], [], content, w ?? world);
            var ran = RunOnExtra(Host2(null), session, user, made);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            // The number 25 replaces the prototype art's number; the subtype and type bits stay. The record is the item's, found from any map.
            Assert.AreEqual(ItemArt(25, 4, subtype: 3), session.QueryObjectArt(new SessionMapId(2), made));
            Assert.AreEqual(ItemArt(25, 4, subtype: 3), session.QueryObjectArt(new SessionMapId(1), made), "a made item's record is one whatever map it names");
            // The same number again writes nothing; the prototype's own art returns the item to its baseline.
            var again = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, RunOnExtra(Host2(null), session, user, made).Outcome);
            Assert.AreEqual(again, session.Version, "an id equal to the current one moves no record");
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetObjectArt(session.Version, new SessionMapId(1), made, prototype.Art, prototype.Art).Disposition);
            Assert.IsNull(session.QueryObjectArt(new SessionMapId(2), made));
            // A prototype the world does not read (refused), no art index (refused) and an ART file that does not exist (nothing written).
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, RunOnExtra(Host2(world with { Item = _ => null }), session, user, made).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, RunOnExtra(Host2(world with { ArtExists = null }), session, user, made).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, RunOnExtra(Host2(world with { ArtExists = _ => false }), session, user, made).Outcome);
            Assert.IsNull(session.QueryObjectArt(new SessionMapId(2), made), "no ART file for the new id");
            // An entity the session does not know as made or carried is still refused (no row): the made number 99 holds nothing.
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, RunOnExtra(Host2(null), session, user, "made:99").Outcome);
            // A made item the session deleted is a destroyed object: its art record goes with it and the action leaves it as it is (the run goes on, nothing refused).
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, RunOnExtra(Host2(null), session, user, made).Outcome);
            Assert.IsNotNull(session.QueryObjectArt(new SessionMapId(2), made));
            Assert.AreEqual(GameSessionDisposition.Changed, session.DestroyLyingItem(session.Version, made).Disposition);
            Assert.IsNull(session.QueryObjectArt(new SessionMapId(2), made), "the record of a deleted item is dropped");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, RunOnExtra(Host2(null), session, user, made).Outcome);
            Assert.IsNull(session.QueryObjectArt(new SessionMapId(2), made));
        });
    }

    [TestMethod]
    public void Change_art_number_reaches_a_map_data_item_the_hero_carried_here_through_the_row_of_its_origin_map()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var any = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ArtExists: _ => true);
            // The item stands on map 1's data (type 14, a generic item); the hero took it there; the shown map is 2, which holds no row of it.
            var itemRow = Scenery(5, 0, ItemArt(9, 4, subtype: 3), type: 14);
            var entity = AreaUseScriptHost.EntityId(itemRow);
            var goneRow = Scenery(6, 0, ItemArt(9, 4, subtype: 3), flags: 1, type: 14);
            var goneEntity = AreaUseScriptHost.EntityId(goneRow);
            var seed = new SessionMapInventorySeed([], [new SessionSeedItem(entity, new(GenericKind, 1, 0, 0, null, []), new(10, 10)), new SessionSeedItem(goneEntity, new(GenericKind, 1, 0, 0, null, []), new(11, 10))]);
            Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(1), seed).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, entity, _ => SessionInsertVerdict.Accepted).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, goneEntity, _ => SessionInsertVerdict.Accepted).Disposition);
            var user = Scenery(1, 830, SceneryArt(9));
            var asked = new List<ScrObjectRef>();
            AreaUseScriptHost Host2(bool origin) => new(session, 2, [user], [], content, any)
            {
                OtherMapRow = origin ? obj => { asked.Add(obj); return obj.Map == 1 && obj.Code == entity ? itemRow : null; } : null,
            };
            // Without the origin row the item is refused, as before; with it the new art is the ground art and the item keeps it wherever it goes.
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, RunOnExtra(Host2(false), session, user, entity).Outcome);
            Assert.IsNull(session.QueryObjectArt(new SessionMapId(2), entity));
            var ran = RunOnExtra(Host2(true), session, user, entity);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(asked.Count > 0 && asked.All(obj => obj == new ScrObjectRef(1, entity)), "the row is asked on the origin map the session names");
            Assert.AreEqual(ItemArt(25, 4, subtype: 3), session.QueryObjectArt(new SessionMapId(1), entity));
            Assert.AreEqual(ItemArt(25, 4, subtype: 3), session.QueryObjectArt(new SessionMapId(2), entity), "the item's record, whatever map is asked");
            // A carried item destroyed in its map data (object flag 0x1) is left as it is.
            var goneHost = new AreaUseScriptHost(session, 2, [user], [], content, any) { OtherMapRow = obj => obj.Code == goneEntity ? goneRow : null };
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, RunOnExtra(goneHost, session, user, goneEntity).Outcome);
            Assert.IsNull(session.QueryObjectArt(new SessionMapId(2), goneEntity));
        });
    }
}
