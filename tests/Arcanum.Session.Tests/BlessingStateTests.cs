using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's blessings and curses (ARC-WORLD-035). Numbers and effects are invented; the half-elf fixture hero has ST 8.
[TestClass]
public sealed class BlessingStateTests
{
    private const long T0 = 54_000_000;
    private static readonly SessionEffectDefinition Strong = new(120, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 2)]);
    private static readonly SessionEffectDefinition Nothing = new(4, []);

    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HeroStateTests.HalfElf() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static int Strength(GameSessionOwner owner) => owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength);

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static void Changed(SessionEffectResult result, GameSessionOwner owner, int entries)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, entries), (result.Disposition, result.Failure, result.Entries));
        Assert.AreEqual(owner.Version, result.Version);
    }

    [TestMethod]
    public void ABlessingIsHeldOnceWithItsTimeAndAddsItsEffect()
    {
        using var owner = Owner();
        Assert.AreEqual(0, owner.QueryHeroBlessings(SessionBlessingKind.Blessing)!.Count);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 55, Strong), owner, 1);
        Assert.AreEqual(1, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { new SessionBlessing(55, 120, T0) }, owner.QueryHeroBlessings(SessionBlessingKind.Blessing)!.ToArray());
        CollectionAssert.AreEqual(new SessionEffect[] { new(120, EffectCause.Bless) }, owner.QueryHeroEffects()!.Effects.ToArray());
        Assert.AreEqual(10, Strength(owner));
        // Already held: nothing, whatever effect the host offers (the source does not look at it).
        var again = owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 55, Strong with { Number = 400 });
        Assert.AreEqual((GameSessionDisposition.NoChange, 1L), (again.Disposition, owner.Version.Revision));

        // A curse with the same number is another list, with the curse cause; a second blessing goes after the first.
        Advance(owner, 1000);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Curse, 55, Nothing), owner, 1);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 60, Strong), owner, 1);
        CollectionAssert.AreEqual(new[] { new SessionBlessing(55, 4, T0 + 1000) }, owner.QueryHeroBlessings(SessionBlessingKind.Curse)!.ToArray());
        CollectionAssert.AreEqual(new[] { new SessionBlessing(55, 120, T0), new SessionBlessing(60, 120, T0 + 1000) },
            owner.QueryHeroBlessings(SessionBlessingKind.Blessing)!.ToArray());
        CollectionAssert.AreEqual(new SessionEffect[] { new(120, EffectCause.Bless), new(4, EffectCause.Curse), new(120, EffectCause.Bless) },
            owner.QueryHeroEffects()!.Effects.ToArray());
        Assert.AreEqual(12, Strength(owner));
    }

    [TestMethod]
    public void RemovingTakesTheFirstEntryAndTheFirstEffectOfItsNumber()
    {
        using var owner = Owner();
        Changed(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Item), owner, 1);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 55, Strong), owner, 1);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 56, Nothing), owner, 1);
        // The removal (0x4C4450): the list closes up and the typed removal (0x4EA100) takes the item's entry, the first 120.
        Changed(owner.RemoveHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 55), owner, 1);
        CollectionAssert.AreEqual(new[] { new SessionBlessing(56, 4, T0) }, owner.QueryHeroBlessings(SessionBlessingKind.Blessing)!.ToArray());
        CollectionAssert.AreEqual(new SessionEffect[] { new(120, EffectCause.Bless), new(4, EffectCause.Bless) }, owner.QueryHeroEffects()!.Effects.ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 55).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveHeroBlessing(owner.Version, SessionBlessingKind.Curse, 56).Disposition);
        // With its effect already gone, removing a blessing still changes the list.
        Changed(owner.RemoveHeroEffect(owner.Version, 4), owner, 1);
        Changed(owner.RemoveHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 56), owner, 0);
        Assert.AreEqual(0, owner.QueryHeroBlessings(SessionBlessingKind.Blessing)!.Count);
    }

    [TestMethod]
    public void RefusalsAreTypedAndWriteNothing()
    {
        using (var plain = Owner(hero: false))
        {
            Assert.IsNull(plain.QueryHeroBlessings(SessionBlessingKind.Curse));
            Assert.AreEqual(GameSessionFailure.EffectsUnavailable, plain.AddHeroBlessing(plain.Version, SessionBlessingKind.Curse, 50, Strong).Failure);
            Assert.AreEqual(GameSessionFailure.EffectsUnavailable, plain.RemoveHeroBlessing(plain.Version, SessionBlessingKind.Curse, 50).Failure);
        }
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.AddHeroBlessing(new(owner.Identity, 5), SessionBlessingKind.Blessing, 50, Strong).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RemoveHeroBlessing(new(owner.Identity, 5), SessionBlessingKind.Blessing, 50).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEffect, owner.AddHeroBlessing(owner.Version, (SessionBlessingKind)2, 50, Strong).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEffect, owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 50, Strong with { Number = 400 }).Failure);
        // The fixture's innate race effect 67, and an effect the hero already has under another rule.
        Assert.AreEqual(GameSessionFailure.InnateEffect, owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 50, Strong with { Number = 67 }).Failure);
        Changed(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Item), owner, 1);
        Assert.AreEqual(GameSessionFailure.EffectMismatch,
            owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 50, Strong with { Changes = [] }).Failure);
        Assert.AreEqual((0, 1L), (owner.QueryHeroBlessings(SessionBlessingKind.Blessing)!.Count, owner.Version.Revision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryHeroBlessings((SessionBlessingKind)2));
    }

    [TestMethod]
    public void AListHoldsAtMostSixtyFourNumbers()
    {
        // The effect entries go away as the list fills, so only the list's own bound stops it.
        using var owner = Owner();
        for (var number = 50; number < 50 + SessionEffectLimits.MaxBlessings; number++)
        {
            Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, number, Nothing), owner, 1);
            Changed(owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Bless, all: true), owner, 1);
        }
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 50, Nothing).Disposition);
        Assert.AreEqual(GameSessionFailure.EffectCapacityExceeded, owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 200, Nothing).Failure);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Curse, 200, Nothing), owner, 1);
        // A full list still saves and restores.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(SessionEffectLimits.MaxBlessings, restored.QueryHeroBlessings(SessionBlessingKind.Blessing)!.Count);
    }

    [TestMethod]
    public void BlessingsRoundTripAndRestoreRefusesListsNoChangeProduces()
    {
        using var owner = Owner();
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 55, Strong), owner, 1);
        Changed(owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Curse, 52, Nothing), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, $"\"blessings\":[{{\"number\":55,\"effect\":120,\"timeMs\":{T0}}}],\"curses\":[{{\"number\":52,\"effect\":4,\"timeMs\":{T0}}}]}}");
        using (var restored = Restore(Decode(text)))
        {
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
            Changed(restored.RemoveHeroBlessing(restored.Version, SessionBlessingKind.Blessing, 55), restored, 1);
            Assert.AreEqual(8, Strength(restored));
        }
        void Refused(string from, string to, GameSessionFailure failure) => Assert.AreEqual(failure,
            GameSessionOwner.Restore(Decode(text.Replace(from, to, StringComparison.Ordinal)), RestoreOptions()).Failure);
        var curse = $"{{\"number\":52,\"effect\":4,\"timeMs\":{T0}}}";
        Refused(curse, curse + "," + curse, GameSessionFailure.InvalidEffect);
        Refused(curse, curse.Replace($"{T0}", $"{T0 + 1}"), GameSessionFailure.InvalidEffect);
        Refused(curse, curse.Replace("\"effect\":4", "\"effect\":67"), GameSessionFailure.InvalidEffect);
        var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(curse, curse.Replace("\"effect\":4", "\"effect\":400"), StringComparison.Ordinal));
        Assert.AreEqual((SnapshotDecodeFailure.OutOfRange, "$.effects.curses[0].effect"), (decoded.Failure, decoded.Path));
        decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(",\"curses\":[", ",\"cursed\":[", StringComparison.Ordinal));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, decoded.Failure);
        using var plain = Owner(hero: false);
        var heroless = GameSessionSnapshotCodec.Encode(plain.Capture()).Replace("\"curses\":[]", "\"curses\":[" + curse + "]", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(heroless), RestoreOptions()).Failure);
    }
}
