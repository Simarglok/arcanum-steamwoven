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
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    // A chest with two items and a vase on the ground (a seed is host data: a restored session gets it again).
    private static SessionMapInventorySeed Seed()
    {
        return new SessionMapInventorySeed(
            [new("chest", SessionHolderKind.Container, false, false, [], ["cup", "knife"])],
            [
                new("cup", InventoryStateTests.Facts(SessionItemType.Generic, 5650), null),
                new("knife", InventoryStateTests.Facts(SessionItemType.Weapon, 2001), null),
                new("vase", InventoryStateTests.Facts(SessionItemType.Generic, 5651), new CellCoord(101, 200)),
            ]);
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
        Assert.AreEqual(SessionObjectHostSteps.WitnessesHearAttack, first.Host);

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
    public void ItemsSoldIntoABrokenContainerAreNotSpilledAndTheSaveStaysValid()
    {
        // ARC-WORLD-090: a merchant's stock chest with a sale in it; the attack spills only the chest's own items.
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = Seed() with { Holders = [.. Seed().Holders, new("smith", SessionHolderKind.Npc, false, true, [], [])] };
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        var pile = new SessionItemKind(SessionItemType.Gold, 9056, 3200, 33200);
        Assert.AreEqual(GameSessionFailure.None, owner.BarterSell(owner.Version, "smith", "kit:1", 1, 0, default, Accept, container: "chest", identify: true).Failure);
        Assert.AreEqual(ObjectBreak.ContainerSpilled, BreakIt(owner, new() { Target = Target("chest", DamageableObjectKind.Container) }).Object!.Break);
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, owner.QueryJunkPiles().Single().Items.Select(item => item.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, owner.QueryItemDecays().Select(d => d.Entity).ToArray(), "the sold item does not decay");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, seed).Disposition);
        // A broken container is no substitute any more: the open refuses it (the viewer then opens the merchant's own list).
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.OpenBarter(owner.Version, "smith", "chest", true, pile, Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.OpenBarter(owner.Version, "smith", null, true, pile, Accept).Disposition);
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
        // ARC-WORLD-063: the items drop one by one, the last first, so the cup, landing where the knife lies, gathers both into a junk pile.
        CollectionAssert.AreEqual(new[] { ("vase", new CellCoord(101, 200)) }, inventory.Ground.Select(g => (g.Item.Entity, g.Cell)).ToArray());
        var junk = inventory.JunkPiles.Single();
        Assert.AreEqual(("junk:1", ChestCell), (junk.Entity, junk.Cell));
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, junk.Items.Select(item => item.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { (0, 1, 1), (1, 1, 1) }, owner.QueryJunkPiles().Single().Items.Select(item => (item.Slot, item.Width, item.Height)).ToArray());
        // ARC-WORLD-062: the spilled items decay; the vase, which lay there at T0, does not.
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, owner.QueryItemDecays().Select(d => d.Entity).ToArray());
        // ARC-WORLD-101: the broken chest is no loot any more; its items lie on the ground and in the pile.
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, owner.LootItem(owner.Version, "chest", "cup", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItem(owner.Version, "cup", Accept).Failure, "an item in a pile is not on the ground");
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "junk:1", "cup", Accept).Failure, "it is looted from the pile");
        Assert.AreEqual("cup", owner.QueryPcInventory().Single().Entity);
        Assert.AreEqual("knife", owner.QueryItemDecays().Single().Entity, "the loot ended the cup's decay");
        Assert.AreEqual("knife", owner.QueryJunkPiles().Single().Items.Single().Entity, "the pile stands while the loot window shows it");
        // The window closes: the pile with one item dissolves and the knife lies loose again, its two days starting over.
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 1000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.AreEqual(("knife", ChestCell), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Last());
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.DelayMs, owner.QueryItemDecays().Single().DeadlineMs);
        // Two game days on, the knife still on the ground is deleted.
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ItemDecayRules.DelayMs, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var decayed = owner.ProcessEvents(new NoWorld()).Decays.Single();
        Assert.AreEqual(("knife", ItemDecayStep.Destroy, false), (decayed.Entity, decayed.Step, decayed.Postponed));
        Assert.IsTrue(owner.IsItemDestroyed("knife"));
        Assert.IsFalse(owner.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == "knife"));

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
        Assert.AreEqual(SessionObjectHostSteps.WitnessesHearBreak | SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound, busted.Host);
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
        Assert.AreEqual((ObjectBreak.Destroyed, SessionObjectHostSteps.WitnessesHearBreak | SessionObjectHostSteps.DestroySound), (plain.Object!.Break, plain.Host));
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

    [TestMethod]
    public void ASpilledContainerSurvivesASaveAtAnyCell()
    {
        foreach (var cell in new[] { new CellCoord(0, 0), new CellCoord((1 << 24) - 1, (1 << 24) - 1), ChestCell })
        {
            using var owner = Owner();
            var chest = Target("chest", DamageableObjectKind.Container, with: b => b with { Cell = cell });
            BreakIt(owner, new() { Target = chest });
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
            Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
            Assert.AreEqual(SessionObjectCondition.Destroyed, restored.QueryObject(chest).Condition);
            // ARC-WORLD-063: the two spilled items lie in a junk pile on the chest's cell.
            var junk = restored.QueryMapInventory(Exterior)!.JunkPiles.Single();
            Assert.AreEqual((cell, 2), (junk.Cell, junk.Items.Count));
        }
        using var bad = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidObject, bad.AttackObject(bad.Version,
            new() { Target = Target("chest", DamageableObjectKind.Container, with: b => b with { Cell = new(1 << 24, 0) }) }).Failure);
    }

    [TestMethod]
    public void ASaveWithAMalformedRespawnEventIsRefused()
    {
        using var owner = Owner();
        BreakIt(owner, new() { Target = Target("barrel", DamageableObjectKind.Scenery, with: b => b with { Respawning = true, RespawnDelay = 10 }) });
        var at = owner.QueryClockSnapshot().GameTimestampMs;
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Refused(string changed)
        {
            Assert.AreNotEqual(text, changed);
            var decoded = GameSessionSnapshotCodec.TryDecode(changed);
            Assert.IsNotNull(decoded.Snapshot, "the edit keeps a well-formed snapshot");
            Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(decoded.Snapshot, RestoreOptions()).Failure);
        }
        Refused(text.Replace($"\"deadlineMs\":{at + 80_000}", $"\"deadlineMs\":{at + 80_001}"));
        Refused(text.Replace($"\"values\":[{at},80000]", $"\"values\":[{at},0]"));
        Refused(text.Replace($"\"values\":[{at},80000]", $"\"values\":[{at + 1},80000]"));
        var events = text.IndexOf("\"pending\"", StringComparison.Ordinal);
        // A scenery that is not recorded off.
        Refused(text[..events] + text[events..].Replace("\"entity\":\"barrel\"", "\"entity\":\"cask\""));
    }

    [TestMethod]
    public void ABustedWindowBustsAgainOnEveryHitAndKeepsItsOtherLockBits()
    {
        using var owner = Owner();
        var window = Target("sector:1:8", DamageableObjectKind.Portal, with: b => b with { Window = true, Openable = new(OpenableKind.Portal, LockRules.LockedBit, 0, 0) });
        BreakIt(owner, new() { Target = window });
        var damage = owner.QueryObject(window).Damage;
        var again = owner.AttackObject(owner.Version, new() { Target = window });
        Assert.AreEqual((true, ObjectBreak.WindowBusted), (again.Object!.Broken, again.Object.Break));
        Assert.IsTrue(again.Host.HasFlag(SessionObjectHostSteps.BustScript));
        Assert.IsTrue(owner.QueryObject(window).Damage >= damage, "its damage only grows");
        Assert.AreEqual(LockRules.LockedBit | LockRules.BustedBit, owner.QueryOpenable(new(Exterior, window.Entity), window.Baseline.Openable!.Value).Flags);
    }

    [TestMethod]
    public void AnItemCanBeInvulnerableOrTrappedAndASpilledItemIsOnTheGround()
    {
        using var owner = Owner();
        var facts = new SessionObjectBaseline { Kind = DamageableObjectKind.Item, Material = ObjectMaterial.Glass };
        var shielded = owner.AttackObject(owner.Version, new() { Item = new("vase", new(4, 0, 0, false)) { Invulnerable = true }, ItemFacts = facts });
        Assert.AreEqual(DamagePrevention.Invulnerable, shielded.Resolution!.Damage!.Prevented);
        Assert.AreEqual(DamagePrevention.Invulnerable, owner.AttackObject(owner.Version,
            new() { Item = new("vase", new(4, 0, 0, false)), ItemFacts = facts with { Invulnerable = true } }).Resolution!.Damage!.Prevented);
        var trapped = BreakIt(owner, new() { Item = new("vase", new(4, 0, 0, false)), ItemFacts = facts with { Trapped = true } });
        Assert.IsTrue(trapped.Host.HasFlag(SessionObjectHostSteps.TrapFires | SessionObjectHostSteps.WitnessesHearBreak));

        BreakIt(owner, new() { Target = Target("chest", DamageableObjectKind.Container) });
        // ARC-WORLD-063: the spilled cup and knife lie in a junk pile, out of an attack's reach; once the knife is taken, the pile
        // dissolves on closing and the cup lies loose, where it can be struck.
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable,
            owner.AttackObject(owner.Version, new() { Item = new("cup", new(400, 0, 0, false)), ItemFacts = facts }).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "junk:1", "knife", Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        var cup = owner.AttackObject(owner.Version, new() { Item = new("cup", new(400, 0, 0, false)), ItemFacts = facts });
        Assert.AreEqual(GameSessionFailure.None, cup.Failure, "a spilled item can be struck where it lies");
        Assert.AreEqual(SessionObjectHostSteps.WitnessesHearAttack, cup.Host);
    }

    [TestMethod]
    public void AStoneDoorTakesNothingFromBareHandsAndARecordedConditionOfAnotherKindIsIgnored()
    {
        using var owner = Owner();
        var door = Target("sector:3:1", DamageableObjectKind.Portal, material: ObjectMaterial.Stone);
        var hit = owner.AttackObject(owner.Version, new() { Target = door });
        Assert.AreEqual((0, false), (hit.Object!.Damage, hit.Object.Broken), "bare hands (at most 5) against the stone reduction of 10");
        Assert.IsTrue(hit.Resolution!.Damage!.Rolled.Normal > 0);
        // A busted condition written for a door (a crafted save): it is not a door's, so it is ignored.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, null,
            [new(Exterior, MapChangeKey.ForEntity(door.Entity, GameSessionOwner.ObjectConditionProperty), (long)SessionObjectCondition.Busted)]).Disposition);
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(door).Condition);
        Assert.AreEqual(GameSessionFailure.None, owner.AttackObject(owner.Version, new() { Target = door }).Failure);
    }

    [TestMethod]
    public void AnUnavailableObjectIsRefusedBeforeTheHerosItemsAndARespawnIsReported()
    {
        using var owner = Owner();
        var barrel = Target("barrel", DamageableObjectKind.Scenery, with: b => b with { Respawning = true, RespawnDelay = -2 });
        BreakIt(owner, new() { Target = barrel });
        var bad = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", new(1, 0, 0, false)) };
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.AttackObject(owner.Version, new() { Target = barrel, HeroWorn = bad }).Failure);
        var pending = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.ObjectRespawn);
        Assert.AreEqual(120_000, pending.DeadlineMs - owner.QueryClockSnapshot().GameTimestampMs, "two game minutes");
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 120_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var fired = owner.ProcessEvents(new NoWorld()).Respawns.Single();
        Assert.AreEqual((Exterior, "barrel"), (fired.Map, fired.Entity));
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(barrel).Condition);
    }

    [TestMethod]
    public void ASpilledItemStaysInItsMendedContainerAndWaitsForItsSeed()
    {
        // ARC-WORLD-062: the chest made whole again holds its items, so their events find nothing on the ground (Keep).
        using var owner = Owner();
        var chest = Target("chest", DamageableObjectKind.Container);
        BreakIt(owner, new() { Target = chest });
        var key = MapChangeKey.ForEntity("chest", GameSessionOwner.ObjectConditionProperty);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, null, [new MapChangeEdit(Exterior, key, null)]).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ItemDecayRules.DelayMs, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var kept = owner.ProcessEvents(new NoWorld()).Decays;
        CollectionAssert.AreEqual(new[] { ("cup", ItemDecayStep.Keep), ("knife", ItemDecayStep.Keep) }, kept.Select(d => (d.Entity, d.Step)).OrderBy(d => d.Entity).ToArray());
        Assert.IsFalse(owner.IsItemDestroyed("cup") || owner.IsItemDestroyed("knife"));

        // A restored session without the map's seed cannot place a spilled item: the decay waits a minute until the seed is back.
        using var spilled = Owner();
        BreakIt(spilled, new() { Target = chest });
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(spilled.Capture())));
        Assert.AreEqual(ClockMutationDisposition.Changed,
            restored.AdvanceGameTime(restored.QueryClockSnapshot().Token, ItemDecayRules.DelayMs, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var waiting = restored.ProcessEvents(new NoWorld()).Decays;
        Assert.IsTrue(waiting.Count == 2 && waiting.All(d => d.Step == ItemDecayStep.Destroy && d.Postponed), "postponed without the seed");
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            restored.AdvanceGameTime(restored.QueryClockSnapshot().Token, ItemDecayRules.RetryDelayMs, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.IsTrue(restored.ProcessEvents(new NoWorld()).Decays.All(d => !d.Postponed));
        // ARC-WORLD-063: the knife, dropped first, decays first in their junk pile; the pile, left with the cup, dissolves and drops it
        // again, so the cup's two days start over (0x43CCA0 → 0x463540 → 0x466E50).
        Assert.IsTrue(restored.IsItemDestroyed("knife") && !restored.IsItemDestroyed("cup"));
        Assert.AreEqual(0, restored.QueryJunkPiles().Count);
        Assert.AreEqual(("cup", restored.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.DelayMs),
            restored.QueryItemDecays().Select(d => (d.Entity, d.DeadlineMs)).Single());
    }

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }
}
