using Arcanum.Data.Combat;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The armour and weapon wear of an attack (ARC-COMBAT-007). A stone golem (an NPC fixture of AttackStateTests with material 0
// and 5,000 extra hit points) wears four armour pieces and a sword from its map seed; the hero's kit is the half-elf fixture's.
// Conditions are invented.
[TestClass]
public sealed class AttackWearStateTests
{
    private const string Golem = "golem";
    private static readonly ItemCondition Fifteen = new(15, 0, 0, false);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static readonly EquipmentItem Blade = new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 20,
        MaxHitPoints = 20,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(4, 9) }, SpeedFactor = 15, DamagesArmor = true,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
        },
    };

    private static readonly Dictionary<EquipmentSlot, SessionWearItem> GolemItems = new()
    {
        [EquipmentSlot.Helmet] = new("golem-helmet", Fifteen),
        [EquipmentSlot.Armor] = new("golem-vest", Fifteen),
        [EquipmentSlot.Gauntlets] = new("golem-gloves", Fifteen),
        [EquipmentSlot.Boots] = new("golem-boots", Fifteen),
        [EquipmentSlot.Weapon] = new("golem-sword", Fifteen),
    };

    private static SessionCritterCombat GolemCombat() =>
        SessionCritterCombat.From(Golem, AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0)).Combat!;

    private static GameSessionOwner Owner(GameSessionLimits? limits = null)
    {
        var owner = GameSessionOwner.Create(Descriptor(limits) with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed(
            [new(Golem, SessionHolderKind.Npc, false, false, [], [.. GolemItems.Values.Select(item => item.Entity)])],
            [.. GolemItems.Select(pair => new SessionSeedItem(pair.Value.Entity,
                InventoryStateTests.Facts(pair.Key == EquipmentSlot.Weapon ? SessionItemType.Weapon : SessionItemType.Armor, 2900, (int)pair.Key), null))]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static AttackResolution Resolution(AttackWear? wear) => new(AttackRefusal.None, AttackOutcome.Hit, AttackMissCause.None,
        HitLocation.Torso, false, false, null, null, null, FateUse.None, false, null, []) { Wear = wear };

    private static AttackWear Wear(DamageRecipient wearer = DamageRecipient.Target, bool critter = true, EquipmentSlot slot = EquipmentSlot.Armor,
        int armor = 0, int weapon = 0, bool destroy = false, bool explode = false, AttackerWearKind kind = AttackerWearKind.None, int amount = 0,
        bool whole = false) => new(wearer, critter, slot, armor, weapon, destroy, explode, kind, amount, whole);

    private static SessionWearSide HeroSide(GameSessionOwner owner, ItemCondition? baseline = null) => new()
    {
        Life = SessionLifeTarget.Hero,
        Worn = owner.QueryEquipment().Worn.ToDictionary(worn => worn.Slot, worn => new SessionWearItem(worn.Entity, baseline ?? new(20, 0, 0, false))),
    };

    private static SessionWearSide GolemSide(IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? worn = null) =>
        new() { Life = GolemCombat().Target, Worn = worn ?? GolemItems };

    private static SessionAttackWearResult Apply(GameSessionOwner owner, AttackWear wear, SessionWearSide? attacker = null, SessionWearSide? target = null,
        bool targetBroken = false) => owner.ApplyAttackWear(owner.Version, new SessionAttackWear
    {
        Resolution = Resolution(wear), Attacker = attacker ?? HeroSide(owner), Target = target ?? GolemSide(), TargetBroken = targetBroken,
    });

    private static SessionWearStep Single(SessionAttackWearResult result, GameSessionDisposition disposition = GameSessionDisposition.Changed)
    {
        Assert.AreEqual((disposition, GameSessionFailure.None, GameSessionFailure.None), (result.Disposition, result.Failure, result.StepFailure));
        Assert.AreEqual(1, result.Steps.Count);
        return result.Steps[0];
    }

    [TestMethod]
    public void TheArmourAtTheHitLocationWearsBreaksAndAtTheSecondBreakIsDeleted()
    {
        using var owner = Owner();
        var wear = Wear(slot: EquipmentSlot.Armor, armor: CombatWearRules.ArmorWear);
        var first = Single(Apply(owner, wear));
        Assert.AreEqual((SessionWearStepKind.Armor, DamageRecipient.Target, (EquipmentSlot?)EquipmentSlot.Armor, "golem-vest", 10, SessionWearSkip.None),
            (first.Kind, first.Wearer, first.Slot, first.Entity, first.Amount, first.Skip));
        Assert.AreEqual((ItemBreakStep.None, Fifteen with { Damage = 10 }), (first.Item!.Value.Break, first.Item.Value.Condition!.Value));
        Assert.AreEqual(1, owner.Version.Revision);

        var broke = Single(Apply(owner, wear)).Item!.Value;
        Assert.AreEqual(ItemBreakStep.Broken, broke.Break);
        Assert.IsNull(broke.Unworn, "taking an NPC's broken item off is the host's");
        Assert.AreEqual(Fifteen with { Broken = true, InventoryArtBroken = true }, owner.QueryItemCondition("golem-vest", Fifteen));

        Assert.AreEqual(ItemBreakStep.None, Single(Apply(owner, wear)).Item!.Value.Break, "a broken item reads as healthy again");
        Assert.AreEqual(ItemBreakStep.Deleted, Single(Apply(owner, wear)).Item!.Value.Break);
        Assert.IsTrue(owner.IsItemDestroyed("golem-vest"));
        Assert.AreEqual(GameSessionFailure.InvalidWear, Apply(owner, wear).Failure, "a deleted item is no longer worn");
        var left = GolemItems.Where(pair => pair.Key != EquipmentSlot.Armor).ToDictionary(pair => pair.Key, pair => pair.Value);

        // Every location strikes its own piece; an empty slot wears nothing.
        foreach (var (slot, entity) in new[] { (EquipmentSlot.Helmet, "golem-helmet"), (EquipmentSlot.Gauntlets, "golem-gloves"), (EquipmentSlot.Boots, "golem-boots") })
            Assert.AreEqual(entity, Single(Apply(owner, Wear(slot: slot, armor: 10), target: GolemSide(left))).Entity);
        var bare = Single(Apply(owner, wear, target: GolemSide(left)), GameSessionDisposition.NoChange);
        Assert.AreEqual((SessionWearSkip.NoItem, (string?)null), (bare.Skip, bare.Entity));
    }

    [TestMethod]
    public void AnAttackerlessArmourWearWearsTheTorsoArmourOfOneCritterOnly()
    {
        // ARC-DEV-245 (GT195): a script's fire damage wears the torso armour by 10, with no attacker.
        using var owner = Owner();
        var first = owner.ApplyArmorWear(owner.Version, GolemSide());
        var step = Single(first);
        Assert.AreEqual((SessionWearStepKind.Armor, DamageRecipient.Target, (EquipmentSlot?)EquipmentSlot.Armor, "golem-vest", 10),
            (step.Kind, step.Wearer, step.Slot, step.Entity, step.Amount));
        Assert.AreEqual(Fifteen with { Damage = 10 }, step.Item!.Value.Condition!.Value);
        Assert.AreEqual(ItemBreakStep.Broken, owner.ApplyArmorWear(owner.Version, GolemSide()).Steps[0].Item!.Value.Break, "the second wear breaks it");

        var left = GolemItems.Where(pair => pair.Key != EquipmentSlot.Armor).ToDictionary(pair => pair.Key, pair => pair.Value);
        var bare = Single(owner.ApplyArmorWear(owner.Version, GolemSide(left)), GameSessionDisposition.NoChange);
        Assert.AreEqual(SessionWearSkip.NoItem, bare.Skip, "no torso armour: nothing wears");
        var unknown = Single(owner.ApplyArmorWear(owner.Version, new SessionWearSide { Life = GolemCombat().Target }), GameSessionDisposition.NoChange);
        Assert.AreEqual(SessionWearSkip.ItemsUnknown, unknown.Skip);

        Assert.AreEqual(GameSessionFailure.InvalidWear, owner.ApplyArmorWear(owner.Version, new SessionWearSide { Worn = GolemItems }).Failure, "no critter");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ApplyArmorWear(owner.Version with { Revision = owner.Version.Revision + 5 }, GolemSide(left)).Failure);
    }

    [TestMethod]
    public void TheCriticalWeaponEffectsDamageThenDeleteTheWearersWeaponUnlessItIsInvulnerable()
    {
        using var owner = Owner();
        var damaged = Single(Apply(owner, Wear(weapon: CombatWearRules.CriticalWeaponDamage)));
        Assert.AreEqual((SessionWearStepKind.WeaponDamage, "golem-sword", Fifteen with { Damage = 10 }),
            (damaged.Kind, damaged.Entity, damaged.Item!.Value.Condition!.Value));

        var shielded = new Dictionary<EquipmentSlot, SessionWearItem>(GolemItems) { [EquipmentSlot.Weapon] = GolemItems[EquipmentSlot.Weapon] with { Invulnerable = true } };
        var passed = Apply(owner, Wear(weapon: 10, destroy: true), target: GolemSide(shielded));
        Assert.AreEqual(GameSessionDisposition.NoChange, passed.Disposition);
        CollectionAssert.AreEqual(new[] { SessionWearSkip.Invulnerable, SessionWearSkip.Invulnerable }, passed.Steps.Select(step => step.Skip).ToArray());
        // Armour wear does not test the flag.
        var vest = new Dictionary<EquipmentSlot, SessionWearItem>(GolemItems) { [EquipmentSlot.Armor] = GolemItems[EquipmentSlot.Armor] with { Invulnerable = true } };
        Assert.AreEqual(SessionWearSkip.None, Single(Apply(owner, Wear(armor: 10), target: GolemSide(vest))).Skip);

        // Damage first (it breaks the sword at 0 hit points), then the deletion of the broken sword, without a second break.
        var both = Apply(owner, Wear(weapon: 10, destroy: true, explode: true));
        Assert.AreEqual(GameSessionDisposition.Changed, both.Disposition);
        CollectionAssert.AreEqual(new[] { SessionWearStepKind.WeaponDamage, SessionWearStepKind.WeaponDestroy }, both.Steps.Select(step => step.Kind).ToArray());
        Assert.AreEqual(ItemBreakStep.Broken, both.Steps[0].Item!.Value.Break);
        Assert.AreEqual(ItemBreakStep.Deleted, both.Steps[1].Item!.Value.Break);
        Assert.IsTrue(owner.IsItemDestroyed("golem-sword"));
        Assert.AreEqual(GameSessionFailure.InvalidWear, Apply(owner, Wear(destroy: true)).Failure, "a deleted weapon is no longer wielded");
    }

    // ARC-SCRIPT-2749 (ledger BS3g): a wear that deletes an item calls the object destroy (0x43CCA0), which runs the item's destroy point (2) first; an answer 0 keeps the item and the step says so.
    [TestMethod]
    public void AnArmourWornOutAsksTheItemDestroyGateAndAnAnswerOfNoKeepsIt()
    {
        using var owner = Owner();
        var asked = new List<string>();
        var answer = false;
        owner.AttachItemDestroyGate(entity => { asked.Add(entity); return answer; });
        var wear = Wear(slot: EquipmentSlot.Armor, armor: CombatWearRules.ArmorWear);
        Assert.AreEqual(ItemBreakStep.None, Single(Apply(owner, wear)).Item!.Value.Break);
        Assert.AreEqual(ItemBreakStep.Broken, Single(Apply(owner, wear)).Item!.Value.Break);
        Assert.AreEqual(ItemBreakStep.None, Single(Apply(owner, wear)).Item!.Value.Break);
        Assert.AreEqual(0, asked.Count, "wear that deletes nothing asks nothing");
        var kept = Single(Apply(owner, wear));
        Assert.AreEqual((ItemBreakStep.Deleted, true, SessionWearSkip.DestroyKept), (kept.Item!.Value.Break, kept.Item.Value.DestroyKept, kept.Skip));
        Assert.IsFalse(owner.IsItemDestroyed("golem-vest"));
        CollectionAssert.AreEqual(new[] { "golem-vest" }, asked);
        answer = true;
        var gone = Single(Apply(owner, wear));
        Assert.AreEqual((ItemBreakStep.Deleted, false, SessionWearSkip.None), (gone.Item!.Value.Break, gone.Item.Value.DestroyKept, gone.Skip));
        Assert.IsTrue(owner.IsItemDestroyed("golem-vest"));
        CollectionAssert.AreEqual(new[] { "golem-vest", "golem-vest" }, asked);
    }

    [TestMethod]
    public void TheCriticalDestroyWeaponAndAnAttackerlessArmourWearAskTheGateToo()
    {
        using var owner = Owner();
        var asked = new List<string>();
        owner.AttachItemDestroyGate(entity => { asked.Add(entity); return false; });
        var kept = Single(Apply(owner, Wear(destroy: true)));
        Assert.AreEqual((SessionWearStepKind.WeaponDestroy, SessionWearSkip.DestroyKept, ItemBreakStep.Deleted), (kept.Kind, kept.Skip, kept.Item!.Value.Break));
        Assert.IsFalse(owner.IsItemDestroyed("golem-sword"), "a destroy point that answers 0 leaves the weapon in the hand");
        Assert.AreEqual(Fifteen, owner.QueryItemCondition("golem-sword", Fifteen), "a destroy with no break writes no damage");

        // The armour of an attackerless wear (fire) breaks once, wears healthy again, then deletes: only the deleting one asks.
        Assert.AreEqual(ItemBreakStep.None, owner.ApplyArmorWear(owner.Version, GolemSide()).Steps[0].Item!.Value.Break);
        Assert.AreEqual(ItemBreakStep.Broken, owner.ApplyArmorWear(owner.Version, GolemSide()).Steps[0].Item!.Value.Break);
        Assert.AreEqual(ItemBreakStep.None, owner.ApplyArmorWear(owner.Version, GolemSide()).Steps[0].Item!.Value.Break);
        Assert.AreEqual(1, asked.Count, "the wears that delete nothing asked nothing");
        owner.AttachItemDestroyGate(entity => { asked.Add(entity); return true; });
        Assert.AreEqual(ItemBreakStep.Deleted, owner.ApplyArmorWear(owner.Version, GolemSide()).Steps[0].Item!.Value.Break);
        Assert.IsTrue(owner.IsItemDestroyed("golem-vest"));
        CollectionAssert.AreEqual(new[] { "golem-sword", "golem-vest" }, asked);
    }

    [TestMethod]
    public void ACriticalSelfHitWearsTheAttackersOwnArmourAndWeapon()
    {
        using var owner = Owner();
        var mail = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium } };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade),
            (_, _) => true, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, new(Kit[1].Kind, [], false, mail),
            (_, _) => true, Accept).Failure);
        var self = Wear(DamageRecipient.Attacker, slot: EquipmentSlot.Armor, armor: 10, weapon: 10, destroy: true, kind: AttackerWearKind.Weapon, amount: 4);
        var result = Apply(owner, self);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.StepFailure));
        CollectionAssert.AreEqual(new[] { SessionWearStepKind.Armor, SessionWearStepKind.WeaponDamage, SessionWearStepKind.WeaponDestroy, SessionWearStepKind.AttackerWeapon },
            result.Steps.Select(step => step.Kind).ToArray());
        CollectionAssert.AreEqual(new[] { "kit:2", "kit:1", "kit:1", "kit:1" }, result.Steps.Select(step => step.Entity).ToArray(), "the hero's own items");
        Assert.IsTrue(result.Steps.Take(3).All(step => step.Wearer == DamageRecipient.Attacker && step.Skip == SessionWearSkip.None));
        Assert.AreEqual(SessionWearSkip.ItemDeleted, result.Steps[3].Skip, "the destroyed weapon wears no more (no-crash rule)");
        Assert.AreEqual(new ItemCondition(20, 0, 10, false), owner.QueryItemCondition("kit:2", new(20, 0, 0, false)));
        Assert.IsTrue(owner.IsItemDestroyed("kit:1"));
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Armor }, owner.QueryEquipment().Worn.Select(worn => worn.Slot).ToArray());
        // The golem's items were not touched.
        Assert.AreEqual(0, owner.QueryDurability().Items.Count(item => item.Entity.StartsWith("golem", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnNpcAttackerWearsItsOwnWeapon()
    {
        using var owner = Owner();
        var step = Single(Apply(owner, Wear(kind: AttackerWearKind.Weapon, amount: 3), attacker: GolemSide(), target: HeroSide(owner)));
        Assert.AreEqual((SessionWearStepKind.AttackerWeapon, DamageRecipient.Attacker, "golem-sword", 3), (step.Kind, step.Wearer, step.Entity, step.Amount));
        Assert.AreEqual(Fifteen with { Damage = 3 }, owner.QueryItemCondition("golem-sword", Fifteen));
        Assert.AreEqual(GameSessionFailure.InvalidWear, Apply(owner, Wear(kind: AttackerWearKind.Weapon, amount: 3),
            attacker: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem>()), target: HeroSide(owner)).Failure, "no weapon to wear");
    }

    [TestMethod]
    public void WearValuesComeFromAnItemsStoredFields()
    {
        var weapon = new CombatWeaponFields(CombatWeaponFlags.None, CombatWeaponSkill.Melee, 0, 0, [new(1, 4), new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
            [0, 0, 0, 0, 0], 11, 0, 1, 0, 7, 0, CombatFieldReader.NoAmmo, 0, 0, 0, 0, 0, 0, 0);
        CombatFields Stored(uint objectFlags, uint useArt, CombatWeaponFields? w) => new(CombatFieldOrigin.Prototype,
            new(5, 6000, objectFlags, 0, 1, 2, 0, 30, 5, 7, 0, [0, 0, 0, 0, 0], 0x60000000),
            new(0, 10, 50, 0, 40, 0, -1, [10000, 10000, 10000, 10000, 10000], 0, 0, 0x60000000, useArt), w, null, null, w is null ? new(0, 0, 0) : null, null, null);
        var sword = SessionWearItem.FromCombatFields("s", Stored(0x400000, 0x60000000 | 4 << 6, weapon))!;
        Assert.AreEqual(("s", new ItemCondition(30, 5, 7, false), true, 4), (sword.Entity, sword.Baseline, sword.Invulnerable, sword.ArtSubtype));
        var torch = SessionWearItem.FromCombatFields("t", Stored(0, 0x60000000 | 4 << 6, null))!;
        Assert.AreEqual((false, 0), (torch.Invulnerable, torch.ArtSubtype), "only a weapon's subtype is read");
        Assert.IsNull(SessionWearItem.FromCombatFields("g", AttackStateTests.Npc()), "a critter is no item");
    }

    // ARC-COMBAT-1930 (0x4B2810, VCG9): the loudness an item's attack event carries comes from the weapon flags of its stored fields: loud 0x1, silent 0x2, a non-weapon normal.
    [TestMethod]
    public void NoiseComesFromAWeaponsLoudAndSilentFlags()
    {
        SessionWearItem Item(CombatWeaponFlags flags, bool weapon = true)
        {
            var w = new CombatWeaponFields(flags, CombatWeaponSkill.Melee, 0, 0, [new(1, 4), new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
                [0, 0, 0, 0, 0], 11, 0, 1, 0, 7, 0, CombatFieldReader.NoAmmo, 0, 0, 0, 0, 0, 0, 0);
            return SessionWearItem.FromCombatFields("i", new(CombatFieldOrigin.Prototype,
                new(5, 6000, 0, 0, 1, 2, 0, 30, 5, 7, 0, [0, 0, 0, 0, 0], 0x60000000),
                new(0, 10, 50, 0, 40, 0, -1, [10000, 10000, 10000, 10000, 10000], 0, 0, 0x60000000, 0x60000000), weapon ? w : null, null, null, weapon ? null : new(0, 0, 0), null, null))!;
        }
        Assert.AreEqual(Loudness.Normal, Item(CombatWeaponFlags.None).Noise);
        Assert.AreEqual(Loudness.Loud, Item(CombatWeaponFlags.Loud).Noise);
        Assert.AreEqual(Loudness.Silent, Item(CombatWeaponFlags.Silent).Noise);
        Assert.AreEqual(Loudness.Loud, Item(CombatWeaponFlags.Loud | CombatWeaponFlags.Silent).Noise, "the loud flag is tested first");
        Assert.AreEqual(Loudness.Normal, Item(CombatWeaponFlags.None, weapon: false).Noise, "a non-weapon reads no weapon flags");
    }

    [TestMethod]
    public void TheHerosWeaponWearsByTheStruckMaterialAndABreakTakesItOff()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade),
            (_, _) => true, Accept).Failure);
        var wear = Wear(kind: AttackerWearKind.Weapon, amount: 4);
        for (var hit = 1; hit <= 4; hit++)
        {
            var step = Single(Apply(owner, wear));
            Assert.AreEqual((SessionWearStepKind.AttackerWeapon, DamageRecipient.Attacker, "kit:1"), (step.Kind, step.Wearer, step.Entity));
            Assert.AreEqual(20 - 4 * hit, owner.QueryEquipment().Worn.Single().Item.HitPoints, "the worn record follows");
        }
        var broke = Single(Apply(owner, wear)).Item!.Value;
        Assert.AreEqual((ItemBreakStep.Broken, (EquipmentSlot?)EquipmentSlot.Weapon), (broke.Break, broke.Unworn));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
        Assert.AreEqual(WeaponKind.Unarmed, owner.QueryHeroAttack()!.Weapon.Kind, "the hero strikes bare-handed now");
    }

    [TestMethod]
    public void BareHandsHurtTheAttackerButNeverTakeItsLastHitPoint()
    {
        using var owner = Owner();
        var max = owner.QueryHeroLife()!.Status.MaxHitPoints;
        var hands = Single(Apply(owner, Wear(kind: AttackerWearKind.Hands, amount: 4)));
        Assert.AreEqual((SessionWearStepKind.AttackerHands, DamageRecipient.Attacker, 4), (hands.Kind, hands.Wearer, hands.Amount));
        Assert.AreEqual(4, hands.Life!.HitPointDamage);
        Assert.AreEqual(4, owner.QueryHeroLife()!.Record.State.HitPointDamage);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = max - 4 - 3 }).Disposition);
        Assert.AreEqual(2, Single(Apply(owner, Wear(kind: AttackerWearKind.Hands, amount: 4))).Life!.HitPointDamage, "3 left: 2 lost");
        var last = Single(Apply(owner, Wear(kind: AttackerWearKind.Hands, amount: 4)), GameSessionDisposition.NoChange);
        Assert.AreEqual((SessionWearSkip.NoHitPoints, 4), (last.Skip, last.Amount));
        Assert.AreEqual(1, owner.QueryHeroLife()!.Status.HitPoints);
        Assert.IsFalse(owner.QueryHeroLife()!.Status.IsDead);

        // The same step serves an NPC attacker (the AI's attack): the golem hits the hero bare-handed.
        var npc = Single(Apply(owner, Wear(kind: AttackerWearKind.Hands, amount: 5), attacker: GolemSide(), target: HeroSide(owner)));
        Assert.AreEqual(5, npc.Life!.HitPointDamage);
        Assert.AreEqual(5, owner.QueryLifeRecord(Golem)!.Value.State.HitPointDamage);
    }

    [TestMethod]
    public void AnObjectTargetWearsTheAttackerOnlyWhileTheHitLeftItWhole()
    {
        using var owner = Owner();
        var door = new SessionWearSide();
        var wear = Wear(critter: false, kind: AttackerWearKind.Hands, amount: 2, whole: true);
        var broken = Single(Apply(owner, wear, target: door, targetBroken: true), GameSessionDisposition.NoChange);
        Assert.AreEqual(SessionWearSkip.TargetBroken, broken.Skip);
        Assert.AreEqual(0, owner.Version.Revision);
        Assert.AreEqual(2, Single(Apply(owner, wear, target: door)).Life!.HitPointDamage);
        // A critter target is never "broken".
        Assert.AreEqual(GameSessionFailure.InvalidWear, Apply(owner, Wear(armor: 10), targetBroken: true).Failure);
    }

    [TestMethod]
    public void WithoutItemDataTheItemStepsAreSkippedAndTheHandsStillWear()
    {
        using var owner = Owner();
        var result = Apply(owner, Wear(armor: 10, weapon: 10, kind: AttackerWearKind.Hands, amount: 3), attacker: new() { Life = SessionLifeTarget.Hero },
            target: new() { Life = GolemCombat().Target });
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { SessionWearSkip.ItemsUnknown, SessionWearSkip.ItemsUnknown, SessionWearSkip.None },
            result.Steps.Select(step => step.Skip).ToArray());
        Assert.AreEqual(0, owner.QueryDurability().Items.Count);
        Assert.AreEqual(3, owner.QueryHeroLife()!.Record.State.HitPointDamage);
        Assert.AreEqual(GameSessionDisposition.NoChange, Apply(owner, Wear()).Disposition, "no wear at all");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyAttackWear(owner.Version, new SessionAttackWear
        {
            Resolution = Resolution(null), Attacker = HeroSide(owner), Target = GolemSide(),
        }).Disposition);
    }

    [TestMethod]
    public void AStepTheDurabilityOwnerRefusesEndsTheWearAndKeepsTheStepsBefore()
    {
        // One durability record at most: the armour takes it, the weapon step is refused.
        using var owner = Owner(new GameSessionLimits(maxDurabilityRecords: 1));
        var result = Apply(owner, Wear(armor: 10, weapon: 10, kind: AttackerWearKind.Hands, amount: 3));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, GameSessionFailure.BudgetExceeded),
            (result.Disposition, result.Failure, result.StepFailure));
        CollectionAssert.AreEqual(new[] { SessionWearStepKind.Armor, SessionWearStepKind.WeaponDamage }, result.Steps.Select(step => step.Kind).ToArray());
        Assert.AreEqual(Fifteen with { Damage = 10 }, owner.QueryItemCondition("golem-vest", Fifteen));
        Assert.AreEqual(0, owner.QueryHeroLife()!.Record.State.HitPointDamage, "the hands step did not run");

        // A refusal of the first step changes nothing: the wear is Rejected with it.
        var first = Apply(owner, Wear(weapon: 10));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, GameSessionFailure.None),
            (first.Disposition, first.Failure, first.StepFailure));
        Assert.AreEqual(1, first.Steps.Count);
    }

    [TestMethod]
    public void BadSidesAreRefusedBeforeAnyChange()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade),
            (_, _) => true, Accept).Failure);
        var revision = owner.Version.Revision;
        var wear = Wear(armor: 10, kind: AttackerWearKind.Weapon, amount: 4);
        void Refused(GameSessionFailure failure, SessionWearSide? attacker = null, SessionWearSide? target = null, GameSessionVersion? version = null,
            AttackWear? with = null)
        {
            var result = owner.ApplyAttackWear(version ?? owner.Version, new SessionAttackWear
            {
                Resolution = Resolution(with ?? wear), Attacker = attacker ?? HeroSide(owner), Target = target ?? GolemSide(),
            });
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(0, result.Steps.Count);
            Assert.AreEqual(revision, owner.Version.Revision);
        }
        Refused(GameSessionFailure.InvalidWear, attacker: new SessionWearSide { Worn = HeroSide(owner).Worn });
        Refused(GameSessionFailure.InvalidWear, attacker: HeroSide(owner) with { Worn = new Dictionary<EquipmentSlot, SessionWearItem>() });
        Refused(GameSessionFailure.InvalidWear, attacker: HeroSide(owner) with
        {
            Worn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:2", Fifteen) },
        });
        Refused(GameSessionFailure.InvalidWear, attacker: HeroSide(owner, new ItemCondition(1, 0, -1, false)));
        Refused(GameSessionFailure.InvalidWear, target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem>(GolemItems)
        {
            [EquipmentSlot.Boots] = new("golem-vest", Fifteen),
        }));
        Refused(GameSessionFailure.InvalidWear, target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Helmet] = new("kit:1", Fifteen) }));
        Refused(GameSessionFailure.InvalidWear, target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem> { [(EquipmentSlot)999] = new("x", Fifteen) }));
        Refused(GameSessionFailure.InvalidWear, target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Helmet] = new("bad id!", Fifteen) }));
        Refused(GameSessionFailure.InvalidWear, target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Helmet] = new("elsewhere", Fifteen) }));
        Refused(GameSessionFailure.InvalidWear, target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem>(GolemItems)
        {
            [EquipmentSlot.Weapon] = GolemItems[EquipmentSlot.Weapon] with { ArtSubtype = 16 },
        }));
        // An NPC never wears the hero's items, even when the hero's side passes none.
        Refused(GameSessionFailure.InvalidWear, attacker: new SessionWearSide { Life = SessionLifeTarget.Hero },
            target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Helmet] = new("kit:2", Fifteen) }), with: Wear(armor: 10));
        // An object carries no worn items, and only a critter wearer takes armour or weapon steps.
        Refused(GameSessionFailure.InvalidWear, target: new SessionWearSide { Worn = new Dictionary<EquipmentSlot, SessionWearItem>() },
            with: Wear(critter: false));
        Refused(GameSessionFailure.InvalidWear, target: new SessionWearSide(), with: Wear(critter: false, armor: 10));
        Refused(GameSessionFailure.InvalidWear, target: new SessionWearSide(), with: Wear(critter: false, destroy: true));
        // The same critter on both sides.
        Refused(GameSessionFailure.InvalidWear, attacker: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem>()),
            target: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem>()), with: Wear(armor: 10));
        Refused(GameSessionFailure.InvalidWear, target: new SessionWearSide { Worn = GolemItems }, with: wear);
        Refused(GameSessionFailure.InvalidWear, target: HeroSide(owner), with: Wear(kind: AttackerWearKind.Hands, amount: 1));
        Refused(GameSessionFailure.InvalidWear, attacker: GolemSide(new Dictionary<EquipmentSlot, SessionWearItem>()), target: HeroSide(owner));
        Refused(GameSessionFailure.InvalidWear, target: new SessionWearSide
        {
            Life = SessionLifeTarget.Critter("bad id!", GolemCombat().Facts, LifeState.Unharmed),
        });
        Refused(GameSessionFailure.StaleVersion, version: new GameSessionVersion(owner.Identity, 99));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ApplyAttackWear(owner.Version, null!));
    }

    [TestMethod]
    public void TheHerosAttackOnAStoneGolemWearsItsArmourAndTheHerosBlade()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade),
            (_, _) => true, Accept).Failure);
        var golem = GolemCombat();
        var blade = new ItemCondition(1000, 0, 0, false);
        // Art subtype 4 wears 2 less: stone 4 − 2.
        var heroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", blade) { ArtSubtype = 4 } };
        var big = GolemItems.ToDictionary(pair => pair.Key, pair => pair.Value with { Baseline = new(1000, 0, 0, false) });
        SessionAttackResult? hit = null;
        for (var attempt = 0; attempt < 400 && hit is null; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = heroWorn, TargetWorn = big });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Recipient == DamageRecipient.Target && result.Life!.HitPointDamage > 0) hit = result;
            else if (result.Resolution!.Damage is null) Assert.IsNull(result.Wear, "a miss wears nothing");
        }
        Assert.IsNotNull(hit, "a hit within the bounded run");
        var slot = CombatWearRules.ArmorSlot(hit!.Resolution!.HitLocation);
        var steps = hit.Wear!.Steps;
        Assert.AreEqual((SessionWearStepKind.Armor, (EquipmentSlot?)slot, big[slot].Entity, 10), (steps[0].Kind, steps[0].Slot, steps[0].Entity, steps[0].Amount),
            "the damage-armour blade wears the golem's armour at the hit location");
        Assert.AreEqual((SessionWearStepKind.AttackerWeapon, "kit:1", 2), (steps[^1].Kind, steps[^1].Entity, steps[^1].Amount), "stone wears the blade by 4 − 2");
        Assert.IsTrue(owner.QueryItemCondition("kit:1", blade)!.Value.Damage >= 2);

        // A refusal comes before any roll.
        var draws = owner.QueryRandom().Draws;
        var refused = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = new Dictionary<EquipmentSlot, SessionWearItem>(), TargetWorn = big });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidWear), (refused.Disposition, refused.Failure));
        Assert.AreEqual(draws, owner.QueryRandom().Draws);
    }

    [TestMethod]
    public void BareHandedTheHeroPaysForHittingStone()
    {
        using var owner = Owner();
        var golem = GolemCombat();
        SessionAttackResult? hit = null;
        for (var attempt = 0; attempt < 400 && hit is null; attempt++)
        {
            var before = owner.QueryHeroLife()!.Record.State.HitPointDamage;
            var result = owner.AttackCritter(owner.Version, new(golem));
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Recipient == DamageRecipient.Target && result.Wear is { Steps.Count: > 0 } wear)
            {
                hit = result;
                var hands = wear.Steps.Single(step => step.Kind == SessionWearStepKind.AttackerHands);
                Assert.AreEqual(4, hands.Amount);
                Assert.AreEqual(before + hands.Life!.HitPointDamage, owner.QueryHeroLife()!.Record.State.HitPointDamage);
            }
        }
        Assert.IsNotNull(hit, "a damaging bare-handed hit within the bounded run");
        Assert.IsFalse(owner.QueryHeroLife()!.Status.IsDead);
    }
}
