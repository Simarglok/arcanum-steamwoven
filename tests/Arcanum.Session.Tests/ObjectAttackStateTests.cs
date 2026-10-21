using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-008: the hero's attack on non-critter objects (ledger "Object damage"). Objects, materials and hit points are
// invented; the hero is the half-elf fixture, bare-handed unless it equips the kit blade. Every attack on an object hits (no skill
// roll; a technology failure needs a technological weapon in magickal hands).
[TestClass]
public sealed class ObjectAttackStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly CellCoord ChestCell = new(120, 210);

    private static SessionObjectTarget Target(string entity, DamageableObjectKind kind, int points = 6, ObjectMaterial material = ObjectMaterial.Cloth,
        Func<SessionObjectBaseline, SessionObjectBaseline>? with = null)
    {
        var baseline = new SessionObjectBaseline
        {
            Kind = kind, Points = points, Material = material,
            Openable = kind switch
            {
                DamageableObjectKind.Portal => new SessionOpenableBaseline(OpenableKind.Portal, 0, 0, 0),
                DamageableObjectKind.Container => new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0),
                _ => null,
            },
            Cell = kind == DamageableObjectKind.Container ? ChestCell : default,
        };
        return new(Exterior, entity, with?.Invoke(baseline) ?? baseline);
    }

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed(
            [new("chest", SessionHolderKind.Container, false, false, [], ["cup", "knife"])],
            [
                new("cup", InventoryStateTests.Facts(SessionItemType.Generic, 5650), null),
                new("knife", InventoryStateTests.Facts(SessionItemType.Weapon, 2001), null),
                new("vase", InventoryStateTests.Facts(SessionItemType.Generic, 5651), new CellCoord(101, 200)),
            ]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    // Attacks until the object breaks (or the bounded run ends); every attack must be accepted.
    private static SessionObjectAttackResult BreakIt(GameSessionOwner owner, SessionObjectAttack attack)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var result = owner.AttackObject(owner.Version, attack);
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure), $"attempt {attempt}");
            if (result.Object?.Broken == true || result.Item?.Break is ItemBreakStep.Broken or ItemBreakStep.Deleted) return result;
        }
        Assert.Fail("the object did not break in a bounded run");
        return null!;
    }

    [TestMethod]
    public void HitsAddDamageAndADoorIsDestroyed()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        Assert.AreEqual(new SessionObjectState(0, 6, SessionObjectCondition.Whole), owner.QueryObject(door));
        var first = owner.AttackObject(owner.Version, new() { Target = door });
        Assert.AreEqual(GameSessionDisposition.Changed, first.Disposition);
        Assert.AreEqual(AttackOutcome.Hit, first.Resolution!.Outcome);
        Assert.AreEqual(first.Resolution.Damage!.HitPointDamage, first.Object!.Damage, "the resolver's hit points");
        Assert.IsTrue(first.Host.HasFlag(SessionObjectHostSteps.GuardsAlerted));

        var broke = BreakIt(owner, new() { Target = door });
        Assert.AreEqual(ObjectBreak.Destroyed, broke.Object!.Break);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(door).Condition);
        Assert.IsTrue(broke.Wear is null || broke.Wear.Steps.All(step => step.Skip == SessionWearSkip.TargetBroken), "no wear on a broken object");
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.AttackObject(owner.Version, new() { Target = door }).Failure, "a destroyed door is gone");
    }

    [TestMethod]
    public void AWindowBustsThroughTheLockDomain()
    {
        using var owner = Owner();
        var window = Target("sector:1:8", DamageableObjectKind.Portal, with: b => b with { Window = true, Openable = new(OpenableKind.Portal, 0, 0, 1) });
        var broke = BreakIt(owner, new() { Target = window });
        Assert.AreEqual(ObjectBreak.WindowBusted, broke.Object!.Break);
        Assert.IsTrue(broke.Host.HasFlag(SessionObjectHostSteps.BustScript));
        var state = owner.QueryOpenable(new(Exterior, window.Entity), window.Baseline.Openable!.Value);
        Assert.AreEqual((LockRules.BustedBit, 0), (state.Flags & LockRules.BustedBit, state.Frame), "busted on frame 0");
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(window).Condition, "a busted window still stands");
        Assert.IsTrue(owner.QueryObject(window).HitPoints <= 0);
    }

    [TestMethod]
    public void AContainerSpillsItsItemsAtItsCellAndAJunkPileIsSpared()
    {
        using var owner = Owner();
        var chest = Target("chest", DamageableObjectKind.Container);
        var broke = BreakIt(owner, new() { Target = chest });
        Assert.AreEqual(ObjectBreak.ContainerSpilled, broke.Object!.Break);
        Assert.IsTrue(broke.Host.HasFlag(SessionObjectHostSteps.SpillEyeCandy));
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(chest).Condition);
        var inventory = owner.QueryMapInventory(Exterior)!;
        Assert.AreEqual(0, inventory.Holders.Single(h => h.Entity == "chest").Items.Count);
        CollectionAssert.AreEqual(new[] { ("vase", new CellCoord(101, 200)), ("cup", ChestCell), ("knife", ChestCell) },
            inventory.Ground.Select(g => (g.Item.Entity, g.Cell)).ToArray());
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "cup", Accept).Failure, "a spilled item is picked up from the ground");
        Assert.IsFalse(owner.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == "cup"));

        var pile = Target("pile", DamageableObjectKind.Container, points: 0, with: b => b with { JunkPile = true, Openable = new(OpenableKind.Container, 0, 0, 0, true) });
        var spared = owner.AttackObject(owner.Version, new() { Target = pile });
        Assert.AreEqual(ObjectBreak.Spared, spared.Object!.Break, "a junk pile without hit points breaks at once and is spared");
        Assert.AreEqual(new SessionObjectState(0, 0, SessionObjectCondition.Whole), owner.QueryObject(pile));
    }

    [TestMethod]
    public void SceneryBustsThenTurnsInvulnerableAndRespawningSceneryComesBack()
    {
        using var owner = Owner();
        var statue = Target("statue", DamageableObjectKind.Scenery, with: b => b with { HasDestroyedArt = true });
        var busted = BreakIt(owner, new() { Target = statue });
        Assert.AreEqual(ObjectBreak.SceneryBusted, busted.Object!.Break);
        Assert.AreEqual(SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound, busted.Host);
        Assert.AreEqual(new SessionObjectState(6, 0, SessionObjectCondition.Busted), owner.QueryObject(statue), "damage set to the maximum");
        var again = owner.AttackObject(owner.Version, new() { Target = statue });
        Assert.AreEqual(DamagePrevention.Invulnerable, again.Resolution!.Damage!.Prevented, "a busted scenery is invulnerable");
        Assert.IsNull(again.Object);

        var barrel = Target("barrel", DamageableObjectKind.Scenery, with: b => b with { Respawning = true, RespawnDelay = 10 });
        var off = BreakIt(owner, new() { Target = barrel });
        Assert.AreEqual(ObjectBreak.SceneryRespawns, off.Object!.Break);
        Assert.AreEqual(new SessionObjectState(0, 6, SessionObjectCondition.Off), owner.QueryObject(barrel));
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.AttackObject(owner.Version, new() { Target = barrel }).Failure);
        var pending = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.ObjectRespawn);
        Assert.AreEqual(80_000, pending.DeadlineMs - owner.QueryClockSnapshot().GameTimestampMs, "10 s at the game rate");

        // The respawn event survives a save and switches the barrel on when it comes due.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(SessionObjectCondition.Off, restored.QueryObject(barrel).Condition);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            restored.AdvanceGameTime(restored.QueryClockSnapshot().Token, 80_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        restored.ProcessEvents(new NoWorld());
        Assert.AreEqual(new SessionObjectState(0, 6, SessionObjectCondition.Whole), restored.QueryObject(barrel));
        Assert.AreEqual(SessionObjectCondition.Busted, restored.QueryObject(statue).Condition, "the map changes are saved");
    }

    [TestMethod]
    public void ATrapBustsOrIsDestroyedAndATrappedObjectFiresItsTrap()
    {
        using var owner = Owner();
        var plain = BreakIt(owner, new() { Target = Target("trap-a", DamageableObjectKind.Trap) });
        Assert.AreEqual((ObjectBreak.Destroyed, SessionObjectHostSteps.DestroySound), (plain.Object!.Break, plain.Host));
        var art = Target("trap-b", DamageableObjectKind.Trap, with: b => b with { HasDestroyedArt = true });
        var busted = BreakIt(owner, new() { Target = art });
        Assert.AreEqual(ObjectBreak.TrapBusted, busted.Object!.Break);
        Assert.AreEqual(new SessionObjectState(0, 6, SessionObjectCondition.Busted), owner.QueryObject(art));
        Assert.AreEqual(ObjectBreak.Destroyed, BreakIt(owner, new() { Target = art }).Object!.Break, "a busted trap is destroyed next");

        var trapped = BreakIt(owner, new() { Target = Target("sector:2:1", DamageableObjectKind.Portal, with: b => b with { Trapped = true }) });
        Assert.IsTrue(trapped.Host.HasFlag(SessionObjectHostSteps.TrapFires));
    }

    [TestMethod]
    public void AnItemOnTheGroundBreaksThroughItsDurability()
    {
        using var owner = Owner();
        var vase = new SessionWearItem("vase", new ItemCondition(4, 0, 0, false));
        var facts = new SessionObjectBaseline { Kind = DamageableObjectKind.Item, Material = ObjectMaterial.Glass };
        var broke = BreakIt(owner, new() { Item = vase, ItemFacts = facts });
        Assert.AreEqual(ItemBreakStep.Broken, broke.Item!.Value.Break);
        Assert.IsTrue(broke.Host.HasFlag(SessionObjectHostSteps.BustScript));
        var deleted = BreakIt(owner, new() { Item = vase, ItemFacts = facts });
        Assert.AreEqual(ItemBreakStep.Deleted, deleted.Item!.Value.Break);
        Assert.IsTrue(owner.IsItemDestroyed("vase"));
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.AttackObject(owner.Version, new() { Item = vase, ItemFacts = facts }).Failure);
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable,
            owner.AttackObject(owner.Version, new() { Item = new("cup", new(4, 0, 0, false)), ItemFacts = facts }).Failure, "an item in a chest");
    }

    [TestMethod]
    public void TheHerosBladeWearsOnStoneWhileTheObjectStaysWhole()
    {
        using var owner = Owner();
        var blade = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15 },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, blade),
            (_, _) => true, Accept).Failure);
        var worn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", new(1000, 0, 0, false)) };
        // A stone wall of a house: stone reduces nothing on scenery, and 500 hit points stay whole.
        var wall = Target("pillar", DamageableObjectKind.Scenery, points: 500, material: ObjectMaterial.Stone);
        var hit = owner.AttackObject(owner.Version, new() { Target = wall, HeroWorn = worn });
        Assert.AreEqual(GameSessionFailure.None, hit.StepFailure);
        var step = hit.Wear!.Steps.Single();
        Assert.AreEqual((SessionWearStepKind.AttackerWeapon, "kit:1", 4, SessionWearSkip.None), (step.Kind, step.Entity, step.Amount, step.Skip));
        Assert.AreEqual(4, owner.QueryItemCondition("kit:1", new(1000, 0, 0, false))!.Value.Damage);
    }

    [TestMethod]
    public void AttacksThatCannotRunAreRefusedBeforeAnyRoll()
    {
        using var plain = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.AttackUnavailable, plain.AttackObject(plain.Version, new() { Target = Target("x", DamageableObjectKind.Trap) }).Failure);

        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        void Refused(SessionObjectAttack attack, GameSessionFailure failure, GameSessionVersion? version = null)
        {
            var draws = owner.QueryRandom().Draws;
            var result = owner.AttackObject(version ?? owner.Version, attack);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(draws, owner.QueryRandom().Draws);
            Assert.AreEqual(0, owner.Version.Revision);
        }
        Refused(new(), GameSessionFailure.InvalidObject);
        Refused(new() { Target = door, Item = new("vase", new(4, 0, 0, false)) }, GameSessionFailure.InvalidObject);
        Refused(new() { Target = door with { Baseline = door.Baseline with { Openable = null } } }, GameSessionFailure.InvalidObject);
        Refused(new() { Target = door with { Baseline = door.Baseline with { JunkPile = true } } }, GameSessionFailure.InvalidObject);
        Refused(new() { Target = door with { Baseline = door.Baseline with { Damage = -1 } } }, GameSessionFailure.InvalidObject);
        Refused(new() { Target = door with { Entity = "bad id!" } }, GameSessionFailure.InvalidObject);
        Refused(new() { Target = Target("x", DamageableObjectKind.Item) }, GameSessionFailure.InvalidObject);
        Refused(new() { Item = new("vase", new(4, 0, 0, false)) }, GameSessionFailure.InvalidObject);
        Refused(new() { Target = door, HeroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", new(1, 0, 0, false)) } },
            GameSessionFailure.InvalidWear);
        Refused(new() { Target = door, Aim = HitLocation.Torso }, GameSessionFailure.InvalidAttack);
        Refused(new() { Target = door, Distance = 2 }, GameSessionFailure.InvalidAttack);
        Refused(new() { Target = door }, GameSessionFailure.StaleVersion, new GameSessionVersion(owner.Identity, 5));
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryObject(door with { Entity = "" }));
    }

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }
}
