using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-033: the hero's player name (PC field 0x10E, the new-character window's Accept 0x545780, ledger NW6) and portrait (field 0xE7)
// in the session and its save (schema 56). Names and portrait numbers are invented.
[TestClass]
public sealed class HeroNameTests
{
    private static readonly byte[] Name = [0x52, 0x65, 0x6E, 0xE9]; // "René" in code page 1252

    private static GameSessionOwner Owner(int portrait = 0)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Portrait = portrait } });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Refused(GameSessionOwner owner, byte[] name, GameSessionFailure failure, GameSessionVersion? version = null)
    {
        var (revision, before) = (owner.Version.Revision, owner.QueryHero()?.PlayerName.ToArray());
        var result = owner.SetHeroPlayerName(version ?? owner.Version, name);
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        CollectionAssert.AreEqual(before, owner.QueryHero()?.PlayerName.ToArray());
    }

    [TestMethod]
    public void A_hero_starts_without_a_name_and_with_its_definition_s_portrait()
    {
        using var owner = Owner(1005);
        Assert.AreEqual(0, owner.QueryHero()!.PlayerName.Length);
        Assert.AreEqual(1005, owner.QueryHero()!.Portrait);
        using var plain = Owner();
        Assert.AreEqual(0, plain.QueryHero()!.Portrait, "no portrait: 0");
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Portrait = -1 } }).Failure,
            "a negative portrait is the portrait code's bug (PO2) and is refused");
    }

    [TestMethod]
    public void Setting_the_name_stores_its_bytes_in_one_revision()
    {
        using var owner = Owner();
        var set = owner.SetHeroPlayerName(owner.Version, Name);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 1L), (set.Disposition, set.Failure, owner.Version.Revision));
        CollectionAssert.AreEqual(Name, owner.QueryHero()!.PlayerName.ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetHeroPlayerName(owner.Version, Name).Disposition, "the same name");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetHeroPlayerName(owner.Version, []).Disposition, "an empty name clears it");
        Assert.AreEqual(0, owner.QueryHero()!.PlayerName.Length);
        var longest = Enumerable.Repeat((byte)'a', SessionHeroLimits.MaxPlayerNameBytes).ToArray();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetHeroPlayerName(owner.Version, longest).Disposition);
    }

    [TestMethod]
    public void A_name_too_long_with_a_nul_or_on_a_stale_version_is_refused()
    {
        using var owner = Owner();
        var stale = owner.Version;
        owner.SetHeroPlayerName(owner.Version, Name);
        Refused(owner, [.. Enumerable.Repeat((byte)'a', SessionHeroLimits.MaxPlayerNameBytes + 1)], GameSessionFailure.InvalidPlayerName);
        Refused(owner, [0x41, 0x00, 0x42], GameSessionFailure.InvalidPlayerName);
        Refused(owner, [0x41], GameSessionFailure.StaleVersion, stale);
        using var heroless = Create();
        Refused(heroless, Name, GameSessionFailure.InvalidHero);
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.SetHeroPlayerName(owner.Version, null!));
    }

    [TestMethod]
    public void The_name_and_portrait_are_saved_as_hex_and_a_number()
    {
        using var owner = Owner(1003);
        owner.SetHeroPlayerName(owner.Version, Name);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"playerName\":\"52656ee9\",\"portrait\":1003}");
        using var restored = GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot!, RestoreOptions()).Owner!;
        CollectionAssert.AreEqual(Name, restored.QueryHero()!.PlayerName.ToArray());
        Assert.AreEqual(1003, restored.QueryHero()!.Portrait);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        foreach (var bad in new[] { "\"52656EE9\"", "\"52656ee\"", "\"zz\"" })
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace("\"52656ee9\"", bad));
            Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.hero.playerName"), (decoded.Failure, decoded.Path), bad);
        }
        Assert.AreEqual(SnapshotDecodeFailure.WrongType, GameSessionSnapshotCodec.TryDecode(text.Replace("\"52656ee9\"", "1")).Failure);
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(text.Replace(",\"portrait\":1003", "")).Failure);
        // Ranges are the restore's: a NUL byte, a name over the bound or a negative portrait is an invalid hero.
        foreach (var bad in new[] { text.Replace("52656ee9", "5200"), text.Replace("52656ee9", string.Concat(Enumerable.Repeat("61", SessionHeroLimits.MaxPlayerNameBytes + 1))),
                     text.Replace("\"portrait\":1003", "\"portrait\":-1") })
            Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(bad).Snapshot!, RestoreOptions()).Failure);
    }
}
