using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-008: the viewer's attackable objects over invented rows (no original data): a door, a window, a container, a
/// junk pile, scenery and a trap, their baselines from stored fields, and what the session destroyed or switched off.</summary>
[TestClass]
public sealed class AreaObjectTargetsTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // The common block (current art 0, flags 18, spell flags 19, name 21, description 22, AC 25, hit points 26..28, material 29,
    // resistances 30, scripts 31) and the type block's flags (36), a scenery's respawn delay (38) and the destroyed ART (24).
    private static AreaObjectRecord Row(int index, int type, int prototype = 1000, uint art = 0x30000400, int points = 12, int material = 2,
        long flags = 0, long respawn = 0, long destroyedArt = -1, long objectFlags = 0) =>
        new(Key(index), 0, type, prototype, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, art), Field(18, objectFlags), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, points), Field(27, 0), Field(28, 0),
            Field(29, material), new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [25]), new(1, [50])], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null), Field(36, flags), Field(38, respawn), Field(24, destroyedArt),
        ], true, true, true);

    private static readonly SessionOpenableBaseline DoorLock = new(OpenableKind.Portal, LockRules.LockedBit, 7, 0);

    private static AreaObjectTargets Targets(params AreaObjectRecord[] rows) => new(1, rows,
        rows.Where(r => r.Type == 1).ToDictionary(r => r.Key, r => ($"sector:1:{r.Key.SourceIndex}", DoorLock)), null);

    [TestMethod]
    public void EachKindGetsItsBaselineFromItsFields()
    {
        using var session = ConversationSessionFixture.Create();
        var door = Row(0, 1);
        var window = Row(1, 1, art: 0x30000000);
        var chest = Row(2, 2, flags: 0x11);
        var pile = Row(3, 2, prototype: SessionObjectFacts.JunkPilePrototypeFile);
        var barrel = Row(4, 3, flags: 0x22, respawn: 10, destroyedArt: 0x30000100);
        var trap = Row(5, 17, destroyedArt: 0x30000200, objectFlags: 0x400000);
        var targets = Targets(door, window, chest, pile, barrel, trap);
        Assert.AreEqual(6, targets.Count);

        var hitDoor = targets.Target(session, door.Key)!;
        Assert.AreEqual(("sector:1:0", DamageableObjectKind.Portal, (string?)null), (hitDoor.Entity, hitDoor.Kind, hitDoor.Refusal));
        var baseline = hitDoor.Object!.Baseline;
        Assert.AreEqual((12, ObjectMaterial.Wood, false, (SessionOpenableBaseline?)DoorLock), (baseline.Points, baseline.Material, baseline.Window, baseline.Openable));
        Assert.AreEqual(new DamageResistances(25, 0, 0, 50), baseline.Resistances, "stored normal 25, fire 50 in the resistance order");
        Assert.IsTrue(targets.Target(session, window.Key)!.Object!.Baseline.Window, "bit 10 of the portal ART clear");

        var box = targets.Target(session, chest.Key)!.Object!.Baseline;
        Assert.AreEqual((DamageableObjectKind.Container, false, new Arcanum.Simulation.Movement.CellCoord(12, 20)), (box.Kind, box.JunkPile, box.Cell));
        Assert.AreEqual(new SessionOpenableBaseline(OpenableKind.Container, 0x11, 0, 0), box.Openable);
        Assert.IsTrue(targets.Target(session, pile.Key)!.Object!.Baseline.JunkPile);

        var scenery = targets.Target(session, barrel.Key)!.Object!.Baseline;
        Assert.AreEqual((true, true, true, 10), (scenery.Respawning, scenery.Busted, scenery.HasDestroyedArt, scenery.RespawnDelay));
        var snare = targets.Target(session, trap.Key)!.Object!.Baseline;
        Assert.AreEqual((DamageableObjectKind.Trap, true, true, false), (snare.Kind, snare.Invulnerable, snare.HasDestroyedArt, snare.Busted));

        Assert.IsNull(targets.Target(session, Key(9)), "an object outside the rows");
        Assert.AreEqual(0, new AreaObjectTargets(0, [door], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null).Count, "no runtime map");
        Assert.AreEqual(0, new AreaObjectTargets(1, [door], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null).Count,
            "a portal the lock domain does not list");
    }

    [TestMethod]
    public void TheCallerSaysWhichUseScriptIsATrap()
    {
        // ARC-COMBAT-011: the baseline carries the caller's trap answer for its row; without one no object is trapped.
        using var session = ConversationSessionFixture.Create();
        var chest = Row(0, 2);
        var barrel = Row(1, 3);
        var targets = Targets(chest, barrel);
        Assert.IsFalse(targets.Target(session, chest.Key)!.Object!.Baseline.Trapped);
        bool Trapped(AreaObjectRecord row) => row.Key == chest.Key;
        Assert.IsTrue(targets.Target(session, chest.Key, Trapped)!.Object!.Baseline.Trapped);
        Assert.IsFalse(targets.Target(session, barrel.Key, Trapped)!.Object!.Baseline.Trapped);
    }

    [TestMethod]
    public void AnObjectWhoseFieldsDoNotReadIsARefusal()
    {
        using var session = ConversationSessionFixture.Create();
        var broken = Row(0, 3) with { EffectiveFields = Row(0, 3).EffectiveFields.Where(f => f.Slot != 38).ToArray() };
        var target = new AreaObjectTargets(1, [broken], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null).Target(session, broken.Key)!;
        Assert.AreEqual((DamageableObjectKind.Scenery, (SessionObjectTarget?)null), (target.Kind, target.Object));
        StringAssert.Contains(target.Refusal, "FieldUnresolved");
        var stone = Row(1, 3, material: 99);
        StringAssert.Contains(new AreaObjectTargets(1, [stone], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null)
            .Target(session, stone.Key)!.Refusal, "bounds");
    }

    // ARC-WORLD-2039 (FM1): an NPC a script's teleport moved to another map is gone from the shown map's lists; an away change naming the shown map itself (another window of it) is not.
    [TestMethod]
    public void AnNpcAwayOnAnotherMapIsGoneFromTheShownMap()
    {
        using var session = ConversationSessionFixture.Create();
        var npc = AreaHeartbeatScriptTests.Row(1, 16, 781);
        var targets = new AreaObjectTargets(1, [npc], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null);
        var entity = AreaHeartbeatScriptTests.Entity(1);
        Assert.AreEqual(0, targets.Gone(session).Count);
        void Away(long map) => Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new(1), AreaCompanionPlacement.MapKey(entity), map)]).Disposition);
        Away(1);
        Assert.AreEqual(0, targets.Gone(session).Count, "standing on the shown map's own number is not away");
        Away(9);
        CollectionAssert.AreEqual(new[] { npc.Key }, targets.Gone(session).ToArray());
        Away(1);
        Assert.AreEqual(0, targets.Gone(session).Count, "back on the map");
    }

    [TestMethod]
    public void ADestroyedOrSwitchedOffObjectIsGoneAndNoLongerATarget()
    {
        using var session = ConversationSessionFixture.Create();
        var door = Row(0, 1);
        var barrel = Row(4, 3, flags: 0x20, respawn: 10);
        var statue = Row(5, 3);
        var targets = Targets(door, barrel, statue);
        Assert.AreEqual(0, targets.Gone(session).Count);
        void Condition(string entity, SessionObjectCondition condition) => Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            [new(new(1), MapChangeKey.ForEntity(entity, GameSessionOwner.ObjectConditionProperty), (long)condition)]).Disposition);
        Condition("sector:1:0", SessionObjectCondition.Destroyed);
        Condition(AreaUseScriptHost.EntityId(barrel), SessionObjectCondition.Off);
        Condition(AreaUseScriptHost.EntityId(statue), SessionObjectCondition.Busted);
        CollectionAssert.AreEquivalent(new[] { door.Key, barrel.Key }, targets.Gone(session).ToArray(), "a busted scenery still stands");
        CollectionAssert.AreEqual(new[] { statue.Key }, targets.Busted(session).ToArray(), "ARC-RENDER-033: a busted scenery takes its destroyed ART");
        Assert.IsNull(targets.Target(session, door.Key));
        Assert.IsNull(targets.Target(session, barrel.Key));
        Assert.IsNotNull(targets.Target(session, statue.Key));
        // ARC-WORLD-065: the same set while nothing it reads changed, so overlays cached on it stay valid.
        var gone = targets.Gone(session);
        Assert.AreSame(gone, targets.Gone(session));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            [new(new(1), MapChangeKey.ForEntity("other", "use.lf"), 1)]).Disposition);
        Assert.AreSame(gone, targets.Gone(session), "an unrelated change keeps the set");
        Condition(AreaUseScriptHost.EntityId(statue), SessionObjectCondition.Destroyed);
        Assert.AreEqual(3, targets.Gone(session).Count);
    }

    [TestMethod]
    public void TheOneObjectQuestionAgreesWithTheWholeSet()
    {
        // ARC-RENDER-3126: the AI asks about one object on every heartbeat, so IsGone must answer as Gone lists it without making the set.
        using var session = ConversationSessionFixture.Create();
        var door = Row(0, 1);
        var barrel = Row(4, 3, flags: 0x20, respawn: 10);
        var hidden = Row(5, 3, objectFlags: 0x2);
        var chest = Row(2, 2, flags: 0x11);
        var npc = AreaHeartbeatScriptTests.Row(1, 16, 781);
        var targets = Targets(door, barrel, hidden, chest, npc);
        var entity = AreaHeartbeatScriptTests.Entity(1);
        void Change(MapChangeKey key, long? value) => Assert.AreEqual(GameSessionDisposition.Changed,
            session.ApplyChanges(session.Version, null, [new(new(1), key, value)]).Disposition);
        void Agree(string step, params AreaObjectKey[] expected)
        {
            foreach (var key in new[] { door.Key, barrel.Key, hidden.Key, chest.Key, npc.Key, Key(9) })
                Assert.AreEqual(expected.Contains(key), targets.IsGone(session, key), $"{step}: {key.SourceIndex}");
            CollectionAssert.AreEquivalent(expected, targets.Gone(session).ToArray(), step);
        }
        Agree("the data stores the scenery off", hidden.Key);
        Change(SessionItemOff.Key(AreaUseScriptHost.EntityId(hidden)), 0);
        Agree("a script turned it on");
        Change(SessionItemOff.Key(AreaUseScriptHost.EntityId(barrel)), 1);
        Change(MapChangeKey.ForEntity("sector:1:0", GameSessionOwner.ObjectConditionProperty), (long)SessionObjectCondition.Destroyed);
        Change(AreaCompanionPlacement.MapKey(entity), 9);
        Agree("off, destroyed and away", barrel.Key, door.Key, npc.Key);
        Change(AreaCompanionPlacement.MapKey(entity), 1);
        Change(MapChangeKey.ForEntity(entity, AreaUseScriptHost.DestroyedProperty), 1);
        Agree("an NPC a script destroyed", barrel.Key, door.Key, npc.Key);
    }

    [TestMethod]
    public void TheSceneriesAScriptToggledAreListedWithTheirStateForTheirLights()
    {
        // ARC-SCRIPT-2585 (ledger OT10): only a scenery with a record of a toggle is listed (true off, false on over a data that stores it off); a scenery nobody toggled keeps the lights the map data loaded.
        using var session = ConversationSessionFixture.Create();
        var lamp = Row(0, 3);
        var braziers = Row(1, 3, objectFlags: 0x2);
        var statue = Row(2, 3);
        var chest = Row(3, 2, flags: 0x11);
        var targets = Targets(lamp, braziers, statue, chest);
        Assert.AreEqual(0, targets.SceneryOffStates(session).Count);
        Assert.IsNull(session.QueryObjectOffRecord(new(1), AreaUseScriptHost.EntityId(lamp)));
        void Toggle(AreaObjectRecord row, long? value) => Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            [new(new(1), SessionItemOff.Key(AreaUseScriptHost.EntityId(row)), value)]).Disposition);
        Toggle(lamp, 1); Toggle(braziers, 0); Toggle(chest, 1);
        var states = targets.SceneryOffStates(session);
        CollectionAssert.AreEquivalent(new[] { (lamp.Key, true), (braziers.Key, false) }, states.Select(p => (p.Key, p.Value)).ToArray(), "a container's record is no scenery's");
        Assert.AreEqual(true, session.QueryObjectOffRecord(new(1), AreaUseScriptHost.EntityId(lamp)));
        Assert.AreSame(states, targets.SceneryOffStates(session));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new(1), MapChangeKey.ForEntity("other", "use.lf"), 1)]).Disposition);
        Assert.AreSame(states, targets.SceneryOffStates(session), "an unrelated change keeps the dictionary, so the lighting made of it stays valid");
        Toggle(lamp, null);
        CollectionAssert.AreEqual(new[] { (braziers.Key, false) }, targets.SceneryOffStates(session).Select(p => (p.Key, p.Value)).ToArray(), "the flag back at the data's state leaves no record");
    }

    [TestMethod]
    public void TheBustedTestReadsEachKindsOwnBit()
    {
        // ARC-AI-1636 (0x43F630): a portal or container reads the lock domain's busted bit 0x80, a scenery or trap its busted condition (the stored flag 0x2 or a recorded break); a destroyed object is not busted.
        using var session = ConversationSessionFixture.Create();
        var door = Row(0, 1);
        var bustedChest = Row(1, 2, flags: 0x80);
        var chest = Row(2, 2, flags: 0x11);
        var storedBusted = Row(3, 3, flags: 0x2);
        var statue = Row(4, 3);
        var snare = Row(5, 17, flags: 0x2);
        var spared = Row(6, 17);
        var targets = Targets(door, bustedChest, chest, storedBusted, statue, snare, spared);
        Assert.IsFalse(targets.IsBusted(session, door.Key), "a whole door");
        Assert.IsTrue(targets.IsBusted(session, bustedChest.Key), "container flag 0x80");
        Assert.IsFalse(targets.IsBusted(session, chest.Key));
        Assert.IsTrue(targets.IsBusted(session, storedBusted.Key), "scenery type flag 0x2");
        Assert.IsFalse(targets.IsBusted(session, statue.Key));
        Assert.IsTrue(targets.IsBusted(session, snare.Key), "trap type flag 0x2");
        Assert.IsFalse(targets.IsBusted(session, spared.Key));
        Assert.IsFalse(targets.IsBusted(session, Key(9)), "a row outside the targets");

        void Condition(string entity, SessionObjectCondition condition) => Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            [new(new(1), MapChangeKey.ForEntity(entity, GameSessionOwner.ObjectConditionProperty), (long)condition)]).Disposition);
        Condition(AreaUseScriptHost.EntityId(statue), SessionObjectCondition.Busted);
        Assert.IsTrue(targets.IsBusted(session, statue.Key), "a break the session recorded");
        Condition(AreaUseScriptHost.EntityId(statue), SessionObjectCondition.Destroyed);
        Assert.IsFalse(targets.IsBusted(session, statue.Key), "a destroyed object answers 0");
        Condition(AreaUseScriptHost.EntityId(storedBusted), SessionObjectCondition.Off);
        Assert.IsFalse(targets.IsBusted(session, storedBusted.Key), "a switched-off one no longer a target");

        // A window the hits broke: the lock domain's busted bit on frame 0 (the portal's baseline carries the map data's flags).
        var window = Row(7, 1, art: 0x30000000);
        var bustedWindow = new AreaObjectTargets(1, [window], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)> { [window.Key] = ("sector:1:7", new(OpenableKind.Portal, LockRules.BustedBit, 0, 0)) }, null);
        Assert.IsTrue(bustedWindow.IsBusted(session, window.Key), "a portal baseline with the busted bit");
    }

    [TestMethod]
    public void TheStatusLineNamesTheBreak()
    {
        var resolution = new AttackResolution(AttackRefusal.None, AttackOutcome.Hit, AttackMissCause.None, HitLocation.Torso, false, false, null, null, null,
            FateUse.None, false, new(DamageRecipient.Target, DamageByType.Zero, DamageByType.Zero, 4, 0, 0, DamageMultiplier.None, false,
                CriticalHitEffects.None, CriticalMissEffects.None, false, DamagePrevention.None), []);
        SessionObjectAttackResult Hit(ObjectHitOutcome hit) => new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, hit, null, null, null,
            SessionObjectHostSteps.None, GameSessionFailure.None, default);
        Assert.AreEqual("hit: 4 damage", AreaObjectTargets.Describe(Hit(new(2, 6, false, ObjectBreak.None, 6))));
        Assert.AreEqual("hit: 4 damage, destroyed", AreaObjectTargets.Describe(Hit(new(8, 12, true, ObjectBreak.Destroyed, 12))));
        // ARC-SCRIPT-2722: an item's deleting break that its destroy point refused.
        SessionObjectAttackResult ItemHit(SessionObjectHostSteps host, bool kept) => new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null,
            new ItemDurabilityResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, Arcanum.Simulation.Items.ItemBreakStep.Deleted, null, default) { DestroyKept = kept }, null, null,
            host, GameSessionFailure.None, default);
        Assert.AreEqual("hit: the item is destroyed", AreaObjectTargets.Describe(ItemHit(SessionObjectHostSteps.DestroySound, false)));
        Assert.AreEqual("hit: the destroy is refused, the item stays", AreaObjectTargets.Describe(ItemHit(SessionObjectHostSteps.DestroySound | SessionObjectHostSteps.DestroyKept, true)));
        Assert.AreEqual("hit: 4 damage, the window is busted", AreaObjectTargets.Describe(Hit(new(8, 12, true, ObjectBreak.WindowBusted, 12))));
        // ARC-COMBAT-011: a trapped object's break names the trap the viewer does not run.
        Assert.AreEqual("hit: 4 damage, destroyed — its trap goes off", AreaObjectTargets.Describe(Hit(new(8, 12, true, ObjectBreak.Destroyed, 12))
            with { Host = SessionObjectHostSteps.TrapFires }));
        // ARC-SCRIPT-2686: a destroy the object's destroy point refused is not called a destroy.
        Assert.AreEqual("hit: 4 damage, the destroy is refused, the object stays", AreaObjectTargets.Describe(Hit(new(8, 12, true, ObjectBreak.Destroyed, 12))
            with { Host = SessionObjectHostSteps.DestroyKept }));
        Assert.AreEqual("hit: 4 damage, the container breaks, but it is not destroyed", AreaObjectTargets.Describe(Hit(new(8, 12, true, ObjectBreak.ContainerSpilled, 12))
            with { Host = SessionObjectHostSteps.DestroyKept }));
        Assert.AreEqual("attack refused: ObjectUnavailable", AreaObjectTargets.Describe(new(GameSessionDisposition.Rejected, GameSessionFailure.ObjectUnavailable,
            null, null, null, null, null, SessionObjectHostSteps.None, GameSessionFailure.None, default)));
    }
}
