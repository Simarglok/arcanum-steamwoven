using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1139 (0x4A8570 → 0x465170 with the weapon slot, 0x4654F0): the AI gate's re-wield of a live NPC. A companion archer whose arrows ran dry takes its bow off
// and fights with the best weapon it can use; a loaded bow stays. Entities and numbers are invented.
[TestClass]
public sealed class NpcRewieldStateTests
{
    private const string Archer = "oid:archer";

    private static EquipmentItem Weapon(int min, int max, CombatSkill skill, int? ammoType = null, int consumption = 0) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        Weapon = new EquipmentWeapon { Skill = skill, Damage = DamageRanges.None with { Normal = new(min, max) }, AmmoType = ammoType, AmmoConsumption = consumption },
    };

    private static EquipmentItem Coat() => new()
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium, AcAdjustment = 5 },
    };

    private sealed class World : ISessionRestockWorld
    {
        public Dictionary<string, SessionWearableItem?> Wear { get; } = new()
        {
            ["bow"] = new(new(SessionItemType.Weapon, 1000 + 2901, 2901, 32901), [], false, Weapon(5, 9, CombatSkill.Bow, ammoType: 0, consumption: 2)) { Worth = 300 },
            ["club"] = new(new(SessionItemType.Weapon, 1000 + 2001, 2001, 32001), [], false, Weapon(1, 4, CombatSkill.Melee)) { Worth = 5 },
            ["coat"] = new(new(SessionItemType.Armor, 1000 + 2030, 2030, 32030), [], false, Coat()) { Worth = 60 },
        };
        public bool Wields { get; set; } = true;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => null;
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => false;
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Unsupported, null);
        public SessionRestockItem? RestockItem(int basicPrototype) => null;
        public SessionItemKind RestockPileKind => new(SessionItemType.Gold, 9056, 3200, 33200);
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
        public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => !Wields ? null
            : new(new EquipmentWearer(), new EquipmentChooser { MeleeLevel = 10, BowLevel = 10, IsPc = true }, item => Wear.GetValueOrDefault(item.Entity), (_, _) => true);
    }

    // The archer wields its bow (1004) and carries arrows (units), a club and a coat in its pack.
    private static GameSessionOwner Open(int units, bool club = true, bool coat = false)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        var arrows = new SessionItemFacts(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, units, []) { AmmoType = 0 };
        var items = new List<string> { "bow", "arrows" };
        var seed = new List<SessionSeedItem>
        {
            new("bow", InventoryStateTests.Facts(SessionItemType.Weapon, 2901, (int)EquipmentSlot.Weapon), null), new("arrows", arrows, null),
        };
        if (club) { items.Add("club"); seed.Add(new("club", InventoryStateTests.Facts(SessionItemType.Weapon, 2001), null)); }
        if (coat) { items.Add("coat"); seed.Add(new("coat", InventoryStateTests.Facts(SessionItemType.Armor, 2030), null)); }
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new SessionMapInventorySeed([new(Archer, SessionHolderKind.Npc, false, false, [], items)], seed)).Failure);
        return owner;
    }

    private static (string, int)[] Locations(GameSessionOwner owner) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Archer).Items.Select(item => (item.Entity, item.Facts.InventoryLocation)).ToArray();

    [TestMethod]
    public void AnArcherWithTooFewArrowsTakesItsBowOffAndWieldsTheBestWeaponItCanUse()
    {
        using var owner = Open(units: 1);
        var before = owner.Version;
        var result = owner.RewieldNpc(owner.Version, Archer, allSlots: false, new World());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreNotEqual(before, owner.Version, "one revision");
        CollectionAssert.AreEqual(new[] { ("bow", -1), ("arrows", 0), ("club", 1004) }, Locations(owner), "one arrow is too few for a shot of two: the club replaces the bow");
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "club") }, owner.QueryNpcWorn(Archer)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray());
        var step = result.Steps.Single();
        Assert.AreEqual(("bow", "club"), (step.TakenOff, step.Wielded));
    }

    [TestMethod]
    public void AnArcherWithNoOtherWeaponFightsBareHanded()
    {
        using var owner = Open(units: 0, club: false);
        var result = owner.RewieldNpc(owner.Version, Archer, allSlots: false, new World());
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { ("bow", -1), ("arrows", 0) }, Locations(owner));
        Assert.AreEqual(0, owner.QueryNpcWorn(Archer)!.Worn.Count, "nothing worn: fists");
    }

    [TestMethod]
    public void AnArcherWithEnoughArrowsKeepsItsBowAndNothingChanges()
    {
        using var owner = Open(units: 5);
        var before = owner.Version;
        var result = owner.RewieldNpc(owner.Version, Archer, allSlots: false, new World());
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(before, owner.Version, "no revision");
        Assert.IsNull(owner.QueryNpcWorn(Archer), "an NPC the session never dressed keeps what its map data says");
        CollectionAssert.AreEqual(new[] { ("bow", 1004), ("arrows", 0), ("club", 0) }, Locations(owner));
    }

    [TestMethod]
    public void TheWeaponCheckLeavesOtherSlotsAloneAndTheFullCheckDressesThem()
    {
        using var owner = Open(units: 5, coat: true);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RewieldNpc(owner.Version, Archer, allSlots: false, new World()).Disposition, "the weapon slot alone: the coat stays in the pack");
        var all = owner.RewieldNpc(owner.Version, Archer, allSlots: true, new World());
        Assert.AreEqual(GameSessionDisposition.Changed, all.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "bow"), (EquipmentSlot.Armor, "coat") }, owner.QueryNpcWorn(Archer)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray());
    }

    [TestMethod]
    public void ARewieldWithoutAWielderOrAWorldOrOnAStaleVersionOrForAnUnknownNpcChangesNothing()
    {
        using var owner = Open(units: 0);
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.EquipmentUnavailable, owner.RewieldNpc(version, Archer, false, new World { Wields = false }).Failure);
        Assert.AreEqual(GameSessionFailure.EquipmentUnavailable, owner.RewieldNpc(version, Archer, false, null).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.RewieldNpc(version, "oid:nobody", false, new World()).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.RewieldNpc(version, "", false, new World()).Failure);
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RewieldNpc(version, Archer, false, new World()).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RewieldNpc(version, Archer, false, new World()).Failure, "the first call took the revision");
    }

    [TestMethod]
    public void AnArcherWithExactlyEnoughArrowsKeepsItsBow()
    {
        using var owner = Open(units: 2);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RewieldNpc(owner.Version, Archer, allSlots: false, new World()).Disposition, "two arrows for a shot of two");
    }

    [TestMethod]
    public void AWeaponOnlyRewieldKeepsTheOtherWornItemsInTheRecordAndANoChangeLeavesTheRecordAlone()
    {
        using var owner = Open(units: 5, coat: true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RewieldNpc(owner.Version, Archer, allSlots: true, new World()).Disposition);
        var world = new World();
        // The arrows run dry: only the weapon slot changes, the coat stays worn in the new record.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendAmmo(owner.Version, new SessionAmmoHolder(Archer), 0, 4).Disposition);
        var result = owner.RewieldNpc(owner.Version, Archer, allSlots: false, world);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "club"), (EquipmentSlot.Armor, "coat") }, owner.QueryNpcWorn(Archer)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray());
        var version = owner.Version;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RewieldNpc(version, Archer, allSlots: false, world).Disposition);
        Assert.AreEqual(version, owner.Version);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "club"), (EquipmentSlot.Armor, "coat") }, owner.QueryNpcWorn(Archer)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray());
    }

    // ARC-WORLD-1284 (flag 0x40, NT3, VPG1): an item that enters the NPC's list marks it, and its next AI pass re-wields every slot (0x4A8570 → 0x4654F0) and clears the bit: the hero's gift of a better
    // weapon is wielded by the archer at its pass (the bow it wielded goes to the pack).
    [TestMethod]
    public void ABetterWeaponTheHeroGivesAnNpcIsWieldedAtItsNextAiPassAndTheBitGoesOut()
    {
        using var owner = Open(units: 5);
        var restock = new World();
        restock.Wear["kit:1"] = new(HeroStateTests.HalfElf().Kit[0].Kind, [], false, Weapon(20, 30, CombatSkill.Melee)) { Worth = 500 };
        var ai = new AiSchedulerTests.World().With(Archer);
        ai.OnReWield = (npc, allSlots) => owner.RewieldNpc(owner.Version, npc, allSlots, restock);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, Archer, "kit:1", _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.IsTrue(owner.QueryDanger(Archer).Flags.HasFlag(Arcanum.Simulation.Ai.DangerFlags.CheckWield), "the insert marked the NPC");
        Assert.IsNull(owner.QueryNpcWorn(Archer), "nothing is wielded before its pass");

        var pass = owner.RunAiPass(owner.Version, Archer, ai);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.IsTrue(pass.Pass!.Gate!.Steps.HasFlag(Arcanum.Simulation.Ai.AiGateSteps.ReWieldAll), "the gate's CHECK_WIELD step ran");
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "kit:1") }, owner.QueryNpcWorn(Archer)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray(), "the better weapon is wielded");
        Assert.AreEqual(-1, Locations(owner).Single(pair => pair.Item1 == "bow").Item2, "the bow it wielded is in the pack");
        Assert.IsFalse(owner.QueryDanger(Archer).Flags.HasFlag(Arcanum.Simulation.Ai.DangerFlags.CheckWield), "the gate cleared the bit");
        // The next pass has nothing to re-wield.
        var again = owner.RunAiPass(owner.Version, Archer, ai);
        Assert.IsFalse(again.Pass!.Gate!.Steps.HasFlag(Arcanum.Simulation.Ai.AiGateSteps.ReWieldAll));
    }
}
