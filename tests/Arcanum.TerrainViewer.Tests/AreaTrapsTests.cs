using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Arcanum.TerrainViewer.Core;
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
    private static AreaObjectRecord Row(int index, int type, int script, uint counters = 0, long x = 0, uint objectFlags = 0)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Use, [0, counters, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, objectFlags, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x == 0 ? 10 + index : x, 20,
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
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private sealed class Effects(int spellFlags = 0)
    {
        public List<(AreaObjectRecord Row, NativeTrapRun Run, int Counters, int Flags)> Calls { get; } = [];
        public int SpellFlags { get; set; } = spellFlags;
        public AreaTrapEffects Side => new((row, run, counters, flags) => { Calls.Add((row, run, counters, flags)); return SessionTrapEffectResult.None; }, SpellFlags);
    }

    private static (AreaUseScriptHost Host, AreaTraps Traps) Hosts(GameSessionOwner session, ScriptContent content, Func<AreaTrapEffects?> effects,
        HashSet<AreaObjectKey>? gone, params AreaObjectRecord[] shown)
    {
        var host = new AreaUseScriptHost(session, 1, shown, [], content);
        var traps = new AreaTraps(session, host, shown, row => gone?.Contains(row.Key) == true, effects);
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
            Assert.IsTrue(used.Trap is { Outcome: SessionTrapOutcome.Fired, Cancels: true, Effect: not null, NotReady: null }, used.Detail);
            Assert.AreEqual(1, effects.Calls.Count);
            Assert.AreEqual((30001, 0x0000_0503, 0), (effects.Calls[0].Run.Script, effects.Calls[0].Counters, effects.Calls[0].Flags));
            Assert.AreSame(chest, effects.Calls[0].Row);
            // No auto-removal: the trap stays and fires again.
            Assert.AreEqual(true, host.IsTrap(chest));
            Assert.AreEqual(SessionTrapOutcome.Fired, host.Run(chest, session.Rules.Scripts).Trap?.Outcome);
            Assert.AreEqual(2, effects.Calls.Count);
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
    public void The_shot_and_the_empty_trap_apply_no_effect()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var effects = new Effects();
            var (_, traps) = Hosts(session, content, () => effects.Side, null);
            var shot = traps.Use(Row(1, Scenery, 30002))!;
            Assert.IsTrue(shot is { Outcome: SessionTrapOutcome.Fired, Effect: null, NotReady: not null }, shot.Describe());
            var none = traps.Use(Row(2, Scenery, 30007))!;
            Assert.IsTrue(none is { Outcome: SessionTrapOutcome.Fired, Effect: null, NotReady: null, Cancels: true }, none.Describe());
            Assert.AreEqual(0, effects.Calls.Count);
        });
    }

    [TestMethod]
    public void The_walk_check_spots_an_unknown_trap_object_only_with_the_detect_flag()
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
            var unread = traps.WalkCheck(new(40, 20));
            Assert.IsTrue(unread is { Spotted: null, RollsNotReady: 2 }, "the spot trap roll is S10's");
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
}
