using Arcanum.Data.Combat;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's equipment (ARC-WORLD-028). The kit is the half-elf fixture's: kit:1 a weapon, kit:2 and kit:3 armour; item values
// are invented.
[TestClass]
public sealed class EquipmentStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    internal static readonly SessionWearArtCheck Drawn = (_, _) => true;

    internal static readonly EquipmentItem Blade = new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 20,
        MaxHitPoints = 20,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 15, MinimumStrength = 6,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
        },
    };

    private static EquipmentItem Mail(int normal = 0, ArmorSizes sizes = ArmorSizes.Medium) => new()
    {
        Kind = EquipmentItemKind.Armor,
        Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = sizes, AcAdjustment = 12, Resistances = new(normal, 0, 0, 0, 0) },
    };

    private static EquipmentItem Ring(int ac) => new()
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring, AcAdjustment = ac },
    };

    internal static SessionWearableItem Wearable(int kit, EquipmentItem item, int[]? scripts = null, bool spells = false) =>
        new(Kit[kit - 1].Kind, scripts ?? [], spells, item);

    private static GameSessionOwner Owner(SessionHeroDefinition? hero = null)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ?? HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static EquipmentMutationResult Ok(EquipmentMutationResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Refused(EquipmentMutationResult result, GameSessionOwner owner, GameSessionFailure failure,
        EquipmentRefusal refusal = EquipmentRefusal.None, long revision = 0)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure, refusal), (result.Disposition, result.Failure, result.Refusal));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    [TestMethod]
    public void TheHeroWieldsAKitWeaponAndItChangesItsAttack()
    {
        using var owner = Owner();
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
        var bare = owner.QueryHeroEquipmentCombat()!;
        Assert.AreEqual((CombatWeapon.Unarmed(), 10, 5), (bare.Weapon, bare.WeaponSpeed, bare.AttackCost));

        var result = Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, 1);
        Assert.AreEqual((null, (SessionInsertVerdict?)null), (result.Displaced, result.Verdict));
        Assert.AreEqual(new SessionWornItem(EquipmentSlot.Weapon, "kit:1", Blade), owner.QueryEquipment().Worn.Single());
        var armed = owner.QueryHeroEquipmentCombat(SkillTraining.Apprentice)!;
        Assert.AreEqual((WeaponKind.Weapon, new DamageRange(2, 8), 4), (armed.Weapon.Kind, armed.Weapon.Damage.Normal, armed.Weapon.MinimumStrength));
        Assert.AreEqual((20, 2), (armed.WeaponSpeed, armed.AttackCost), "15 + 5 trained: 8 − 20 / 3");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray(),
            "a worn item stays in the hero's list");
    }

    [TestMethod]
    public void WornArmourGivesArmourClassAndProtectsTheOverexertingHero()
    {
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Mail(normal: 70)), Drawn, Accept), owner, 1);
        var combat = owner.QueryHeroEquipmentCombat()!;
        Assert.AreEqual((12, 70), (combat.ArmorClassAdjustment, combat.Resistances.Normal));

        // The round's overexertion takes 2 fatigue less ¾ of the worn normal resistance: 2 − 52 × 2 / 100 = 1.
        var world = new SoloWorld();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StartCombatRound(owner.Version, world).Disposition);
        var over = owner.ConsumeActionPoints(owner.Version, SessionCombatant.Hero, 12, world);
        Assert.AreEqual(SessionActionPointUse.Overexerted, over.Charge);
        Assert.AreEqual(1, owner.QueryHeroLife()!.Record.State.FatigueDamage);
    }

    private sealed class SoloWorld : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [new(SessionCombatant.Hero, new(0, 0))];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    [TestMethod]
    public void RefusalsComeInOrderAndChangeNothing()
    {
        using var none = GameSessionOwner.Create(Descriptor()).Owner!;
        Refused(none.Equip(none.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), none, GameSessionFailure.EquipmentUnavailable);
        Refused(none.Unequip(none.Version, EquipmentSlot.Weapon, Accept), none, GameSessionFailure.EquipmentUnavailable);

        using var owner = Owner();
        var stale = owner.Version;
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1)), Drawn, Accept), owner, 1);
        Refused(owner.Equip(owner.Version, "bad id!", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, GameSessionFailure.InvalidItem, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:1", (EquipmentSlot)999, Wearable(1, Blade), Drawn, Accept), owner, GameSessionFailure.InvalidItem, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade with { Weapon = null }), Drawn, Accept), owner,
            GameSessionFailure.InvalidItem, revision: 1);
        Refused(owner.Equip(stale, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, GameSessionFailure.StaleVersion, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:9", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, GameSessionFailure.ItemNotAvailable, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(2, Blade), Drawn, Accept), owner, GameSessionFailure.InventoryMismatch, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Mail()), Drawn, Accept), owner, GameSessionFailure.InventoryMismatch, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade, [SessionScriptPoint.WieldOn]), Drawn, Accept), owner,
            GameSessionFailure.ItemScriptUnsupported, revision: 1);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade, [SessionScriptPoint.WieldOff]), Drawn, Accept), owner,
            GameSessionFailure.ItemScriptUnsupported, revision: 1);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade, [SessionScriptPoint.Examine, SessionScriptPoint.Use]), Drawn, Accept).Disposition,
            "other attachments do not run on a move");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept).Disposition);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade, spells: true), Drawn, Accept), owner,
            GameSessionFailure.ItemEffectUnsupported, revision: 3);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Helmet, Wearable(1, Blade), Drawn, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.WrongType, 3);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade with { Broken = true }), Drawn, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.Broken, 3);
        Refused(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Mail(sizes: ArmorSizes.Small)), Drawn, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.WrongSize, 3);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), (_, _) => false, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.NotWieldable, 3);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade with { NoDrop = true }), Drawn, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.NotDroppable, 3);
        Assert.AreEqual(1, owner.QueryEquipment().Worn.Count, "only the ring");
    }

    [TestMethod]
    public void TheArtCheckRunsOnlyWhereTheSourceLooksAndAfterTheRules()
    {
        using var owner = Owner();
        var asked = new List<EquipmentSlot>();
        bool Art(EquipmentSlot slot, SessionWearableItem _) { asked.Add(slot); return true; }
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring2, Wearable(3, Ring(1)), Art, Accept), owner, 1);
        Refused(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Mail(sizes: ArmorSizes.Large)), Art, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.WrongSize, 1);
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Mail()), Art, Accept), owner, 2);
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Armor }, asked.ToArray());
    }

    [TestMethod]
    public void TheCurrentOccupantGoesBackThroughTheInsertCheck()
    {
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring1, Wearable(2, Ring(2)), Drawn, Accept), owner, 1);
        var checkedItems = new List<SessionInventoryItem>();
        SessionInsertCheck Full = item => { checkedItems.Add(item); return SessionInsertVerdict.NoRoom; };
        var refused = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(5)), Drawn, Full);
        Refused(refused, owner, GameSessionFailure.InsertRefused, revision: 1);
        Assert.AreEqual(SessionInsertVerdict.NoRoom, refused.Verdict);
        Assert.AreEqual(("kit:2", Kit[1].Kind), (checkedItems.Single().Entity, checkedItems.Single().Facts.Kind));

        var swapped = Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(5)), Drawn, Accept), owner, 2);
        Assert.AreEqual(("kit:2", (SessionInsertVerdict?)SessionInsertVerdict.Accepted), (swapped.Displaced, swapped.Verdict));
        Assert.AreEqual(5, owner.QueryHeroEquipmentCombat()!.ArmorClassAdjustment);

        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring2, Wearable(2, Ring(2)), Drawn, Accept), owner, 3);
        Assert.AreEqual(7, owner.QueryHeroEquipmentCombat()!.ArmorClassAdjustment);
    }

    [TestMethod]
    public void AFixedTwoHandedWeaponAndAShieldExcludeEachOther()
    {
        using var owner = Owner();
        var great = Blade with { Weapon = Blade.Weapon! with { TwoHanded = true, HandCountFixed = true } };
        var shield = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Shield } };
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Shield, Wearable(2, shield), Drawn, Accept), owner, 1);
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, great), Drawn, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.NoFreeHand, 1);

        // A save that pairs them is refused.
        Ok(owner.Unequip(owner.Version, EquipmentSlot.Shield, Accept), owner, 2);
        Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, great), Drawn, Accept), owner, 3);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var shieldJson = ",{\"slot\":\"Shield\",\"entity\":\"kit:2\",\"item\":{\"kind\":\"Armor\",\"complexity\":0,\"noDrop\":false," +
            "\"broken\":false,\"uber\":false,\"light\":0,\"usesShieldLocation\":false,\"hp\":0,\"maxHp\":0,\"weapon\":null," +
            "\"armor\":{\"coverage\":\"Shield\",\"sizes\":0,\"maleOnly\":false,\"femaleOnly\":false,\"ac\":0,\"magicAc\":0," +
            "\"resistances\":[0,0,0,0,0],\"magicResistances\":[0,0,0,0,0],\"unarmedBonus\":0}},\"scripts\":[],\"itemSpells\":false}";
        var paired = text.Replace("],\"durability\":", shieldJson + "],\"durability\":", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidEquipment, GameSessionOwner.Restore(Decode(paired), RestoreOptions()).Failure);
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(paired.Replace("\"handCountFixed\":true", "\"handCountFixed\":false",
            StringComparison.Ordinal)), RestoreOptions()).Failure, "without the fixed hand count they share the hands");
    }

    [TestMethod]
    public void AHostCallbackThatChangesTheSessionMakesTheStepStale()
    {
        using var owner = Owner();
        bool Meddle(EquipmentSlot slot, SessionWearableItem item)
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1)), Drawn, Accept).Disposition);
            return true;
        }
        Refused(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Meddle, Accept), owner, GameSessionFailure.StaleVersion, revision: 1);
        Assert.AreEqual(EquipmentSlot.Ring1, owner.QueryEquipment().Worn.Single().Slot, "only the callback's own change");

        SessionInsertCheck Unwear = _ => { owner.Unequip(owner.Version, EquipmentSlot.Ring1, Accept); return SessionInsertVerdict.Accepted; };
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring2, Wearable(2, Ring(2)), Drawn, Accept), owner, 2);
        Refused(owner.Unequip(owner.Version, EquipmentSlot.Ring2, Unwear), owner, GameSessionFailure.StaleVersion, revision: 3);
        Assert.AreEqual(EquipmentSlot.Ring2, owner.QueryEquipment().Worn.Single().Slot);
    }

    [TestMethod]
    public void ARingMovesBetweenTheRingSlots()
    {
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1)), Drawn, Accept), owner, 1);
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring2, Wearable(3, Ring(1)), Drawn, Accept), owner, 2);
        Assert.AreEqual(new SessionWornItem(EquipmentSlot.Ring2, "kit:3", Ring(1)), owner.QueryEquipment().Worn.Single());
    }

    [TestMethod]
    public void UnequippingPutsTheItemBackUnlessItCannotLeave()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept).Disposition);
        Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, 1);
        Refused(owner.Unequip(owner.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.TooHeavy), owner, GameSessionFailure.InsertRefused, revision: 1);
        Refused(owner.Unequip(owner.Version, (EquipmentSlot)7, Accept), owner, GameSessionFailure.InvalidItem, revision: 1);
        var off = Ok(owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept), owner, 2);
        Assert.AreEqual("kit:1", off.Displaced);
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
    }

    [TestMethod]
    public void ANoDropItemCanBeWornButNotTakenOff()
    {
        // The source refuses a no-drop item at check-remove, so it can never enter a slot through the wield call; one that is worn
        // (restored from a save) cannot leave it.
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1)), Drawn, Accept), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("\"noDrop\":false", "\"noDrop\":true", StringComparison.Ordinal);
        using var restored = Restore(Decode(text));
        Refused(restored.Unequip(restored.Version, EquipmentSlot.Ring1, Accept), restored, GameSessionFailure.WearRefused, EquipmentRefusal.NotDroppable, 1);
        Refused(restored.Equip(restored.Version, "kit:2", EquipmentSlot.Ring1, Wearable(2, Ring(2)), Drawn, Accept), restored,
            GameSessionFailure.WearRefused, EquipmentRefusal.NotDroppable, 1);
    }

    [TestMethod]
    public void CrippledArmsFromTheLifeDomainRefuseAShield()
    {
        using var owner = Owner();
        var injured = owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1, Effects = CriticalHitEffects.CrippleArm });
        Assert.AreEqual(GameSessionDisposition.Changed, injured.Disposition);
        Assert.IsTrue(owner.QueryHeroLife()!.Record.State.Conditions.HasFlag(LifeConditions.CrippledArmOne));
        var shield = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Shield } };
        Refused(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Shield, Wearable(2, shield), Drawn, Accept), owner,
            GameSessionFailure.WearRefused, EquipmentRefusal.Crippled, 1);
        Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, 2);
    }

    [TestMethod]
    public void TheWearerIsTheHerosRaceAndGender()
    {
        using var dwarf = Owner(HalfElf() with { Race = CharacterRace.Dwarf, RaceEffect = null });
        Refused(dwarf.Equip(dwarf.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Mail()), Drawn, Accept), dwarf,
            GameSessionFailure.WearRefused, EquipmentRefusal.WrongSize);
        using var woman = Owner(HalfElf() with { Gender = CharacterGender.Female, GenderEffect = FemaleEffect });
        var maleOnly = Mail() with { Armor = Mail().Armor! with { MaleOnly = true } };
        Refused(woman.Equip(woman.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, maleOnly), Drawn, Accept), woman,
            GameSessionFailure.WearRefused, EquipmentRefusal.WrongGender);
    }

    [TestMethod]
    public void TakenItemsCanBeWornToo()
    {
        using var owner = Owner();
        var ring = new SessionItemFacts(new(SessionItemType.Armor, 3804, 2804, 32804), 1, 0, 0, null, []);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior,
            new([], [new SessionSeedItem("ring", ring, new CellCoord(100, 200))])).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "ring", Accept).Disposition);
        Ok(owner.Equip(owner.Version, "ring", EquipmentSlot.Ring1, new(ring.Kind, [], false, Ring(3)), Drawn, Accept), owner, 2);
    }

    [TestMethod]
    public void EquipmentIsSavedInSlotOrderAndRestored()
    {
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Mail(normal: 4)), Drawn, Accept), owner, 1);
        Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"equipment\":[{\"slot\":\"Weapon\",\"entity\":\"kit:1\",\"item\":{\"kind\":\"Weapon\",\"complexity\":0," +
            "\"noDrop\":false,\"broken\":false,\"uber\":false,\"light\":0,\"usesShieldLocation\":false,\"hp\":20,\"maxHp\":20," +
            "\"weapon\":{\"twoHanded\":false,\"handCountFixed\":false,\"ignoresResistance\":false,\"damagesArmor\":false,\"skill\":\"Melee\"," +
            "\"damage\":[[2,8],[0,0],[0,0],[0,0],[0,0]],\"magicDamage\":[0,0,0,0,0],\"toHit\":0,\"magicToHit\":0,\"speed\":15,\"magicSpeed\":0," +
            "\"range\":1,\"magicRange\":0,\"minStrength\":6,\"magicMinStrength\":0,\"ammoType\":null,\"ammoConsumption\":0," +
            "\"critHitChart\":\"Cutting\",\"critMissChart\":\"Bladed\",\"magicCritHitChance\":0,\"magicCritHitEffect\":0," +
            "\"magicCritMissChance\":0,\"magicCritMissEffect\":0},\"armor\":null},\"scripts\":[],\"itemSpells\":false}," +
            "{\"slot\":\"Armor\",\"entity\":\"kit:2\",\"item\":{\"kind\":\"Armor\",\"complexity\":0,\"noDrop\":false,\"broken\":false," +
            "\"uber\":false,\"light\":0,\"usesShieldLocation\":false,\"hp\":0,\"maxHp\":0,\"weapon\":null,\"armor\":{\"coverage\":\"Torso\"," +
            "\"sizes\":2,\"maleOnly\":false,\"femaleOnly\":false,\"ac\":12,\"magicAc\":0,\"resistances\":[4,0,0,0,0]," +
            "\"magicResistances\":[0,0,0,0,0],\"unarmedBonus\":0}},\"scripts\":[],\"itemSpells\":false}],\"durability\":");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryEquipment().Worn.ToArray(), restored.QueryEquipment().Worn.ToArray());
        Assert.AreEqual(owner.QueryHeroEquipmentCombat(), restored.QueryHeroEquipmentCombat());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void RestoreRefusesEquipmentTheHeroCouldNotWear()
    {
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1)), Drawn, Accept), owner, 1);
        var saved = owner.Capture();
        var ring = new SessionWornItem(EquipmentSlot.Ring1, "kit:3", Ring(1));
        var ringAsWeapon = GameSessionSnapshotCodec.Encode(saved).Replace("\"slot\":\"Ring1\"", "\"slot\":\"Weapon\"", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidEquipment, GameSessionOwner.Restore(Decode(ringAsWeapon), RestoreOptions()).Failure);
        var unknown = GameSessionSnapshotCodec.Encode(saved).Replace("\"entity\":\"kit:3\",\"item\"", "\"entity\":\"kit:7\",\"item\"", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidEquipment, GameSessionOwner.Restore(Decode(unknown), RestoreOptions()).Failure);
        var weaponInRing = GameSessionSnapshotCodec.Encode(saved).Replace("\"entity\":\"kit:3\",\"item\"", "\"entity\":\"kit:1\",\"item\"", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidEquipment, GameSessionOwner.Restore(Decode(weaponInRing), RestoreOptions()).Failure,
            "kit:1 is a weapon, not armour");
        var noBlock = GameSessionSnapshotCodec.Encode(saved).Replace("\"weapon\":null,\"armor\":{", "\"weapon\":null,\"armor\":null,\"x\":{", StringComparison.Ordinal);
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, GameSessionSnapshotCodec.TryDecode(noBlock).Failure);
        Assert.AreEqual(ring, owner.QueryEquipment().Worn.Single());

        // Without a hero nothing can be worn.
        var text = GameSessionSnapshotCodec.Encode(saved);
        using var plain = GameSessionOwner.Create(Descriptor()).Owner!;
        var heroless = GameSessionSnapshotCodec.Encode(plain.Capture()).Replace(NoEquipment,
            text[text.IndexOf(",\"equipment\":[", StringComparison.Ordinal)..text.IndexOf(",\"durability\":", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidEquipment, GameSessionOwner.Restore(Decode(heroless), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void DecodingIsStrict()
    {
        using var owner = Owner();
        Ok(owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1)), Drawn, Accept), owner, 1);
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring2, Wearable(2, Ring(2)), Drawn, Accept), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), to);
        }
        Fails("\"slot\":\"Ring2\"", "\"slot\":\"Ring1\"", SnapshotDecodeFailure.NotCanonical, "$.equipment[1].slot");
        Fails("\"entity\":\"kit:2\",\"item\"", "\"entity\":\"kit:3\",\"item\"", SnapshotDecodeFailure.NotCanonical, "$.equipment[1].entity");
        Fails("\"slot\":\"Ring2\"", "\"slot\":\"ring2\"", SnapshotDecodeFailure.OutOfRange, "$.equipment[1].slot");
        Fails("\"slot\":\"Ring2\"", "\"slot\":\"1002\"", SnapshotDecodeFailure.OutOfRange, "$.equipment[1].slot");
        Fails("\"coverage\":\"Ring\",\"sizes\":0,\"maleOnly\":false,\"femaleOnly\":false,\"ac\":2", "\"coverage\":\"Ring\",\"sizes\":0,\"maleOnly\":false,\"femaleOnly\":false,\"ac\":1001",
            SnapshotDecodeFailure.OutOfRange, "$.equipment[1].item.armor.ac");
        Fails("\"light\":0", "\"light\":16", SnapshotDecodeFailure.OutOfRange, "$.equipment[0].item.light");
        Fails("\"resistances\":[0,0,0,0,0]", "\"resistances\":[0,0,0,0]", SnapshotDecodeFailure.OutOfRange, "$.equipment[0].item.armor.resistances");
        Fails("\"hp\":0", "\"hp\":-1", SnapshotDecodeFailure.OutOfRange, "$.equipment[0].item.hp");
    }

    private static CombatFields Stored(int type, uint itemFlags = 0, uint currentArt = 0x60000000, uint inventoryArt = 0x60000000,
        CombatWeaponFields? weapon = null, CombatArmorFields? armor = null, CombatGenericFields? generic = null) =>
        new(CombatFieldOrigin.Prototype, new(type, 6000, 0, 0, 1, 2, 0, 30, 5, 7, 0, [0, 0, 0, 0, 0], currentArt),
            new(itemFlags, 10, 50, 0, 40, 0, -1, [10000, 10000, 10000, 10000, 10000], 0, 0, inventoryArt, 0x60000000), weapon, null, armor, generic, null, null);

    [TestMethod]
    public void StoredCombatFieldsBecomeWearValues()
    {
        var weapon = new CombatWeaponFields(CombatWeaponFlags.TwoHanded | CombatWeaponFlags.HandCountFixed | CombatWeaponFlags.IgnoreResistance,
            CombatWeaponSkill.Bow, 5, 6, [new(1, 4), new(0, 0), new(2, 3), new(0, 0), new(5, 9)], [1, 0, 0, 2, 0], 11, 3, 8, 2, 7, -1,
            (int)CombatAmmoType.Arrow, 1, 2, 10, 11, 4, 12, 13);
        var bow = SessionEquipmentFacts.FromCombatFields(Stored(5, 0x20 | 0x40000 | 0x800, weapon: weapon))!;
        Assert.AreEqual((EquipmentItemKind.Weapon, 40, true, true, EquipmentLight.Large, false, 35, 28),
            (bow.Kind, bow.MagickTechComplexity, bow.NoDrop, bow.Uber, bow.Light, bow.Broken, bow.MaxHitPoints, bow.HitPoints));
        var w = bow.Weapon!;
        Assert.AreEqual((true, true, true, false, CombatSkill.Bow), (w.TwoHanded, w.HandCountFixed, w.IgnoresResistance, w.DamagesArmor, w.Skill));
        Assert.AreEqual(new DamageRanges(new(1, 4), new(0, 0), new(2, 3), new(0, 0), new(5, 9)), w.Damage);
        Assert.AreEqual(new DamageByType(1, 0, 0, 2, 0), w.MagicDamage);
        Assert.AreEqual((5, 6, 11, 3, 8, 2, 7, -1, (int?)0, 1), (w.ToHit, w.MagicToHit, w.SpeedFactor, w.MagicSpeed, w.Range, w.MagicRange,
            w.MinimumStrength, w.MagicMinimumStrength, w.AmmoType, w.AmmoConsumption));
        Assert.AreEqual((CriticalHitChart.Impaling, CriticalMissChart.Bows, 10, 11, 12, 13), (w.CriticalHitChart, w.CriticalMissChart,
            w.MagicCriticalHitChance, w.MagicCriticalHitEffect, w.MagicCriticalMissChance, w.MagicCriticalMissEffect));
        Assert.IsNull(SessionEquipmentFacts.FromCombatFields(Stored(5, weapon: weapon with { AmmoType = CombatFieldReader.NoAmmo }))!.Weapon!.AmmoType);
        Assert.IsNull(SessionEquipmentFacts.FromCombatFields(Stored(5, weapon: weapon with { CriticalMissChart = 9 })), "an unknown chart");

        var armor = new CombatArmorFields(CombatArmorFlags.SizeSmall | CombatArmorFlags.SizeLarge | CombatArmorFlags.FemaleOnly, 6, 4,
            [10, 20, 30, 40, 50], [1, 2, 3, 4, 5], 0, 0, 7);
        var boots = SessionEquipmentFacts.FromCombatFields(Stored(7, currentArt: 0x60000400, inventoryArt: 0x60010000, armor: armor))!;
        Assert.AreEqual((EquipmentItemKind.Armor, true, EquipmentSlot.Boots), (boots.Kind, boots.Broken, EquipmentRules.WearSlot(boots)));
        var a = boots.Armor!;
        Assert.AreEqual((ArmorSizes.Small | ArmorSizes.Large, false, true, 6, 4, 7), (a.Sizes, a.MaleOnly, a.FemaleOnly, a.AcAdjustment, a.MagicAcAdjustment, a.UnarmedBonusDamage));
        Assert.AreEqual((new EquipmentResistances(10, 20, 30, 40, 50), new EquipmentResistances(1, 2, 3, 4, 5)), (a.Resistances, a.MagicResistances));
        Assert.IsNull(SessionEquipmentFacts.FromCombatFields(Stored(7, inventoryArt: 0x6001C000, armor: armor)), "coverage 7 does not exist");

        var torch = SessionEquipmentFacts.FromCombatFields(Stored(14, 0x200, generic: new(0x1, 0, 0)))!;
        Assert.AreEqual((EquipmentItemKind.Generic, true, EquipmentLight.Small, EquipmentSlot.Shield), (torch.Kind, torch.UsesShieldLocation, torch.Light,
            EquipmentRules.WearSlot(torch)));
        Assert.IsNull(SessionEquipmentFacts.FromCombatFields(Stored(9)), "food is not worn");
    }

    [TestMethod]
    public void WearableFactsCarryWhatTheBestItemChoiceReads()
    {
        // ARC-WORLD-034: worth, the identified bit 0x1, the throwable weapon flag 0x10, any item spell, the stored weight and scripts.
        var weapon = new CombatWeaponFields(CombatWeaponFlags.Throwable, CombatWeaponSkill.Throwing, 0, 0,
            [new(1, 4), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, 6, 0, 3, 0, CombatFieldReader.NoAmmo, 0, 1, 0, 0, 1, 0, 0);
        var fields = Stored(5, 0x1, weapon: weapon);
        fields = fields with { Object = fields.Object with { ScriptPoints = [SessionScriptPoint.Examine, SessionScriptPoint.WieldOn] } };
        var kind = new SessionItemKind(SessionItemType.Weapon, 6000, 1, 2);
        var facts = SessionEquipmentFacts.Wearable(kind, fields)!;
        Assert.AreEqual((kind, 50, true, true, 10, false), (facts.Kind, facts.Worth, facts.IdentifiedFlag, facts.Throwable, facts.Weight, facts.HasItemSpells));
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.Examine, SessionScriptPoint.WieldOn }, facts.ScriptPoints.ToArray());
        var spelled = fields with { Item = fields.Item! with { Spells = [10000, 12, 10000, 10000, 10000] } };
        Assert.IsTrue(SessionEquipmentFacts.Wearable(kind, spelled)!.HasItemSpells);
        var plain = SessionEquipmentFacts.Wearable(kind, Stored(5, weapon: weapon with { Flags = CombatWeaponFlags.None }))!;
        Assert.AreEqual((false, false), (plain.IdentifiedFlag, plain.Throwable));
        Assert.IsNull(SessionEquipmentFacts.Wearable(new(SessionItemType.Food, 4600, 1, 2), Stored(9)), "food is worn nowhere");
    }
}
