using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-2159 (ledger IW35, IE4; exe 0x578EA0 -> 0x461CA0 -> 0x466640): a sale onto a slot of a merchant's doll runs the item's event scripts with the merchant as the wearer: its wield on (24) and insert item (21) change the merchant's own effects, the weight test is the
// caller's insert check; a merchant without a wield context, or a verdict that refuses, sells nothing. Entities and scripts are invented.
[TestClass]
public sealed class BarterSellWornScriptTests
{
    private const string Merchant = "oid:merchant";
    private static readonly SessionLifeTarget Target = SessionCritterCombat.From(Merchant, AttackStateTests.Npc()).Combat!.Target;

    private static readonly EquipmentItem RingFacts = new()
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring, AcAdjustment = 1 },
    };

    private static SessionInsertVerdict Accept(SessionInventoryItem _) => SessionInsertVerdict.Accepted;

    private static GameSessionOwner Open()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior,
            new SessionMapInventorySeed([new(Merchant, SessionHolderKind.Npc, false, true, [], [])], [])).Failure);
        return owner;
    }

    private static SessionWearableItem Ring(params int[] points) => new(Kit[2].Kind, points, false, RingFacts);

    private static SessionNpcWieldScripts Wield(NpcWieldItemScriptTests.Host host) => new(host, Target, [.. AttackStateTests.Stats()]);

    private static int Charms(GameSessionOwner owner) => owner.QueryNpcEffects(Target)!.Entries.Count(e => e.Number == NpcWieldItemScriptTests.Charm && e.Cause == EffectCause.Item);

    [TestMethod]
    public void The_merchant_wears_a_sold_ring_with_its_wield_on_script_over_its_own_effects()
    {
        using var owner = Open();
        var host = new NpcWieldItemScriptTests.Host();
        host.Slots[("kit:3", SessionScriptPoint.WieldOn)] = NpcWieldItemScriptTests.AddCharm;
        var ring = Ring(SessionScriptPoint.WieldOn);
        var before = owner.Version;

        // Without the script host or the merchant's wield context the sale is refused with nothing changed.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.BarterSellWorn(owner.Version, Merchant, "kit:3", EquipmentSlot.Ring1, ring, 0, default, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.BarterSellWorn(owner.Version, Merchant, "kit:3", EquipmentSlot.Ring1, ring, 0, default, Accept, scripts: host).Failure);
        // The weight test (the caller's insert check) refuses before any script runs.
        var heavy = owner.BarterSellWorn(owner.Version, Merchant, "kit:3", EquipmentSlot.Ring1, ring, 0, default, _ => SessionInsertVerdict.TooHeavy, scripts: host, npcWearer: Wield(host));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, SessionInsertVerdict.TooHeavy, 0), (heavy.Disposition, heavy.Failure, heavy.Verdict, heavy.Scripts.Count));
        Assert.AreEqual((before, 0, null), (owner.Version, Charms(owner), owner.QueryNpcWorn(Merchant)));

        var sold = owner.BarterSellWorn(owner.Version, Merchant, "kit:3", EquipmentSlot.Ring1, ring, 0, default, Accept, scripts: host, npcWearer: Wield(host));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (sold.Disposition, sold.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOn, "kit:3") }, sold.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(1, Charms(owner), "the wield on added its charm to the merchant");
        Assert.AreEqual(0, owner.QueryHeroEffects()!.Effects.Count(effect => effect.Number == NpcWieldItemScriptTests.Charm), "and none to the hero");
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Ring1, "kit:3") }, owner.QueryNpcWorn(Merchant)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray());
    }

    [TestMethod]
    public void Wield_on_and_insert_item_run_for_the_merchant_in_that_order_and_the_insert_run_is_reported()
    {
        using var owner = Open();
        var host = new NpcWieldItemScriptTests.Host();
        host.Slots[("kit:3", SessionScriptPoint.WieldOn)] = NpcWieldItemScriptTests.AddCharm;
        host.Slots[("kit:3", SessionScriptPoint.InsertItem)] = NpcWieldItemScriptTests.Say;
        var sold = owner.BarterSellWorn(owner.Version, Merchant, "kit:3", EquipmentSlot.Ring1, Ring(SessionScriptPoint.WieldOn, SessionScriptPoint.InsertItem), 0, default, Accept, scripts: host, npcWearer: Wield(host));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (sold.Disposition, sold.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOn, "kit:3"), (SessionScriptPoint.InsertItem, "kit:3") }, sold.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.IsNotNull(sold.InsertScript);
        Assert.AreEqual(1, sold.Messages.Count, "the insert script's printed line");
        Assert.AreEqual(1, Charms(owner));
    }

    [TestMethod]
    public void A_run_the_session_cannot_apply_refuses_the_sale_and_leaves_no_worn_record()
    {
        using var owner = Open();
        var host = new NpcWieldItemScriptTests.Host();
        host.Slots[("kit:3", SessionScriptPoint.WieldOn)] = NpcWieldItemScriptTests.Broken;
        var before = owner.Version;
        var refused = owner.BarterSellWorn(owner.Version, Merchant, "kit:3", EquipmentSlot.Ring1, Ring(SessionScriptPoint.WieldOn), 0, default, Accept, scripts: host, npcWearer: Wield(host));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        Assert.AreEqual((before, 0, null), (owner.Version, Charms(owner), owner.QueryNpcWorn(Merchant)));
    }
}
