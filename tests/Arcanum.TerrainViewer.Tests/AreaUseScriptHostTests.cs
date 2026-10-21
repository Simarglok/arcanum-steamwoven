using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-015: the viewer's use-script host over an invented content stack and map (no original data): use
/// slots from any row of the shown map, the S4 guards from the session, the teleport of the player, and the use slot written
/// back as map changes.</summary>
[TestClass]
public sealed class AreaUseScriptHostTests
{
    private const byte Triggerer = 0, Attachee = 1, Number = 3;

    private static AreaObjectKey Key(int index, int map = 1) => new(new string('a', 64), map, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    // A row with a use slot (point 1: local flags, counters, script) and a name number; spell flags when given.
    private static AreaObjectRecord Row(int index, int type, int script, int name, uint spellFlags = 0, int map = 1) => Row(Key(index, map), type, script, name, spellFlags);

    private static AreaObjectRecord Row(AreaObjectKey key, int type, int script, int name, uint spellFlags = 0)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Use, [0, 0, script])], null),
            new(21, AreaPresence.Value, AreaValueOrigin.Instance, name, [], null),
            new(19, AreaPresence.Value, AreaValueOrigin.Instance, spellFlags, [], null),
        };
        return new(key, 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + key.SourceIndex, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], fields, true, true, true);
    }

    private static SessionItemFacts Facts(int name, int location = 0) => new(new(SessionItemType.Armor, 4000 + name, name, 100 + name), 5, 0, location, null, []);

    // A gate (scenery, 700..706 by test), a ground item named 50, and an NPC holding an item named 70 in its weapon location.
    private static AreaObjectRecord Gate(int script, uint spellFlags = 0) => Row(0, 3, script, 3000, spellFlags);
    private static AreaInventoryMap Inventory() => new(1,
        new([new(Entity(9), SessionHolderKind.Npc, false, false, [], [Entity(5)])],
            [new SessionSeedItem(Entity(4), Facts(50), new CellCoord(14, 20)), new SessionSeedItem(Entity(5), Facts(70, 1004), null)]),
        new[] { Row(4, 7, 0, 50), Row(5, 7, 0, 70), Row(9, 16, 0, 9000) }.ToDictionary(row => Entity(row.Key.SourceIndex)));

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

    private static readonly Clause Skip = new(ScrCatalogue.Action.ReturnSkipDefault);
    private static Clause TeleportPc(int map) => new(ScrCatalogue.Action.Teleport, (Triggerer, 0), N(map), N(5), N(6));

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-015-");
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
            // 700: holding an item named 50 opens the way; otherwise a line is printed. The local flag 3 is set either way.
            Write("scr/00700gate.scr", Program(If(new(ScrCatalogue.Condition.HasItemNamed, (Triggerer, 0), N(50)), TeleportPc(5076), new(ScrCatalogue.Action.PrintLine, N(1), N(0))),
                Do(new(ScrCatalogue.Action.SetLocalFlag, N(3))), Do(Skip)));
            Write("dlg/00700gate.dlg", Encoding.Latin1.GetBytes("{1}{It is locked.}{}{}{}{}{}"));
            // 701: the PC's quest 1012 in state 2 (accepted) opens the way; the gate (another object) asks the quest's global state.
            Write("scr/00701quest.scr", Program(If(new(ScrCatalogue.Condition.PcQuestState, (Triggerer, 0), N(1012), N(2)), TeleportPc(4998), new(0)),
                If(new(ScrCatalogue.Condition.PcQuestState, (Attachee, 0), N(1012), N(2)), new(ScrCatalogue.Action.SetGlobalFlag, N(8)), new(0)), Do(Skip)));
            // 702: the gate is named 3000 and not mind-controlled.
            Write("scr/00702named.scr", Program(If(new(ScrCatalogue.Condition.IsNamed, (Attachee, 0), N(3000)), TeleportPc(5076), Skip), Do(Skip)));
            // 703: the gate's teleport, which the viewer does not model.
            Write("scr/00703other.scr", Program(Do(new(ScrCatalogue.Action.Teleport, (Attachee, 0), N(5076), N(5), N(6))), Do(Skip)));
            // 704: a dialog, outside the use set.
            Write("scr/00704talk.scr", Program(Do(new(ScrCatalogue.Action.Dialog, (Attachee, 0), N(1))), Do(Skip)));
            // 705: removes itself after the first use; then has no reputation and a teleport behind it.
            Write("scr/00705once.scr", Program(If(new(ScrCatalogue.Condition.HasReputation, (Triggerer, 0), N(4)), TeleportPc(5076), new(0)),
                Do(new(ScrCatalogue.Action.RemoveThisScript)), Do(Skip)));
            // 706: a tile script that keeps a local flag and teleports the player; 707: a sector script that sets a flag.
            Write("scr/00706tile.scr", Program(Do(new(ScrCatalogue.Action.SetLocalFlag, N(2))), Do(TeleportPc(5076)), Do(Skip)));
            Write("scr/00707sector.scr", Program(Do(new(ScrCatalogue.Action.SetGlobalFlag, N(9))), Do(new(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(Skip)));
            // 708 (ARC-UI-049): a book for the PC (the triggerer) and a plaque for the gate (not a PC).
            Write("scr/00708read.scr", Program(Do(new(ScrCatalogue.Action.ShowBook, N(1020), (Triggerer, 0))),
                Do(new(ScrCatalogue.Action.ShowPlaque, N(1500), (Attachee, 0))), Do(Skip)));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent? content, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [Inventory()], content);

    [TestMethod]
    public void A_guard_holds_back_the_teleport_until_the_pc_holds_the_item()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var gate = Gate(700);
            var host = Host(session, content, gate);
            Assert.AreEqual(new ScrSlot(700, 0, 0), host.Slot(new(1, Entity(0)), ScrPoint.Use), "any row of the shown map has its slot");

            var locked = host.Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, locked.Outcome, locked.Detail);
            CollectionAssert.AreEqual(new[] { "It is locked." }, locked.Lines.ToArray());
            // The use slot's local flag 3 is the session's now; the loaded row is unchanged.
            Assert.AreEqual(new ScrSlot(700, 1u << 3, 0), host.Slot(new(1, Entity(0)), ScrPoint.Use));
            Assert.AreEqual(1L << 3, session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(0), AreaUseScriptHost.LocalFlagsProperty)));

            var inventory = new AreaInventoryHost([Inventory()]);
            Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
            Assert.AreEqual(GameSessionDisposition.Changed, inventory.PickUp(session, Entity(4)).Disposition);
            var open = host.Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, open.Outcome, open.Detail);
            Assert.AreEqual(new ScrTeleport(host.Pc, 5076, 5, 6), open.Teleport);
            Assert.AreEqual(0, open.Lines.Count);
        });
    }

    [TestMethod]
    public void The_quest_guard_reads_the_pc_entry_and_another_objects_global_state()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var gate = Gate(701);
            var host = Host(session, content, gate);
            Assert.AreEqual(0, host.QuestState(host.Pc, 1012), "no entry");
            Assert.AreEqual(0, host.QuestState(host.Pc, 12), "not a quest number");
            var none = host.Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, none.Outcome, none.Detail);
            Assert.IsTrue(host.GlobalFlag(8), "the gate reads the global state: accepted (2) until a PC completes or botches it");

            Assert.AreNotEqual(GameSessionDisposition.Rejected, session.SetQuestState(session.Version, 1012, SessionQuestState.Accepted).Disposition);
            Assert.AreEqual((int)SessionQuestState.Accepted, host.QuestState(host.Pc, 1012));
            Assert.AreEqual((int)session.QueryGlobalQuest(1012), host.QuestState(new(1, Entity(0)), 1012));
            var ran = host.Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(-1, ran.Teleport!.RuntimeMap, "map 4,998: the current map");
        });
    }

    [TestMethod]
    public void Names_holds_and_wielded_items_come_from_rows_and_the_holders_lists()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var inventory = new AreaInventoryHost([Inventory()]);
            Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
            var host = Host(session, content, Gate(702));
            var npc = new ScrObjectRef(1, Entity(9));
            Assert.AreEqual(9000, host.NameOf(npc), "a holder of the inventory rows");
            Assert.AreEqual(3000, host.NameOf(new(1, Entity(0))));
            Assert.AreEqual(0, host.NameOf(host.Pc), "the PC's name number is not modelled");
            Assert.IsTrue(host.HoldsItemNamed(npc, 70));
            Assert.IsFalse(host.HoldsItemNamed(npc, 50));
            Assert.AreEqual(70, host.WieldedItemName(npc, 1004));
            Assert.IsNull(host.WieldedItemName(npc, 1005));
            Assert.IsFalse(host.HasReputation(host.Pc, 4));
            // ARC-WORLD-044: the PC's reputations are the session's; no other object has one (SG5).
            Assert.AreEqual(GameSessionDisposition.Changed, session.AddReputation(session.Version, 1004).Disposition);
            Assert.IsTrue(host.HasReputation(host.Pc, 1004));
            Assert.IsFalse(host.HasReputation(npc, 1004));
            Assert.IsFalse(host.HasReputation(host.Pc, 1005));

            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, host.Run(Gate(702), session.Rules.Scripts).Outcome);
            var controlled = Gate(702, AreaUseScriptHost.MindControlledFlag);
            var held = Host(session, content, controlled);
            Assert.IsTrue(held.IsMindControlled(new(1, Entity(0))));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, held.Run(controlled, session.Rules.Scripts).Outcome, "a mind-controlled object is never named");
        });
    }

    [TestMethod]
    public void A_reading_action_for_the_pc_is_kept_and_one_for_another_viewer_is_not()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var host = Host(session, content, Gate(708));
            var ran = host.Run(Gate(708), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(2, ran.Run!.Effects.Readings.Count, "the run asked for both");
            CollectionAssert.AreEqual(new[] { new ScrReadingRequest(ScrReadingKind.Book, 1020, new ScrObjectRef(1, AreaScriptHost.PcCode)) },
                host.TakeReadings().ToArray(), "only a player viewer gets the window (WR15)");
            Assert.AreEqual(0, host.TakeReadings().Count, "taken once");
        });
    }

    [TestMethod]
    public void Runs_the_viewer_does_not_model_are_refused_without_changing_the_session()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var before = session.Version;
            var other = Host(session, content, Gate(703)).Run(Gate(703), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, other.Outcome, other.Detail);
            var talk = Host(session, content, Gate(704)).Run(Gate(704), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, talk.Outcome);
            StringAssert.Contains(talk.Detail, "script 704");
            Assert.AreEqual(AreaScriptedUseOutcome.ContentUnavailable, Host(session, null, Gate(700)).Run(Gate(700), session.Rules.Scripts).Outcome);
            var elsewhere = Row(0, 3, 700, 3000, map: 2);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, Host(session, content, Gate(700)).Run(elsewhere, session.Rules.Scripts).Outcome);
            Assert.AreEqual(before, session.Version);
            Assert.IsFalse(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.Dialog));
            Assert.IsTrue(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.Teleport));
        });
    }

    [TestMethod]
    public void A_script_that_removes_itself_runs_once()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var gate = Gate(705);
            var host = Host(session, content, gate);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(gate, session.Rules.Scripts).Outcome, "no reputation");
            Assert.AreEqual(0, host.Slot(new(1, Entity(0)), ScrPoint.Use)!.Value.Script);
            var again = host.Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, again.Outcome);
            Assert.AreEqual(ScrStop.NoScript, again.Run!.Stop);
        });
    }

    [TestMethod]
    public void Sector_objects_with_the_one_null_id_digest_keep_their_own_use_slots()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // Two scenery objects of one sector: the same id digest (a null id), told apart by their window ordinals.
            AreaObjectKey Sector(int index) => new(new string('a', 64), 1, AreaSourceKind.Static, 42, index, index * 100L, new string('0', 64), 100);
            var first = Row(Sector(0), 3, 705, 3000);
            var second = Row(Sector(1), 3, 705, 3001);
            Assert.AreEqual("sector:42:0", AreaUseScriptHost.EntityId(first));
            Assert.AreNotEqual(AreaUseScriptHost.EntityId(first), AreaUseScriptHost.EntityId(second));
            Assert.AreEqual(Entity(9), AreaUseScriptHost.EntityId(Row(9, 16, 0, 9000)), "a mobile keeps its own id");
            var host = Host(session, content, first, second);
            Assert.AreEqual(3001, host.NameOf(new(1, AreaUseScriptHost.EntityId(second))));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(first, session.Rules.Scripts).Outcome);
            Assert.AreEqual(0, host.Slot(new(1, AreaUseScriptHost.EntityId(first)), ScrPoint.Use)!.Value.Script, "the first removed its script");
            Assert.AreEqual(705, host.Slot(new(1, AreaUseScriptHost.EntityId(second)), ScrPoint.Use)!.Value.Script, "the second keeps its own");
        });
    }

    [TestMethod]
    public void A_teleport_the_caller_cannot_take_leaves_the_use_undone()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var gate = Gate(700);
            var host = Host(session, content, gate);
            var before = session.Version;
            var asked = new List<ScrTeleport>();
            // Without the item: no teleport, so the refusal is never asked and the run applies.
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(gate, session.Rules.Scripts, t => { asked.Add(t); return "NO"; }).Outcome);
            Assert.AreEqual(0, asked.Count);
            Assert.AreNotEqual(before, session.Version);

            var inventory = new AreaInventoryHost([Inventory()]);
            Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
            Assert.AreEqual(GameSessionDisposition.Changed, inventory.PickUp(session, Entity(4)).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
                [new MapChangeEdit(new SessionMapId(1), MapChangeKey.ForEntity(Entity(0), AreaUseScriptHost.LocalFlagsProperty), null)]).Disposition);
            var held = session.Version;
            var refused = host.Run(gate, session.Rules.Scripts, t => { asked.Add(t); return "DESTINATION_NOT_OPENED_IN_MAP_MODE 5076"; });
            Assert.AreEqual((AreaScriptedUseOutcome.TeleportRefused, "DESTINATION_NOT_OPENED_IN_MAP_MODE 5076"), (refused.Outcome, refused.Detail));
            Assert.AreEqual(new ScrTeleport(host.Pc, 5076, 5, 6), asked.Single());
            Assert.AreEqual(held, session.Version, "the local flag 3 is not written back");
        });
    }
    // ARC-SCRIPT-017: tile and sector scripts run for the PC with no attachee; their slot lives on their sector.
    [TestMethod]
    public void Tile_and_sector_scripts_run_for_the_pc_and_keep_their_slot_on_the_sector_by_rule()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var sector = new Arcanum.Data.Maps.WorldSectorCoordinate(3, 4);
            var tile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, sector, 70, new(198, 257), 0, 0, 706);
            var own = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry, sector, -1, new(192, 256), 0x10, 0, 707);
            Assert.AreEqual(("tile.70.lf", "sector.script"), (AreaUseScriptHost.PlacedProperty(tile, AreaUseScriptHost.PlacedLocalFlagsPart), AreaUseScriptHost.PlacedProperty(own, AreaUseScriptHost.PlacedScriptPart)));
            var host = Host(session, content);

            var stepped = host.RunPlaced(tile, session.Rules.Scripts);
            Assert.AreEqual((AreaScriptedUseOutcome.Teleport, new ScrTeleport(host.Pc, 5076, 5, 6)), (stepped.Outcome, stepped.Teleport));
            Assert.AreEqual(new ScrSlot(706, 1u << 2, 0), host.PlacedSlot(tile), "Steamwoven keeps a tile script's own change");
            Assert.AreEqual(1L << 2, session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForSector(sector.Id, "tile.70.lf")));

            using var original = ConversationSessionFixture.Create();
            var plain = Host(original, content);
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, plain.RunPlaced(tile, GameRules.Original.Scripts).Outcome);
            Assert.AreEqual(new ScrSlot(706, 0, 0), plain.PlacedSlot(tile), "the original stores the unchanged record back (ledger X3)");

            var entered = host.RunPlaced(own, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, entered.Outcome, entered.Detail);
            Assert.IsTrue(host.GlobalFlag(9));
            Assert.AreEqual(new ScrSlot(707, 0x10 | 1u << 1, 0), host.PlacedSlot(own));
            Assert.ThrowsExactly<ArgumentException>(() => host.RunPlaced(tile with { Trigger = Arcanum.Data.Maps.MapTransitionTrigger.Use }, session.Rules.Scripts));
            // The original keeps a sector script's change too (ledger X3 sector).
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, plain.RunPlaced(own, GameRules.Original.Scripts).Outcome);
            Assert.AreEqual(new ScrSlot(707, 0x10 | 1u << 1, 0), plain.PlacedSlot(own));
            // A tile script that removes itself runs once (Steamwoven); the next entry runs default and is silent.
            var once = tile with { Tile = 71, Script = 705 };
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunPlaced(once, session.Rules.Scripts).Outcome);
            Assert.AreEqual(0, host.PlacedSlot(once).Script);
            Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrStop.NoScript), (host.RunPlaced(once, session.Rules.Scripts).Outcome, host.RunPlaced(once, session.Rules.Scripts).Run!.Stop));
        });
    }
}
