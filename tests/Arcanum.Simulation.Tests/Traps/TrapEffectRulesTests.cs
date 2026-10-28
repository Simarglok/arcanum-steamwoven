using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Traps.TrapEffectRules;

namespace Arcanum.Simulation.Tests.Traps;

// ARC-WORLD-107: the native trap effects of ledger TT11–TT14. Counters, tags and amounts are invented within the original's ranges.
[TestClass]
public sealed class TrapEffectRulesTests
{
    private sealed class Rolls(params int[] values) : ICombatRollSource
    {
        private readonly Queue<int> _values = new(values);
        public List<(CombatRollPurpose Purpose, int Min, int Max)> Asked { get; } = [];

        public int Roll(CombatRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return _values.Dequeue();
        }
    }

    // Counters as the script stores them: spell (top byte), shooter tag, maximum, minimum (low byte).
    private static int Counters(int spell, int tag, int second, int low) => unchecked((int)((uint)spell << 24 | (uint)tag << 16 | (uint)second << 8 | (uint)low));

    [TestMethod]
    public void Each_native_script_decodes_its_kind_damage_type_range_spell_and_tag()
    {
        var counters = Counters(0xC8, 7, 12, 3);
        Assert.AreEqual(new TrapEffect(30000, TrapEffectKind.Spell, DamageType.Normal, 0, 0, 0xC8, 0), Of(30000, counters));
        Assert.AreEqual(new TrapEffect(30001, TrapEffectKind.Damage, DamageType.Normal, 3, 12, 0xC8, 0), Of(30001, counters));
        Assert.AreEqual(new TrapEffect(30002, TrapEffectKind.Shot, DamageType.Normal, 3, 12, 0xC8, 7), Of(30002, counters));
        Assert.AreEqual(new TrapEffect(30003, TrapEffectKind.Damage, DamageType.Normal, 3, 12, 0xC8, 0), Of(30003, counters));
        Assert.AreEqual(DamageType.Fire, Of(30004, counters)!.Value.DamageType);
        Assert.AreEqual(DamageType.Electrical, Of(30005, counters)!.Value.DamageType);
        Assert.AreEqual(DamageType.Poison, Of(30006, counters)!.Value.DamageType);
        // 30007 (the shooter's script) and any other number are no effect.
        Assert.IsNull(Of(30007, counters));
        Assert.IsNull(Of(29999, counters));
        Assert.IsNull(Of(30008, counters));
    }

    [TestMethod]
    public void The_spell_trap_always_casts_and_a_damage_or_shot_casts_only_a_nonzero_top_byte()
    {
        Assert.IsTrue(Of(30000, Counters(0, 0, 0, 0))!.Value.CastsSpell, "30000 casts spell 0 too");
        Assert.IsFalse(Of(30001, Counters(0, 0, 5, 1))!.Value.CastsSpell);
        Assert.IsTrue(Of(30006, Counters(1, 0, 5, 1))!.Value.CastsSpell);
        Assert.AreEqual(0xFF, Of(30002, Counters(0xFF, 0, 0, 0))!.Value.Spell, "the top byte is unsigned");
    }

    [TestMethod]
    public void The_shooter_is_the_first_listed_30007_object_with_the_traps_tag()
    {
        TrapShooterCandidate[] listed =
        [
            new("wall", 30007, Counters(0, 3, 0, 0)), new("scenery", 30002, Counters(0, 7, 0, 0)),
            new("first", 30007, Counters(9, 7, 1, 1)), new("second", 30007, Counters(0, 7, 0, 0)),
        ];
        Assert.AreEqual("first", Shooter(listed, 7), "the tag is the third byte only; another script never shoots");
        Assert.AreEqual("wall", Shooter(listed, 3));
        Assert.IsNull(Shooter(listed, 4));
        Assert.IsNull(Shooter([], 7));
    }

    [TestMethod]
    public void Only_a_pc_placed_trap_credits_the_player()
    {
        Assert.IsTrue(CreditsPlayer(TrapRules.PlacedByPcFlag));
        Assert.IsTrue(CreditsPlayer(TrapRules.PlacedByPcFlag | TrapRules.SpottedFlag));
        Assert.IsFalse(CreditsPlayer(TrapRules.SpottedFlag));
    }

    [TestMethod]
    public void The_amount_is_the_random_range_with_no_draw_for_an_empty_range_and_a_reversed_one_swapped()
    {
        var rolls = new Rolls(9, 4);
        Assert.AreEqual(9, Amount(Of(30001, Counters(0, 0, 12, 3))!.Value, rolls));
        Assert.AreEqual(4, Amount(Of(30004, Counters(0, 0, 2, 6))!.Value, rolls));
        Assert.AreEqual(5, Amount(Of(30006, Counters(0, 0, 5, 5))!.Value, rolls), "an empty range draws nothing");
        Assert.AreEqual(0, Amount(Of(30000, Counters(1, 0, 9, 1))!.Value, rolls), "a spell trap draws nothing");
        CollectionAssert.AreEqual(new[] { (CombatRollPurpose.DamageNormal, 3, 12), (CombatRollPurpose.DamageFire, 2, 6) }, rolls.Asked);
        Assert.AreEqual(new DamageByType(0, 0, 7, 0, 0), Damage(Of(30005, 0)!.Value, 7));
    }
}
