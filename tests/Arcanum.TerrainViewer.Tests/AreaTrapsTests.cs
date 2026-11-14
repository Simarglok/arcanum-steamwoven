using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-110: the viewer's trap host over an invented content stack and map (scripts, entities, cells and flags are
/// invented): the trap baseline of a row, a use that meets a trap before the use script, the walk's check of the next tile, the step onto a
/// trap object, the use-script host's slot after a removal, and a container's loot open stopped by a trap.</summary>
[TestClass]
public sealed class AreaTrapsTests
{
    private const int TrapObject = 17, Scenery = 3, Detect = TrapRules.DetectTrapsSpellFlag;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with a use slot (point 1: local flags, counters, script) and object flags on (x, 20).
    private static AreaObjectRecord Row(int index, int type, int script, uint counters = 0, long x = 0, uint objectFlags = 0, long? difficulty = null, long y = 20)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Use, [0, counters, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, objectFlags, [], null),
        };
        if (difficulty is { } hard) fields.Add(new(37, AreaPresence.Value, AreaValueOrigin.Instance, hard, [], null));
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x == 0 ? 10 + index : x, y,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private static CellCoord Cell(AreaObjectRecord row) => new((int)row.WorldX!.Value, (int)row.WorldY!.Value);

    private static byte[] Program(uint headerFlags, int action)
    {
        var bytes = new byte[64 + 132];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), headerFlags);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 44), action);
        return bytes;
    }

    // ARC-WORLD-1805: the installed teleporting doors' script shape: line 0 teleports the triggerer (focus kind 0) to a literal map, x and y, line 1 ends with the given action.
    private static byte[] TeleportProgram(int map, int x, int y, int finalAction)
    {
        var bytes = new byte[64 + 2 * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 2);
        var at = 64 + 44;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), ScrCatalogue.Action.Teleport);
        bytes[at + 4] = 0; bytes[at + 5] = 3; bytes[at + 6] = 3; bytes[at + 7] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 4), map);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 8), x);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 12), y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 132 + 44), finalAction);
        return bytes;
    }

    // 30001: a non-magical normal-damage trap on the triggerer alone; 30004: a fire trap that removes itself (0x4); 30002: a shot;
    // 30007: the trap that does nothing; 709: an ordinary trap script (0x1) that runs default; 700: no trap.
    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-world-110-");
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
            var skip = ScrCatalogue.Action.ReturnSkipDefault;
            Write("scr/30001normal.scr", Program(0x1, skip));
            Write("scr/30004fire.scr", Program(0x1 | 0x4, skip));
            Write("scr/30002shot.scr", Program(0x1, skip));
            Write("scr/30007none.scr", Program(0x1, skip));
            Write("scr/00709trap.scr", Program(0x1, ScrCatalogue.Action.ReturnRunDefault));
            Write("scr/00700plain.scr", Program(0, skip));
            Write("scr/00710port.scr", TeleportProgram(5076, 5, 6, ScrCatalogue.Action.ReturnRunDefault));
            Write("scr/00711port.scr", TeleportProgram(5076, 5, 6, skip));
            Write("scr/00712port.scr", TeleportProgram(5000, 5, 6, ScrCatalogue.Action.ReturnRunDefault));
            Write("scr/00713port.scr", TeleportProgram(5000, 5, 6, skip));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private sealed class Effects(int spellFlags = 0)
    {
        public List<(AreaObjectRecord Row, NativeTrapRun Run, int Counters, int Flags)> Calls { get; } = [];
        public int SpellFlags { get; set; } = spellFlags;
        public AreaTrapEffects Side => new((row, run, counters, flags) => { Calls.Add((row, run, counters, flags)); return SessionTrapEffectResult.None; }, SpellFlags)
        {
            ApplyByCritter = ByCritter ? (entity, row, run, counters, flags) => { Triggerers.Add(entity); Calls.Add((row, run, counters, flags)); return SessionTrapEffectResult.None; } : null,
            SpellFlagsOf = entity => WalkerFlags.GetValueOrDefault(entity),
        };

        // ARC-WORLD-1469: the critters' side of the cast host: who the triggerer of each fired native run was, and each critter's spell flags.
        public bool ByCritter { get; set; } = true;
        public List<string> Triggerers { get; } = [];
        public Dictionary<string, int> WalkerFlags { get; } = [];
    }

    private static (AreaUseScriptHost Host, AreaTraps Traps) Hosts(GameSessionOwner session, ScriptContent content, Func<AreaTrapEffects?> effects,
        HashSet<AreaObjectKey>? gone, params AreaObjectRecord[] shown) => Hosts(session, content, effects, gone, null, shown);

    private static (AreaUseScriptHost Host, AreaTraps Traps) Hosts(GameSessionOwner session, ScriptContent content, Func<AreaTrapEffects?> effects,
        HashSet<AreaObjectKey>? gone, Func<AreaObjectRecord, int>? light, params AreaObjectRecord[] shown)
    {
        var host = new AreaUseScriptHost(session, 1, shown, [], content);
        var traps = new AreaTraps(session, host, shown, row => gone?.Contains(row.Key) == true, effects, light);
        host.Traps = traps.Use;
        return (host, traps);
    }

    [TestMethod]
    public void A_rows_baseline_is_its_carrier_use_slot_header_and_object_flags()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var (host, _) = Hosts(session, content, () => null, null);
            Assert.AreEqual(new SessionTrapBaseline(TrapCarrier.TrapObject, 30004, 0x5, 0x0302_0105, 0x100),
                host.TrapBaseline(Row(1, TrapObject, 30004, 0x0302_0105, objectFlags: 0x100)));
            Assert.AreEqual(new SessionTrapBaseline(TrapCarrier.TrappedObject, 700, 0, 0, 0), host.TrapBaseline(Row(2, Scenery, 700)));
            Assert.AreEqual(new SessionTrapBaseline(TrapCarrier.TrappedObject, 799, 0, 0, 0), host.TrapBaseline(Row(3, Scenery, 799)), "a script the content lacks");
            Assert.IsNull(new AreaUseScriptHost(session, 1, [], [], null).TrapBaseline(Row(1, TrapObject, 30004)), "no script content");
        });
    }

    // ARC-WORLD-125: a script trap device writes its slot onto a container whose map data holds no use slot at all (the original's slot 1 element is empty); the host reads the written slot and the trap fires.
    [TestMethod]
    public void A_container_with_no_use_slot_in_its_data_takes_a_script_trap_device_s_written_slot()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var bare = new AreaObjectRecord(Key(7), 0, Scenery, 0, "p", "p", "r", "s", AreaScope.Core, null, 17, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
            var (host, _) = Hosts(session, content, () => effects.Side, null, bare);
            Assert.AreEqual(0, host.UseScriptOf(bare), "no slot: no script");
            Assert.IsNull(host.TrapBaseline(bare));
            Assert.IsNull(host.Slot(new ScrObjectRef(bare.Key.RuntimeMap, AreaUseScriptHost.EntityId(bare)), ScrPoint.Use), "and no slot is read before a write");
            var map = AreaUseScriptHost.Trapped(bare).Map;
            var entity = AreaUseScriptHost.EntityId(bare);
            var written = session.ApplyChanges(session.Version, null,
            [
                new(map, MapChangeKey.ForEntity(entity, "use.script"), 30001), new(map, MapChangeKey.ForEntity(entity, "use.lf"), 0), new(map, MapChangeKey.ForEntity(entity, "use.counters"), 0x0000_0503),
                new(map, MapChangeKey.ForEntity(entity, GameSessionOwner.TrapStateProperty), 5),
            ]);
            Assert.AreEqual(GameSessionDisposition.Changed, written.Disposition);
            Assert.AreEqual(30001, host.UseScriptOf(bare));
            Assert.AreEqual(new SessionTrapBaseline(TrapCarrier.TrappedObject, 30001, 0x1, 0x0000_0503, 0), host.TrapBaseline(bare));
            Assert.IsTrue(host.TrapState(bare) is { Trapped: true, Spotted: true, PlacedByPc: true });
            Assert.AreEqual(0x1u, host.TrapHeaderFlags(30001), "the header's trap class comes from the script file");
            Assert.AreEqual(0u, host.TrapHeaderFlags(0));
            Assert.AreEqual(0u, host.TrapHeaderFlags(4242), "a script the content lacks");
            var used = host.Run(bare, session.Rules.Scripts);
            Assert.IsTrue(used.Trap is { Outcome: SessionTrapOutcome.Fired, Cancels: true }, used.Detail);
            Assert.AreEqual((30001, 0x0000_0503), (effects.Calls.Single().Run.Script, effects.Calls.Single().Counters));
        });
    }

    [TestMethod]
    public void A_native_trap_fires_on_a_use_and_cancels_the_use_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var chest = Row(1, Scenery, 30001, 0x0000_0503);
            var (host, _) = Hosts(session, content, () => effects.Side, null, chest);
            Assert.AreEqual(true, host.IsTrap(chest));
            var used = host.Run(chest, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, used.Outcome, used.Detail);
            Assert.IsNull(used.Run, "the use script does not run");
            Assert.IsTrue(used.Trap is { Outcome: SessionTrapOutcome.Fired, Cancels: true, Effect: not null }, used.Detail);
            Assert.AreEqual(1, effects.Calls.Count);
            Assert.AreEqual((30001, 0x0000_0503, 0), (effects.Calls[0].Run.Script, effects.Calls[0].Counters, effects.Calls[0].Flags));
            Assert.AreSame(chest, effects.Calls[0].Row);
            // No auto-removal: the trap stays and fires again.
            Assert.AreEqual(true, host.IsTrap(chest));
            Assert.AreEqual(SessionTrapOutcome.Fired, host.Run(chest, session.Rules.Scripts).Trap?.Outcome);
            Assert.AreEqual(2, effects.Calls.Count);
        });
    }

    // ARC-WORLD-1581 (TT18, TT7): the hero's door use node meets the door's trap through the same entry point a container's use does; a door (type 1) carries a trap as any other object.
    [TestMethod]
    public void A_trapped_door_meets_the_hero_on_its_use_and_stays_shut_when_the_trap_goes_off()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var door = Row(1, 1, 30004, 0x0000_0201);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, door);
            Assert.AreEqual(new SessionTrapBaseline(TrapCarrier.TrappedObject, 30004, 0x5, 0x0000_0201, 0), host.TrapBaseline(door));
            var met = traps.Use(door)!;
            Assert.IsTrue(met is { Cause: SessionTrapCause.Use, Outcome: SessionTrapOutcome.Fired, Cancels: true, Result.State.Removed: true }, met.Describe());
            Assert.AreEqual(1, effects.Calls.Count);
            Assert.AreSame(door, effects.Calls[0].Row);
            Assert.AreEqual(0, effects.Triggerers.Count, "the hero's own effect, not a critter's");
            Assert.IsNull(traps.Use(door), "the trap is gone: the door opens as any other");
        });
    }

    // ARC-WORLD-1668 (TT18, 0x428A10 -> 0x441980): after the trap meeting the door's own use script runs with the opener as the triggerer and the extra object; "skip default" stops the use, run default, no script and a trap that is gone do not.
    [TestMethod]
    public void A_doors_own_use_script_stops_the_use_only_when_it_skips_the_default_and_an_ordinary_trap_script_runs_after_its_meeting()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects();
            var skipping = Row(1, 1, 700);      // a plain use script that skips the default
            var ordinaryTrap = Row(2, 1, 709);  // an ordinary trap script (0x1) that runs default
            var plainDoor = Row(3, 1, 0);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, skipping, ordinaryTrap, plainDoor);
            var pc = host.Pc;
            var skipped = host.RunUseScript(AreaUseScriptHost.EntityId(skipping), pc, pc, session.Rules.Scripts);
            Assert.IsTrue(AreaUseScriptHost.UseStops(skipped), skipped.Detail);
            // The meeting comes first (an ordinary trap script is a fired meeting without a native run: it does not cancel), then the script runs its default.
            var met = traps.Use(ordinaryTrap)!;
            Assert.IsTrue(met is { Outcome: SessionTrapOutcome.Fired, Cancels: false, Result.Run: null }, met.Describe());
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(ordinaryTrap), pc, pc, session.Rules.Scripts);
            Assert.IsTrue(ran is { Outcome: AreaScriptedUseOutcome.NoTeleport, Run.Outcome: ScrOutcome.RunDefault }, ran.Detail);
            Assert.IsFalse(AreaUseScriptHost.UseStops(ran), "run default lets the door open");
            // An NPC opener is the triggerer and the extra object as well.
            var npc = new ScrObjectRef(1, "oid:thug");
            Assert.IsTrue(AreaUseScriptHost.UseStops(host.RunUseScript(AreaUseScriptHost.EntityId(skipping), npc, npc, session.Rules.Scripts)), "the same answer for an NPC opener");
            // No script on the door: nothing runs and nothing stops the use.
            var none = host.RunUseScript(AreaUseScriptHost.EntityId(plainDoor), pc, pc, session.Rules.Scripts);
            Assert.IsNull(none.Run);
            Assert.IsFalse(AreaUseScriptHost.UseStops(none));
            // A native trap script the trap code left unmet (no cast host) is the dispatcher's trap stop, no answer of a script: the use goes on.
            var native = Row(4, 1, 30001);
            var (unmetHost, _) = Hosts(session, content, () => null, null, native);
            var unmet = unmetHost.RunUseScript(AreaUseScriptHost.EntityId(native), pc, pc, session.Rules.Scripts);
            Assert.AreEqual(ScrStop.TrapScript, unmet.Run?.Stop, unmet.Detail);
            Assert.IsFalse(AreaUseScriptHost.UseStops(unmet), "an unmet native trap does not keep the door shut");
            // A run the host did not apply (an object that is no row of the map) stops nothing.
            Assert.IsFalse(AreaUseScriptHost.UseStops(host.RunUseScript("oid:nobody", pc, pc, session.Rules.Scripts)));
        });
    }

    // ARC-WORLD-1805 (TT18, TT20): a teleport a door's use script asks for is a request the script records, the opener's own (the triggerer): the caller that can queue the hero's change of place takes it and the door goes on as the script's answer says.
    [TestMethod]
    public void A_doors_use_script_teleport_of_the_hero_is_taken_by_a_caller_that_queues_it_and_the_answer_still_decides_the_door()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects();
            var runDefault = Row(1, 1, 710);
            var skipping = Row(2, 1, 711);
            var (host, _) = Hosts(session, content, () => effects.Side, null, runDefault, skipping);
            var pc = host.Pc;
            var asked = new List<ScrTeleport>();
            var opens = host.RunUseScript(AreaUseScriptHost.EntityId(runDefault), pc, pc, session.Rules.Scripts, teleport => { asked.Add(teleport); return null; });
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, opens.Outcome, opens.Detail);
            Assert.AreEqual((5076, 5, 6), (opens.Teleport!.CatalogueMap, opens.Teleport.X, opens.Teleport.Y));
            Assert.AreEqual(pc, opens.Teleport.Object, "the triggerer is the hero");
            Assert.AreEqual(1, asked.Count);
            Assert.IsFalse(AreaUseScriptHost.UseStops(opens), "run default: the door opens first, the teleport is only a request");
            var shut = host.RunUseScript(AreaUseScriptHost.EntityId(skipping), pc, pc, session.Rules.Scripts, _ => null);
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, shut.Outcome, shut.Detail);
            Assert.IsTrue(AreaUseScriptHost.UseStops(shut), "skip default keeps the door shut, the request standing");
            // A caller that cannot take it leaves the run unapplied and the door opens as before.
            var refused = host.RunUseScript(AreaUseScriptHost.EntityId(skipping), pc, pc, session.Rules.Scripts, _ => "no map");
            Assert.AreEqual(AreaScriptedUseOutcome.TeleportRefused, refused.Outcome, refused.Detail);
            Assert.IsFalse(AreaUseScriptHost.UseStops(refused));
            // No caller: the run is refused as before (a use component's script).
            var plain = host.RunUseScript(AreaUseScriptHost.EntityId(runDefault), pc, pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.TeleportRefused, plain.Outcome, plain.Detail);
            Assert.IsFalse(AreaUseScriptHost.UseStops(plain));
            // An NPC opener's own teleport is not modelled (it moves another object than the hero): the run is not applied, the door opens.
            var npc = new ScrObjectRef(1, "oid:thug");
            var other = host.RunUseScript(AreaUseScriptHost.EntityId(skipping), npc, npc, session.Rules.Scripts, _ => null);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, other.Outcome, other.Detail);
            Assert.IsFalse(AreaUseScriptHost.UseStops(other), "an unapplied run stops nothing");
        });
    }

    // ARC-WORLD-1837 (ledger TT20): the NPC side of the same request: the opener's own teleport (the triggerer) to a cell of the shown map is recorded after the run applied, the door goes on as the script's answer says.
    [TestMethod]
    public void A_doors_use_script_teleport_of_an_npc_opener_is_a_request_recorded_after_the_run_applied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            const int NpcRowType = 16;
            var runDefault = Row(1, 1, 712);
            var skipping = Row(2, 1, 713);
            var elsewhere = Row(3, 1, 710);   // catalogue 5076: another map
            var npcRow = Row(4, NpcRowType, 0, x: 30);
            var npc = new ScrObjectRef(1, AreaUseScriptHost.EntityId(npcRow));
            var log = new List<string>();
            string? refusal = null;
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                NpcTeleportRefusal: t => { log.Add($"check {t.Object.Code}"); return refusal; },
                RequestNpcTeleport: t => log.Add($"request {t.Object.Code} {t.X},{t.Y}"));
            var host = new AreaUseScriptHost(session, 1, [runDefault, skipping, elsewhere, npcRow], [], content, world);

            var opens = host.RunUseScript(AreaUseScriptHost.EntityId(runDefault), npc, npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, opens.Outcome, opens.Detail);
            Assert.AreEqual(npc, opens.NpcTeleports.Single().Object);
            CollectionAssert.AreEqual(new[] { $"check {npc.Code}", $"request {npc.Code} 5,6" }, log, "asked first, recorded after the run applied");
            Assert.IsFalse(AreaUseScriptHost.UseStops(opens), "run default: the door opens, the NPC moves at the next ping");
            var shut = host.RunUseScript(AreaUseScriptHost.EntityId(skipping), npc, npc, session.Rules.Scripts);
            Assert.IsTrue(AreaUseScriptHost.UseStops(shut), "skip default keeps the door shut, the request standing");
            Assert.AreEqual(4, log.Count);

            // A refusal of the host leaves the run unapplied (the door opens as before), nothing recorded.
            log.Clear(); refusal = "target not held";
            var refused = host.RunUseScript(AreaUseScriptHost.EntityId(runDefault), npc, npc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
            Assert.IsFalse(AreaUseScriptHost.UseStops(refused));
            CollectionAssert.AreEqual(new[] { $"check {npc.Code}" }, log);

            // Another map, an opener that is no NPC row of the shown map, and the hero's own teleport (the caller's, not the NPC hooks') stay as before.
            log.Clear(); refusal = null;
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, host.RunUseScript(AreaUseScriptHost.EntityId(elsewhere), npc, npc, session.Rules.Scripts).Outcome);
            var stranger = new ScrObjectRef(1, "oid:thug");
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, host.RunUseScript(AreaUseScriptHost.EntityId(runDefault), stranger, stranger, session.Rules.Scripts).Outcome);
            var pc = host.Pc;
            var hero = host.RunUseScript(AreaUseScriptHost.EntityId(runDefault), pc, pc, session.Rules.Scripts, _ => null);
            Assert.AreEqual(AreaScriptedUseOutcome.Teleport, hero.Outcome, hero.Detail);
            Assert.AreEqual(0, log.Count, "no NPC hook ran for these");
        });
    }

    [TestMethod]
    public void A_removed_trap_leaves_the_use_slot_empty()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var chest = Row(1, Scenery, 30004);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, chest);
            Assert.AreEqual(new ScrSlot(30004, 0, 0), host.Slot(new(1, AreaUseScriptHost.EntityId(chest)), ScrPoint.Use));
            var met = traps.Use(chest)!;
            Assert.IsTrue(met is { Outcome: SessionTrapOutcome.Fired, Result.State.Removed: true }, met.Describe());
            Assert.AreEqual(false, host.IsTrap(chest));
            Assert.AreEqual(new ScrSlot(0, 0, 0), host.Slot(new(1, AreaUseScriptHost.EntityId(chest)), ScrPoint.Use), "the removal cleared the script");
            Assert.IsNull(traps.Use(chest), "no trap any more");
            var plain = host.Run(chest, session.Rules.Scripts);
            Assert.IsTrue(plain is { Trap: null, Run.Stop: ScrStop.NoScript }, plain.Detail);
        });
    }

    [TestMethod]
    public void Without_a_cast_host_no_trap_is_met()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, Scenery, 30004);
            var (host, traps) = Hosts(session, content, () => null, null, chest, Row(2, TrapObject, 30004, x: 30));
            var used = host.Run(chest, session.Rules.Scripts);
            Assert.IsTrue(used is { Trap: null, Run.Stop: ScrStop.TrapScript }, used.Detail);
            Assert.IsNull(traps.Step(new(30, 20)));
            Assert.IsNull(traps.WalkCheck(new(30, 20)).Spotted);
            Assert.AreEqual(true, host.IsTrap(chest), "untouched");
        });
    }

    [TestMethod]
    public void The_detect_traps_flag_spots_a_trap_on_a_use_and_then_it_fires()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects(Detect);
            var chest = Row(1, Scenery, 30001);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, chest);
            var spotted = traps.Use(chest)!;
            Assert.IsTrue(spotted is { Outcome: SessionTrapOutcome.Spotted, Cancels: true, Result.PlayerLine: true, Result.State.Spotted: true }, spotted.Describe());
            Assert.AreEqual(0, effects.Calls.Count);
            var fired = host.Run(chest, session.Rules.Scripts);
            Assert.IsTrue(fired.Trap is { Outcome: SessionTrapOutcome.Fired }, "a known trap fires on the next use");
            Assert.AreEqual(1, effects.Calls.Count);
            Assert.AreEqual(TrapRules.SpottedFlag, effects.Calls[0].Flags & TrapRules.SpottedFlag, "the trap state's flags");
        });
    }

    [TestMethod]
    public void An_ordinary_trap_script_runs_as_the_use_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var lever = Row(1, Scenery, 709);
            var (host, _) = Hosts(session, content, () => effects.Side, null, lever, Row(2, Scenery, 700));
            var used = host.Run(lever, session.Rules.Scripts);
            Assert.IsTrue(used.Trap is { Outcome: SessionTrapOutcome.Fired, Cancels: false, Result.Run: null }, used.Detail);
            Assert.AreEqual(ScrOutcome.RunDefault, used.Run?.Outcome, "the dispatcher ran it");
            Assert.AreEqual(0, effects.Calls.Count);
            Assert.IsNull(host.Run(Row(2, Scenery, 700), session.Rules.Scripts).Trap, "no trap");
        });
    }

    [TestMethod]
    public void The_shot_is_not_met_and_the_empty_trap_applies_no_effect()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects(Detect);
            var shotRow = Row(1, Scenery, 30002);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, shotRow);
            Assert.IsNull(traps.Use(shotRow), "the shot slice is not ready: the trap waits, unspotted");
            Assert.IsTrue(host.Run(shotRow, session.Rules.Scripts) is { Trap: null, Run.Stop: ScrStop.TrapScript });
            Assert.IsTrue(host.TrapState(shotRow) is { Trapped: true, Spotted: false });
            effects.SpellFlags = 0;
            var none = traps.Use(Row(2, Scenery, 30007))!;
            Assert.IsTrue(none is { Outcome: SessionTrapOutcome.Fired, Effect: null, Cancels: true }, none.Describe());
            Assert.AreEqual(0, effects.Calls.Count);
        });
    }

    [TestMethod]
    public void The_shot_is_met_and_applied_once_the_host_lists_its_shooters()
    {
        // ARC-TECHNOLOGY-1619 (TT12, TT15): a host that lists the shooter candidates (AreaTrapEffects.ListsShooters) meets a 30002 trap as any native trap; the effect is the cast host's.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects(0);
            var shotRow = Row(1, Scenery, 30002);
            var (_, traps) = Hosts(session, content, () => effects.Side with { ListsShooters = true }, null, shotRow);
            var met = traps.Use(shotRow)!;
            Assert.IsTrue(met is { Outcome: SessionTrapOutcome.Fired, Cancels: true }, met.Describe());
            CollectionAssert.AreEqual(new[] { 30002 }, effects.Calls.Select(c => c.Run.Script).ToArray());
        });
    }

    [TestMethod]
    public void A_step_meets_only_the_first_trap_object_and_no_ordinary_trap_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var inert = Row(1, TrapObject, 0, x: 70);
            var behind = Row(2, TrapObject, 30001, x: 70);
            var ordinary = Row(3, TrapObject, 709, x: 71);
            var (_, traps) = Hosts(session, content, () => effects.Side, null, inert, behind, ordinary);
            Assert.IsNull(traps.Step(new(70, 20)), "the first trap object carries no trap (TT9: the first one only)");
            Assert.IsNull(traps.Step(new(71, 20)), "an ordinary trap script on a step is a later slice");
            Assert.AreEqual(0, effects.Calls.Count);
        });
    }

    // ARC-WORLD-1149 (FR-LIGHT-011): the spot roll reads the light at the trap object's cell from the host's delegate; a level outside 0..255 refuses the roll.
    [TestMethod]
    public void The_spot_roll_asks_for_the_light_at_the_trap_and_refuses_a_level_outside_the_range()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects();
            var plate = Row(1, TrapObject, 30001, x: 200, objectFlags: TrapRules.DontDrawFlag);
            var asked = new List<AreaObjectRecord>();
            var level = 128;
            var (_, traps) = Hosts(session, content, () => effects.Side, null, row => { asked.Add(row); return level; }, plate);
            Assert.AreEqual(1, traps.WalkCheck(Cell(plate)).Rolls);
            Assert.AreSame(plate, asked.Single(), "one roll asked for the light at its trap");
            level = 300;
            var other = Row(2, TrapObject, 30001, x: 201, objectFlags: TrapRules.DontDrawFlag);
            var (_, refusing) = Hosts(session, content, () => effects.Side, null, row => level, other);
            var draws = session.QueryRandom().Draws;
            Assert.AreEqual(0, refusing.WalkCheck(Cell(other)).Rolls, "a light outside 0..255 refuses the roll");
            Assert.AreEqual(draws, session.QueryRandom().Draws, "a refused roll draws nothing");
        });
    }

    // ARC-MAGIC-057 (ledger SP1): without the detect flag a hero's walk check and use make the spot trap roll; the outcome depends on the seed and
    // the hero's skill, so the test follows the counts and the state, never the odds.
    [TestMethod]
    public void The_walk_check_and_a_use_roll_the_heros_spot_trap_skill_when_the_detect_flag_is_off()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects();
            var plates = Enumerable.Range(1, 24).Select(i => Row(i, TrapObject, 30001, x: 200 + i, objectFlags: TrapRules.DontDrawFlag)).ToArray();
            var (host, traps) = Hosts(session, content, () => effects.Side, null, plates);
            var spotted = 0;
            foreach (var plate in plates)
            {
                var draws = session.QueryRandom().Draws;
                var walk = traps.WalkCheck(Cell(plate));
                Assert.AreEqual(1, walk.Rolls, "one spot roll for the tile's one unknown trap");
                Assert.IsTrue(session.QueryRandom().Draws >= draws + 2, "the roll draws the check's two numbers");
                Assert.AreEqual(walk.Spotted is not null, host.TrapState(plate)!.Value.Spotted, "a spotted trap is marked, a missed one is not");
                spotted += walk.Spotted is null ? 0 : 1;
                if (walk.Spotted is not null)
                    Assert.AreEqual(0, traps.WalkCheck(Cell(plate)).Rolls, "a known trap needs no roll");
            }
            Assert.IsTrue(spotted <= plates.Length);
            var use = Row(25, Scenery, 30001);
            var (_, usingTraps) = Hosts(session, content, () => effects.Side, null, use);
            var useDraws = session.QueryRandom().Draws;
            var meeting = usingTraps.Use(use)!;
            Assert.IsTrue(session.QueryRandom().Draws >= useDraws + 2, "the use rolled the spot trap skill");
            Assert.IsTrue(meeting.Outcome is SessionTrapOutcome.Spotted or SessionTrapOutcome.Fired, "a use rolls once and then spots or fires");
            Assert.AreEqual(meeting.Outcome == SessionTrapOutcome.Spotted, meeting.Result.PlayerLine);
        });
    }

    // The original's step reaches the entry point (and its spot roll) only when the mover's spell flags 0x2 and 0x4 are clear (0x4305D0, TT9).
    [TestMethod]
    public void A_step_over_flag_means_no_spot_roll_on_the_step()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects(spellFlags: 0x2);
            var plate = Row(1, TrapObject, 30001, x: 60, objectFlags: TrapRules.DontDrawFlag);
            var (_, traps) = Hosts(session, content, () => effects.Side, null, plate);
            var draws = session.QueryRandom().Draws;
            Assert.AreEqual(SessionTrapOutcome.SteppedOver, traps.Step(Cell(plate))!.Outcome);
            Assert.AreEqual(draws, session.QueryRandom().Draws, "a stepped-over trap draws nothing");
            effects.SpellFlags = 0;
            traps.Step(Cell(plate));
            Assert.IsTrue(session.QueryRandom().Draws >= draws + 2, "a step with no flag rolls");
        });
    }

    [TestMethod]
    public void The_walk_check_spots_an_unknown_trap_object_with_the_detect_flag_without_a_roll()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var gone = new HashSet<AreaObjectKey>();
            var first = Row(1, TrapObject, 30001, x: 40, objectFlags: TrapRules.DontDrawFlag);
            var second = Row(2, TrapObject, 30001, x: 40, objectFlags: TrapRules.DontDrawFlag);
            var (host, traps) = Hosts(session, content, () => effects.Side, gone, first, second, Row(3, TrapObject, 0, x: 41));
            Assert.AreEqual(2, traps.TrapObjectsOn(new(40, 20)).Count);
            Assert.AreEqual(new AreaTrapWalk(null, null, 0), traps.WalkCheck(new(41, 20)), "an untrapped trap object");

            effects.SpellFlags = Detect;
            var spotted = traps.WalkCheck(new(40, 20));
            Assert.AreSame(first, spotted.Spotted, "the first in snapshot order");
            Assert.AreEqual(GameSessionDisposition.Changed, spotted.Mark?.Disposition);
            var state = host.TrapState(first)!.Value;
            Assert.IsTrue(state.Spotted && (state.ObjectFlags & TrapRules.DontDrawFlag) == 0, "marked and drawn");
            Assert.AreSame(second, traps.WalkCheck(new(40, 20)).Spotted, "a known trap is skipped");
            Assert.IsNull(traps.WalkCheck(new(40, 20)).Spotted, "both known: the walk goes on");
            gone.Add(first.Key);
            Assert.AreEqual(1, traps.TrapObjectsOn(new(40, 20)).Count, "a destroyed trap object is no longer met");
        });
    }

    [TestMethod]
    public void A_step_sets_off_the_first_trap_object_and_ends_the_walk()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var trap = Row(1, TrapObject, 30004, 0x0000_0201, x: 50, objectFlags: TrapRules.DontDrawFlag);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, trap);
            Assert.IsNull(traps.Step(new(51, 20)), "no trap object there");

            effects.SpellFlags = TrapRules.StepImmuneSpellFlags & 0x2;
            var over = traps.Step(Cell(trap))!;
            Assert.IsTrue(over is { Outcome: SessionTrapOutcome.SteppedOver, EndsWalk: false }, over.Describe());

            effects.SpellFlags = 0;
            var fired = traps.Step(Cell(trap))!;
            Assert.IsTrue(fired is { Cause: SessionTrapCause.Step, Outcome: SessionTrapOutcome.Fired, EndsWalk: true, Result.State.Removed: true }, fired.Describe());
            Assert.AreEqual(1, effects.Calls.Count);
            Assert.AreEqual(SessionObjectCondition.Destroyed, session.QueryObjectCondition(new(1), AreaUseScriptHost.EntityId(trap),
                Arcanum.Simulation.Combat.DamageableObjectKind.Trap), "a removed trap object is destroyed");
            Assert.IsNull(traps.Step(Cell(trap)), "no trap any more");
            Assert.AreEqual(false, host.IsTrap(trap));
        });
    }

    [TestMethod]
    public void A_spotted_trap_object_on_a_step_ends_the_walk_without_firing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects(Detect);
            var trap = Row(1, TrapObject, 30001, x: 60);
            var (_, traps) = Hosts(session, content, () => effects.Side, null, trap);
            var spotted = traps.Step(Cell(trap))!;
            Assert.IsTrue(spotted is { Outcome: SessionTrapOutcome.Spotted, EndsWalk: true }, spotted.Describe());
            Assert.AreEqual(0, effects.Calls.Count);
            Assert.IsTrue(traps.Step(Cell(trap)) is { Outcome: SessionTrapOutcome.Fired }, "a known trap fires on the next step");
        });
    }

    // ARC-SKILLS-1156 (ledger SP3): the host's disarm press. The outcome depends on the seed and the hero's skill, so the test follows the state, never the odds.
    [TestMethod]
    public void The_trap_objects_difficulty_is_field_308_slot_37_and_only_a_trap_object_has_one()
    {
        Assert.AreEqual(40, AreaTraps.TrapDifficulty(Row(1, TrapObject, 30001, difficulty: 40)));
        Assert.AreEqual(0, AreaTraps.TrapDifficulty(Row(2, TrapObject, 30001)), "no field: none");
        Assert.AreEqual(0, AreaTraps.TrapDifficulty(Row(3, Scenery, 30001, difficulty: 40)), "a trapped scenery has no trap difficulty (SP3)");
    }

    [TestMethod]
    public void Only_a_known_trap_is_a_disarm_target_and_the_known_trap_objects_of_a_tile_are_listed()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects();
            var plate = Row(1, TrapObject, 30001, x: 200, objectFlags: TrapRules.DontDrawFlag);
            var chest = Row(2, Scenery, 30001, x: 300);
            var plain = Row(3, Scenery, 700, x: 301);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, plate, chest, plain);
            Assert.IsFalse(traps.KnownTrap(plate) || traps.KnownTrap(chest) || traps.KnownTrap(plain), "nothing is spotted yet");
            Assert.AreEqual(0, traps.KnownTrapObjectsOn(Cell(plate)).Count);
            Assert.AreEqual(GameSessionDisposition.Changed, session.MarkTrapSpotted(session.Version, AreaUseScriptHost.Trapped(plate), host.TrapBaseline(plate)!.Value, TrapObserver.Player).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, session.MarkTrapSpotted(session.Version, AreaUseScriptHost.Trapped(chest), host.TrapBaseline(chest)!.Value, TrapObserver.Player).Disposition);
            Assert.IsTrue(traps.KnownTrap(plate) && traps.KnownTrap(chest));
            Assert.IsFalse(traps.KnownTrap(plain), "no trap on it");
            Assert.AreSame(plate, traps.KnownTrapObjectsOn(Cell(plate)).Single());
        });
    }

    [TestMethod]
    public void A_disarm_press_rolls_once_for_a_known_trap_and_nothing_for_an_unknown_one_or_without_a_cast_host()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var effects = new Effects();
            var chest = Row(1, Scenery, 30001, x: 300);
            var available = true;
            var (host, traps) = Hosts(session, content, () => available ? effects.Side : null, null, chest);
            var insert = new SessionInsertCheck(_ => SessionInsertVerdict.Accepted);
            var draws = session.QueryRandom().Draws;
            Assert.IsNull(traps.Disarm(chest, null, insert), "an unknown trap is no target");
            Assert.AreEqual(draws, session.QueryRandom().Draws, "nothing is drawn");
            Assert.AreEqual(GameSessionDisposition.Changed, session.MarkTrapSpotted(session.Version, AreaUseScriptHost.Trapped(chest), host.TrapBaseline(chest)!.Value, TrapObserver.Player).Disposition);
            available = false;
            Assert.IsNull(traps.Disarm(chest, null, insert), "without a cast host a sprung trap could not take effect");
            available = true;
            var revision = session.Version.Revision;
            var disarm = traps.Disarm(chest, null, insert)!;
            Assert.AreEqual(GameSessionDisposition.Changed, disarm.Roll.Disposition);
            Assert.AreEqual(revision + 1, session.Version.Revision, "one revision");
            Assert.IsTrue(session.QueryRandom().Draws >= draws + 2, "the check's draws");
            var state = host.TrapState(chest)!.Value;
            Assert.AreEqual(disarm.Outcome is TrapDisarm.Removed or TrapDisarm.RemovedWithDevice || disarm.Outcome == TrapDisarm.Sprung && state.Removed, state.Removed);
            if (disarm.Outcome == TrapDisarm.Sprung) Assert.AreEqual(1, effects.Calls.Count, "a sprung native trap takes effect through the cast host");
            else Assert.AreEqual(0, effects.Calls.Count);
            StringAssert.Contains(disarm.Describe(), disarm.Outcome switch { TrapDisarm.Removed => "disarmed", TrapDisarm.RemovedWithDevice => "disarmed", TrapDisarm.Failed => "failed", _ => "goes off" });
            Assert.IsNull(new AreaUseScriptHost(session, 1, [], [], null).GiveHeroPrototypeItem(6047, insert), "no item data: no device");
        });
    }

    // ARC-MAGIC-1189 (ledger TT10, SP2): the walk check of an NPC's or a companion's step. A master NPC's sheet; the trap's tile is the NPC's east neighbour.
    private static (SessionNpcAdvancement Loaded, Arcanum.Simulation.Character.AdvancementModifiers Modifiers)? MasterSheet(string id)
    {
        int[] basic = new int[12];
        basic[(int)Arcanum.Simulation.Skills.Skill.SpotTrap] = 20 | (int)Arcanum.Simulation.Combat.SkillTraining.Master << 6;
        var sheet = new Arcanum.Simulation.Character.AdvancementSheet(Arcanum.Simulation.Character.CharacterRace.Human, false, [8, 8, 8, 8, 20, 20, 8, 8], 0, 0, 1, 0,
            [.. basic], [.. new int[4]], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0);
        return (new SessionNpcAdvancement(id, sheet, 0), Arcanum.Simulation.Character.AdvancementModifiers.None);
    }

    // ARC-WORLD-1469 (ledger TT9, TT8, TT7): the meeting of an NPC or a companion with a trap object under its step and with a trapped object it uses.
    private static AreaTrapWalker Free(string entity = "oid:npc") => new(entity, false, null);
    private static AreaTrapWalker Led(string entity = "oid:companion") => new(entity, true, null);

    [TestMethod]
    public void A_free_npc_s_step_sets_off_a_trap_object_it_knows_with_itself_as_the_triggerer_and_ends_its_walk()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var trap = Row(1, TrapObject, 30004, 0x0000_0201, x: 50, objectFlags: TrapRules.DontDrawFlag);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, trap);
            Assert.IsNull(traps.StepNpc(Free(), new(51, 20)), "no trap object there");
            var fired = traps.StepNpc(Free("oid:thug"), Cell(trap))!;
            Assert.IsTrue(fired is { Cause: SessionTrapCause.Step, Outcome: SessionTrapOutcome.Fired, EndsWalk: true, Result.State.Removed: true }, fired.Describe());
            CollectionAssert.AreEqual(new[] { "oid:thug" }, effects.Triggerers, "the walker is the triggerer of the run");
            Assert.AreEqual(1, effects.Calls.Count);
            Assert.AreEqual(0, effects.Calls.Count(c => c.Row != trap), "only the trap's own effect");
            Assert.IsNull(traps.StepNpc(Free(), Cell(trap)), "the trap object is gone");
            Assert.AreEqual(false, host.IsTrap(trap));
        });
    }

    [TestMethod]
    public void A_step_ends_its_walk_on_any_trap_object_the_mover_does_not_step_over_even_where_no_meeting_is_made()
    {
        // ARC-WORLD-1582 (TT17): the step function ends the walk on every trap object it lists, whatever the entry point answered.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var shot = Row(1, TrapObject, 30002, 0x0000_0201, x: 50, objectFlags: TrapRules.DontDrawFlag);
            var ordinary = Row(2, TrapObject, 700, x: 52, objectFlags: TrapRules.DontDrawFlag);
            var native = Row(3, TrapObject, 30004, 0x0000_0201, x: 54, objectFlags: TrapRules.DontDrawFlag);
            var gone = new HashSet<AreaObjectKey>();
            var (host, traps) = Hosts(session, content, () => effects.Side, gone, shot, ordinary, native);
            Assert.IsFalse(traps.StepEndsWalk(new(51, 20)), "no trap object there");
            Assert.IsFalse(traps.StepEndsWalk(new(51, 20), Free()));
            Assert.IsNull(traps.StepNpc(Free(), Cell(shot)), "the 30002 shot is no meeting");
            Assert.IsNull(traps.StepNpc(Free(), Cell(ordinary)), "an ordinary script is no meeting on a step");
            foreach (var row in new[] { shot, ordinary, native })
            {
                Assert.IsTrue(traps.StepEndsWalk(Cell(row)), "the hero's walk ends there");
                Assert.IsTrue(traps.StepEndsWalk(Cell(row), Free()), "and a free NPC's");
                Assert.IsTrue(traps.StepEndsWalk(Cell(row), Led()), "and a companion's");
            }
            effects.WalkerFlags["oid:ghost"] = TrapRules.StepImmuneSpellFlags & 0x2;
            Assert.IsFalse(traps.StepEndsWalk(Cell(native), Free("oid:ghost")), "a mover with the step-over spell flags runs none of it");
            effects.SpellFlags = 0x4;
            Assert.IsFalse(traps.StepEndsWalk(Cell(native)), "nor does the hero with them");
            effects.SpellFlags = 0;
            effects.ByCritter = false;
            Assert.IsTrue(traps.StepEndsWalk(Cell(native), Free()), "a cast host that cannot take a critter's trap still ends the walk");
            // A trap object the session destroyed is not listed.
            effects.ByCritter = true;
            Assert.IsNotNull(traps.StepNpc(Free(), Cell(native)));
            Assert.AreEqual(false, host.IsTrap(native), "the fired trap removed itself");
            gone.Add(native.Key);
            Assert.IsFalse(traps.StepEndsWalk(Cell(native)), "a trap object the session destroyed is not listed");
        });
    }

    [TestMethod]
    public void A_walker_whose_spell_flags_step_over_traps_sets_none_off_and_without_the_critter_side_no_trap_is_met()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var trap = Row(1, TrapObject, 30004, 0x0000_0201, x: 50, objectFlags: TrapRules.DontDrawFlag);
            var (_, traps) = Hosts(session, content, () => effects.Side, null, trap);
            effects.WalkerFlags["oid:ghost"] = TrapRules.StepImmuneSpellFlags & 0x2;
            var over = traps.StepNpc(Free("oid:ghost"), Cell(trap))!;
            Assert.IsTrue(over is { Outcome: SessionTrapOutcome.SteppedOver, EndsWalk: false }, over.Describe());
            Assert.AreEqual(0, effects.Calls.Count);
            effects.ByCritter = false;
            Assert.IsNull(traps.StepNpc(Free(), Cell(trap)), "a cast host that cannot take a critter's trap meets none: no trap is consumed without its effect");
        });
    }

    [TestMethod]
    public void A_trap_the_pc_did_not_place_is_known_to_a_free_npc_and_only_a_led_one_spots_it_and_marks_it_for_the_player()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var plain = Row(1, TrapObject, 30001, x: 60, objectFlags: TrapRules.DontDrawFlag);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, plain);
            // The detect flag of the player's spells spots without a roll for an observer that does not know the trap: a companion does not know a trap nobody spotted, so it spots it and marks it for the player.
            effects.SpellFlags = Detect;
            var led = traps.StepNpc(Led(), Cell(plain))!;
            Assert.IsTrue(led is { Outcome: SessionTrapOutcome.Spotted, EndsWalk: true }, led.Describe());
            Assert.AreEqual(0, effects.Calls.Count);
            Assert.IsTrue(host.TrapState(plain)!.Value.Spotted, "a companion marks it: the player knows it");
            var after = traps.StepNpc(Led("oid:other"), Cell(plain))!;
            Assert.IsTrue(after is { Outcome: SessionTrapOutcome.Fired }, "a known trap fires on the next step: " + after.Describe());
            CollectionAssert.AreEqual(new[] { "oid:other" }, effects.Triggerers);
            // A free NPC knows every trap the PC did not place: no spot attempt, the trap goes off.
            var other = Row(2, TrapObject, 30001, x: 61, objectFlags: TrapRules.DontDrawFlag);
            var (_, otherTraps) = Hosts(session, content, () => effects.Side, null, other);
            var free = otherTraps.StepNpc(Free("oid:free"), Cell(other))!;
            Assert.IsTrue(free is { Outcome: SessionTrapOutcome.Fired }, free.Describe());
        });
    }

    [TestMethod]
    public void A_trap_the_pc_placed_is_unknown_to_a_free_npc_which_spots_it_without_marking_and_known_to_a_led_one_which_sets_it_off()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects(Detect);
            var placed = Row(1, TrapObject, 30001, x: 60, objectFlags: TrapRules.PlacedByPcFlag | TrapRules.DontDrawFlag);
            var (host, traps) = Hosts(session, content, () => effects.Side, null, placed);
            var free = traps.StepNpc(Free(), Cell(placed))!;
            Assert.IsTrue(free is { Outcome: SessionTrapOutcome.Spotted, EndsWalk: true }, free.Describe());
            Assert.IsFalse(host.TrapState(placed)!.Value.Spotted, "a free NPC marks nothing (TT4)");
            Assert.AreEqual(0, effects.Calls.Count);
            var led = traps.StepNpc(Led(), Cell(placed))!;
            Assert.IsTrue(led is { Outcome: SessionTrapOutcome.Fired }, "the PC's own trap is known to its companion: " + led.Describe());
        });
    }

    [TestMethod]
    public void An_unknown_trap_met_by_a_walker_without_a_sheet_fires_as_a_missed_spot_does()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var placed = Row(1, TrapObject, 30001, x: 60, objectFlags: TrapRules.PlacedByPcFlag | TrapRules.DontDrawFlag);
            var (_, traps) = Hosts(session, content, () => effects.Side, null, placed);
            var draws = session.QueryRandom().Draws;
            var met = traps.StepNpc(Free(), Cell(placed))!;
            Assert.IsTrue(met is { Outcome: SessionTrapOutcome.Fired, EndsWalk: true }, met.Describe());
            Assert.AreEqual(draws, session.QueryRandom().Draws, "no sheet, no spot roll");
            Assert.AreEqual(1, effects.Calls.Count);
        });
    }

    [TestMethod]
    public void A_trapped_object_an_npc_uses_fires_on_it_and_stops_the_use_and_an_untrapped_one_goes_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var door = Row(1, Scenery, 30001, 0x0000_0503, objectFlags: 0);
            var plain = Row(2, Scenery, 700);
            var (_, traps) = Hosts(session, content, () => effects.Side, null, door, plain);
            Assert.IsNull(traps.UseNpc(plain, Free()), "an ordinary use script is no trap");
            var used = traps.UseNpc(door, Free("oid:opener"))!;
            Assert.IsTrue(used is { Cause: SessionTrapCause.Use, Outcome: SessionTrapOutcome.Fired, Cancels: true }, used.Describe());
            CollectionAssert.AreEqual(new[] { "oid:opener" }, effects.Triggerers);
            effects.ByCritter = false;
            Assert.IsNull(traps.UseNpc(door, Free()), "no critter side: the trap is not consumed");
        });
    }

    [TestMethod]
    public void A_walker_with_a_sheet_rolls_its_own_spot_trap_on_an_unknown_trap_it_steps_on_and_a_step_that_did_not_begin_meets_nothing()
    {
        WithContent(content =>
        {
            AreaTraps? traps = null;
            var effects = new Effects();
            var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), traps: () => traps, sheet: MasterSheet);
            var placed = Row(1, TrapObject, 30001, x: 11, objectFlags: TrapRules.PlacedByPcFlag | TrapRules.DontDrawFlag);
            (_, traps) = Hosts(scene.Session, content, () => effects.Side, null, placed);
            Assert.IsFalse(scene.Host.StepMeetsTrap(scene.Npc, new(11, 20)), "no step began: the trap is not met");
            Assert.AreEqual(0, effects.Calls.Count);
            scene.Host.BeginStep(scene.Npc, new(11, 20), false);
            var draws = scene.Session.QueryRandom().Draws;
            Assert.IsTrue(scene.Host.StepMeetsTrap(scene.Npc, new(11, 20)), "the trap is spotted or goes off, so the walk ends");
            Assert.IsTrue(scene.Session.QueryRandom().Draws > draws, "the entry point's spot attempt rolls the NPC's own skill (a free NPC does not know the PC's plate)");
            Assert.IsFalse(scene.Host.StepMeetsTrap(scene.Npc, new(11, 20)), "one meeting per begun step");
        });
    }

    [TestMethod]
    public void An_npc_that_uses_a_trapped_object_through_the_ai_host_meets_its_trap_and_the_meeting_is_taken_once()
    {
        WithContent(content =>
        {
            AreaTraps? traps = null;
            var effects = new Effects();
            var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), traps: () => traps, sheet: MasterSheet);
            var door = Row(1, Scenery, 30001, 0x0000_0503);
            var plain = Row(2, Scenery, 700);
            (_, traps) = Hosts(scene.Session, content, () => effects.Side, null, door, plain);
            Assert.IsFalse(scene.Host.TrapMetByUse(scene.Npc, plain), "no trap: the use goes on");
            Assert.AreEqual(0, scene.Host.TakeTrapMeetings().Count);
            Assert.IsTrue(scene.Host.TrapMetByUse(scene.Npc, door), "a trap that fires stops the use");
            CollectionAssert.AreEqual(new[] { scene.Npc }, effects.Triggerers);
            Assert.AreEqual(1, scene.Host.TakeTrapMeetings().Count);
            Assert.IsFalse(scene.Host.TrapMetByUse(Arcanum.Simulation.Ai.AiSchedulerRules.Pc, door), "the hero's use is the viewer's own");
        });
    }

    [TestMethod]
    public void An_npc_s_walk_goal_ends_on_the_tile_of_the_trap_object_it_stepped_on_and_the_trap_fires_once()
    {
        WithContent(content =>
        {
            AreaTraps? traps = null;
            var effects = new Effects();
            var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (16, 20), (16, 20)), traps: () => traps, sheet: MasterSheet);
            var plate = Row(1, TrapObject, 30004, 0x0000_0201, x: 12, y: 18, objectFlags: TrapRules.DontDrawFlag);
            AreaUseScriptHost host;
            (host, traps) = Hosts(scene.Session, content, () => effects.Side, null, plate);
            scene.Host.RunRoutine(scene.Npc);
            Assert.IsNotNull(scene.Host.RunOf(scene.Npc), scene.Host.TraceOf(scene.Npc)?.Route);
            var frames = new Arcanum.World.FrameStep(scene.Session, AreaAiTestSteps.NoAi).WithAi(scene.Host);
            for (var n = 0; n < 2000 && effects.Calls.Count == 0; n++) frames.AdvanceHost(50);
            Assert.AreEqual(1, effects.Calls.Count, "the trap fired once: " + scene.Host.TraceOf(scene.Npc)?.Step);
            Assert.AreEqual(new CellCoord(12, 18), scene.Host.Tile(scene.Npc), "the step that met the trap put the NPC on its tile, short of its standpoint");
            var walk = scene.Host.RunOf(scene.Npc)!.Frames.Last(frame => frame.Goal == Arcanum.Simulation.Goals.GoalType.AttemptMove);
            Assert.AreEqual(walk.Path.Length, walk.PathIndex, "the walk goal's path index is its length: the walk ends on the trap's tile (TT9)");
            CollectionAssert.AreEqual(new[] { scene.Npc }, effects.Triggerers);
            Assert.IsTrue(host.TrapState(plate)!.Value.Removed, "an auto-removing trap is gone");
            for (var n = 0; n < 2000 && scene.Host.RunOf(scene.Npc) is not null; n++) frames.AdvanceHost(50);
            Assert.AreEqual(1, effects.Calls.Count, "the trap is gone: the walk on to the standpoint meets nothing");
            var meetings = scene.Host.TakeTrapMeetings();
            Assert.AreEqual(1, meetings.Count);
            Assert.AreEqual(0, scene.Host.TakeTrapMeetings().Count, "taken once");
        });
    }

    [TestMethod]
    public void A_companion_that_spots_a_trap_on_its_next_tile_stops_and_marks_it_for_the_player()
    {
        WithContent(content =>
        {
            AreaTraps? traps = null;
            var effects = new Effects();
            var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), companion: true, led: true, traps: () => traps, sheet: MasterSheet);
            var plate = Row(1, TrapObject, 30001, x: 11, objectFlags: TrapRules.DontDrawFlag);
            AreaUseScriptHost host;
            (host, traps) = Hosts(scene.Session, content, () => effects.Side, null, plate);
            var east = new CellCoord(11, 20);
            var blocked = false;
            for (var attempt = 0; attempt < 60 && !blocked; attempt++)
            {
                var draws = scene.Session.QueryRandom().Draws;
                blocked = scene.Host.StepBlocked(scene.Npc, east, false) == AiStepBlock.CannotStart;
                if (!blocked) Assert.IsTrue(scene.Session.QueryRandom().Draws >= draws + 2, "a missed attempt is one spot trap roll of the NPC's own");
            }
            Assert.IsTrue(blocked, "a master with Perception 20 spots the trap within 60 attempts");
            Assert.IsTrue(host.TrapState(plate)!.Value.Spotted, "a companion marks the trap: the player knows it");
            Assert.AreEqual(AiStepBlock.Clear, scene.Host.StepBlocked(scene.Npc, east, false), "a known trap is skipped by the walk check");
            StringAssert.Contains(scene.Host.TraceOf(scene.Npc)!.Step!, "spotted a trap");
        });
    }

    [TestMethod]
    public void A_free_npc_knows_every_trap_the_pc_did_not_place_and_rolls_without_marking_for_one_the_pc_placed()
    {
        WithContent(content =>
        {
            AreaTraps? traps = null;
            var effects = new Effects();
            var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), traps: () => traps, sheet: MasterSheet);
            var plain = Row(1, TrapObject, 30001, x: 11, objectFlags: TrapRules.DontDrawFlag);
            var placed = Row(2, TrapObject, 30001, x: 9, objectFlags: TrapRules.DontDrawFlag | TrapRules.PlacedByPcFlag);
            AreaUseScriptHost host;
            (host, traps) = Hosts(scene.Session, content, () => effects.Side, null, plain, placed);
            var draws = scene.Session.QueryRandom().Draws;
            Assert.AreEqual(AiStepBlock.Clear, scene.Host.StepBlocked(scene.Npc, new(11, 20), false), "it knows the plain trap: no roll");
            Assert.AreEqual(draws, scene.Session.QueryRandom().Draws);
            var spotted = false;
            for (var attempt = 0; attempt < 60 && !spotted; attempt++)
            {
                var before = scene.Session.QueryRandom().Draws;
                spotted = scene.Host.StepBlocked(scene.Npc, new(9, 20), false) == AiStepBlock.CannotStart;
                Assert.IsTrue(scene.Session.QueryRandom().Draws > before, "the trap the PC placed is unknown to it: one roll each attempt");
            }
            Assert.IsTrue(spotted, "a master spots it within 60 attempts, and the step is blocked");
            Assert.IsFalse(host.TrapState(placed)!.Value.PlacedByPc && host.TrapState(placed)!.Value.Spotted, "a free NPC marks nothing (TT4)");
        });
    }

    [TestMethod]
    public void Without_a_trap_host_or_a_sheet_an_npc_walks_as_before()
    {
        WithContent(content =>
        {
            var effects = new Effects();
            var plate = Row(1, TrapObject, 30001, x: 11, objectFlags: TrapRules.DontDrawFlag);
            var noHost = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), sheet: MasterSheet);
            Assert.AreEqual(AiStepBlock.Clear, noHost.Host.StepBlocked(noHost.Npc, new(11, 20), false));
            AreaTraps? traps = null;
            var noSheet = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), companion: true, led: true, traps: () => traps);
            (_, traps) = Hosts(noSheet.Session, content, () => effects.Side, null, plate);
            var draws = noSheet.Session.QueryRandom().Draws;
            Assert.AreEqual(AiStepBlock.Clear, noSheet.Host.StepBlocked(noSheet.Npc, new(11, 20), false));
            Assert.AreEqual(draws, noSheet.Session.QueryRandom().Draws, "an NPC with no sheet has no skill to roll");
        });
    }
}
