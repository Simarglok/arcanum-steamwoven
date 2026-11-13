using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-255 (GT195): damage that is no hero attack — a script's, a spell's, a burn's, a trap's — reaches a door, window, container,
/// scenery or trap of the shown map through the cast map's object rules (invented rows, no original data): the material reduction, the stored
/// resistances, the hit points and the break at 0.</summary>
[TestClass]
public sealed class AreaSpellCastObjectDamageTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // As AreaObjectTargetsTests: the common block (hit points 26, material 29, resistances 30) and the type block's flags (36).
    private static AreaObjectRecord Row(int index, int type, int points = 12, int material = 2, long flags = 0, long x = 10, long y = 20) =>
        new(Key(index), 0, type, 1000, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x30000400), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, points), Field(27, 0), Field(28, 0),
            Field(29, material), new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [25])], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null), Field(36, flags), Field(38, 0), Field(24, -1),
        ], true, true, true);

    private static readonly SessionOpenableBaseline DoorLock = new(OpenableKind.Portal, 0, 0, 0);

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

    private sealed record Fixture(GameSessionOwner Session, AreaSpellCastMap Map, AreaObjectTargets Targets, SessionSpellCaster Caster, AreaObjectRecord[] Rows);

    // A wooden door (0, 12 hit points, wood reduces 5), an iron-bound chest (1, 12, metal reduces 10) and a barrel (2, scenery, 12, cloth).
    private static Fixture Create()
    {
        var session = ConversationSessionFixture.Create();
        AreaObjectRecord[] rows = [Row(0, 1, material: 2), Row(1, 2, material: 5, x: 12), Row(2, 3, material: 7, x: 14), Row(3, 16, x: 16)];
        var targets = new AreaObjectTargets(1, rows, rows.Where(r => r.Type == 1).ToDictionary(r => r.Key, r => ($"sector:1:{r.Key.SourceIndex}", DoorLock)), null);
        var combat = new AreaCombatHost(new AreaInventoryHost([]));
        var map = new AreaSpellCastMap(session, 1, Steps(), rows, combat, () => null, () => null, objects: row => targets.Target(session, row.Key));
        return new(session, map, targets, new SessionSpellCaster(session, _ => null, map, new NoEvents(), _ => null), rows);
    }

    [TestMethod]
    public void ADoorAndAChestAnswerTheirMaterialAndKindAndAnNpcRowAnswersNone()
    {
        var f = Create();
        var (door, doorKind) = f.Map.ObjectCombat(f.Map.Id(f.Rows[0]))!.Value;
        Assert.AreEqual((AttackTargetKind.Portal, ObjectMaterial.Wood, 25), (doorKind, door.Material, door.Resistances.Normal));
        var (chest, chestKind) = f.Map.ObjectCombat(f.Map.Id(f.Rows[1]))!.Value;
        Assert.AreEqual((AttackTargetKind.Container, ObjectMaterial.Metal), (chestKind, chest.Material));
        Assert.AreEqual(AttackTargetKind.OtherObject, f.Map.ObjectCombat(f.Map.Id(f.Rows[2]))!.Value.Kind);
        Assert.IsNull(f.Map.ObjectCombat(f.Map.Id(f.Rows[3])), "an NPC row is no object here");
        Assert.IsNull(f.Map.ObjectCombat("row:nowhere"));
        Assert.IsNull(new AreaSpellCastMap(f.Session, 1, Steps(), f.Rows, new AreaCombatHost(new AreaInventoryHost([])), () => null, () => null).ObjectCombat(f.Map.Id(f.Rows[0])),
            "without the object host such damage is not ready");
    }

    [TestMethod]
    public void AScriptsDamageToADoorLosesTheMaterialValueAndBreaksTheDoorAtZeroHitPoints()
    {
        var f = Create();
        var id = f.Map.Id(f.Rows[0]);
        // 10 normal damage, unresistable (the stored 25 % is skipped) and wood's 5 taken: the door loses what is left.
        var first = f.Caster.ApplyScriptDamage(id, 10, 0, unresistable: true);
        Assert.IsTrue(first.Applied);
        Assert.AreEqual(5, first.HitPointDamage, "10 less wood's 5; unresistable skips the stored resistance");
        var state = f.Session.QueryObject(f.Targets.Target(f.Session, f.Rows[0].Key)!.Object!);
        Assert.AreEqual((5, SessionObjectCondition.Whole), (state.Damage, state.Condition));

        var hits = f.Map.TakeObjectHits();
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual((id, 5, false), (hits[0].Id, hits[0].Hit.Damage, hits[0].Hit.Broken));
        Assert.AreEqual(0, f.Map.TakeObjectHits().Count, "taken once");
        // ARC-COMBAT-1138: the floater of the hit reads what the door lost (5), later hits what they added, not the door's total.
        Assert.AreEqual(5, AreaCombatFloaters.Of(hits[0]).HitPoints);
        Assert.IsTrue(AreaCombatFloaters.Of(hits[0]).ObjectStep);

        // 17 more (12 after the wood) bring the door to 0: it is destroyed.
        var last = f.Caster.ApplyScriptDamage(id, 17, 0, unresistable: true);
        Assert.IsTrue(last.Applied);
        Assert.AreEqual(ObjectBreak.Destroyed, f.Map.TakeObjectHits().Single().Hit.Break);
        Assert.IsNull(f.Targets.Target(f.Session, f.Rows[0].Key), "a destroyed door is no target any more");
        Assert.IsTrue(f.Targets.Gone(f.Session).Contains(f.Rows[0].Key), "and the view stops drawing it");
        Assert.IsFalse(f.Caster.ApplyScriptDamage(id, 5, 0, unresistable: true).Applied, "a destroyed object takes nothing");
    }

    [TestMethod]
    public void AnAmountThatTheMaterialTakesAllOfDoesNothing()
    {
        var f = Create();
        var hit = f.Caster.ApplyScriptDamage(f.Map.Id(f.Rows[1]), 8, 0, unresistable: true);
        Assert.AreEqual(0, hit.HitPointDamage, "metal's 10 cover 8 (the step floors at 0)");
        Assert.AreEqual(0, f.Map.TakeObjectHits().Count);
        Assert.AreEqual(0, f.Session.QueryObject(f.Targets.Target(f.Session, f.Rows[1].Key)!.Object!).Damage);
    }

    [TestMethod]
    public void TheEntityOfAnObjectFindsItsCastIdThroughItsKey()
    {
        var f = Create();
        var door = f.Targets.KeyOfEntity("sector:1:0");
        Assert.AreEqual(f.Rows[0].Key, door);
        Assert.AreEqual(f.Map.Id(f.Rows[0]), f.Map.IdOf(door!.Value));
        Assert.IsNull(f.Targets.KeyOfEntity("sector:1:99"));
        Assert.IsNull(f.Targets.KeyOfEntity(Key(3).ObjectIdSha256), "an NPC is no attackable object");
    }

    [TestMethod]
    public void TwoStaticRowsOfDifferentSectorsWithTheSameIndexHaveTheirOwnIds()
    {
        // The source index repeats from sector to sector: the id is the sector entity a script names the row by, not row:Static:N.
        var session = ConversationSessionFixture.Create();
        AreaObjectRecord Static(long sector, int x) => Row(2, 3, x: x) with { Key = Key(2) with { Kind = AreaSourceKind.Static, Sector = sector, ObjectIdSha256 = new string((char)('a' + (int)sector), 64) } };
        var one = Static(5, 10);
        var two = Static(6, 12);
        var map = new AreaSpellCastMap(session, 1, Steps(), [one, two], new AreaCombatHost(new AreaInventoryHost([])), () => null, () => null);
        Assert.AreEqual((AreaUseScriptHost.EntityId(one), AreaUseScriptHost.EntityId(two)), (map.Id(one), map.Id(two)));
        Assert.AreNotEqual(map.Id(one), map.Id(two));
        Assert.AreSame(one, map.Row(map.Id(one)));
        Assert.AreSame(two, map.Row(map.Id(two)));
        Assert.AreEqual(new SpellLocation(12, 20), map.Object(map.Id(two))!.Location);
    }

    [TestMethod]
    public void ABurnAndATrapDamageReachTheSameObjectRules()
    {
        var f = Create();
        var chest = f.Map.Id(f.Rows[1]);
        var burned = f.Caster.ApplyBurn(chest, DamageByType.Zero.With(DamageType.Fire, 14));
        Assert.IsTrue(burned.Applied);
        Assert.AreEqual(4, burned.HitPointDamage, "fire 14 less metal's 10");
        Assert.AreEqual(4, f.Session.QueryObject(f.Targets.Target(f.Session, f.Rows[1].Key)!.Object!).Damage);
        Assert.AreEqual(1, f.Map.TakeObjectHits().Count);
    }

    [TestMethod]
    public void AVetoedBurnIsCountedAndNamesTheBurningObjectAsTheAttacker()
    {
        // ARC-DEV-261 (GT94, GT198): the burner's owner is the burn's attacker; the object's taking-damage script cancels the burn before any step.
        var f = Create();
        var chest = f.Map.Id(f.Rows[1]);
        var asked = new List<(string Victim, string? Attacker)>();
        f.Caster.TakingDamage = (victim, attacker) => { asked.Add((victim, attacker)); return false; };
        var burner = new AreaFireBurner(f.Session, () => f.Caster, _ => [chest], _ => null);
        Assert.AreEqual(1, burner.Burn(new Arcanum.Simulation.Movement.CellCoord(1, 1), normal: false, doubled: false, burner: "torch"));
        Assert.AreEqual((1, 0, 0L), (burner.Vetoed, burner.Hurt, burner.HitPointsTaken));
        CollectionAssert.AreEqual(new[] { (chest, (string?)"torch") }, asked);
        Assert.AreEqual(0, f.Map.TakeObjectHits().Count, "the object took nothing");
    }

    [TestMethod]
    public void AScriptsEntityOfAnObjectFindsItsCastId()
    {
        // ARC-SCRIPT-1576: a mobile row with no session entity is row:Mobile:N here but oid:digest in the script host; a sector static is the same in both.
        var f = Create();
        var scenery = f.Rows[2];
        var scriptName = AreaUseScriptHost.EntityId(scenery);
        Assert.AreEqual("oid:" + scenery.Key.ObjectIdSha256, scriptName);
        Assert.AreNotEqual(scriptName, f.Map.Id(scenery));
        Assert.AreEqual(f.Map.Id(scenery), f.Map.CastIdOfScriptEntity(scriptName));
        Assert.AreEqual(f.Map.Id(scenery), f.Map.CastIdOfScriptEntity(f.Map.Id(scenery)), "the cast id itself passes through");
        var door = f.Rows[0];
        Assert.AreEqual(f.Map.Id(door), f.Map.CastIdOfScriptEntity(AreaUseScriptHost.EntityId(door)));
        Assert.AreEqual("oid:nowhere", f.Map.CastIdOfScriptEntity("oid:nowhere"), "an unknown entity passes through and finds no run");
        Assert.AreEqual(SessionSpellCaster.Hero, f.Caster.CastIdOf(SessionSpellCaster.Hero));
        Assert.AreEqual(f.Map.Id(scenery), f.Caster.CastIdOf(scriptName), "the caster asks its map");

        var session = ConversationSessionFixture.Create();
        AreaObjectRecord Static(long sector) => Row(2, 3) with { Key = Key(2) with { Kind = AreaSourceKind.Static, Sector = sector } };
        var sector = Static(5);
        var map = new AreaSpellCastMap(session, 1, Steps(), [sector], new AreaCombatHost(new AreaInventoryHost([])), () => null, () => null);
        Assert.AreEqual("sector:5:2", AreaUseScriptHost.EntityId(sector));
        Assert.AreEqual(map.Id(sector), map.CastIdOfScriptEntity("sector:5:2"), "a sector object is named alike");
    }
}
