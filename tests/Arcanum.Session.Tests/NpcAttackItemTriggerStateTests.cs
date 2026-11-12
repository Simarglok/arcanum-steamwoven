using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1188: the damage step's item events of an NPC's attack (0x4B4390 serves any attacker, ledger IC1-IC2; contract
// docs/world/item-spell-triggers.md): the NPC's worn items fire "damages opponent" (and with the weapon "with item"), its victim's, the hero's
// or another NPC's, fire "parent hit" with the NPC as the other object. A recording host shows the executor calls. Values and items are invented.
[TestClass]
public sealed class NpcAttackItemTriggerStateTests
{
    private const string Rat = "oid:rat", Victim = "oid:victim", Archer = "oid:archer", Hero = SessionSpellCaster.Hero;
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class Recorder : ISessionItemTriggers
    {
        public List<(uint Mask, string Item, string Holder, string? Other)> Calls { get; } = [];
        public bool Supports(string item) => true;

        public SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null, SpellLocation tile = default, string? runOther = null)
        {
            Calls.Add((eventMask, item, holder, other));
            return SessionItemTriggerResult.None;
        }

        public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder) => SessionItemTriggerResult.None;
        public SessionItemTriggerResult Remove(string item, string holder) => SessionItemTriggerResult.None;
    }

    private static SessionCritterCombat Combat(string entity, IEnumerable<CombatFields>? carried = null)
    {
        var build = SessionCritterCombat.From(entity, Npc(), carried);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static CombatFields BowFields() =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(5, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, 0, 0, (int)EquipmentSlot.Weapon, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28, 0),
            new CombatWeaponFields(0, CombatWeaponSkill.Bow, 0, 0, [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            null, null, null, null, null);

    private static EquipmentItem VestItem() => new()
    {
        Kind = EquipmentItemKind.Armor,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium },
    };

    // The NPCs' items the session tracks (a map seed): a collar of the rat, a cap of the victim, the archer's bow.
    private static SessionMapInventorySeed Seed() => new(
        [new(Rat, SessionHolderKind.Npc, false, false, [], ["rat-collar"]), new(Victim, SessionHolderKind.Npc, false, false, [], ["victim-cap"]),
         new(Archer, SessionHolderKind.Npc, false, false, [], ["archer-bow"])],
        [new SessionSeedItem("rat-collar", InventoryStateTests.Facts(SessionItemType.Armor, 2906, (int)EquipmentSlot.Armor), null),
         new SessionSeedItem("victim-cap", InventoryStateTests.Facts(SessionItemType.Armor, 2901, (int)EquipmentSlot.Helmet), null),
         new SessionSeedItem("archer-bow", InventoryStateTests.Facts(SessionItemType.Weapon, 2904, (int)EquipmentSlot.Weapon), null)]);

    // A hero wearing an invented vest (kit:2).
    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, new(Kit[1].Kind, [], false, VestItem()), (_, _) => true, Accept).Failure);
        return owner;
    }

    private static bool Plain(SessionAttackResult result) =>
        result.Recipient == DamageRecipient.Target && result.Life is not null && result.Resolution!.Damage!.HitEffects == CriticalHitEffects.None
        && result.StepFailure == GameSessionFailure.None;

    [TestMethod]
    public void A_bite_on_the_hero_fires_the_hero_s_parent_hit_then_the_rat_s_damages_opponent()
    {
        using var owner = Owner();
        var rat = Combat(Rat);
        var worn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Armor] = new("rat-collar", Sturdy) };
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var host = new Recorder();
            var result = owner.AttackByNpc(owner.Version, new(rat) { AttackerWorn = worn, TargetWorn = AreaHeroWorn(owner), ItemTriggers = host });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Life is null) { Assert.AreEqual(0, host.Calls.Count, "a miss fires nothing"); continue; }
            if (!Plain(result)) continue;
            CollectionAssert.AreEqual(new (uint, string, string, string?)[]
            {
                (ParentHit | WeaponMelee, "kit:2", Hero, Rat), (ParentDamagesOpponent, "rat-collar", Rat, Hero), (TargetHit, "rat-collar", Rat, Hero),
            }, host.Calls.ToArray(), "the bite is a melee hit; the victim's items come first");
            CollectionAssert.AreEqual(host.Calls.ToArray(), result.ItemSpells.Select(f => (f.Mask, f.Item, f.Holder, f.Other)).ToArray());

            // Without a host nothing fires.
            Assert.AreEqual(0, owner.AttackByNpc(owner.Version, new(rat) { AttackerWorn = worn }).ItemSpells.Count);
            return;
        }
        Assert.Fail("no plain hit in the bounded run");
    }

    private static Dictionary<EquipmentSlot, SessionWearItem> AreaHeroWorn(GameSessionOwner owner) =>
        owner.QueryEquipment().Worn.ToDictionary(w => w.Slot, w => new SessionWearItem(w.Entity, Sturdy));

    [TestMethod]
    public void A_bite_on_another_NPC_fires_its_items_as_the_victim_and_the_biter_s_as_the_attacker()
    {
        using var owner = Owner();
        var rat = Combat(Rat);
        var victim = Combat(Victim);
        var ratWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Armor] = new("rat-collar", Sturdy) };
        var victimWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Helmet] = new("victim-cap", Sturdy) };
        for (var attempt = 0; attempt < 600 && !owner.QueryNpcKilled(Victim); attempt++)
        {
            var host = new Recorder();
            var result = owner.AttackByNpc(owner.Version, new(rat) { Target = victim, AttackerWorn = ratWorn, TargetWorn = victimWorn, ItemTriggers = host });
            if (!Plain(result) || result.Life!.Events.HasFlag(LifeEvents.Died)) continue;
            CollectionAssert.AreEqual(new (uint, string, string, string?)[]
            {
                (ParentHit | WeaponMelee, "victim-cap", Victim, Rat), (ParentDamagesOpponent, "rat-collar", Rat, Victim), (TargetHit, "rat-collar", Rat, Victim),
            }, host.Calls.ToArray());
            return;
        }
        Assert.Fail("no plain hit in the bounded run");
    }

    [TestMethod]
    public void A_bow_shot_names_the_wielded_bow_and_the_parent_hit_has_no_melee_bit()
    {
        using var owner = Owner();
        var archer = Combat(Archer, [BowFields()]);
        var worn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("archer-bow", Sturdy) };
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var host = new Recorder();
            var result = owner.AttackByNpc(owner.Version, new(archer)
            {
                Distance = 6, LineOfFire = new(0, false), AttackerWorn = worn, TargetWorn = AreaHeroWorn(owner), ItemTriggers = host,
            });
            if (!Plain(result)) continue;
            CollectionAssert.AreEqual(new (uint, string, string, string?)[]
            {
                (ParentHit, "kit:2", Hero, Archer), (ParentDamagesOpponent, "archer-bow", Archer, Hero), (TargetHit, "archer-bow", Archer, Hero),
                (ParentDamagesOpponentWithItem, "archer-bow", Archer, Hero),
            }, host.Calls.ToArray(), "the bow's reach is above 1: no melee bit; the wielded bow also fires with item");
            return;
        }
        Assert.Fail("no plain hit in the bounded run");
    }
}
