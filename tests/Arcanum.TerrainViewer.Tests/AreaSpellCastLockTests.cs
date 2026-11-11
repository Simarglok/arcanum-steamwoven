using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-037 (MA-6): the shown map's portals and containers as a spell's lock flags read them — the session's lock identity and baseline, the lock level of the row (slot 37), the lock flags the session holds
/// in the target object the area tests read — and a Magelock and an Unlocking Cantrip through the cast host on the cast map (invented rows, no original data).</summary>
[TestClass]
public sealed class AreaSpellCastLockTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // The common block of AreaSpellCastObjectDamageTests plus the lock level (37) and the key (38).
    private static AreaObjectRecord Row(int index, int type, long flags, long level, long x) =>
        new(Key(index), 0, type, 1000, "p", "p", "r", "s", AreaScope.Core, null, x, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x30000400), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 12), Field(27, 0), Field(28, 0),
            Field(29, 2), new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [25])], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null), Field(36, flags), Field(37, level), Field(38, 0), Field(24, -1),
        ], true, true, true);

    private static AreaStepSnapshot Steps()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 24; x++) for (var y = 0; y < 24; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    private sealed class NoEvents : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed record Fixture(GameSessionOwner Session, AreaSpellCastMap Map, SessionSpellCaster Caster, AreaObjectRecord[] Rows, SessionSpellRecord Unlock, SessionSpellRecord Magelock);

    // A locked door of level 40 (0), a locked chest of level 5 (1), a scenery barrel (2) and a held, unlocked door (3).
    private static Fixture Create()
    {
        var session = ConversationSessionFixture.Create();
        AreaObjectRecord[] rows = [Row(0, 1, LockRules.LockedBit, 40, 10), Row(1, 2, LockRules.LockedBit, 5, 12), Row(2, 3, 0, 0, 14), Row(3, 1, LockRules.MagicallyHeldBit, 0, 16)];
        var baselines = rows.Where(r => r.Type == 1).ToDictionary(r => r.Key, r => ($"sector:1:{r.Key.SourceIndex}", new SessionOpenableBaseline(OpenableKind.Portal, (int)r.EffectiveFields.Single(f => f.Slot == 36).Number!.Value, 0, 0)));
        var targets = new AreaObjectTargets(1, rows, baselines, null);
        var map = new AreaSpellCastMap(session, 1, Steps(), rows, new AreaCombatHost(new AreaInventoryHost([])), () => null, () => null, objects: row => targets.Target(session, row.Key));
        SessionSpellRecord Spell(int number, int field, uint flag, bool on) => SessionSpellRecords.Translate(new SpellRecord
        {
            Number = number, MinimumWillpower = 0, Cost = 0, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0, DurationStat = -1, DurationStatLevel = 0, DurationStatModifier = 0, DurationTriggerCount = 0,
            ResistStat = -1, ResistModifier = 0, Range = 99, IsTechnology = false, ChargesFromCells = false, SkipsBeginCost = false, Info = SpellInfo.Neutral, Enabled = true, NoResist = false, NoReflect = true,
            CastingAnimation = -1, Missile = -1, CasterEyeCandy = [-1, -1, -1, -1, -1], TargetEyeCandy = [-1, -1, -1, -1, -1], NoStack = 0,
            CancelsSpellFlags = 0, CancelsEnvironmentSpellFlags = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, ItemTriggers = 0,
            AiPriorities = Enumerable.Repeat(-1, 10).ToArray(), AiDefensiveSecond = 0, LinesPresent = [true, true, false, false, false], KeysRead = [],
            Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)],
            Components = [new(SpellAction.Begin, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, on))],
            HasDamage = false, SpellFlagsSet = 0, AreaLine = null, ComponentLines = [],
        });
        var unlock = Spell(1, 46, 1, false);
        var magelock = Spell(75, 46, 4, true);
        var records = new[] { unlock, magelock }.ToDictionary(r => r.Data.Number);
        return new(session, map, new SessionSpellCaster(session, n => records.GetValueOrDefault(n), map, new NoEvents(), _ => null), rows, unlock, magelock);
    }

    [TestMethod]
    public void A_portal_row_answers_its_session_identity_baseline_and_lock_level_and_a_scenery_row_none()
    {
        var f = Create();
        var door = f.Map.Openable(f.Map.Id(f.Rows[0]))!;
        Assert.AreEqual(("sector:1:0", 40), (door.Openable.Entity, door.LockLevel));
        Assert.AreEqual(new SessionOpenableBaseline(OpenableKind.Portal, LockRules.LockedBit, 0, 0), door.Baseline);
        var chest = f.Map.Openable(f.Map.Id(f.Rows[1]));
        Assert.AreEqual(OpenableKind.Container, chest!.Baseline.Kind);
        Assert.AreEqual(5, chest.LockLevel);
        Assert.IsNull(f.Map.Openable(f.Map.Id(f.Rows[2])), "scenery has no lock");
        Assert.IsNull(f.Map.Openable("row:nowhere"));
    }

    [TestMethod]
    public void The_target_objects_carry_the_lock_flags_the_session_holds_for_the_area_tests()
    {
        var f = Create();
        var id = f.Map.Id(f.Rows[0]);
        Assert.AreEqual(LockRules.LockedBit, f.Map.Object(id)!.PortalFlags);
        Assert.AreEqual(LockRules.MagicallyHeldBit, f.Map.Object(f.Map.Id(f.Rows[3]))!.PortalFlags, "the held door the data stores");
        var door = f.Map.Openable(id)!;
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteOpenableFlag(f.Session.Version, door.Openable, door.Baseline, LockRules.MagicallyHeldBit, true).Disposition);
        Assert.AreEqual(LockRules.LockedBit | LockRules.MagicallyHeldBit, f.Map.Object(id)!.PortalFlags, "the session's write is read over the baseline");
        Assert.AreEqual(SpellTargetObject.MagicallyHeld, f.Map.Object(id)!.PortalFlags & SpellTargetObject.MagicallyHeld);
    }

    [TestMethod]
    public void Magelock_and_an_unlocking_cantrip_write_the_session_state_of_the_row_the_hero_aimed_at()
    {
        var f = Create();
        var id = f.Map.Id(f.Rows[0]);
        var door = f.Map.Openable(id)!;
        // The hero is off this map's cells (no hero cell), so the caster's run objects are built by hand.
        var to = new SpellRunObject(id, SpellTargetObject.Portal);
        SpellRun Run(SessionSpellRecord record, int aptitude) => new(0, record.Data.Number, SpellRunAction.Begin, new(SessionSpellCaster.Hero, SpellTargetObject.Pc, aptitude), new(SessionSpellCaster.Hero, SpellTargetObject.Pc, aptitude), new(5, 5), to, new(10, 20), null, SpellRunFlags.Active);
        f.Caster.Apply(Run(f.Magelock, 100), 0, new(to, new(10, 20)));
        Assert.AreEqual(OpenAttempt.MagicallyHeld, f.Session.AttemptOpen(door.Openable, door.Baseline, false));
        // The lock level 40 is above the aptitude 30: the lock holds; at 40 it opens, the held bit stays.
        f.Caster.Apply(Run(f.Unlock, 30), 0, new(to, new(10, 20)));
        Assert.AreEqual(LockRules.LockedBit | LockRules.MagicallyHeldBit, f.Session.QueryOpenable(door.Openable, door.Baseline).Flags);
        f.Caster.Apply(Run(f.Unlock, 40), 0, new(to, new(10, 20)));
        Assert.AreEqual(LockRules.MagicallyHeldBit, f.Session.QueryOpenable(door.Openable, door.Baseline).Flags);
        Assert.IsFalse(f.Caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), string.Join(" | ", f.Caster.Log.Select(e => $"{e.Kind}:{e.Value}:{e.Object}")));
        Assert.AreEqual(0, f.Session.QueryEvents().Count, "no Lock event");
    }
}
