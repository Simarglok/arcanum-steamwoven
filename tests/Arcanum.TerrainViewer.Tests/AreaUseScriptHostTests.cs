using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

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
            Write("scr/00700gate.scr", Program(If(new(ScrCatalogue.Condition.HasItemNamed, (Triggerer, 0), N(50)), TeleportPc(5076), new(ScrCatalogue.Action.PrintLine, N(1), N(6))),
                Do(new(ScrCatalogue.Action.SetLocalFlag, N(3))), Do(Skip)));
            Write("dlg/00700gate.dlg", Encoding.Latin1.GetBytes("{1}{It is locked.}{}{}{}{}{}"));
            // 701: the PC's quest 1012 in state 2 (accepted) opens the way; the gate (another object) asks the quest's global state.
            Write("scr/00701quest.scr", Program(If(new(ScrCatalogue.Condition.PcQuestState, (Triggerer, 0), N(1012), N(2)), TeleportPc(4998), new(0)),
                If(new(ScrCatalogue.Condition.PcQuestState, (Attachee, 0), N(1012), N(2)), new(ScrCatalogue.Action.SetGlobalFlag, N(8)), new(0)), Do(Skip)));
            // 702: the gate is named 3000 and not mind-controlled.
            Write("scr/00702named.scr", Program(If(new(ScrCatalogue.Condition.IsNamed, (Attachee, 0), N(3000)), TeleportPc(5076), Skip), Do(Skip)));
            // 703: the gate's teleport, which the viewer does not model.
            Write("scr/00703other.scr", Program(Do(new(ScrCatalogue.Action.Teleport, (Attachee, 0), N(5076), N(5), N(6))), Do(Skip)));
            // 760: two printed lines of different entry types, then a teleport (ARC-DEV-309).
            Write("scr/00760print.scr", Program(Do(new(ScrCatalogue.Action.PrintLine, N(1), N(6))), Do(new(ScrCatalogue.Action.PrintLine, N(2), N(4))), Do(TeleportPc(5076)), Do(Skip)));
            Write("dlg/00760print.dlg", Encoding.Latin1.GetBytes("{1}{First line.}{}{}{}{}{}{2}{Second line.}{}{}{}{}{}"));
            // 704: a dialog, outside the use set.
            Write("scr/00704talk.scr", Program(Do(new(ScrCatalogue.Action.Dialog, (Attachee, 0), N(1))), Do(Skip)));
            // 705: removes itself after the first use; then has no reputation and a teleport behind it.
            Write("scr/00705once.scr", Program(If(new(ScrCatalogue.Condition.HasReputation, (Triggerer, 0), N(4)), TeleportPc(5076), new(0)),
                Do(new(ScrCatalogue.Action.RemoveThisScript)), Do(Skip)));
            // 706: a tile script that keeps a local flag and teleports the player; 707: a sector script that sets a flag.
            Write("scr/00706tile.scr", Program(Do(new(ScrCatalogue.Action.SetLocalFlag, N(2))), Do(TeleportPc(5076)), Do(Skip)));
            Write("scr/00707sector.scr", Program(Do(new(ScrCatalogue.Action.SetGlobalFlag, N(9))), Do(new(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(Skip)));
            // 740, 741 (ARC-SCRIPT-046): a tile script for the NPC named 9000 that calls 741 with the player as the triggerer and the NPC as
            // the attachee; 741 asks for node 3 of its own DLG (header local flags 0b10, which the NPC's use slot takes).
            Write("scr/00740tile.scr", Program(If(new(ScrCatalogue.Condition.IsNamed, (Triggerer, 0), N(9000)), new(0), new(ScrCatalogue.Action.Goto, N(2))),
                Do(new(ScrCatalogue.Action.CallScript, N(741), N(0), (21, 0), (Triggerer, 0))), Do(Skip)));
            var called = Program(Do(new(ScrCatalogue.Action.Dialog, N(3))), Do(Skip));
            BinaryPrimitives.WriteUInt32LittleEndian(called, 0b10);
            Write("scr/00741called.scr", called);
            // 750 (ARC-SCRIPT-052, 1724's and 1779's shape): a tile that stops the NPC named 9000 who steps on it attacking and destroys him.
            Write("scr/00750exit.scr", Program(If(new(ScrCatalogue.Condition.IsNamed, (Triggerer, 0), N(9000)), new(0), new(ScrCatalogue.Action.Goto, N(3))),
                Do(new(ScrCatalogue.Action.StopAttacking, (Triggerer, 0))), Do(new(ScrCatalogue.Action.Destroy, (Triggerer, 0))), Do(Skip)));
            // 751: the same for whoever steps on it; 752: whoever steps on it stops attacking.
            Write("scr/00751any.scr", Program(Do(new(ScrCatalogue.Action.Destroy, (Triggerer, 0))), Do(Skip)));
            Write("scr/00752stop.scr", Program(Do(new(ScrCatalogue.Action.StopAttacking, (Triggerer, 0))), Do(Skip)));
            // 770..772 (ARC-DEV-157, bug B23 and B20): a tile that reads the triggerer's alignment (19) into the global variable 5; one that reads a stat the session does not hold (8, carry weight); and one that loops over everyone in the vicinity
            // (focus kind 8).
            Write("scr/00770stat.scr", Program(Do(new(ScrCatalogue.Action.GetStat, N(19), (Triggerer, 0), (2, 0))), Do(new(ScrCatalogue.Action.Assign, (1, 5), (2, 0))), Do(Skip)));
            Write("scr/00771unknown.scr", Program(Do(new(ScrCatalogue.Action.GetStat, N(8), (Triggerer, 0), (2, 0))), Do(new(ScrCatalogue.Action.SetGlobalFlag, N(43))), Do(Skip)));
            Write("scr/00772loop.scr", Program(Do(new(ScrCatalogue.Action.LoopFor, (8, 0))), Do(new(ScrCatalogue.Action.SetGlobalFlag, N(44))), Do(new(ScrCatalogue.Action.LoopEnd)),
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(45))), Do(Skip)));
            // 773, 774 (ARC-DEV-205, bug B22): a sector script that reveals town map 9 twice and sets a flag, as the wreck's 3036 does with its own town map;
            // and one that names a town map outside the session's range before setting a flag.
            Write("scr/00773townmap.scr", Program(Do(new(ScrCatalogue.Action.UnfogTownMap, N(9))), Do(new(ScrCatalogue.Action.UnfogTownMap, N(9))),
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(46))), Do(Skip)));
            Write("scr/00774townmap.scr", Program(Do(new(ScrCatalogue.Action.UnfogTownMap, N(5000))), Do(new(ScrCatalogue.Action.SetGlobalFlag, N(47))), Do(Skip)));
            // 775 (ARC-DEV-298, bug B20): a tile script that floats its DLG line 1 over the PC, as the ghost of Brego's callee (2789) floats the ghost's.
            Write("scr/00775float.scr", Program(Do(new(ScrCatalogue.Action.FloatLine, N(1), (Triggerer, 0))), Do(new(ScrCatalogue.Action.SetGlobalFlag, N(48))), Do(Skip)));
            Write("dlg/00775float.dlg", Encoding.Latin1.GetBytes("{1}{Help me.}{}{}{}{}{}"));
            // 721, 722 (ARC-SCRIPT-020): an item's use script that sets a flag and runs default, and one that skips default.
            Write("scr/00721use.scr", Program(Do(new(ScrCatalogue.Action.SetGlobalFlag, N(31))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00722stop.scr", Program(Do(new(ScrCatalogue.Action.SetGlobalFlag, N(32))), Do(Skip)));
            // 720: raises the story state, sets a quest (for the PC and for the gate, which is ignored), marks an area, learns a rumor
            // and adds a reputation (ARC-SCRIPT-019).
            Write("scr/00720story.scr", Program(Do(new(ScrCatalogue.Action.SetStoryState, N(5))), Do(new(ScrCatalogue.Action.SetPcQuestState, (Triggerer, 0), N(1016), N(2))),
                Do(new(ScrCatalogue.Action.SetPcQuestState, (Attachee, 0), N(1017), N(2))), Do(new(ScrCatalogue.Action.MarkMapLocation, N(44), (Triggerer, 0))),
                Do(new(ScrCatalogue.Action.SetRumor, N(1100), (Triggerer, 0))), Do(new(ScrCatalogue.Action.AddReputation, (Triggerer, 0), N(1005))), Do(Skip)));
            // 730 (ARC-WORLD-081): the plant generator's core: next to every scenery named 1673 in the PC's vicinity, prototype 10061.
            Write("scr/00730plants.scr", Program(Do(new(ScrCatalogue.Action.AssignObject, (Attachee, 0), (Triggerer, 0))),
                Do(new(ScrCatalogue.Action.LoopFor, (15, 0))),
                If(new(ScrCatalogue.Condition.IsNamed, (10, 0), N(1673)), new(ScrCatalogue.Action.CreateObject, N(10041), (10, 0)), new(0)),
                Do(new(ScrCatalogue.Action.LoopEnd)), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 708 (ARC-UI-049): a book for the PC (the triggerer) and a plaque for the gate (not a PC).
            Write("scr/00708read.scr", Program(Do(new(ScrCatalogue.Action.ShowBook, N(1020), (Triggerer, 0))),
                Do(new(ScrCatalogue.Action.ShowPlaque, N(1500), (Attachee, 0))), Do(Skip)));
            // 709..712 (ARC-COMBAT-011): header flags 0x1 (non-magical trap), 0x2 (magical trap), 0x4 (removes itself, no trap), both trap bits.
            foreach (var (number, flags) in new[] { (709, 0x1u), (710, 0x2u), (711, 0x4u), (712, 0x3u) })
            {
                var trap = Program(Do(Skip));
                BinaryPrimitives.WriteUInt32LittleEndian(trap.AsSpan(48), flags);
                Write($"scr/00{number}trap.scr", trap);
            }
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent? content, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [Inventory()], content);

    [TestMethod]
    public void A_use_script_is_a_trap_by_its_header_flags()
    {
        // ARC-COMBAT-011 (0x4BBDD0, OD6): the point-1 script as the session holds it, flags 0x1 or 0x2; no script, a script the
        // content lacks and other flags are none; without content the answer is unknown.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var host = Host(session, content, Gate(709), Row(1, 3, 710, 3001), Row(2, 3, 711, 3002), Row(3, 3, 0, 3003), Row(6, 3, 799, 3006));
            Assert.AreEqual(true, host.IsTrap(Gate(709)));
            Assert.AreEqual(true, host.IsTrap(Row(1, 3, 710, 3001)));
            Assert.AreEqual(false, host.IsTrap(Row(2, 3, 711, 3002)));
            Assert.AreEqual(false, host.IsTrap(Row(3, 3, 0, 3003)), "no use script");
            Assert.AreEqual(false, host.IsTrap(Row(6, 3, 799, 3006)), "a script the content does not hold");
            Assert.AreEqual(false, host.IsTrap(Gate(700)), "a use script without trap flags");
            Assert.AreEqual(true, host.IsTrap(Row(7, 3, 712, 3007)), "both trap bits");
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new SessionMapId(1),
                MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.ScriptProperty), 1L << 40).Disposition);
            Assert.AreEqual(false, host.IsTrap(Row(1, 3, 710, 3001)), "a stored number no script can have");
            // The session's script replaces the loaded one (a use script that removed itself, or another written back).
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new SessionMapId(1),
                MapChangeKey.ForEntity(Entity(0), AreaUseScriptHost.ScriptProperty), 0).Disposition);
            Assert.AreEqual(false, host.IsTrap(Gate(709)));
            Assert.IsNull(Host(session, null, Gate(709)).IsTrap(Gate(709)), "no script content");
        });
    }

    [TestMethod]
    public void A_run_that_teleports_hands_over_all_its_printed_lines_with_their_types_in_order()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var gate = Gate(760);
            var ran = Host(session, content, gate).Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, ran.Outcome, ran.Detail);
            CollectionAssert.AreEqual(new[] { new AreaPrintedLine(6, "First line."), new AreaPrintedLine(4, "Second line.") }, ran.Printed.ToArray());
            CollectionAssert.AreEqual(new[] { "First line.", "Second line." }, ran.Lines.ToArray());
            Assert.AreEqual(2, AreaScriptMessages.Entries(ran.Printed).Count);
        });
    }

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
            // ARC-DEV-309 (B21): the line carries the entry type the script named (6, a transient text of the message window, IS2, PW1, ML2).
            CollectionAssert.AreEqual(new[] { new AreaPrintedLine(6, "It is locked.") }, locked.Printed.ToArray());
            CollectionAssert.AreEqual(new[] { AreaMessageEntry.Text(AreaMessageKind.Hover, "It is locked.") }, AreaScriptMessages.Entries(locked.Printed).ToArray());
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
            Assert.AreEqual(0, open.Printed.Count);
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
    // ARC-WORLD-081: an open map for the create action: every step stays on the map and every path is the straight walk.
    private sealed class OpenMap : Arcanum.Simulation.Items.IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public Arcanum.Simulation.Items.DropPath Path(CellCoord from, CellCoord to, Arcanum.Simulation.Items.DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to; at = Step(at, steps[^1])!.Value) steps.Add(Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y))));
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    // ARC-WORLD-081: a sector script creates the generator's item next to the marker in the PC's vicinity, up to the Steamwoven limit.
    [TestMethod]
    public void A_sector_script_creates_items_next_to_the_marker_in_the_pcs_vicinity_up_to_the_limit()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // The marker (scenery named 1673) at (12, 20); the same marker 11 tiles further is outside the vicinity; a busted one does not count.
            var marker = Row(2, 3, 0, 1673);
            var far = Row(Key(13), 3, 0, 1673) with { WorldX = 23 };
            var busted = Row(3, 3, 0, 1673);
            var herb = new AreaPrototypeItem(new(SessionItemType.Food, 10061, 7001, 10061), 0x1234, new(1));
            var world = new AreaScriptWorld(() => new CellCoord(12, 22), row => row.Key == busted.Key, number => number == 10041 ? herb : null, () => new OpenMap());
            var host = new AreaUseScriptHost(session, 1, [Gate(0), marker, far, busted], [Inventory()], content, world);
            Assert.AreEqual(2, host.SceneryInVicinity(host.Pc).Count, "the gate and the marker; not the far or the busted one");
            var sector = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry, new(0, 0), -1, new(0, 0), 0, 0, 730);
            for (var entry = 0; entry < Arcanum.Simulation.Items.CreatedItemRules.Limit + 1; entry++)
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunPlaced(sector, session.Rules.Scripts).Outcome);
            var created = host.TakeCreations();
            Assert.AreEqual(Arcanum.Simulation.Items.CreatedItemRules.Limit + 1, created.Count);
            Assert.IsTrue(created.Take(Arcanum.Simulation.Items.CreatedItemRules.Limit).All(c => c.Result.Disposition == GameSessionDisposition.Changed && c.Near.Code == AreaUseScriptHost.EntityId(marker)));
            Assert.AreEqual(GameSessionDisposition.NoChange, created[^1].Result.Disposition, "the patch is full under Steamwoven");
            var placed = session.QueryPlacedItems();
            Assert.IsTrue(placed.All(p => p.Source == SessionPlacedSource.Created && p.Kind == herb.Kind && Arcanum.Simulation.Items.CreatedItemRules.Near(new(12, 20), p.Cell)));
            // The session's rule mode decides the limit, not the run's script rules: the original's mode adds one more.
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunPlaced(sector, GameRules.Original.Scripts).Outcome);
            Assert.AreEqual(Arcanum.Simulation.Items.CreatedItemRules.Limit, session.QueryPlacedItems().Count);
            session.SelectRules(RuleMode.Original);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunPlaced(sector, session.Rules.Scripts).Outcome);
            Assert.AreEqual(Arcanum.Simulation.Items.CreatedItemRules.Limit + 1, session.QueryPlacedItems().Count);
            // A prototype that is no item leaves the whole run unapplied.
            var noWorld = new AreaUseScriptHost(session, 1, [marker], [Inventory()], content,
                new AreaScriptWorld(() => new CellCoord(12, 22), _ => false, _ => null, () => new OpenMap()));
            var refused = noWorld.RunPlaced(sector, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
            StringAssert.Contains(refused.Detail, "no item prototype");
        });
    }

    // ARC-DEV-205 (bug B22): a sector script's unfog town map is admitted and reveals the town map in the session; a number the session cannot keep is
    // skipped, as the original skips a town map it holds no info for, and the run's other effects apply.
    [TestMethod]
    public void A_sector_scripts_unfog_town_map_reveals_the_town_map_in_the_session()
    {
        Assert.IsTrue(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.UnfogTownMap));
        Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.Actions.Contains(ScrCatalogue.Action.UnfogTownMap));
        Assert.IsFalse(AreaScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.UnfogTownMap), "no dialog script of the installed data carries it");
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var host = Host(session, content);
            var sector = new Arcanum.Data.Maps.WorldSectorCoordinate(3, 4);
            var entered = host.RunPlaced(new(Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry, sector, -1, new(192, 256), 0, 0, 773), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, entered.Outcome, entered.Detail);
            CollectionAssert.AreEqual(new[] { 9, 9 }, entered.Run!.Effects.TownMaps.ToArray());
            CollectionAssert.AreEqual(new[] { 9 }, session.QueryRevealedTownMaps().ToArray(), "revealed once");
            Assert.IsTrue(host.GlobalFlag(46));

            var skipped = host.RunPlaced(new(Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry, sector, -1, new(192, 256), 0, 0, 774), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, skipped.Outcome, skipped.Detail);
            CollectionAssert.AreEqual(new[] { 9 }, session.QueryRevealedTownMaps().ToArray());
            Assert.IsTrue(host.GlobalFlag(47), "the run's other effects apply");
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

    // ARC-SCRIPT-046: a tile script runs for an NPC that entered its tile as well (ledger TR3), and a conversation it or a script it calls
    // asks for is handed to the caller with the run's effects applied.
    [TestMethod]
    public void A_tile_script_runs_for_an_npc_and_hands_over_the_conversation_its_callee_asks_for()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var tile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(3, 4), 72, new(198, 257), 0, 0, 740);
            var host = Host(session, content, Gate(0));
            var npc = new ScrObjectRef(1, Entity(9));

            var ran = host.RunPlaced(tile, session.Rules.Scripts, null, npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(new ScrDialogStart(host.Pc, npc, 741, 0, 3) { Point = ScrPoint.Use }, ran.Dialog);
            Assert.AreEqual(new ScrSlot(0, 0b10, 0), host.Slot(npc, ScrPoint.Use), "the NPC's use slot took 741's flags before the conversation (0x447282)");
            Assert.AreEqual(ScrStop.ReturnSkipDefault, ran.Run!.Stop, "the tile script went on after its call");

            // The PC is not named 9000: nothing; a scenery triggerer and a sector script for an NPC are not runs of the original.
            Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, (ScrDialogStart?)null), (host.RunPlaced(tile, session.Rules.Scripts).Outcome, host.RunPlaced(tile, session.Rules.Scripts).Dialog));
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, host.RunPlaced(tile, session.Rules.Scripts, null, new ScrObjectRef(1, AreaUseScriptHost.EntityId(Gate(0)))).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, host.RunPlaced(tile, session.Rules.Scripts, null, new ScrObjectRef(2, Entity(9))).Outcome);
            var sector = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry, new(3, 4), -1, new(192, 256), 0, 0, 707);
            Assert.ThrowsExactly<ArgumentException>(() => host.RunPlaced(sector, session.Rules.Scripts, null, npc));
            // A tile script's own dialog has no NPC to talk to (a tile has no attachee): the run applies nothing and counts as refused, so a
            // tile's unconditional teleport still fires.
            var alone = host.RunPlaced(tile with { Tile = 73, Script = 741 }, session.Rules.Scripts);
            Assert.AreEqual((AreaScriptedUseOutcome.Refused, (ScrDialogStart?)null), (alone.Outcome, alone.Dialog), alone.Detail);
            StringAssert.Contains(alone.Detail, "no NPC");

            // An NPC with no loaded use element cannot keep the stored slot: the run is not applied, before anything applies.
            var bare = Row(10, 16, 0, 9000);
            bare = bare with { EffectiveFields = bare.EffectiveFields.Where(f => f.Slot != 31).ToArray() };
            var other = Host(session, content, Gate(0), bare);
            var held = session.Version;
            var unkept = other.RunPlaced(tile, session.Rules.Scripts, null, new ScrObjectRef(1, AreaUseScriptHost.EntityId(bare)));
            Assert.AreEqual((AreaScriptedUseOutcome.EffectsRejected, (ScrDialogStart?)null), (unkept.Outcome, unkept.Dialog), unkept.Detail);
            StringAssert.Contains(unkept.Detail, "point 1 slot"); StringAssert.Contains(unkept.Detail, "no loaded element");
            Assert.AreEqual(held, session.Version);
        });
    }

    // ARC-SCRIPT-052 (TD1–TD3): a tile's stop attacking and destroy of the NPC who steps on it; the destroyed NPC leaves the map's objects.
    [TestMethod]
    public void A_tile_stops_the_npc_who_steps_on_it_attacking_and_destroys_him()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var tile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(3, 4), 72, new(198, 257), 0, 0, 750);
            var npcRow = Row(9, 16, 0, 9000);
            var stops = new List<SessionPeaceStep>();
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, Stopped: stops.Add);
            var host = new AreaUseScriptHost(session, 1, [Gate(0)], [Inventory()], content, world);
            var npc = new ScrObjectRef(1, Entity(9));
            var targets = new AreaObjectTargets(1, [npcRow], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null);

            // The PC is not named 9000: nothing.
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunPlaced(tile, session.Rules.Scripts).Outcome);
            Assert.IsFalse(AreaUseScriptHost.Destroyed(session, 1, Entity(9)));
            var ran = host.RunPlaced(tile, session.Rules.Scripts, null, npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            CollectionAssert.AreEqual(new[] { Entity(9) }, stops.Select(s => s.Entity).ToArray(), "the stop's host part ran for the NPC");
            Assert.IsTrue(AreaUseScriptHost.Destroyed(session, 1, Entity(9)));
            CollectionAssert.AreEqual(new[] { npcRow.Key }, targets.Gone(session).ToArray(), "the view no longer draws or reaches him");
            // TD2: an NPC already destroyed is left as it is.
            var version = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunPlaced(tile with { Tile = 74, Script = 751 }, session.Rules.Scripts, null, npc).Outcome);
            Assert.AreEqual(version, session.Version);

            // The PC is not destroyed (not modelled), nor an NPC with a destroy-point script (the destroy event is not run): nothing applies.
            var held = session.Version;
            var pc = host.RunPlaced(tile with { Tile = 75, Script = 751 }, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, pc.Outcome, pc.Detail);
            var scripted = Row(10, 16, 0, 9000);
            scripted = scripted with { EffectiveFields = scripted.EffectiveFields.Select(f => f.Slot != 31 ? f
                : f with { Elements = [.. f.Elements, new AreaSparseElement(ScrPoint.Destroy, [0, 0, 760])] }).ToArray() };
            var guarded = new AreaUseScriptHost(session, 1, [Gate(0), scripted], [Inventory()], content, world)
                .RunPlaced(tile with { Tile = 76 }, session.Rules.Scripts, null, new ScrObjectRef(1, Entity(10)));
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, guarded.Outcome, guarded.Detail);
            StringAssert.Contains(guarded.Detail, "destroy script 760");
            Assert.AreEqual(held, session.Version);
            Assert.IsFalse(AreaUseScriptHost.Destroyed(session, 1, Entity(10)));
        });
    }

    // ARC-DEV-157 (B23, GS1–GS3): a tile script's get stat is the session hero's stat by the original's number (the session tests hold the values); with
    // no hero, for an NPC and for a stat the session does not hold the dispatch ends as not modelled, reported as refused, with nothing applied.
    [TestMethod]
    public void A_tile_script_that_reads_a_stat_the_host_cannot_answer_is_refused_as_not_modelled_and_applies_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var tile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(3, 4), 72, new(198, 257), 0, 0, 770);
            var npcRow = Row(9, 16, 0, 9000);
            var host = new AreaUseScriptHost(session, 1, [Gate(0), npcRow], [Inventory()], content);
            var version = session.Version;
            var noHero = host.RunPlaced(tile, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, noHero.Outcome, noHero.Detail);
            Assert.AreEqual(ScrStop.NotModelled, noHero.Run!.Stop, "a session without a hero holds no stats");
            StringAssert.Contains(noHero.Detail, "NotModelled");
            var unknown = host.RunPlaced(tile with { Script = 771 }, session.Rules.Scripts);
            Assert.AreEqual(ScrStop.NotModelled, unknown.Run!.Stop);
            Assert.IsFalse(host.GlobalFlag(43), "the line after the read did not run");
            var byNpc = host.RunPlaced(tile with { Tile = 73 }, session.Rules.Scripts, null, new ScrObjectRef(1, Entity(9)));
            Assert.AreEqual(ScrStop.NotModelled, byNpc.Run!.Stop, "an NPC's stats are not read here");
            Assert.AreEqual(version, session.Version, "nothing applied");
            Assert.IsNull(host.EffectiveStat(new ScrObjectRef(1, Entity(9)), 5));
            Assert.IsNull(host.EffectiveStat(host.Pc, 8));
            Assert.IsFalse(host.IsDumbByBackground(new ScrObjectRef(1, Entity(9))));
            Assert.IsFalse(host.IsDumbByBackground(host.Pc), "no hero, no dumb background");
        });
    }

    // ARC-DEV-157 (B20, TG4): a tile script may loop over everyone in the vicinity; a tile has no attachee, so the list is empty (PG3) and the loop body does not run.
    [TestMethod]
    public void A_tile_script_loops_over_the_vicinity_of_no_attachee_as_an_empty_list()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var tile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(3, 4), 72, new(198, 257), 0, 0, 772);
            Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.FocusKinds.Contains(ScrFocusKind.EveryoneInVicinity));
            Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.FocusKinds.Contains(ScrFocusKind.AnyoneInVicinity));
            Assert.IsFalse(AreaUseScriptHost.Admission.FocusKinds.Contains(ScrFocusKind.EveryoneInVicinity), "a use click's set is unchanged");
            Assert.IsTrue(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.GetStat));
            Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.Actions.Contains(ScrCatalogue.Action.GetStat));
            var host = Host(session, content, Gate(0));
            var ran = host.RunPlaced(tile, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsFalse(host.GlobalFlag(44), "the loop body did not run");
            Assert.IsTrue(host.GlobalFlag(45), "the lines after the loop end ran");
        });
    }

    // ARC-DEV-298 (B20): float line (23) is in the tile and sector set, not in a use click's: a tile script floats its line over the PC and the host hands it out once.
    [TestMethod]
    public void A_tile_script_floats_a_line_over_the_pc_and_hands_it_out_once()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var tile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(3, 4), 72, new(198, 257), 0, 0, 775);
            Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.Actions.Contains(ScrCatalogue.Action.FloatLine));
            Assert.IsFalse(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.FloatLine), "a use click's set is unchanged");
            var host = new AreaUseScriptHost(session, 1, [Gate(0)], [Inventory()], content, new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null));
            var ran = host.RunPlaced(tile, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(48), "the lines after the float ran");
            CollectionAssert.AreEqual(new[] { new AreaScriptFloat(host.Pc, "Help me.") }, host.TakeFloats().ToArray());
            Assert.AreEqual(0, host.TakeFloats().Count);
        });
    }

    // ARC-SCRIPT-052 (TD3): the PC's stop reaches its followers; a stop the session would refuse (an NPC fighting the hero without reaction
    // facts) and a destroy of an NPC destroyed in its map data change nothing.
    [TestMethod]
    public void A_tile_stop_reaches_the_pcs_followers_and_a_stop_or_destroy_that_would_not_apply_changes_nothing()
    {
        WithContent(content =>
        {
            var stopTile = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(3, 4), 72, new(198, 257), 0, 0, 752);
            var npc = new ScrObjectRef(1, Entity(9));
            var stops = new List<SessionPeaceStep>();
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, Stopped: stops.Add);
            using (var session = ConversationSessionFixture.Create())
            {
                Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, Entity(9)).Disposition);
                var ran = new AreaUseScriptHost(session, 1, [Gate(0)], [Inventory()], content, world).RunPlaced(stopTile, session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                CollectionAssert.AreEqual(new[] { Entity(9) }, stops.Select(s => s.Entity).ToArray(), "the PC's stop is each follower's");
            }
            stops.Clear();
            using (var session = ConversationSessionFixture.Create())
            {
                // Its focus is a follower of the PC, so the peace is with the hero's side.
                Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, Entity(4)).Disposition);
                var focus = session.SetDangerState(session.Version, new SessionDangerRequest { Entity = Entity(9), State = DangerState.CombatFocus, Target = SessionCombatant.Critter(Entity(4)) });
                Assert.AreNotEqual(GameSessionDisposition.Rejected, focus.Disposition, focus.Failure.ToString());
                var held = session.Version;
                var refused = new AreaUseScriptHost(session, 1, [Gate(0)], [Inventory()], content, world).RunPlaced(stopTile, session.Rules.Scripts, null, npc);
                Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
                StringAssert.Contains(refused.Detail, "reaction facts");
                Assert.AreEqual((held, 0), (session.Version, stops.Count));

                var gone = Row(10, 16, 0, 9000);
                gone = gone with { EffectiveFields = [.. gone.EffectiveFields, new AreaFieldValue(18, AreaPresence.Value, AreaValueOrigin.Instance, 0x1, [], null)] };
                var destroyTile = stopTile with { Script = 751 };
                var again = new AreaUseScriptHost(session, 1, [Gate(0), gone], [Inventory()], content, world).RunPlaced(destroyTile, session.Rules.Scripts, null, new ScrObjectRef(1, Entity(10)));
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, again.Outcome, again.Detail);
                Assert.AreEqual(held, session.Version, "TD2: one destroyed in its map data is left as it is");
            }
        });
    }

    [TestMethod]
    public void Story_actions_change_the_pc_s_story_in_the_session()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var gate = Gate(720);
            var host = Host(session, content, gate);
            var ran = host.Run(gate, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual((5, SessionQuestState.Accepted), (session.QueryStoryState(), session.QueryQuest(1016)!.Value.State));
            Assert.IsNull(session.QueryQuest(1017), "a request for the gate (not a PC) changes nothing");
            CollectionAssert.AreEqual(new[] { 44 }, session.QueryKnownAreas().ToArray());
            Assert.IsNotNull(session.QueryRumor(1100));
            Assert.IsTrue(session.HeroHasReputation(1005));
            Assert.AreEqual(5, host.StoryState());
        });
    }

    // ARC-SCRIPT-020: a held item's use script runs on its origin map's inventory row; its outcome decides the use.
    [TestMethod]
    public void An_item_s_use_script_runs_with_the_pc_as_the_user_and_its_outcome_decides()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var items = new AreaInventoryMap(1, new([], [new SessionSeedItem(Entity(6), Facts(60), new CellCoord(16, 20)), new SessionSeedItem(Entity(7), Facts(61), new CellCoord(17, 20))]),
                new[] { Row(6, 13, 721, 60), Row(7, 13, 722, 61) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
            // Map 2 is shown: the item taken on map 1 runs on map 1's inventory row.
            var host = new AreaUseScriptHost(session, 2, [], [items], content);
            var go = host.RunItemUse(1, Entity(6), session.Rules.Scripts);
            Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.RunDefault), (go.Outcome, go.Run!.Outcome), go.Detail);
            Assert.IsTrue(host.GlobalFlag(31));
            var stop = host.RunItemUse(1, Entity(7), session.Rules.Scripts);
            Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.SkipDefault), (stop.Outcome, stop.Run!.Outcome));
            Assert.IsTrue(host.GlobalFlag(32), "the effects apply either way, as the original's run does");
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, host.RunItemUse(2, Entity(6), session.Rules.Scripts).Outcome, "not a row of a loaded map");
        });
    }

    // ARC-DEV-316 (B32 tail): an item taken on another map runs its use script on the row of its home map, which the host reads through
    // OtherMapRow (the viewer wires the inventory host's resolver); without it the item is not a row of a loaded map.
    [TestMethod]
    public void An_item_taken_on_a_map_the_host_was_not_given_runs_its_use_script_through_the_other_map_row()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var home = new AreaInventoryMap(1, new([], [new SessionSeedItem(Entity(6), Facts(60), new CellCoord(16, 20))]),
                new[] { Row(6, 13, 721, 60) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
            var asked = new List<ScrObjectRef>();
            var host = new AreaUseScriptHost(session, 2, [], [], content)
            {
                OtherMapRow = obj => { asked.Add(obj); return obj.Map == 1 && home.TryGetRow(obj.Code, out var row) ? row : null; },
            };
            var go = host.RunItemUse(1, Entity(6), session.Rules.Scripts);
            Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.RunDefault), (go.Outcome, go.Run!.Outcome), go.Detail);
            Assert.IsTrue(host.GlobalFlag(31), "the script's effects apply");
            Assert.IsTrue(asked.Count > 0 && asked.All(obj => obj == new ScrObjectRef(1, Entity(6))), "only the running item is asked, on its origin map");
            Assert.IsNull(host.IsDead(new ScrObjectRef(1, Entity(6))), "outside an item's use run an object of an unheld map has no row");
            Assert.IsTrue(asked.All(obj => obj == new ScrObjectRef(1, Entity(6))), "no row is asked outside the item's run");
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, host.RunItemUse(3, Entity(6), session.Rules.Scripts).Outcome, "a map the answer does not know");
            var without = new AreaUseScriptHost(session, 2, [], [], content);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, without.RunItemUse(1, Entity(6), session.Rules.Scripts).Outcome, "no answer: not a row of a loaded map");
        });
    }
}
