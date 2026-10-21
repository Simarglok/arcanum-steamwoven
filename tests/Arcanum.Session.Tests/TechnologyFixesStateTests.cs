using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-TECHNOLOGY-009: the owner's decisions of 2026-10-21 in the session. AMA5: a device in a container pays from the container's
// cell stack in Steamwoven; Original keeps the failed lookup. CCA1: an affordability check spends nothing in either mode. A chest
// with cells and bullets beside a hero with its own cells; entities, prototypes and numbers are invented (cells follow 7060).
[TestClass]
public sealed class TechnologyFixesStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SpellPaymentRecord Cells = new(ChargesFromCells: true, MaintainPeriod: 0, DurationSeconds: 0);
    private static readonly SpellPayer Device = new(SpellPayerKind.Item);

    private static SessionItemFacts Ammo(int prototype, int type, int units) =>
        new(new(SessionItemType.Ammo, prototype, 100, 200), 1, 0, 0, units, []) { AmmoType = type };

    private static SessionMapInventorySeed Seed() => new(
        [new("chest", SessionHolderKind.Container, false, false, [], ["chest-bullets", "chest-cells"])],
        [
            new("chest-bullets", Ammo(7059, 1, 7), null),
            new("chest-cells", Ammo(7060, 2, 6), null),
            new("hero-cells", Ammo(7060, 2, 10), new CellCoord(101, 200)),
        ]);

    private static GameSessionOwner Owner(RuleMode mode)
    {
        var owner = CreateWith(HalfElf());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        owner.SelectRules(mode);
        return owner;
    }

    [TestMethod]
    public void A_device_in_a_container_pays_from_the_containers_cells_in_steamwoven_only()
    {
        using var steamwoven = Owner(RuleMode.Steamwoven);
        var source = steamwoven.ContainerCellSource("chest", -50)!;
        // The parent is no critter, so the power is the complexity: a technological item needs 2 × 4 − trunc(4 × −50 / −50) = 4.
        var paid = SpellPaymentRules.Pay(Device, 4, true, 147, Cells, source);
        Assert.AreEqual((SpellPaymentStatus.Paid, 4), (paid.Status, paid.Cells!.Cells));
        Assert.AreEqual(new SessionAmmoRecord("chest-cells", 2, 2), steamwoven.QueryAmmo("chest-cells"), "the chest's stack keeps the rest");
        Assert.AreEqual(1L, steamwoven.Version.Revision, "one revision a spend");
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, SpellPaymentRules.Pay(Device, 3, true, 147, Cells, source).Status, "too few cells");
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Device, 2, true, 147, Cells, source).Status);
        Assert.IsTrue(steamwoven.IsItemDestroyed("chest-cells"), "the last cells remove the stack");
        Assert.AreEqual(new SessionAmmoRecord("chest-bullets", 1, 7), steamwoven.QueryAmmo("chest-bullets"), "another type is untouched");
        Assert.AreEqual(GameSessionFailure.AmmoShort, steamwoven.SpendAmmo(steamwoven.Version, SessionAmmoHolder.ContainerOf("chest"), 2, 1).Failure, "no stack left");
        var bullets = steamwoven.SpendAmmo(steamwoven.Version, SessionAmmoHolder.ContainerOf("chest"), 1, 3);
        Assert.AreEqual((GameSessionDisposition.Changed, "chest-bullets", 4), (bullets.Disposition, bullets.Stack, bullets.UnitsAfter));
        Assert.AreEqual("chest-bullets:4", string.Join(" ", steamwoven.QueryMapInventory(Exterior)!.Holders.Single().Items
            .Select(item => $"{item.Entity}:{item.Facts.Quantity}")), "the chest shows its spent units; the emptied cell stack is gone");

        // Original: the container's lookup never finds its stack (AM6), so the device has no cells and a spend finds nothing.
        using var original = Owner(RuleMode.Original);
        var refused = SpellPaymentRules.Pay(Device, 4, true, 147, Cells, original.ContainerCellSource("chest", -50)!);
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, refused.Status);
        Assert.AreEqual(GameSessionFailure.AmmoShort, original.SpendAmmo(original.Version, SessionAmmoHolder.ContainerOf("chest"), 2, 1).Failure);
        Assert.AreEqual((new SessionAmmoRecord("chest-cells", 2, 6), 0L), (original.QueryAmmo("chest-cells"), original.Version.Revision));
        // The switch is read at each decision: back in Steamwoven the same session finds the stack.
        original.SelectRules(RuleMode.Steamwoven);
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Device, 4, true, 147, Cells, original.ContainerCellSource("chest", -50)!).Status);
    }

    [TestMethod]
    public void A_container_holder_is_checked_and_a_tracked_container_stack_is_saved()
    {
        using var owner = Owner(RuleMode.Steamwoven);
        Assert.IsNull(owner.ContainerCellSource("nowhere", -50), "no attached seed lists it");
        Assert.IsNull(owner.ContainerCellSource("hero-cells", -50), "an item is no container");
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.SpendAmmo(owner.Version, SessionAmmoHolder.ContainerOf("nowhere"), 2, 1).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.SpendAmmo(owner.Version, new SessionAmmoHolder("chest"), 2, 1).Failure, "a container is no NPC");
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.SpendAmmo(owner.Version, new SessionAmmoHolder("smith", "chest"), 2, 1).Failure, "both kinds at once");
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.SpendAmmo(owner.Version, SessionAmmoHolder.ContainerOf("made:1"), 2, 1).Failure);
        Assert.IsFalse(SessionAmmoHolder.ContainerOf("chest").IsHero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendAmmo(owner.Version, SessionAmmoHolder.ContainerOf("chest"), 2, 5).Disposition);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, Seed()).Disposition);
        Assert.AreEqual(new SessionAmmoRecord("chest-cells", 2, 1), restored.QueryAmmo("chest-cells"), "the spent units are saved");
    }

    [TestMethod]
    public void An_affordability_check_spends_no_cells_in_either_mode()
    {
        foreach (var mode in new[] { RuleMode.Original, RuleMode.Steamwoven })
        {
            using var owner = Owner(mode);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "hero-cells", Accept).Disposition);
            var revision = owner.Version.Revision;
            var hero = owner.HeroCellSource()!;
            var check = SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50), 7, false, 168, Cells, hero);
            Assert.AreEqual((SpellPaymentStatus.Paid, revision, 10), (check.Status, owner.Version.Revision, owner.QueryHeroAmmo().Single().Units),
                $"{mode}: CCA1 decided without a switch, the check takes nothing");
            var device = owner.HeroCellSource(itemComplexity: -50)!;
            Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Device, 3, false, 147, Cells, device).Status);
            Assert.AreEqual((revision, 10), (owner.Version.Revision, owner.QueryHeroAmmo().Single().Units), $"{mode}: an item's check too");
            if (mode == RuleMode.Steamwoven)
            {
                Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Device, 4, false, 147, Cells, owner.ContainerCellSource("chest", -50)!).Status);
                Assert.AreEqual((revision, new SessionAmmoRecord("chest-cells", 2, 6)), (owner.Version.Revision, owner.QueryAmmo("chest-cells")), "a container's check too");
            }
        }
    }
}
