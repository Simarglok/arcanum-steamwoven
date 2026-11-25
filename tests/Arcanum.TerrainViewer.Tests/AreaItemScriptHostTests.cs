using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-013: the viewer's item script host over an invented content stack and map (no original data):
/// slots from the area rows, printed lines and their refusals, and the inventory host's transfers with and without it.</summary>
[TestClass]
public sealed class AreaItemScriptHostTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    // An item row whose script array holds script on the insert-item point (21) and words that are not a slot on point 0.
    private static AreaObjectRecord Row(int index, int type, long x, long y, int script) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], [new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null,
                [new AreaSparseElement(0, [1, 2]), new AreaSparseElement(SessionScriptPoint.InsertItem, [4, 0, script])], null)], true, true, true);

    private static SessionItemFacts Facts(int name) => new(new(SessionItemType.Armor, 4000 + name, name, 100 + name), 5, 0, 0, null, [SessionScriptPoint.InsertItem]);

    // Four ground items with insert-item scripts 500 (plain line), 501 (a line naming the NPC), 502 (no DLG), 503 (a line
    // with an action field), and a dead body.
    private static AreaInventoryMap Map() => new(1,
        new([new(Entity(9), SessionHolderKind.Npc, true, false, [], [])],
            [.. Enumerable.Range(0, 4).Select(i => new SessionSeedItem(Entity(i), Facts(10 + i), new CellCoord(10 + i, 20)))]),
        new[] { Row(0, 7, 10, 20, 500), Row(1, 7, 11, 20, 501), Row(2, 7, 12, 20, 502), Row(3, 7, 13, 20, 503), Row(9, 16, 14, 20, 0) }
            .ToDictionary(row => Entity(row.Key.SourceIndex)));

    private static byte[] PrintAndSkip()
    {
        var bytes = new byte[64 + 2 * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 2);
        var print = bytes.AsSpan(64 + 44, 44);
        BinaryPrimitives.WriteInt32LittleEndian(print, ScrCatalogue.Action.PrintLine);
        print[4] = 3; print[5] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(print[12..], 1); BinaryPrimitives.WriteInt32LittleEndian(print[16..], 6);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 132 + 44), ScrCatalogue.Action.ReturnSkipDefault);
        return bytes;
    }

    // ARC-DEV-366: a tech book's insert script in short: get stat 4 (intelligence) of the triggerer into a local, then add effect 168 with the item cause.
    private static byte[] TomeScript()
    {
        (int Type, (byte Kind, int Value)[] Operands)[] lines =
        [
            (ScrCatalogue.Action.GetStat, [(3, 4), (0, 0), (2, 0)]),
            (ScrCatalogue.Action.AddEffect, [(0, 0), (3, TomeEffect), (3, 5)]),
            (ScrCatalogue.Action.ReturnRunDefault, []),
        ];
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var slot = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(slot, lines[line].Type);
            for (var i = 0; i < lines[line].Operands.Length; i++)
            {
                slot[4 + i] = lines[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], lines[line].Operands[i].Value);
            }
        }
        return bytes;
    }

    private const int TomeEffect = 168;

    // The tome lies on the ground with the insert-item script 510.
    private static AreaInventoryMap TomeMap() => new(1,
        new([], [new SessionSeedItem(Entity(0), Facts(10), new CellCoord(10, 20))]),
        new Dictionary<string, AreaObjectRecord> { [Entity(0)] = Row(0, 7, 10, 20, 510) });

    // "+1 expertise in a discipline" for the one number the tables hold.
    private static EffectRule? Tables(int number) => number == TomeEffect
        ? new(TomeEffect, true, [new(EffectAttributeKind.TechDiscipline, 3, EffectChangeOperator.Add, 1)]) : null;

    private static T WithContent<T>(Func<ScriptContent, T> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-013-");
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
            foreach (var name in new[] { "00500plain", "00501named", "00502silent", "00503acting", "00504same", "00505touch" }) Write($"scr/{name}.scr", PrintAndSkip());
            Write("scr/00510tome.scr", TomeScript());
            Write("dlg/00500plain.dlg", Encoding.Latin1.GetBytes("{1}{It hums for @pcname@.}{It hums softly.}{}{}{}{}"));
            Write("dlg/00501named.dlg", Encoding.Latin1.GetBytes("{1}{@npcname@ hums.}{}{}{}{}{}"));
            Write("dlg/00505touch.dlg", Encoding.Latin1.GetBytes("{1}{@pcname@ touches @npcname@.}{@pcname@ touches @npcname@.}{}{}{}{}"));
            Write("dlg/00504same.dlg", Encoding.Latin1.GetBytes("{1}{It hums.}{It hums.}{}{}{}{}"));
            Write("dlg/00503acting.dlg", Encoding.Latin1.GetBytes("{1}{It hums.}{}{}{}{}{gf 5 1}"));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            return test(content);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void A_printed_line_names_the_hero_by_its_player_name()
    {
        // ARC-SCRIPT-2787 (0x416B00): @pcname@ is the hero's player name; a line naming the NPC stays unsupported on an item run.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetHeroPlayerName(session.Version, System.Text.Encoding.Latin1.GetBytes("Zed")).Disposition);
            var host = new AreaItemScriptHost(session, [Map()], content);
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "It hums for Zed."), Pair(host.PrintedLine(500, 1)));
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLine(501, 1).Status);
            return 0;
        });
    }

    [TestMethod]
    public void A_printed_line_names_an_item_attachee_and_an_npc_triggerer_through_the_shown_maps_use_host()
    {
        // ARC-SCRIPT-2811 (0x44B390 → 0x416B00, ledger NM3, NM4): @npcname@ is the attachee item's shown name (its own observer), @pcname@ with an NPC triggerer that NPC's not-yet-met description (no observer); a name the host cannot read is refused.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetHeroPlayerName(session.Version, System.Text.Encoding.Latin1.GetBytes("Zed")).Disposition);
            var npcRow = Row(9, 16, 14, 20, 0);
            var shown = new AreaUseScriptHost(session, 1, [npcRow], [], content) { ObjectName = (row, recognition, marked) => $"[{row.Type}:{recognition}:{marked}]" };
            var host = new AreaItemScriptHost(session, [Map()], content, null, () => shown);
            var pc = new ScrObjectRef(1, AreaScriptHost.PcCode);
            var item = new ScrObjectRef(1, Entity(0));
            var npc = new ScrObjectRef(1, AreaUseScriptHost.EntityId(npcRow));
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "Zed touches [7:Recognized:False]."), Pair(host.PrintedLineOf(505, 1, pc, item, npcTriggerer: false)), "the hero triggers: its player name; the item: its description");
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "[16:NotRecognized:False] touches [7:Recognized:False]."), Pair(host.PrintedLineOf(505, 1, npc, item, npcTriggerer: true)), "an NPC triggers: its unmet description");
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "Zed touches Zed."), Pair(host.PrintedLineOf(505, 1, pc, pc, npcTriggerer: false)), "the hero as attachee: its player name");
            // A triggerer the use host has no row of, an attachee no loaded row names (a made item), and a host without a name source are refused.
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineOf(505, 1, new ScrObjectRef(1, Entity(5)), item, npcTriggerer: true).Status);
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineOf(505, 1, pc, new ScrObjectRef(1, "made:1"), npcTriggerer: false).Status);
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, new AreaItemScriptHost(session, [Map()], content).PrintedLineOf(505, 1, pc, item, npcTriggerer: false).Status);
            shown.ObjectName = null;
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineOf(505, 1, pc, item, npcTriggerer: false).Status);
            // Without the run's objects the old readings stand: a line naming either is refused.
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLine(505, 1).Status);
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineForNpc(505, 1).Status);
            return 0;
        });
    }

    [TestMethod]
    public void The_host_reads_slots_from_rows_and_prints_only_plain_lines()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var host = new AreaItemScriptHost(session, [Map()], content);
            Assert.AreEqual(new ScrSlot(500, 4, 0), host.Slot(new(1, Entity(0)), SessionScriptPoint.InsertItem));
            Assert.IsNull(host.Slot(new(1, Entity(0)), 0), "an element without three words is no slot");
            Assert.IsNull(host.Slot(new(2, Entity(0)), SessionScriptPoint.InsertItem), "another map");
            Assert.AreEqual(ScrScriptStatus.Found, host.Script(500).Status);
            Assert.AreEqual(ScrScriptStatus.Missing, host.Script(600).Status);
            Assert.IsFalse(host.GlobalFlag(5));
            Assert.AreEqual(new ScrObjectRef(session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode), host.Player());
            Assert.IsNull(host.Leader(new(1, Entity(9))));

            // The diagnostic PC is male: the text, with the diagnostic PC name.
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "It hums for [PC]."), Pair(host.PrintedLine(500, 1)));
            Assert.AreEqual((SessionPrintedLineStatus.Shown, " "), Pair(host.PrintedLine(500, 7)), "a missing line prints one space");
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLine(501, 1).Status, "whose name @npcname@ is on an item run is not read");
            Assert.AreEqual(SessionPrintedLineStatus.NoDialog, host.PrintedLine(502, 1).Status);
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLine(503, 1).Status, "the original would run the action field");
            Assert.AreEqual(SessionPrintedLineStatus.NoDialog, new AreaItemScriptHost(session, [Map()], null).PrintedLine(500, 1).Status);

            // ARC-AI-1655 (IS2): the line of a run an NPC triggers is the text or the alternate text by the NPC's gender, which the host does not read: a line whose two texts agree is shown, one that differs is not.
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "It hums."), Pair(host.PrintedLineForNpc(504, 1)));
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineForNpc(500, 1).Status, "text and alternate text differ");
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineForNpc(501, 1).Status, "@npcname@ is not read");
            Assert.AreEqual(SessionPrintedLineStatus.NoDialog, host.PrintedLineForNpc(502, 1).Status);
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLineForNpc(503, 1).Status, "the action field");
            Assert.AreEqual((SessionPrintedLineStatus.Shown, " "), Pair(host.PrintedLineForNpc(504, 7)), "a missing line prints one space");
            return 0;
        });
    }

    private static (SessionPrintedLineStatus, string) Pair(SessionPrintedLine line) => (line.Status, line.Text);

    [TestMethod]
    public void The_inventory_host_runs_insert_scripts_only_with_content()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var bare = new AreaInventoryHost([Map()]);
            Assert.AreEqual(GameSessionFailure.None, bare.Attach(session));
            Assert.IsNull(bare.ItemScripts(session));
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.PickUp(session, Entity(0)).Failure);

            var host = new AreaInventoryHost([Map()], scripts: content);
            var plain = host.PickUp(session, Entity(0));
            Assert.AreEqual((GameSessionDisposition.Changed, " — It hums for [PC]."), (plain.Disposition, AreaInventoryHost.Printed(plain)));
            Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(2)).Disposition, "no DLG: taken, nothing printed");
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, host.PickUp(session, Entity(1)).Failure);
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, host.PickUp(session, Entity(3)).Failure);
            Assert.AreEqual(string.Empty, AreaInventoryHost.Printed(default));
            CollectionAssert.AreEqual(new[] { Entity(0), Entity(2) }, session.QueryPcInventory().Select(item => item.Entity).ToArray());
            return 0;
        });
    }

    [TestMethod]
    public void The_host_answers_an_effect_rule_only_from_its_tables()
    {
        using var session = ConversationSessionFixture.Create();
        Assert.IsNull(new AreaItemScriptHost(session, [Map()], null).EffectDefinition(TomeEffect), "no tables, no rule");
        var host = new AreaItemScriptHost(session, [Map()], null, Tables);
        var rule = host.EffectDefinition(TomeEffect)!;
        Assert.AreEqual(new SessionEffectDefinition(TomeEffect, [new(EffectAttributeKind.TechDiscipline, 3, EffectChangeOperator.Add, 1)]).Changes.Length, rule.Changes.Length);
        Assert.IsTrue(rule.SameAs(new SessionEffectDefinition(TomeEffect, [new(EffectAttributeKind.TechDiscipline, 3, EffectChangeOperator.Add, 1)])));
        Assert.IsNull(host.EffectDefinition(169), "a number the tables do not hold");
        Assert.IsNull(new AreaItemScriptHost(session, [Map()], null, number => throw new ArgumentException("outside the tables")).EffectDefinition(5), "a number the tables refuse");
    }

    // ARC-SCRIPT-2375 (IV30): the name number of an item row is the map data's until a script's rename wrote the item's own field; the host's data read ignores the record, the name readers do not.
    [TestMethod]
    public void An_items_data_name_and_its_renamed_number_are_read_apart()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaItemScriptHost(session, [Map()], null);
        var item = new ScrObjectRef(1, Entity(0));
        Assert.AreEqual(0, host.ItemNameInData(item), "the row holds no name field");
        Assert.IsNull(host.ItemNameInData(new ScrObjectRef(1, "made:1")), "a made item has no row");
        Assert.IsNull(host.ItemNameInData(new ScrObjectRef(1, Entity(7))), "no row at all");
        var row = Row(0, 7, 10, 20, 500);
        Assert.AreEqual(0, AreaUseScriptHost.NameOf(session, row));
        var written = session.ApplyChanges(session.Version, null, [new(new SessionMapId(row.Key.RuntimeMap), SessionItemName.Key(Entity(0)), 2805)]);
        Assert.AreNotEqual(GameSessionDisposition.Rejected, written.Disposition);
        Assert.AreEqual(2805, AreaUseScriptHost.NameOf(session, row), "the name readers follow the rename");
        Assert.AreEqual(0, host.ItemNameInData(item), "the data read leaves the record out");
        // A row that is not an item keeps its own number whatever a record says.
        var other = Row(9, 16, 14, 20, 0);
        Assert.AreEqual(0, AreaUseScriptHost.NameOf(session, other));
    }

    // ARC-SCRIPT-2432 (IV40): a critter's attachment slot in a point 21 run is the shown map's use host's reading, and a change script of it is planned as the use host's own map changes (a part back at its loaded value clears its record).
    [TestMethod]
    public void A_change_script_of_a_critter_is_planned_over_the_use_hosts_slot_and_any_other_object_is_not_kept()
    {
        using var session = ConversationSessionFixture.Create();
        const int Loaded = 700, Changed = 1358;
        AreaObjectRecord WithDialog(int index, int type) =>
            new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
                [], [new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [5, 6, Loaded])], null)], true, true, true);
        AreaObjectRecord critter = WithDialog(1, 16), scenery = WithDialog(2, 4), noElement = Row(3, 16, 13, 20, 0);
        var shown = new AreaUseScriptHost(session, 1, [critter, scenery, noElement], [], null);
        var host = new AreaItemScriptHost(session, [Map()], null, null, () => shown);
        var npc = new ScrObjectRef(1, Entity(1));
        Assert.AreEqual(new ScrSlot(Loaded, 5, 6), host.Slot(npc, ScrPoint.Dialog));
        var edits = host.PlanObjectSlot(new(ScrSource.Object, npc, ScrPoint.Dialog, new ScrSlot(Changed, 5, 6)));
        Assert.IsNotNull(edits);
        Assert.AreEqual(3, edits.Count);
        Assert.IsTrue(edits.All(edit => edit.Key.Entity == npc.Code));
        CollectionAssert.AreEqual(new long?[] { Changed, null, null }, edits.Select(edit => edit.Value).ToArray(), "only the number differs from the loaded element");
        Assert.IsTrue(shown.IsCritterRow(npc));
        // Applied, the host reads the changed number and the held flags and counters.
        Assert.AreNotEqual(GameSessionDisposition.Rejected, session.ApplyChanges(session.Version, null, edits).Disposition);
        Assert.AreEqual(new ScrSlot(Changed, 5, 6), host.Slot(npc, ScrPoint.Dialog));
        // A write back to the loaded number clears the record.
        var back = host.PlanObjectSlot(new(ScrSource.Object, npc, ScrPoint.Dialog, new ScrSlot(Loaded, 5, 6)))!;
        Assert.IsTrue(back.All(edit => edit.Value is null));
        // A scenery, a critter without the element, an unknown object, a point the host does not keep and a write that is not an object's are refused.
        Assert.IsNull(host.PlanObjectSlot(new(ScrSource.Object, new ScrObjectRef(1, Entity(2)), ScrPoint.Dialog, new ScrSlot(Changed, 0, 0))), "a scenery");
        Assert.IsNull(host.PlanObjectSlot(new(ScrSource.Object, new ScrObjectRef(1, Entity(3)), ScrPoint.Dialog, new ScrSlot(Changed, 0, 0))), "no loaded element");
        Assert.IsNull(host.PlanObjectSlot(new(ScrSource.Object, new ScrObjectRef(1, Entity(7)), ScrPoint.Dialog, new ScrSlot(Changed, 0, 0))), "no row");
        Assert.IsNull(host.PlanObjectSlot(new(ScrSource.Object, npc, ScrPoint.Count - 1, new ScrSlot(Changed, 0, 0))), "a point it does not keep");
        Assert.IsNull(new AreaItemScriptHost(session, [Map()], null).PlanObjectSlot(new(ScrSource.Object, npc, ScrPoint.Dialog, new ScrSlot(Changed, 0, 0))), "no shown map");
    }

    // ARC-SCRIPT-2336 (FK8): the follower list of a point 21 run comes from the shown map's use host; without one the host cannot answer and the session refuses the move.
    [TestMethod]
    public void The_host_has_no_follower_list_without_a_shown_map()
    {
        using var session = ConversationSessionFixture.Create();
        var leader = new ScrObjectRef(1, "pc");
        Assert.IsNull(new AreaItemScriptHost(session, [Map()], null).FollowersOf(leader), "no shown map host");
        Assert.IsNull(new AreaItemScriptHost(session, [Map()], null, null, () => null).FollowersOf(leader), "a shown map that is not there");
    }

    [TestMethod]
    public void An_item_script_that_reads_a_stat_and_adds_an_effect_runs_through_the_inventory_host()
    {
        WithContent(content =>
        {
            // Without the effect tables the add-effect action has no rule: the pickup is refused, as before (the host knows nothing of effect 168).
            using var refusing = ConversationSessionFixture.CreateWithHero();
            var bare = new AreaInventoryHost([TomeMap()], scripts: content);
            Assert.AreEqual(GameSessionFailure.None, bare.Attach(refusing));
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.PickUp(refusing, Entity(0)).Failure);

            // With them the run reads the hero's intelligence (get stat is admitted) and adds the effect with the item cause.
            using var session = ConversationSessionFixture.CreateWithHero();
            var host = new AreaInventoryHost([TomeMap()], scripts: content) { EffectRules = Tables };
            Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
            var picked = host.PickUp(session, Entity(0));
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
            CollectionAssert.AreEqual(new[] { new SessionEffect(TomeEffect, Arcanum.Simulation.Character.EffectCause.Item) }, session.QueryHeroEffects()!.Effects.ToArray());
            Assert.AreEqual(1, session.QueryHeroTechnology()!.Value[3].Expertise - session.QueryHeroTechnology()!.Value[2].Expertise);
            return 0;
        });
    }
}
