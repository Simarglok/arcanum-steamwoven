using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-015: the spell UI's activation, cursor mask and no-target message (ledger SU rows). Masks are the target names' values.
[TestClass]
public sealed class SpellUiRulesTests
{
    private const ulong Self = 0x1, Critter = 0x44, Tile = 0x8000UL << 32, NoSelf = 0x10000004, Inventory = 0x2000004;

    private static SpellActivation Activate(ulong begin, bool aggressive = false, bool alt = false, bool shift = false, bool active = true,
        bool enabled = true, bool points = true) => SpellUiRules.Activate(active, enabled, begin, aggressive, alt, shift, points);

    [TestMethod]
    public void A_caster_that_cannot_act_or_a_disabled_spell_does_nothing()
    {
        Assert.AreEqual(SpellActivationKind.Nothing, Activate(Critter, active: false).Kind);
        Assert.AreEqual(SpellActivationKind.Nothing, Activate(Critter, enabled: false).Kind);
    }

    [TestMethod]
    public void An_area_without_a_target_casts_at_once_before_the_action_points_are_asked()
    {
        foreach (var begin in new[] { 0UL, SpellUiRules.RadiusBit, Self, Self | Critter })
            Assert.AreEqual(SpellActivationKind.CastAtCaster, Activate(begin, points: false).Kind, $"0x{begin:X}");
        Assert.AreEqual(SpellActivationKind.Target, Activate(Self | Tile).Kind, "self with the tile bit needs a target");
        Assert.AreEqual(SpellActivationKind.Target, Activate(SpellUiRules.RadiusBit | 0x4).Kind, "only exactly the radius bit");
    }

    [TestMethod]
    public void A_targeted_spell_needs_its_action_points_and_opens_the_cursor()
    {
        Assert.AreEqual(SpellActivationKind.NoActionPoints, Activate(Critter, points: false).Kind);
        Assert.AreEqual(new SpellActivation(SpellActivationKind.Target, Critter), Activate(Critter));
    }

    [TestMethod]
    public void An_aggressive_spell_spares_the_party_unless_an_alt_key_is_held_and_alt_toggles_the_open_cursor()
    {
        Assert.AreEqual(Critter | SpellUiRules.NonPartyBit, Activate(Critter, aggressive: true).Mask);
        Assert.AreEqual(Critter, Activate(Critter, aggressive: true, alt: true).Mask);
        Assert.AreEqual(Critter, SpellUiRules.AltPressed(Critter | SpellUiRules.NonPartyBit, true));
        Assert.AreEqual(Critter | SpellUiRules.NonPartyBit, SpellUiRules.AltReleased(Critter, true));
        Assert.AreEqual(Critter, SpellUiRules.AltReleased(Critter, false), "a spell that is not aggressive keeps its mask");
    }

    [TestMethod]
    public void Shift_casts_an_object_spell_at_the_player_unless_it_may_not_target_its_caster()
    {
        Assert.AreEqual(SpellActivationKind.CastAtPlayer, Activate(Critter, shift: true).Kind);
        Assert.AreEqual(SpellActivationKind.Target, Activate(NoSelf, shift: true).Kind);
        Assert.AreEqual(SpellActivationKind.Target, Activate(Tile, shift: true).Kind, "a tile spell has no object bit");
        Assert.AreEqual(SpellActivationKind.NoActionPoints, Activate(Critter, shift: true, points: false).Kind, "the action points come first");
    }

    [TestMethod]
    public void An_inventory_spell_is_recognised_and_only_the_not_damaged_reason_has_a_message()
    {
        Assert.IsTrue(SpellUiRules.TargetsInventory(Inventory));
        Assert.IsFalse(SpellUiRules.TargetsInventory(Critter));
        Assert.AreEqual(606, SpellUiRules.NoTargetMessage(SpellUiRules.DamagedBit));
        Assert.IsNull(SpellUiRules.NoTargetMessage(0x400UL << 32));
        Assert.IsNull(SpellUiRules.NoTargetMessage(0));
    }
}
