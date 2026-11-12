using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-095 (MIG7d, TM10, GR36): the links of an NPC (its combat focus, who hit it last, who it flees from, its enemy list) are fields of its
/// directory entry, found by the holder's identity, with the map the holder's links were written on and the targets' identities; they are saved as the
/// rows of <c>objects.links</c> (schema 65). Under the Original setting a link to an object that has since left for another map is erased when the
/// holder's map opens (GQ7 B); Steamwoven keeps the identity.
/// </summary>
[TestClass]
public sealed class ObjectLinkStateTests
{
    private const string Wolf = "oid:wolf", Sogg = "oid:sogg", Virgil = "oid:virgil", Owl = "oid:owl";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant WolfHandle = SessionCombatant.Critter(Wolf), SoggHandle = SessionCombatant.Critter(Sogg),
        VirgilHandle = SessionCombatant.Critter(Virgil);
    private static readonly ActorPlacement AtCave = new(Cave, new(3, 4), NeighborDirection.YPlus);
    private static readonly ActorPlacement AtExterior = new(Exterior, new(5, 5), NeighborDirection.XPlus);

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionDangerRequest Focus(string entity, SessionCombatant? target) =>
        new() { Entity = entity, State = DangerState.CombatFocus, Target = target, Reaction = new(new(60, Arcanum.Simulation.Character.CharacterRace.Human, false, false),
            new(Arcanum.Simulation.Character.CharacterRace.HalfElf, 0, 0, [], [], [], false), 30) };

