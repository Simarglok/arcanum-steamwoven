using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1720 (ledger PI5, OH3): the hero's re-wield after its weapon wore out (0x4B83E0 → 0x465170). The kit is three weapons (kit:1..kit:3);
// item values are invented.
[TestClass]
public sealed class WieldBestWeaponTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;
    private static readonly ItemCondition Baseline = new(15, 5, 0, false);

    private static EquipmentItem Weapon(int min, int max, int range = 1) => new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(min, max) }, Range = range },
    };

    private static readonly EquipmentItem Knife = Weapon(10, 20), Dagger = Weapon(1, 3), Axe = Weapon(30, 50), Spear = Weapon(5, 9, range: 3);

    private static SessionWearableItem Offer(int kit, EquipmentItem item) => new(Kit[0].Kind, [], false, item) { Worth = 10 + kit };

    private static SessionWearableLookup Lookup(params EquipmentItem?[] items) =>
        entity => entity.StartsWith("kit:", StringComparison.Ordinal) && int.Parse(entity[4..], System.Globalization.CultureInfo.InvariantCulture) is var n
            && n <= items.Length && items[n - 1] is { } item ? Offer(n, item) : null;

    private static GameSessionOwner Owner(EquipmentItem wielded)
    {
        var owner = CreateWith(HalfElf() with { Kit = [Kit[0], Kit[0], Kit[0]] });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Offer(1, wielded), Drawn, Accept).Disposition);
        return owner;
    }

    private static void Break(GameSessionOwner owner, string entity)
    {
        var broken = owner.DamageItem(owner.Version, entity, Baseline, 100);
        Assert.AreEqual((GameSessionDisposition.Changed, ItemBreakStep.Broken, (EquipmentSlot?)EquipmentSlot.Weapon), (broken.Disposition, broken.Break, broken.Unworn));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count, "the broken weapon left the slot");
    }

    private static string? Wielded(GameSessionOwner owner) => owner.QueryEquipment().Worn.SingleOrDefault(w => w.Slot == EquipmentSlot.Weapon)?.Entity;

    [TestMethod]
    public void ABrokenWeaponIsReplacedByTheBestWeaponLeft()
    {
        using var owner = Owner(Knife);
        Break(owner, "kit:1");
        var revision = owner.Version.Revision;
        // The broken knife is held but not eligible (its condition record wins over the host's facts); the axe beats the dagger.
        var result = owner.WieldBestWeapon(owner.Version, Lookup(Knife, Dagger, Axe), Drawn, Accept, null);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, revision + 1), (result.Disposition, result.Failure, owner.Version.Revision));
        Assert.AreEqual(new SessionEquipBestStep(EquipmentSlot.Weapon, null, "kit:3", EquipmentRefusal.None, null), result.Steps.Single());
        Assert.AreEqual("kit:3", Wielded(owner));
    }

    [TestMethod]
    public void AWeaponThatReachesTheTargetWinsOverAStrongerOneThatDoesNot()
    {
        using var owner = Owner(Knife);
        Break(owner, "kit:1");
        // The spear (range 3) is the only weapon that reaches a target 3 cells away; the axe (range 1) is stronger.
        var far = owner.WieldBestWeapon(owner.Version, Lookup(Knife, Spear, Axe), Drawn, Accept, new SessionWieldTarget(3, false));
        Assert.AreEqual(GameSessionDisposition.Changed, far.Disposition);
        Assert.AreEqual("kit:2", Wielded(owner), "the weapon that reaches the target");

        // Next to the target several reach it and the best of those wins; with no target the best overall wins too.
        using var near = Owner(Knife);
        Break(near, "kit:1");
        Assert.AreEqual(GameSessionDisposition.Changed, near.WieldBestWeapon(near.Version, Lookup(Knife, Spear, Axe), Drawn, Accept, new SessionWieldTarget(1, false)).Disposition);
        Assert.AreEqual("kit:3", Wielded(near));
        using var none = Owner(Knife);
        Break(none, "kit:1");
        Assert.AreEqual(GameSessionDisposition.Changed, none.WieldBestWeapon(none.Version, Lookup(Knife, Spear, Axe), Drawn, Accept, null).Disposition);
        Assert.AreEqual("kit:3", Wielded(none));
    }

    [TestMethod]
    public void WithNoUsableWeaponTheSlotStaysEmptyAndNothingChanges()
    {
        using var owner = Owner(Knife);
        Break(owner, "kit:1");
        var revision = owner.Version.Revision;
        var result = owner.WieldBestWeapon(owner.Version, Lookup(Knife), Drawn, Accept, new SessionWieldTarget(1, false));
        Assert.AreEqual((GameSessionDisposition.NoChange, revision), (result.Disposition, owner.Version.Revision));
        Assert.IsNull(Wielded(owner));
    }

    [TestMethod]
    public void TheHostsArtAnswerAndBadInputAreHonoured()
    {
        using var owner = Owner(Knife);
        Break(owner, "kit:1");
        // The host's art answer decides: a weapon whose art cannot be drawn is not wielded (the dagger stays in the pack).
        var refused = owner.WieldBestWeapon(owner.Version, Lookup(Knife, Dagger), (_, _) => false, Accept, null);
        Assert.AreEqual(GameSessionDisposition.NoChange, refused.Disposition);
        Assert.IsNull(Wielded(owner));
        var wielded = owner.WieldBestWeapon(owner.Version, Lookup(Knife, Dagger), Drawn, Accept, null);
        Assert.AreEqual("kit:2", Wielded(owner));
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Weapon }, wielded.Steps.Select(step => step.Slot).ToArray(), "only the weapon slot is asked");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.WieldBestWeapon(owner.Version, Lookup(Knife), Drawn, Accept, new SessionWieldTarget(-1, false)));
        var stale = owner.WieldBestWeapon(owner.Version with { Revision = 0 }, Lookup(Knife), Drawn, Accept, null);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
    }
}
