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

/// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO1–WO10): the world-object actions and conditions of set S37 through the use-script host over an invented content stack and map (no original data): set lock state and toggle open or
/// closed on a portal through the session's lock state, obj is open reading it, the blocked-sector list and the generators' block bits through the session, and create item into an NPC's inventory list. ARC-SCRIPT-1279 (ledger
/// WO14, WO16): transfer item, destroy item named, set written start and toggle item inventory display through the session's named-item move and destroy and its item field writes.</summary>
[TestClass]
public sealed class AreaWorldObjectScriptTests
{
    private const byte Attachee = 1, Number = 3, Player = 21;
    private const int PortalType = 1, NpcType = 16, SceneryType = 3, WrittenType = 13, GenericType = 14;
    private static readonly SessionItemKind Herb = new(SessionItemType.Food, 10061, 7001, 10061);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int type, int point, int script)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(point, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));
    private static (Clause, Clause, Clause) If(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);

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

    private static readonly Clause RunDefault = new(ScrCatalogue.Action.ReturnRunDefault), SkipDefault = new(ScrCatalogue.Action.ReturnSkipDefault);

    // Scripts 840 to 849: lock and unlock, toggle (and the later open test), toggle and test, the sector toggle, the generator toggle, the negative sector, create item and a created gold pile.
    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1164-");
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
            Write("scr/00840unlock.scr", Program(Do(new(ScrCatalogue.Action.SetLockState, (Attachee, -1), N(0))), Do(RunDefault)));
            Write("scr/00841lock.scr", Program(Do(new(ScrCatalogue.Action.SetLockState, (Attachee, -1), N(1))), Do(RunDefault)));
            Write("scr/00842toggle.scr", Program(Do(new(ScrCatalogue.Action.ToggleOpenClosed, (Attachee, -1))), Do(RunDefault)));
            Write("scr/00843open.scr", Program(If(new(ScrCatalogue.Condition.ObjIsOpen, (Attachee, -1)), SkipDefault, RunDefault)));
            Write("scr/00844sector.scr", Program(Do(new(ScrCatalogue.Action.ToggleSectorBlocked, N(12), N(34))),
                If(new(ScrCatalogue.Condition.SectorIsBlocked, N(12), N(34)), SkipDefault, RunDefault)));
            Write("scr/00845generator.scr", Program(Do(new(ScrCatalogue.Action.ToggleMonsterGenerator, N(7))),
                If(new(ScrCatalogue.Condition.MonsterGenDisabled, N(7)), SkipDefault, RunDefault)));
            Write("scr/00846negative.scr", Program(Do(new(ScrCatalogue.Action.ToggleSectorBlocked, N(-2), N(5))), Do(RunDefault)));
            Write("scr/00847item.scr", Program(Do(new(ScrCatalogue.Action.CreateItem, N(10041), (Attachee, -1))), Do(RunDefault)));
            Write("scr/00848gold.scr", Program(Do(new(ScrCatalogue.Action.CreateItem, N(10042), (Attachee, -1))), Do(RunDefault)));
            Write("scr/00849touch.scr", Program(Do(new(ScrCatalogue.Action.TouchArt, N(1234))), Do(RunDefault)));
            // ARC-SCRIPT-1279: destroy item named 7001 from the attachee, transfer item 7001 from the attachee to the player, set written start 9 and toggle item inventory display of the attachee, set written start of a second item.
            Write("scr/00850destroy.scr", Program(Do(new(ScrCatalogue.Action.DestroyItemNamed, N(7001), (Attachee, -1))), Do(RunDefault)));
            Write("scr/00851transfer.scr", Program(Do(new(ScrCatalogue.Action.TransferItem, N(7001), (Attachee, -1), (Player, -1))), Do(RunDefault)));
            Write("scr/00852written.scr", Program(Do(new(ScrCatalogue.Action.SetWrittenStart, (Attachee, -1), N(9))), Do(RunDefault)));
            Write("scr/00853display.scr", Program(Do(new(ScrCatalogue.Action.ToggleItemInventoryDisplay, (Attachee, -1))), Do(RunDefault)));
            Write("scr/00856transfertwice.scr", Program(Do(new(ScrCatalogue.Action.TransferItem, N(7001), (Attachee, -1), (Player, -1))), Do(new(ScrCatalogue.Action.TransferItem, N(7001), (Player, -1), (Attachee, -1))), Do(RunDefault)));
            Write("scr/00855destroytwice.scr", Program(Do(new(ScrCatalogue.Action.DestroyItemNamed, N(7001), (Attachee, -1))), Do(new(ScrCatalogue.Action.DestroyItemNamed, N(7001), (Attachee, -1))), Do(RunDefault)));
            Write("scr/00854twice.scr", Program(Do(new(ScrCatalogue.Action.ToggleItemInventoryDisplay, (Attachee, -1))), Do(new(ScrCatalogue.Action.ToggleItemInventoryDisplay, (Attachee, -1))), Do(RunDefault)));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaScriptWorld World(Func<ScrObjectRef, AreaScriptOpenable?>? openable = null) => new(() => null, _ => false, number => number switch
    {
        10041 => new AreaPrototypeItem(Herb, 0x1234, new(1)),
        10042 => new AreaPrototypeItem(new(SessionItemType.Gold, 10042, 7002, 10042), 0x1234, new(1)),
        _ => null,
    }, () => null, Openable: openable);

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, AreaScriptWorld? world, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, world ?? World());

    // A door of five frames in the map: the facts the viewer's portals give the host. The lock flags bit 1 is locked.
    private static Func<ScrObjectRef, AreaScriptOpenable?> Door(AreaObjectRecord row, int flags = 0) => obj => obj.Code == AreaUseScriptHost.EntityId(row)
        ? new(new(new(1), obj.Code), new(OpenableKind.Portal, flags, 0, 0), new PortalArt(true, 0, 5), 5) : null;

    private static AreaObjectRecord Gate(int script) => Row(1, PortalType, ScrPoint.Use, script);

    [TestMethod]
    public void The_lock_actions_write_the_sessions_lock_state_of_a_portal()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var door = Gate(840);
            var facts = Door(door, flags: LockRules.LockedBit)(new(1, AreaUseScriptHost.EntityId(door)))!;
            Assert.IsTrue(session.QueryOpenable(facts.Openable, facts.Baseline).Locked, "locked at T0");
            var unlocked = Host(session, content, World(Door(door, LockRules.LockedBit)), door).Run(door, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, unlocked.Outcome, unlocked.Detail);
            Assert.IsFalse(session.QueryOpenable(facts.Openable, facts.Baseline).Locked, "unlocked by the script");

            var again = Gate(841);
            var locked = Host(session, content, World(Door(again)), again).Run(again, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, locked.Outcome, locked.Detail);
            var lockedFacts = Door(again)(new(1, AreaUseScriptHost.EntityId(again)))!;
            Assert.IsTrue(session.QueryOpenable(lockedFacts.Openable, lockedFacts.Baseline).Locked, "locked by the script");
        });
    }

    [TestMethod]
    public void The_open_toggle_steps_a_portal_to_its_last_frame_and_back_and_obj_is_open_reads_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var door = Gate(842);
            var facts = Door(door)(new(1, AreaUseScriptHost.EntityId(door)))!;
            var world = World(Door(door));
            Assert.AreEqual(0, session.QueryOpenable(facts.Openable, facts.Baseline).Frame);
            var opened = Host(session, content, world, door).Run(door, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, opened.Outcome, opened.Detail);
            Assert.AreNotEqual(0, session.QueryOpenable(facts.Openable, facts.Baseline).Frame, "open");
            Assert.IsTrue(session.QueryOpenable(facts.Openable, facts.Baseline).Open);
            // obj is open: the condition script (the same door's entity, another script) skips the default for an open door only.
            var sameDoor = Row(1, PortalType, ScrPoint.Use, 843);
            var run = Host(session, content, World(Door(sameDoor)), sameDoor).Run(sameDoor, session.Rules.Scripts);
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Run!.Outcome, "open");
            var closed = Row(1, PortalType, ScrPoint.Use, 842);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, World(Door(closed)), closed).Run(closed, session.Rules.Scripts).Outcome);
            Assert.AreEqual(0, session.QueryOpenable(facts.Openable, facts.Baseline).Frame, "toggled back to closed");
            Assert.AreEqual(ScrOutcome.RunDefault, Host(session, content, World(Door(sameDoor)), sameDoor).Run(sameDoor, session.Rules.Scripts).Run!.Outcome, "closed");
        });
    }

    [TestMethod]
    public void Without_the_portals_facts_the_lock_and_open_actions_are_left_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var door = Gate(842);
            var version = session.Version;
            var run = Host(session, content, World(null), door).Run(door, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, run.Outcome);
            Assert.AreEqual(version, session.Version, "nothing applied");
        });
    }

    [TestMethod]
    public void A_toggle_of_the_blocked_sector_list_writes_the_session_and_its_condition_reads_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 844);
            var host = Host(session, content, null, npc);
            Assert.IsFalse(session.QuerySectorBlocked(new(1), 12, 34));
            var first = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, first.Outcome, first.Detail);
            Assert.AreEqual(ScrOutcome.SkipDefault, first.Run!.Outcome, "blocked after the toggle, as the run reads it");
            Assert.IsTrue(session.QuerySectorBlocked(new(1), 12, 34));
            CollectionAssert.AreEqual(new[] { (12, 34) }, session.QueryBlockedSectors(new(1)).ToArray());
            Assert.IsFalse(session.QuerySectorBlocked(new(2), 12, 34), "the list is the map's");
            var second = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(ScrOutcome.RunDefault, second.Run!.Outcome);
            Assert.IsFalse(session.QuerySectorBlocked(new(1), 12, 34), "taken off again");
            Assert.AreEqual(0, session.QueryBlockedSectors(new(1)).Count);
        });
    }

    [TestMethod]
    public void A_sector_with_a_negative_number_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 846);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(0, session.QueryBlockedSectors(new(1)).Count);
        });
    }

    [TestMethod]
    public void A_toggle_of_the_generator_block_writes_the_counter_table_and_its_condition_reads_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 845);
            var host = Host(session, content, null, npc);
            Assert.IsFalse(session.QueryGeneratorSlot(7)!.Value.Blocked);
            Assert.AreEqual(ScrOutcome.SkipDefault, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Run!.Outcome);
            Assert.IsTrue(session.QueryGeneratorSlot(7)!.Value.Blocked);
            Assert.AreEqual(ScrOutcome.RunDefault, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Run!.Outcome);
            Assert.IsFalse(session.QueryGeneratorSlot(7)!.Value.Blocked, "the second toggle clears it");
        });
    }

    [TestMethod]
    public void Create_item_puts_a_new_item_into_the_npcs_inventory_list()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 847);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(new(1), new SessionMapInventorySeed([new(entity, SessionHolderKind.Npc, false, true, [], [])], [])).Failure);
            var host = Host(session, content, null, npc);
            var run = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, run.Outcome, run.Detail);
            Assert.AreEqual(GameSessionDisposition.Changed, host.LastCreatedItem!.Value.Disposition);
            var held = session.QueryMapInventory(new(1))!.Holders.Single(h => h.Entity == entity).Items;
            Assert.AreEqual(1, held.Count);
            Assert.AreEqual(Herb, held[0].Facts.Kind);
        });
    }

    [TestMethod]
    public void Create_item_of_gold_or_for_an_npc_with_no_list_is_left_unapplied_and_touch_art_is_a_no_op()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var rules = session.Rules.Scripts;
            var gold = Row(2, NpcType, ScrPoint.Heartbeat, 848);
            var goldEntity = AreaUseScriptHost.EntityId(gold);
            Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(new(1), new SessionMapInventorySeed([new(goldEntity, SessionHolderKind.Npc, false, true, [], [])], [])).Failure);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, gold).RunHeartbeat(gold, ScrPoint.Heartbeat, rules).Outcome, "gold is not modelled");
            var listless = Row(3, NpcType, ScrPoint.Heartbeat, 847);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, listless).RunHeartbeat(listless, ScrPoint.Heartbeat, rules).Outcome, "no inventory list");
            var touch = Row(4, NpcType, ScrPoint.Heartbeat, 849);
            var version = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, touch).RunHeartbeat(touch, ScrPoint.Heartbeat, rules).Outcome);
            Assert.AreEqual(version, session.Version, "touch art changes nothing");
        });
    }

    [TestMethod]
    public void The_hosts_sets_hold_the_world_object_commands_they_apply()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission })
        {
            foreach (var action in ScrAdmissionSet.WorldObjectHostActions) Assert.IsTrue(set.Actions.Contains(action), $"{set.Id}: action {action}");
            foreach (var condition in ScrAdmissionSet.WorldObjectConditions) Assert.IsTrue(set.Conditions.Contains(condition), $"{set.Id}: condition {condition}");
            // ARC-SCRIPT-1279: every action of the slice has a host now.
            CollectionAssert.AreEquivalent(ScrAdmissionSet.WorldObjectActions.ToArray(), ScrAdmissionSet.WorldObjectHostActions.ToArray());
        }
    }

    private static SessionItemFacts ItemFacts(SessionItemType type, int name, int location = 0) => new(new(type, 1000 + name, name, 30000 + name), 5, 0, location, null, []);

    // An NPC holding two items (a ring named 7001, a sword named 7002) in a seed of map 1.
    private static string SeedNpc(GameSessionOwner session, AreaObjectRecord npc, int ringLocation = 0)
    {
        var entity = AreaUseScriptHost.EntityId(npc);
        var seed = new SessionMapInventorySeed([new(entity, SessionHolderKind.Npc, false, false, [], ["ring", "sword"])],
            [new("ring", ItemFacts(SessionItemType.Armor, 7001, ringLocation), null), new("sword", ItemFacts(SessionItemType.Weapon, 7002), null)]);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(new(1), seed).Failure);
        return entity;
    }

    [TestMethod]
    public void Destroy_item_named_deletes_the_first_item_of_the_holders_list_with_the_name()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 850);
            var entity = SeedNpc(session, npc);
            var host = Host(session, content, null, npc);
            var run = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, run.Outcome, run.Detail);
            Assert.AreEqual((GameSessionDisposition.Changed, "ring"), (host.LastNamedItem!.Value.Disposition, host.LastNamedItem.Value.Item));
            Assert.IsTrue(session.IsItemDestroyed("ring"));
            CollectionAssert.AreEqual(new[] { "sword" }, session.QueryMapInventory(new(1))!.Holders.Single(h => h.Entity == entity).Items.Select(item => item.Entity).ToArray());
            // The run again finds no such item and changes nothing.
            var version = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual((GameSessionDisposition.NoChange, version), (host.LastNamedItem!.Value.Disposition, session.Version));
        });
    }

    [TestMethod]
    public void Two_item_actions_on_one_holder_and_name_leave_the_run_unapplied_as_the_second_is_not_planned_against_the_first()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 855);
            SeedNpc(session, npc);
            var version = session.Version;
            var run = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, run.Outcome, run.Detail);
            Assert.AreEqual(version, session.Version, "nothing applied");
            Assert.IsFalse(session.IsItemDestroyed("ring"));
        });
    }

    [TestMethod]
    public void A_transfer_and_its_reverse_of_one_item_name_leave_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 856);
            SeedNpc(session, npc);
            var version = session.Version;
            var run = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, run.Outcome, run.Detail);
            Assert.AreEqual(version, session.Version, "nothing applied");
        });
    }

    [TestMethod]
    public void Transfer_item_moves_the_first_named_item_from_the_attachee_to_the_player()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 851);
            var entity = SeedNpc(session, npc);
            var host = Host(session, content, null, npc);
            var run = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, run.Outcome, run.Detail);
            Assert.AreEqual((GameSessionDisposition.Changed, "ring"), (host.LastNamedItem!.Value.Disposition, host.LastNamedItem.Value.Item));
            Assert.IsTrue(session.QueryPcInventory().Any(item => item.Entity == "ring"), "the hero holds it now");
            CollectionAssert.AreEqual(new[] { "sword" }, session.QueryMapInventory(new(1))!.Holders.Single(h => h.Entity == entity).Items.Select(item => item.Entity).ToArray());
            Assert.IsFalse(session.IsItemDestroyed("ring"));
        });
    }

    [TestMethod]
    public void A_transfer_the_insert_check_declines_leaves_the_item_and_the_run_goes_on_and_a_worn_item_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var npc = Row(2, NpcType, ScrPoint.Heartbeat, 851);
            var entity = SeedNpc(session, npc);
            var host = new AreaUseScriptHost(session, 1, [npc], [], content, World()) { InsertCheck = _ => SessionInsertVerdict.NoRoom };
            var run = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, run.Outcome, run.Detail);
            Assert.AreEqual(GameSessionFailure.InsertRefused, host.LastNamedItem!.Value.Failure);
            Assert.AreEqual(2, session.QueryMapInventory(new(1))!.Holders.Single(h => h.Entity == entity).Items.Count, "nothing moved");

            using var other = ConversationSessionFixture.CreateWithHero();
            SeedNpc(other, npc, ringLocation: 1006);
            var version = other.Version;
            var worn = Host(other, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, other.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, worn.Outcome, "a worn item's move would run the wield-off events the session does not");
            Assert.AreEqual(version, other.Version, "nothing applied");
        });
    }

    [TestMethod]
    public void Set_written_start_and_toggle_display_write_the_item_fields_of_an_item_row_only()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var rules = session.Rules.Scripts;
            var note = Row(1, WrittenType, ScrPoint.Use, 852);
            var noteEntity = AreaUseScriptHost.EntityId(note);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, note).Run(note, rules).Outcome);
            Assert.AreEqual(new SessionItemFieldWrites(9, false), session.QueryItemFieldWrites(noteEntity), "a written item takes the start");
            // Another item type does nothing for set written start (the original writes the field for a written item only); a scenery object takes neither.
            var other = Row(2, GenericType, ScrPoint.Use, 852);
            var scenery = Row(3, SceneryType, ScrPoint.Use, 853);
            var version = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, other).Run(other, rules).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, scenery).Run(scenery, rules).Outcome);
            Assert.AreEqual(version, session.Version, "nothing written");
            // The display toggle takes an item of the types 5..14, and a second toggle gives the bit back.
            var toggled = Row(4, GenericType, ScrPoint.Use, 853);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, toggled).Run(toggled, rules).Outcome);
            Assert.AreEqual(new SessionItemFieldWrites(null, true), session.QueryItemFieldWrites(AreaUseScriptHost.EntityId(toggled)));
            var twice = Row(5, WrittenType, ScrPoint.Use, 854);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, twice).Run(twice, rules).Outcome);
            Assert.IsTrue(session.QueryItemFieldWrites(AreaUseScriptHost.EntityId(twice)).IsEmpty, "two toggles in one run leave nothing");
            // The fields are saved.
            using var loaded = ConversationSessionFixture.SaveAndLoad(session);
            Assert.AreEqual(new SessionItemFieldWrites(9, false), loaded.QueryItemFieldWrites(noteEntity));
            Assert.AreEqual(2, loaded.QueryAllItemFieldWrites().Count);
        });
    }

    [TestMethod]
    public void A_containers_toggle_opens_to_the_next_frame_and_closes_to_the_previous_one()
    {
        // 0x502BC0 (next frame) and 0x502C00 (previous frame, never below 0): an open container of three frames goes 2 -> 1 -> 0, a closed one opens to 1.
        var container = new AreaScriptOpenable(default!, default!, null, 3);
        Assert.AreEqual(1, container.ToggledFrame(0));
        Assert.AreEqual(1, container.ToggledFrame(2));
        Assert.AreEqual(0, container.ToggledFrame(1));
        Assert.AreEqual(0, new AreaScriptOpenable(default!, default!, null, 1).ToggledFrame(0), "a one-frame container cannot open");
    }
}