    private static void Moves(GameSessionOwner owner, ActorPlacement to) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, to, []).Disposition);

    // The wolf (on the exterior) is focused on, was hit last by and lists Virgil, and also lists the hero; Virgil is then recorded as standing on the cave.
    private static GameSessionOwner WolfHoldingVirgil(RuleMode rules)
    {
        var owner = Owner(rules);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Focus(Wolf, VirgilHandle)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Wolf, VirgilHandle).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddDangerEnemy(owner.Version, Wolf, Hero).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddDangerEnemy(owner.Version, Wolf, VirgilHandle).Disposition);
        return owner;
    }

    private static void VirgilStandsOn(GameSessionOwner owner, SessionMapId map) =>
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Virgil, "npc.map"), map.RuntimeMap).Disposition);

    [TestMethod]
    public void TheLinksAreFieldsOfTheHoldersEntryOnTheMapTheyWereWrittenOn()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Steamwoven);
        var links = owner.QueryObjectLinks(Wolf);
        CollectionAssert.AreEqual(new[] { "link.enemies", "link.focus", "link.hitby" }, links.Select(link => link.Property).ToArray(), "field order, ordinal");
        Assert.IsTrue(links.All(link => link.Entity == Wolf && link.Map == Exterior));
        CollectionAssert.AreEqual(new[] { Hero, VirgilHandle }, links[0].Targets.ToArray(), "the enemy list keeps its order");
        CollectionAssert.AreEqual(new[] { VirgilHandle }, links[1].Targets.ToArray());
        CollectionAssert.AreEqual(new[] { VirgilHandle }, links[2].Targets.ToArray());
        Assert.AreEqual(3, owner.QueryObjectLinks(Exterior).Count);
        Assert.AreEqual(0, owner.QueryObjectLinks(Cave).Count, "another map holds none");
        Assert.AreEqual(0, owner.QueryObjectLinks(Sogg).Count, "an object with no link has none");
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryObjectLinks(""));

        // The same holder's links written after the hero moved stand on the map of the call; the first holder's stay where they were written.
        Moves(owner, AtCave);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Focus(Owl, SoggHandle)).Disposition);
        Assert.AreEqual(Cave, owner.QueryObjectLinks(Owl).Single().Map);
        Assert.AreEqual(Exterior, owner.QueryObjectLinks(Wolf)[0].Map);
        CollectionAssert.AreEqual(new[] { Owl }, owner.QueryObjectLinks(Cave).Select(link => link.Entity).ToArray());

        // A change that leaves the links as they are keeps their map (a danger bit alone); clearing every link frees the holder's entry.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, new() { Entity = Wolf, State = DangerState.None }).Disposition);
        Assert.AreEqual(Exterior, owner.QueryObjectLinks(Wolf).First().Map, "the links the call did not touch keep the map they were written on");
    }

    [TestMethod]
    public void TheLinksRoundTripAsRowsOfTheObjectsSectionAndAHolderWithNoBitHasNoDangerRecord()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Steamwoven);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Sogg, WolfHandle).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"schemaVersion\":91,");
        StringAssert.Contains(text, ",\"danger\":{\"records\":[{\"entity\":\"oid:wolf\",\"flags\":[\"Fighting\",\"CheckWield\",\"CheckGrenade\"]}]},");
        StringAssert.Contains(text, ",\"links\":[{\"entity\":\"oid:sogg\",\"property\":\"link.hitby\",\"map\":79,\"targets\":[\"oid:wolf\"]}," +
            "{\"entity\":\"oid:wolf\",\"property\":\"link.enemies\",\"map\":79,\"targets\":[\"\",\"oid:virgil\"]}," +
            "{\"entity\":\"oid:wolf\",\"property\":\"link.focus\",\"map\":79,\"targets\":[\"oid:virgil\"]}," +
            "{\"entity\":\"oid:wolf\",\"property\":\"link.hitby\",\"map\":79,\"targets\":[\"oid:virgil\"]}],\"created\":[],\"lastCreatedNumber\":0}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        var before = owner.QueryDanger(Wolf); var after = restored.QueryDanger(Wolf);
        Assert.AreEqual((before.Flags, before.Focus, before.FleeingFrom, before.HitBy), (after.Flags, after.Focus, after.FleeingFrom, after.HitBy), "the record is composed of its bits and its links");
        CollectionAssert.AreEqual(before.Enemies.ToArray(), after.Enemies.ToArray());
        Assert.AreEqual(WolfHandle, restored.QueryDanger(Sogg).HitBy);
        Assert.AreEqual(DangerFlags.None, restored.QueryDanger(Sogg).Flags);
        Assert.AreEqual(Exterior, restored.QueryObjectLinks(Sogg).Single().Map);
    }

    [TestMethod]
    public void ARestoreRefusesALinkRowThatDoesNotFitTheVocabulary()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Steamwoven);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Refuses(string damaged, GameSessionFailure failure, GameSessionLimits? limits = null) =>
            Assert.AreEqual(failure, GameSessionOwner.Restore(Decode(damaged), RestoreOptions(limits)).Failure, damaged);

        Refuses(text.Replace("link.focus", "link.fear"), GameSessionFailure.InvalidSnapshot);
        Refuses(text.Replace("\"property\":\"link.focus\",\"map\":79,\"targets\":[\"oid:virgil\"]", "\"property\":\"link.focus\",\"map\":79,\"targets\":[\"oid:virgil\",\"oid:sogg\"]"),
            GameSessionFailure.InvalidSnapshot);
        Refuses(text.Replace("\"property\":\"link.focus\",\"map\":79,\"targets\":[\"oid:virgil\"]", "\"property\":\"link.focus\",\"map\":79,\"targets\":[]"),
            GameSessionFailure.InvalidSnapshot);
        Refuses(text.Replace("\"targets\":[\"\",\"oid:virgil\"]", "\"targets\":[\"oid:virgil\",\"oid:virgil\"]"), GameSessionFailure.InvalidSnapshot);
        Refuses(text.Replace("\"property\":\"link.focus\",\"map\":79", "\"property\":\"link.focus\",\"map\":80"), GameSessionFailure.InvalidSnapshot);
        Refuses(text, GameSessionFailure.BudgetExceeded, new GameSessionLimits(maxDangerEnemies: 1));
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange,
            GameSessionSnapshotCodec.TryDecode(text.Replace("\"property\":\"link.enemies\",\"map\":79", "\"property\":\"link.enemies\",\"map\":0")).Failure);
        // The decode alone refuses rows out of order, a row that names a map outside 1.., and a field that is not a string.
        var first = "{\"entity\":\"oid:wolf\",\"property\":\"link.enemies\",\"map\":79,\"targets\":[\"\",\"oid:virgil\"]}";
        var second = "{\"entity\":\"oid:wolf\",\"property\":\"link.focus\",\"map\":79,\"targets\":[\"oid:virgil\"]}";
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical,
            GameSessionSnapshotCodec.TryDecode(text.Replace(first + "," + second, second + "," + first)).Failure);
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(text.Replace(first + "," + second, first + "," + first)).Failure);
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField,
            GameSessionSnapshotCodec.TryDecode(text.Replace("\"property\":\"link.focus\",\"map\":79,\"targets\"", "\"property\":\"link.focus\",\"map\":79,\"extra\":1,\"targets\"")).Failure);
    }

    [TestMethod]
    public void ASchema64SaveMovesItsLinksOutOfTheDangerRecords()
    {
        // A schema 64 text written by hand: the danger records carry the four handles; the sogg record holds only a link. Both moved holders end on the
        // map the save was on, and the sogg record (no danger bit) is dropped from the danger section.
        using var owner = WolfHoldingVirgil(RuleMode.Steamwoven);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Sogg, WolfHandle).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.WithoutFrameClocks(SnapshotUpgradeTests.WithoutTownMaps(SnapshotUpgradeTests.Schema78(text).Replace(NoGoals + NoSpells82 + NoNewspaper, ""))).Replace("\"schemaVersion\":78,\"world\"", "\"schemaVersion\":64,\"world\"").Replace(NoScripts78, "").Replace(NoResidency, "")
            .Replace(",\"danger\":{\"records\":[{\"entity\":\"oid:wolf\",\"flags\":[\"Fighting\",\"CheckWield\",\"CheckGrenade\"]}]}",
                ",\"danger\":{\"records\":[{\"entity\":\"oid:sogg\",\"flags\":[],\"focus\":null,\"fleeingFrom\":null,\"hitBy\":\"oid:wolf\",\"enemies\":[]}," +
                "{\"entity\":\"oid:wolf\",\"flags\":[\"Fighting\",\"CheckWield\",\"CheckGrenade\"],\"focus\":\"oid:virgil\",\"fleeingFrom\":null," +
                "\"hitBy\":\"oid:virgil\",\"enemies\":[\"\",\"oid:virgil\"]}]}");
        var start = older.IndexOf(",\"links\":[{", StringComparison.Ordinal);
        Assert.IsTrue(start > 0);
        older = older.Remove(start, older.IndexOf("}]}", start, StringComparison.Ordinal) + 2 - start);
        StringAssert.Contains(older, "\"hitBy\":\"oid:wolf\"");
        Assert.IsFalse(older.Contains("\"links\"", StringComparison.Ordinal), "the schema 64 layout has no links section");

        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(upgraded), "the upgraded save is the schema 65 text it came from");
        using var restored = Restore(upgraded);
        CollectionAssert.AreEqual(owner.QueryDanger(Wolf).Enemies.ToArray(), restored.QueryDanger(Wolf).Enemies.ToArray());
        Assert.AreEqual(owner.QueryDanger(Wolf).Focus, restored.QueryDanger(Wolf).Focus);
        Assert.AreEqual(WolfHandle, restored.QueryDanger(Sogg).HitBy);

        // A damaged save is refused with its path, not repaired.
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.danger.records[0].hitBy"),
            Failure(older.Replace("\"hitBy\":\"oid:wolf\"", "\"hitBy\":7")));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.danger.records[0].enemies"),
            Failure(older.Replace(",\"enemies\":[]},{\"entity\":\"oid:wolf\"", "},{\"entity\":\"oid:wolf\"")));
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.danger.records[1].enemies"),
            Failure(older.Replace("\"enemies\":[\"\",\"oid:virgil\"]", "\"enemies\":\"\"")));
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.danger.records[1].entity"),
            Failure(older.Replace("{\"entity\":\"oid:wolf\",\"flags\":[\"Fighting\"", "{\"entity\":\"oid:sogg\",\"flags\":[\"Fighting\"")));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.objects.links"),
            Failure(older.Replace("\"state\":[]}", "\"state\":[],\"links\":[]}")));
    }

    private static (SnapshotDecodeFailure, string?) Failure(string text)
    {
        var result = GameSessionSnapshotCodec.TryDecode(text);
        Assert.IsNull(result.Snapshot, text);
        return (result.Failure, result.Path);
    }

    [TestMethod]
    public void ALinkToAnObjectThatLeftIsErasedWhenTheMapOpensUnderTheOriginalSettingAndKeptUnderSteamwoven()
    {
        foreach (var (rules, erased) in new[] { (RuleMode.Original, true), (RuleMode.Steamwoven, false) })
        {
            using var owner = WolfHoldingVirgil(rules);
            VirgilStandsOn(owner, Cave);
            // The wolf stays on the exterior, so the hero's walk to the cave opens the cave and leaves the wolf's links alone.
            Moves(owner, AtCave);
            Assert.AreEqual(3, owner.QueryObjectLinks(Wolf).Count, rules.ToString());
            // Coming back opens the exterior: Virgil stands on the cave now.
            Moves(owner, AtExterior);
            var wolf = owner.QueryDanger(Wolf);
            if (erased)
            {
                Assert.IsNull(wolf.Focus);
                Assert.IsNull(wolf.HitBy);
                CollectionAssert.AreEqual(new[] { Hero }, wolf.Enemies.ToArray(), "the hero is always there; the order of the rest stays");
                Assert.AreEqual(1, owner.QueryObjectLinks(Wolf).Count);
            }
            else
            {
                Assert.AreEqual(VirgilHandle, wolf.Focus);
                Assert.AreEqual(VirgilHandle, wolf.HitBy);
                CollectionAssert.AreEqual(new[] { Hero, VirgilHandle }, wolf.Enemies.ToArray());
                Assert.AreEqual(3, owner.QueryObjectLinks(Wolf).Count);
            }
            Assert.AreEqual(DangerFlags.Fighting | DangerFlags.CheckWield | DangerFlags.CheckGrenade, wolf.Flags, "the danger bits are not links");
        }
    }

    [TestMethod]
    public void AGroupThatTravelsWithTheHeroAndAnObjectThatStandsHereKeepTheirLinks()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Original);
        // Virgil follows the hero: away on the cave, but in the party, so he arrives with the hero (the travelling group rebinds).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Virgil, PartyStateTests.Facts(maxFollowers: 5), true).Disposition);
        VirgilStandsOn(owner, Cave);
        Moves(owner, AtCave);
        Moves(owner, AtExterior);
        Assert.AreEqual(3, owner.QueryObjectLinks(Wolf).Count, "a follower in the party arrives with the hero");

        // An object recorded as standing on the map that opens is there; one with no away record stands at home.
        using var here = WolfHoldingVirgil(RuleMode.Original);
        VirgilStandsOn(here, Exterior);
        Moves(here, AtCave);
        Moves(here, AtExterior);
        Assert.AreEqual(3, here.QueryObjectLinks(Wolf).Count);
        using var home = WolfHoldingVirgil(RuleMode.Original);
        Moves(home, AtCave);
        Moves(home, AtExterior);
        Assert.AreEqual(3, home.QueryObjectLinks(Wolf).Count);
    }

    [TestMethod]
    public void OnlyTheLinksWrittenOnTheOpeningMapAreJudged()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Original);
        VirgilStandsOn(owner, Cave);
        Moves(owner, AtCave);
        // The owl's link to Virgil was written on the cave: Virgil stands there, so it stays when the exterior opens, and is judged (kept) when the cave opens.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Owl, VirgilHandle).Disposition);
        Moves(owner, AtExterior);
        Assert.AreEqual(VirgilHandle, owner.QueryDanger(Owl).HitBy, "a link of the cave is not judged when the exterior opens");
        Assert.IsNull(owner.QueryDanger(Wolf).HitBy, "the wolf's link to the object that left is erased");
        Moves(owner, AtCave);
        Assert.AreEqual(VirgilHandle, owner.QueryDanger(Owl).HitBy, "Virgil stands on the cave");
    }

    [TestMethod]
    public void ALoadOpensTheSavedMapUnderTheSettingItIsRestoredWith()
    {
        using var steamwoven = WolfHoldingVirgil(RuleMode.Steamwoven);
        VirgilStandsOn(steamwoven, Cave);
        var snapshot = Decode(GameSessionSnapshotCodec.Encode(steamwoven.Capture()));

        using var original = Restore(snapshot, RestoreOptions() with { Rules = RuleMode.Original });
        Assert.IsNull(original.QueryDanger(Wolf).Focus);
        CollectionAssert.AreEqual(new[] { Hero }, original.QueryDanger(Wolf).Enemies.ToArray());
        using var kept = Restore(snapshot, RestoreOptions() with { Rules = RuleMode.Steamwoven });
        Assert.AreEqual(VirgilHandle, kept.QueryDanger(Wolf).Focus);
        Assert.AreEqual(3, kept.QueryObjectLinks(Wolf).Count);

        // The mode is the setting of the moment: a save restored under Steamwoven and saved again keeps the links for the other setting.
        using var again = Restore(Decode(GameSessionSnapshotCodec.Encode(kept.Capture())), RestoreOptions() with { Rules = RuleMode.Steamwoven });
        Assert.AreEqual(3, again.QueryObjectLinks(Wolf).Count);
    }

    [TestMethod]
    public void TheOriginalSettingSwitchIsOnlyInTheOriginalProfile()
    {
        Assert.IsTrue(GameRules.Original.Lifecycle.OpeningMapNullsLinksToLeftObjects);
        Assert.IsFalse(GameRules.Steamwoven.Lifecycle.OpeningMapNullsLinksToLeftObjects);
    }

    [TestMethod]
    public void APlacementOnAnotherMapOpensItAndAHolderLeftWithNoLinkAndNoBitIsGone()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Original);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Sogg, VirgilHandle).Disposition);
        VirgilStandsOn(owner, Cave);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, AtCave).Disposition);
        Assert.AreEqual(3, owner.QueryObjectLinks(Wolf).Count, "the cave opened, the wolf's links are the exterior's");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, AtExterior).Disposition);
        Assert.IsNull(owner.QueryDanger(Wolf).Focus);
        // Sogg held one link and no danger bit: nothing is left of its record, its links or its map.
        Assert.AreEqual(0, owner.QueryObjectLinks(Sogg).Count);
        Assert.IsNull(owner.QueryDanger(Sogg).HitBy);
        Assert.IsFalse(owner.QueryDangerRecords().Records.Any(record => record.Entity == Sogg));
        Assert.AreEqual(1, owner.QueryObjectLinks(Exterior).Count(link => link.Entity == Wolf), "only the hero stays on the wolf's enemy list");
        Assert.IsFalse(GameSessionSnapshotCodec.Encode(owner.Capture()).Contains("oid:sogg", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheRecordBudgetCountsHoldersOfBitsAndHoldersOfLinksTogether()
    {
        using var owner = WolfHoldingVirgil(RuleMode.Steamwoven);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Sogg, WolfHandle).Disposition);
        var snapshot = Decode(GameSessionSnapshotCodec.Encode(owner.Capture()));
        // The wolf holds bits and links, the sogg links alone: two records, though the danger section lists one.
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameSessionOwner.Restore(snapshot, RestoreOptions(new GameSessionLimits(maxDangerRecords: 1))).Failure);
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(snapshot, RestoreOptions(new GameSessionLimits(maxDangerRecords: 2))).Failure);
    }
}
