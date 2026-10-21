using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
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
        Assert.IsNull(targets.Target(session, door.Key));
        Assert.IsNull(targets.Target(session, barrel.Key));
        Assert.IsNotNull(targets.Target(session, statue.Key));
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
        Assert.AreEqual("hit: 4 damage, the window is busted", AreaObjectTargets.Describe(Hit(new(8, 12, true, ObjectBreak.WindowBusted, 12))));
        Assert.AreEqual("attack refused: ObjectUnavailable", AreaObjectTargets.Describe(new(GameSessionDisposition.Rejected, GameSessionFailure.ObjectUnavailable,
            null, null, null, null, null, SessionObjectHostSteps.None, GameSessionFailure.None, default)));
    }
}
