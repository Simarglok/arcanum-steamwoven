using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-1740 (FR-ITEM-019, ledger BT6, SR12, PS14; schema 101): the saved gambling roll and the bet over the synthetic seed of InventoryStateTests (virgil
// alive with a ring, a sword, a worn vest and a ring copy; the merchant with 5 coins) and a half-elf hero with 385 gold. Entities and numbers are invented.
// A hero of 5 Gambling points (level 20, intelligence 20) against a merchant of level 0 has the capped effectiveness 95 and wins on a roll of 95 or less (a
// seed whose first roll is at most 90 is chosen); a hero of 0 points against a merchant of level 20 has effectiveness 0 and loses on every roll (a roll is at least 1).
[TestClass]
public sealed class GamblingStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionInsertCheck NoRoom = _ => SessionInsertVerdict.NoRoom;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    // ARC-UI-2234: the seed of InventoryStateTests with a shown chest that lists a gem and a stack of 12 pellets (ammunition type 1).
    private static SessionMapInventorySeed ChestSeed()
    {
        var seed = Seed();
        var gem = Facts(SessionItemType.Generic, 5700);
        var pellets = Facts(SessionItemType.Ammo, 7060, quantity: 12) with { AmmoType = 1 };
        return seed with
        {
            Holders = [.. seed.Holders.Select(h => h.Entity == "chest" ? h with { Items = ["gem", "pellets"] } : h)],
            Items = [.. seed.Items, new SessionSeedItem("gem", gem, null), new SessionSeedItem("pellets", pellets, null)],
        };
    }

    private static GameSessionOwner Gambler(int points, ulong seed = 0, SessionMapInventorySeed? inventory = null)
    {
        var stats = points == 0 ? Defaults() : [20, 20, 20, 20, 20, 20, 20, 20, 0, 0];
        int[] basic = new int[12];
        basic[(int)Skill.Gambling] = points;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var created = GameSessionOwner.Create(Descriptor() with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        var owner = created.Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, inventory ?? Seed()).Failure);
        return owner;
    }

    // A winning hero on a seed whose first roll is at most 90 (so that roll + 0 is within the effectiveness of 95), the roll thrown.
    private static GameSessionOwner Winner()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var owner = Gambler(5, seed);
            owner.EnsureGamblingRoll(owner.Version);
            if (owner.QueryGamblingRoll() is <= 90) return owner;
            owner.Dispose();
        }
        throw new AssertFailedException("no seed throws a roll of 90 or less");
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static SessionGambleBet Bet(string item = "sword", int level = 0, int price = 120, string npc = "virgil") =>
        new(npc, level, item, 1, price, Bullets, GoldPile);

    private static SessionGambleResult Wager(GameSessionOwner owner, SessionGambleBet bet, SessionInsertCheck? hero = null) =>
        owner.Gamble(owner.Version, bet, hero ?? Accept, Accept);

    [TestMethod]
    public void A_new_session_has_no_roll_and_the_first_one_is_thrown_once_from_the_session_generator()
    {
        using var owner = Gambler(0);
        Assert.IsNull(owner.QueryGamblingRoll());
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var thrown = owner.EnsureGamblingRoll(owner.Version);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        Assert.IsTrue(owner.QueryGamblingRoll() is >= 1 and <= 100);
        Assert.AreEqual((revision + 1, draws + 1), (owner.Version.Revision, owner.QueryRandom().Draws));
        var roll = owner.QueryGamblingRoll();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EnsureGamblingRoll(owner.Version).Disposition, "a roll already thrown stays");
        Assert.AreEqual((roll, revision + 1, draws + 1), (owner.QueryGamblingRoll(), owner.Version.Revision, owner.QueryRandom().Draws));
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.EnsureGamblingRoll(new(owner.Identity, 99)).Failure);
        // The same seed throws the same roll.
        using var twin = Gambler(0);
        twin.EnsureGamblingRoll(twin.Version);
        Assert.AreEqual(roll, twin.QueryGamblingRoll());
    }

    [TestMethod]
    public void A_win_moves_the_item_for_nothing_and_the_check_draw_becomes_the_next_roll()
    {
        using var owner = Winner();
        var saved = owner.QueryGamblingRoll();
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var result = Wager(owner, Bet());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true, 0), (result.Disposition, result.Failure, result.Won, result.Paid));
        Assert.AreEqual(saved, result.Check!.Check.Roll, "the check uses the roll that was saved");
        Assert.AreEqual(95, result.Check.Check.Effectiveness, "the capped effectiveness of a level 20 gambler against level 0");
        Assert.AreEqual(result.Check.NextGamblingRoll, owner.QueryGamblingRoll(), "the draw of the check is the next roll");
        Assert.AreEqual((revision + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws), "the check's two draws, the move: one revision");
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "sword"));
        CollectionAssert.AreEqual(new[] { "ring", "vest", "ring-copy" }, List(owner, "virgil"));
        Assert.AreEqual((385, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "a win costs the hero nothing and pays the merchant nothing");
    }

    [TestMethod]
    public void A_loss_charges_the_price_and_moves_no_item()
    {
        using var owner = Gambler(0);
        owner.EnsureGamblingRoll(owner.Version);
        var (revision, draws, list) = (owner.Version.Revision, owner.QueryRandom().Draws, List(owner, "virgil"));
        var result = Wager(owner, Bet(level: 20), NoRoom);
        Assert.AreEqual((GameSessionDisposition.Changed, false, 120), (result.Disposition, result.Won, result.Paid));
        Assert.AreEqual(0, result.Check!.Check.Effectiveness);
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "sword"), "ARC-UI-2215: the move block is skipped, so no room test either");
        CollectionAssert.AreEqual(list, List(owner, "virgil").Take(list.Length).ToArray(), "the sword and the rest stay in the merchant's list (a coin pile joins its end)");
        Assert.AreEqual((265, 120), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
        Assert.AreEqual((revision + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws), "the check's two draws and the payment: one revision");
    }

    // ARC-UI-2234 (FR-ITEM-019): a lost bet on an ammunition stack stake pays price x count, the stack stays with the merchant, and a count above its units is
    // refused with nothing rerolled (the roll, the generator and the coins as they were).
    [TestMethod]
    public void A_loss_on_an_ammo_stack_pays_price_times_count_and_leaves_the_stack_with_the_merchant()
    {
        using var owner = Gambler(0);
        var stack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        owner.EnsureGamblingRoll(owner.Version);
        var before = (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold(), owner.QueryNpcGold("virgil"));
        var tooMany = Wager(owner, Bet(stack, level: 20, price: 3) with { Count = 31 });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.AmmoShort), (tooMany.Disposition, tooMany.Failure));
        Assert.AreEqual(before, (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold(), owner.QueryNpcGold("virgil")), "no reroll, no coins");

        var revision = owner.Version.Revision;
        var result = Wager(owner, Bet(stack, level: 20, price: 3) with { Count = 20 }, NoRoom);
        Assert.AreEqual((GameSessionDisposition.Changed, false, 60), (result.Disposition, result.Won, result.Paid));
        Assert.AreEqual((325, 60), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "price x count, 3 x 20");
        Assert.AreEqual(30, owner.QueryAmmo(stack)!.Value.Units, "the stack keeps all its units");
        Assert.IsTrue(List(owner, "virgil").Contains(stack));
        Assert.AreEqual(0, owner.QueryHeroAmmo().Count, "nothing reached the hero");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the check's draws and the payment: one revision");
        // Exactly the units staked is allowed too.
        var whole = Wager(owner, Bet(stack, level: 20, price: 3) with { Count = 30 });
        Assert.AreEqual((GameSessionDisposition.Changed, 90, 150), (whole.Disposition, whole.Paid, owner.QueryNpcGold("virgil")));
        Assert.AreEqual(235, owner.QueryGold());
        Assert.AreEqual(30, owner.QueryAmmo(stack)!.Value.Units);
    }

    // ARC-UI-2234: the same loss with the shown container as the stake's holder: the item or the stack stays in the container, a stake the container does not list is refused.
    [TestMethod]
    public void A_loss_on_a_container_stake_pays_and_leaves_the_item_in_the_container()
    {
        using var owner = Gambler(0, inventory: ChestSeed());
        owner.EnsureGamblingRoll(owner.Version);
        string[] Chest() => owner.QueryHolderItems("chest")!.Select(item => item.Entity).ToArray();
        var listed = Chest();
        CollectionAssert.AreEqual(new[] { "gem", "pellets" }, listed);
        var gem = Wager(owner, Bet("gem", level: 20, price: 50) with { Container = "chest" }, NoRoom);
        Assert.AreEqual((GameSessionDisposition.Changed, false, 50), (gem.Disposition, gem.Won, gem.Paid));
        Assert.AreEqual((335, 50), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
        CollectionAssert.AreEqual(listed, Chest(), "the gem stays in the chest");
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "gem"));
        // A stack of the container: price x count, all its units stay.
        var pellets = Wager(owner, Bet("pellets", level: 20, price: 4) with { Container = "chest", Count = 5 });
        Assert.AreEqual((GameSessionDisposition.Changed, false, 20), (pellets.Disposition, pellets.Won, pellets.Paid));
        Assert.AreEqual((315, 70), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
        CollectionAssert.AreEqual(listed, Chest());
        Assert.AreEqual(12, owner.QueryAmmo("pellets")!.Value.Units);
        // Refused with nothing rerolled: units above the stack, an item the chest does not list (the merchant's ring), an unknown container.
        var guard = (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold());
        foreach (var (bet, failure) in new[]
        {
            (Bet("pellets", level: 20, price: 4) with { Container = "chest", Count = 13 }, GameSessionFailure.AmmoShort),
            (Bet("ring", level: 20) with { Container = "chest" }, GameSessionFailure.ItemNotAvailable),
            (Bet("gem", level: 20) with { Container = "nowhere" }, GameSessionFailure.UnknownHolder),
        })
        {
            var refused = Wager(owner, bet);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (refused.Disposition, refused.Failure));
            Assert.AreEqual(guard, (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold()), "no reroll, no coins");
        }
    }

    [TestMethod]
    public void A_loss_at_price_0_changes_the_roll_in_one_revision_and_moves_nothing()
    {
        using var owner = Gambler(0);
        owner.EnsureGamblingRoll(owner.Version);
        var (revision, draws, list) = (owner.Version.Revision, owner.QueryRandom().Draws, List(owner, "virgil"));
        var result = Wager(owner, Bet(level: 20, price: 0));
        Assert.AreEqual((GameSessionDisposition.Changed, false, 0), (result.Disposition, result.Won, result.Paid));
        Assert.AreEqual((revision + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws));
        Assert.AreEqual(result.Check!.NextGamblingRoll, owner.QueryGamblingRoll());
        CollectionAssert.AreEqual(list, List(owner, "virgil"));
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "sword"));
        Assert.AreEqual((385, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
    }

    [TestMethod]
    public void A_loss_whose_new_coin_pile_the_merchant_refuses_puts_the_roll_back_and_moves_nothing()
    {
        using var owner = Gambler(0);
        owner.EnsureGamblingRoll(owner.Version);
        var before = (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold());
        var result = owner.Gamble(owner.Version, Bet(level: 20), Accept, NoRoom);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused), (result.Disposition, result.Failure));
        Assert.AreEqual(before, (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold()), "no reroll, no coins");
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "sword"));
    }

    [TestMethod]
    public void A_loss_on_an_item_the_merchant_does_not_list_is_refused_before_the_roll()
    {
        using var owner = Gambler(0);
        owner.EnsureGamblingRoll(owner.Version);
        var before = (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold());
        var result = Wager(owner, Bet("no-such-item", level: 20));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable), (result.Disposition, result.Failure));
        Assert.AreEqual(before, (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold()), "no reroll");
    }

    [TestMethod]
    public void A_bet_before_the_first_roll_throws_it_in_the_same_step_and_draws_the_check_after()
    {
        using var owner = Gambler(0);
        var draws = owner.QueryRandom().Draws;
        var result = Wager(owner, Bet(level: 20));
        Assert.AreEqual((GameSessionDisposition.Changed, false), (result.Disposition, result.Won));
        Assert.AreEqual(draws + 3, owner.QueryRandom().Draws, "the missing roll, then the check's two draws");
        Assert.AreEqual(result.Check!.NextGamblingRoll, owner.QueryGamblingRoll());
    }

    [TestMethod]
    public void A_refused_move_leaves_the_roll_the_generator_and_the_hero_as_they_were()
    {
        using var owner = Winner();
        var (version, roll, draws, gold) = (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold());
        var noRoom = Wager(owner, Bet(), NoRoom);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused), (noRoom.Disposition, noRoom.Failure));
        Assert.IsNotNull(noRoom.Check, "the check ran on a trial copy");
        Assert.AreEqual((version, roll, draws, gold), (owner.Version, owner.QueryGamblingRoll(), owner.QueryRandom().Draws, owner.QueryGold()), "no reroll by refusing");
        // The same roll still decides the retry.
        var retry = Wager(owner, Bet());
        Assert.AreEqual(roll, retry.Check!.Check.Roll);
        // A lost bet the hero cannot pay is refused with nothing changed.
        using var poor = Gambler(0);
        poor.EnsureGamblingRoll(poor.Version);
        var before = (poor.Version, poor.QueryGamblingRoll(), poor.QueryRandom().Draws);
        var short1 = Wager(poor, Bet(level: 20, price: 386));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.GoldShort), (short1.Disposition, short1.Failure));
        Assert.AreEqual(before, (poor.Version, poor.QueryGamblingRoll(), poor.QueryRandom().Draws));
        Assert.AreEqual(385, poor.QueryGold());
    }

    [TestMethod]
    public void Refusals_come_before_anything_is_rolled()
    {
        using var owner = Gambler(5);
        void Refused(SessionGambleResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 0L, (int?)null), (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryGamblingRoll()));
        }
        Refused(Wager(owner, Bet(level: 21)), GameSessionFailure.InvalidItem);
        Refused(Wager(owner, Bet(level: -1)), GameSessionFailure.InvalidItem);
        Refused(Wager(owner, Bet(price: -1)), GameSessionFailure.InvalidItem);
        Refused(owner.Gamble(new(owner.Identity, 9), Bet(), Accept, Accept), GameSessionFailure.StaleVersion);
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        var none = heroless.Gamble(heroless.Version, Bet(), Accept, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.GamblingUnavailable), (none.Disposition, none.Failure));
    }

    [TestMethod]
    public void An_armed_gambling_fate_point_wins_and_is_used()
    {
        using var owner = Gambler(0);
        owner.EnsureGamblingRoll(owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GrantHeroFatePoint(owner.Version).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArmHeroFate(owner.Version, FateKind.Gambling).Disposition);
        var result = Wager(owner, Bet(level: 20));
        Assert.AreEqual((GameSessionDisposition.Changed, true, true, 0), (result.Disposition, result.Won, result.FateUsed, result.Paid), "a certain loss won by fate");
        Assert.IsTrue(result.Check!.Critical);
        Assert.IsFalse(owner.HeroFateArmed(FateKind.Gambling), "the point is used");
    }

    [TestMethod]
    public void The_roll_is_saved_and_a_load_restores_it_so_that_reloading_does_not_change_the_next_bet()
    {
        using var owner = Winner();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"gamblingRoll\":" + owner.QueryGamblingRoll() + ",\"lighting\":");
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        Assert.AreEqual(owner.QueryGamblingRoll(), decoded.Snapshot!.GamblingRoll);
        using var restored = Restore(decoded.Snapshot);
        Assert.AreEqual(owner.QueryGamblingRoll(), restored.QueryGamblingRoll());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "a load and a save again make the same text");
        // The bet after the load decides on the same roll as it would have before it.
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        var first = Wager(owner, Bet());
        var second = Wager(restored, Bet());
        Assert.AreEqual((first.Check!.Check.Roll, first.Won), (second.Check!.Check.Roll, second.Won));
        // A save taken before the first roll saves null.
        using var fresh = Gambler(0);
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(fresh.Capture()), NoGamblingRoll + ",\"lighting\":");
    }

    [TestMethod]
    public void A_schema_100_save_upgrades_with_no_roll_and_a_damaged_one_is_refused()
    {
        using var owner = Gambler(0);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema100(current);
        StringAssert.Contains(older, "\"schemaVersion\":100,");
        Assert.IsFalse(older.Contains("gamblingRoll", StringComparison.Ordinal));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot!.SchemaVersion);
        Assert.IsNull(upgraded.Snapshot.GamblingRoll, "an older save has thrown none: the next barter window throws the first");
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"lighting\":", NoGamblingRoll + ",\"lighting\":"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.gamblingRoll"), (damaged.Failure, damaged.Path));
    }

    [TestMethod]
    public void A_damaged_roll_is_refused_with_its_path_and_a_restore_refuses_one_the_codec_would_not_write()
    {
        using var owner = Gambler(0);
        owner.EnsureGamblingRoll(owner.Version);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var section = ",\"gamblingRoll\":" + owner.QueryGamblingRoll() + ",\"lighting\":";
        (string Replacement, SnapshotDecodeFailure Failure)[] cases =
        [
            (",\"gamblingRoll\":0,\"lighting\":", SnapshotDecodeFailure.OutOfRange),
            (",\"gamblingRoll\":101,\"lighting\":", SnapshotDecodeFailure.OutOfRange),
            (",\"gamblingRoll\":-3,\"lighting\":", SnapshotDecodeFailure.OutOfRange),
            (",\"gamblingRoll\":\"7\",\"lighting\":", SnapshotDecodeFailure.WrongType),
            (",\"gamblingRoll\":4.5,\"lighting\":", SnapshotDecodeFailure.OutOfRange),
        ];
        foreach (var (replacement, failure) in cases)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(section, replacement));
            Assert.AreEqual((failure, "$.gamblingRoll"), (decoded.Failure, decoded.Path), replacement);
        }
        var saved = owner.Capture();
        foreach (var bad in new[] { 0, 101 })
        {
            var snapshot = new GameSessionSnapshot(saved.SchemaVersion, saved.Identity, saved.Revision, saved.Clock, saved.Flags, saved.Actor, saved.Maps.ToArray(), saved.Story, saved.Party, saved.Hero,
                saved.Inventory, saved.Equipment, saved.Durability, saved.Schematics, saved.Effects, saved.Experience, saved.Advancement, saved.Random, saved.Life, saved.Reactions, saved.Combat, saved.CombatMode,
                saved.Concealment, saved.Danger, saved.Events, saved.Lighting, saved.Sound, saved.Hotkeys, saved.Objects, saved.Scripts, saved.Residency, saved.Goals, saved.Spells, saved.Newspaper, saved.Flights,
                saved.Generators, saved.Encounters, saved.BarLights, bad);
            var result = GameSessionOwner.Restore(snapshot, RestoreOptions());
            result.Owner?.Dispose();
            Assert.AreEqual(GameSessionFailure.InvalidGamblingRoll, result.Failure, bad.ToString());
        }
    }
}
