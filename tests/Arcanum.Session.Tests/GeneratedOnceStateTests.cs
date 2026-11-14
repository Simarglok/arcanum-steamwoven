using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-100: the generated-once containers in the session (flag 0x200, ledger RK10–RK12): the first loot open's generation with the
// original generator seeded from the hero and the host's tile x and name bytes, the restock event's seeded generation, the cleared source
// and flag (no restock after it), and the saved list (schema 55). A synthetic seed: a crate with flag 0x200 (a pile of 30 coins, an axe
// and a persistent ring) and a plain chest; the half-elf hero. Entities and numbers are invented.
[TestClass]
public sealed class GeneratedOnceStateTests
{
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private const int CrateSource = 7, TileX = 1451;
    private static readonly byte[] Name = [(byte)'V', (byte)'i', (byte)'r', (byte)'g', 0xE9];
    // Rates of 50 so that the draws decide which lines create.
    private static readonly RestockSet CrateSet = new(1, 1000, [new(50, 1), new(50, 2), new(50, 3), new(50, 1), new(50, 2), new(50, 3), new(50, 1)]);

    private sealed class World : ISessionRestockWorld
    {
        public Dictionary<string, RestockHolder> Facts { get; } = new()
        {
            ["crate"] = new(RestockHolderKind.Container, CrateSource, false, false, RestockRules.GeneratedOnceFlag, false),
            ["chest"] = new(RestockHolderKind.Container, CrateSource, false, false, 0, false),
            ["smith"] = new(RestockHolderKind.Npc, CrateSource, false, false, 0, false),
        };
        public SessionGeneratedOnceFacts? Once { get; set; } = new(TileX, Name);
        public Dictionary<int, SessionRestockSet> Sets { get; } = new() { [CrateSource] = new(SessionRestockSetStatus.Read, CrateSet) };

        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => Facts.TryGetValue(entity, out var facts) ? facts : null;
        public string? Merchant { get; set; }
        public bool Ready { get; set; } = true;
        public string? RestockMerchant(SessionMapId map, string container) => Merchant;
        public bool RestockReady => Ready;
        public bool Persistent(SessionMapId map, string item) => item == "ring";
        public SessionRestockSet RestockSet(int source) => Sets[source];
        public SessionRestockItem? RestockItem(int basicPrototype) => new(new(SessionItemType.Generic, 9100 + basicPrototype, 2300 + basicPrototype, 32300 + basicPrototype));
        public SessionItemKind RestockPileKind => GoldPile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
        public SessionGeneratedOnceFacts? GeneratedOnceFacts(SessionMapId map, string container) => Once;
    }

    private static SessionMapInventorySeed Seed() => new(
        [
            new("crate", SessionHolderKind.Container, false, false, [], ["gold-a", "axe", "ring"]),
            new("chest", SessionHolderKind.Container, false, false, [], []),
            new("smith", SessionHolderKind.Npc, false, true, [], []),
        ],
        [
            new("gold-a", Facts(SessionItemType.Gold, 3200, quantity: 30), null),
            new("axe", Facts(SessionItemType.Weapon, 2002), null),
            new("ring", Facts(SessionItemType.Armor, 2805), null),
        ]);

    private const int HeroBackground = 41;

    private static GameSessionOwner Owner(bool hero = true, GameSessionLimits? limits = null)
    {
        var descriptor = hero ? Descriptor(limits) with { Hero = HeroStateTests.HalfElf() with { Background = HeroBackground } } : Descriptor(limits);
        var owner = GameSessionOwner.Create(descriptor).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    // The half-elf (race 3) male (1) hero's seed: race × background + gender + tile x and the name's byte pairs as signed chars.
    private static int HeroSeed() => unchecked(3 * HeroBackground + 1 + TileX + 'V' * 'i' + 'r' * 'g' + (sbyte)0xE9 * 0);

    private static SessionInventoryItem[] Items(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.ToArray();

    private static void AssertGenerated(GameSessionOwner owner, SessionRestockRegeneration done, int seed)
    {
        var loot = RestockRules.GenerateOnce(CrateSet, seed);
        Assert.AreEqual((SessionRestockOutcome.Regenerated, 2, loot.Coins, 0, 0), (done.Outcome, done.Removed, done.Coins, done.Refused, done.Skipped));
        var items = Items(owner, "crate");
        Assert.AreEqual("ring", items[0].Entity, "the persistent item stays (RK5)");
        Assert.AreEqual((done.Pile, SessionItemType.Gold), (items[1].Entity, items[1].Facts.Kind.Type));
        CollectionAssert.AreEqual(loot.BasicPrototypes.Select(number => 2300 + number).ToArray(), items.Skip(2).Select(item => item.Facts.Kind.Name).ToArray());
        CollectionAssert.AreEqual(done.Created.ToArray(), items.Skip(2).Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void The_first_open_generates_from_the_seeded_original_generator_and_clears_the_source()
    {
        using var owner = Owner();
        var world = new World();
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var opened = owner.GenerateContainerOnce(owner.Version, "crate", world);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (opened.Disposition, opened.Failure));
        AssertGenerated(owner, opened.Regeneration!, HeroSeed());
        Assert.AreEqual((revision + 1, draws), (owner.Version.Revision, owner.QueryRandom().Draws), "one revision; the session's generator draws nothing");
        Assert.IsTrue(owner.IsItemDestroyed("gold-a") && owner.IsItemDestroyed("axe"));
        Assert.IsTrue(owner.QueryContainerGenerated(Exterior, "crate"));
        // The window's schedule right after finds no source (RK12), and a second open changes nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ScheduleRestock(owner.Version, "crate", hasSource: true).Disposition);
        Assert.AreEqual(0, owner.QueryRestocks().Count);
        var again = owner.GenerateContainerOnce(owner.Version, "crate", world);
        Assert.AreEqual((GameSessionDisposition.NoChange, (SessionRestockRegeneration?)null), (again.Disposition, again.Regeneration));
        Assert.AreEqual(revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void The_same_hero_at_the_same_place_gets_the_same_goods_and_the_seed_follows_the_name_and_place()
    {
        using var first = Owner();
        using var second = Owner();
        var a = first.GenerateContainerOnce(first.Version, "crate", new World()).Regeneration!;
        var b = second.GenerateContainerOnce(second.Version, "crate", new World()).Regeneration!;
        Assert.AreEqual(a.Coins, b.Coins);
        CollectionAssert.AreEqual(Items(first, "crate").Select(item => item.Facts.Kind).ToArray(), Items(second, "crate").Select(item => item.Facts.Kind).ToArray());
        // Another name or place is another seed; an unset name adds nothing; without a hero the seed is the tile x alone.
        using var renamed = Owner();
        AssertGenerated(renamed, renamed.GenerateContainerOnce(renamed.Version, "crate", new World { Once = new(TileX + 1, []) }).Regeneration!,
            3 * HeroBackground + 1 + TileX + 1);
        using var alone = Owner(hero: false);
        AssertGenerated(alone, alone.GenerateContainerOnce(alone.Version, "crate", new World()).Regeneration!, TileX);
    }

    [TestMethod]
    public void A_restock_event_generates_it_once_and_it_never_restocks()
    {
        using var owner = Owner();
        var world = new World();
        world.Facts["crate"] = world.Facts["crate"] with { ContainerFlags = RestockRules.GeneratedOnceFlag | RestockRules.RegeneratesAloneFlag };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, "crate", hasSource: true).Disposition);
        var draws = owner.QueryRandom().Draws;
        var fired = Fire(owner, world).Restocks.Single();
        Assert.AreEqual(RestockStep.Regenerate, fired.Decision.Step);
        AssertGenerated(owner, fired.Regenerations.Single(), HeroSeed());
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "the seeded draws are not the session's");
        Assert.IsTrue(owner.QueryContainerGenerated(Exterior, "crate"));
        // The host's data still shows the source and flag; the session's cleared source wins: no event, and a first open changes nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ScheduleRestock(owner.Version, "crate", hasSource: true).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.GenerateContainerOnce(owner.Version, "crate", world).Disposition);
    }

    [TestMethod]
    public void Through_its_merchant_the_merchant_draws_from_the_session_and_the_container_seeded()
    {
        using var owner = Owner();
        var world = new World { Merchant = "smith" };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, "crate", hasSource: true).Disposition);
        var draws = owner.QueryRandom().Draws;
        var fired = Fire(owner, world).Restocks.Single();
        Assert.AreEqual(new RestockDecision(RestockStep.RegenerateMerchantAndContainer, true), fired.Decision);
        CollectionAssert.AreEqual(new[] { "smith", "crate" }, fired.Regenerations.Select(r => r.Holder).ToArray(), "the merchant first");
        Assert.AreEqual(draws + 1 + CrateSet.Lines.Count, owner.QueryRandom().Draws, "the merchant's coins and lines only");
        AssertGenerated(owner, fired.Regenerations[1], HeroSeed());
        Assert.IsTrue(owner.QueryContainerGenerated(Exterior, "crate"));
        Assert.IsFalse(owner.QueryContainerGenerated(Exterior, "smith"));
    }

    [TestMethod]
    public void A_pending_event_of_a_generated_container_does_nothing()
    {
        using var owner = Owner();
        var world = new World();
        world.Facts["crate"] = world.Facts["crate"] with { ContainerFlags = RestockRules.GeneratedOnceFlag | RestockRules.RegeneratesAloneFlag };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, "crate", hasSource: true).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GenerateContainerOnce(owner.Version, "crate", world).Disposition);
        var kept = Items(owner, "crate").Select(item => item.Entity).ToArray();
        var revision = owner.Version.Revision;
        var fired = Fire(owner, world).Restocks.Single();
        Assert.AreEqual((RestockStep.None, 0), (fired.Decision.Step, fired.Regenerations.Count), "no source any more (RK10)");
        CollectionAssert.AreEqual(kept, Items(owner, "crate").Select(item => item.Entity).ToArray());
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void Without_the_hosts_seed_facts_or_with_an_unsupported_set_the_container_keeps_its_list()
    {
        using var owner = Owner();
        var world = new World { Once = null };
        var revision = owner.Version.Revision;
        var opened = owner.GenerateContainerOnce(owner.Version, "crate", world);
        Assert.AreEqual((GameSessionDisposition.NoChange, SessionRestockOutcome.Unsupported), (opened.Disposition, opened.Regeneration!.Outcome));
        world.Facts["crate"] = world.Facts["crate"] with { ContainerFlags = RestockRules.GeneratedOnceFlag | RestockRules.RegeneratesAloneFlag };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, "crate", hasSource: true).Disposition);
        Assert.AreEqual(SessionRestockOutcome.Unsupported, Fire(owner, world).Restocks.Single().Regenerations.Single().Outcome);
        world.Once = new(TileX, Name);
        world.Sets[CrateSource] = new(SessionRestockSetStatus.Unsupported, null);
        Assert.AreEqual(SessionRestockOutcome.Unsupported, owner.GenerateContainerOnce(owner.Version, "crate", world).Regeneration!.Outcome);
        CollectionAssert.AreEqual(new[] { "gold-a", "axe", "ring" }, Items(owner, "crate").Select(item => item.Entity).ToArray());
        Assert.IsFalse(owner.QueryContainerGenerated(Exterior, "crate"));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "only the schedule's revision");
    }

    [TestMethod]
    public void An_unreachable_set_clears_and_counts_as_generated_and_a_container_without_a_source_is_cleared_every_time()
    {
        using var owner = Owner();
        var world = new World();
        world.Sets[CrateSource] = new(SessionRestockSetStatus.Unreachable, null);
        var opened = owner.GenerateContainerOnce(owner.Version, "crate", world).Regeneration!;
        Assert.AreEqual((SessionRestockOutcome.Cleared, 2, 0), (opened.Outcome, opened.Removed, opened.Created.Count));
        Assert.IsTrue(owner.QueryContainerGenerated(Exterior, "crate"), "the source and flag are cleared after the lines (RK7, RK10)");
        // Flag 0x200 with source 0 (none installed): cleared at each open without a seed, and the flag stays.
        using var bare = Owner();
        world.Facts["crate"] = world.Facts["crate"] with { InventorySource = 0 };
        world.Once = null;
        var first = bare.GenerateContainerOnce(bare.Version, "crate", world);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionRestockOutcome.Cleared, 2), (first.Disposition, first.Regeneration!.Outcome, first.Regeneration.Removed));
        Assert.IsFalse(bare.QueryContainerGenerated(Exterior, "crate"));
        CollectionAssert.AreEqual(new[] { "ring" }, Items(bare, "crate").Select(item => item.Entity).ToArray());
        Assert.AreEqual(SessionRestockOutcome.Cleared, bare.GenerateContainerOnce(bare.Version, "crate", world).Regeneration!.Outcome);
    }

    [TestMethod]
    public void Other_containers_change_nothing_and_bad_requests_are_refused_before_any_effect()
    {
        using var owner = Owner();
        var world = new World();
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (owner.GenerateContainerOnce(owner.Version, "chest", world).Disposition,
            owner.GenerateContainerOnce(owner.Version, "chest", world).Failure), "no flag 0x200");
        foreach (var holder in new[] { "pc", "-", "made:3", "bad id" })
            Assert.AreEqual(GameSessionFailure.InvalidItem, owner.GenerateContainerOnce(owner.Version, holder, world).Failure, holder);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.GenerateContainerOnce(new(owner.Identity, 99), "crate", world).Failure);
        foreach (var holder in new[] { "smith", "nobody" })
            Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.GenerateContainerOnce(owner.Version, holder, world).Failure, holder);
        world.Facts["crate"] = world.Facts["crate"] with { Kind = RestockHolderKind.Npc };
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.GenerateContainerOnce(owner.Version, "crate", world).Failure, "the host's kind");
        Assert.AreEqual(GameSessionFailure.InventoryUnavailable, owner.GenerateContainerOnce(owner.Version, "crate", new World { Ready = false }).Failure,
            "a host not ready could read the set as unreachable");
        using var unseeded = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.InventoryUnavailable, unseeded.GenerateContainerOnce(unseeded.Version, "crate", new World()).Failure);
        Assert.AreEqual(0L, owner.Version.Revision);
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.GenerateContainerOnce(owner.Version, "crate", null!));
    }

    [TestMethod]
    public void A_clear_the_budgets_cannot_hold_is_refused_before_any_effect()
    {
        using var owner = Owner(limits: new GameSessionLimits(maxDurabilityRecords: 1));
        var opened = owner.GenerateContainerOnce(owner.Version, "crate", new World());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (opened.Disposition, opened.Failure));
        Assert.AreEqual(0L, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "gold-a", "axe", "ring" }, Items(owner, "crate").Select(item => item.Entity).ToArray());
        Assert.IsFalse(owner.QueryContainerGenerated(Exterior, "crate"));
    }

    [TestMethod]
    public void The_generated_containers_are_saved()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GenerateContainerOnce(owner.Version, "crate", new World()).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "}],\"generated\":[{\"map\":79,\"entity\":\"crate\"}],\"npcWorn\":[],\"itemFields\":[],\"holderSlots\":[{\"holder\":\"crate\",\"slots\":[");
        // ARC-AI-1753: the items the generation made into the container keep the places the insert gave them.
        StringAssert.Contains(text, "]}]},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0},\"equipment\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.IsTrue(restored.QueryContainerGenerated(Exterior, "crate"));
        Assert.IsFalse(restored.QueryContainerGenerated(new(80), "crate"));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, restored.ScheduleRestock(restored.Version, "crate", hasSource: true).Disposition);
        // Out of order, a duplicate, map 0, the player and a made item are refused.
        var row = "{\"map\":79,\"entity\":\"crate\"}";
        foreach (var damaged in new[] { "{\"map\":79,\"entity\":\"crate\"},{\"map\":79,\"entity\":\"box\"}", row + "," + row, "{\"map\":80,\"entity\":\"a\"}," + row })
            Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(text.Replace(row, damaged)).Failure, damaged);
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(text.Replace(row, "{\"map\":0,\"entity\":\"crate\"}")).Failure);
        foreach (var entity in new[] { "pc", "made:7" })
        {
            var damaged = Decode(text.Replace(row, $"{{\"map\":79,\"entity\":\"{entity}\"}}"));
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(damaged, RestoreOptions()).Failure, entity);
        }
    }

    [TestMethod]
    public void The_generated_goods_and_the_kept_items_are_looted_from_the_container()
    {
        // ARC-WORLD-101: the window's loot after the first open takes the created items and the persistent ring; the destroyed seed
        // items do not move, nor does the gold pile through LootItem (a container's coins go through TakeGoldFromNpc).
        using var owner = Owner();
        var opened = owner.GenerateContainerOnce(owner.Version, "crate", new World()).Regeneration!;
        Assert.IsTrue(opened.Created.Count > 0);
        var created = opened.Created[0];
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "crate", created, _ => SessionInsertVerdict.Accepted).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "crate", "ring", _ => SessionInsertVerdict.Accepted).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "crate", "axe", _ => SessionInsertVerdict.Accepted).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "crate", created, _ => SessionInsertVerdict.Accepted).Failure, "taken once");
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.LootItem(owner.Version, "crate", opened.Pile!, _ => SessionInsertVerdict.Accepted).Failure);
        CollectionAssert.DoesNotContain(Items(owner, "crate").Select(item => item.Entity).ToArray(), created);
        CollectionAssert.Contains(owner.QueryMadeItems().Select(item => item.Entity).ToArray(), created);
        CollectionAssert.Contains(owner.QueryPcInventory().Select(item => item.Entity).ToArray(), "ring");
        // The plain chest of the same seed is lootable too, though empty.
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "chest", "ring", _ => SessionInsertVerdict.Accepted).Failure);
    }

    // ARC-WORLD-1301 (VPG6): the break of a container with flag 0x200 never generated regenerates it first (0x463730 → 0x463E20, RK13, OD9).
    private static readonly CellCoord CrateCell = new(120, 210);

    private static SessionObjectTarget CrateTarget(string entity = "crate") => new(Exterior, entity, new SessionObjectBaseline
    {
        Kind = DamageableObjectKind.Container, Points = 6, Material = ObjectMaterial.Cloth, Cell = CrateCell,
        Openable = new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0),
    });

    // Loose ground items and the items of junk piles (an item in a pile is not in Ground), with the cell each lies on.
    private static (SessionInventoryItem Item, CellCoord Cell)[] Lying(GameSessionOwner owner) => owner.QueryMapInventory(Exterior) is { } view
        ? [.. view.Ground.Select(ground => (ground.Item, ground.Cell)), .. view.JunkPiles.SelectMany(pile => pile.Items.Select(item => (item, pile.Cell)))] : [];

    private static string[] OnTheGround(GameSessionOwner owner) => Lying(owner).Select(lying => lying.Item.Entity).ToArray();

    [TestMethod]
    public void A_broken_container_with_flag_0x200_generates_first_and_spills_the_goods_with_the_kept_items()
    {
        using var owner = Owner();
        owner.GenerateOnceWorld = () => new World();
        var revision = owner.Version.Revision;
        var broke = owner.DamageObject(owner.Version, CrateTarget(), 100);
        Assert.AreEqual((GameSessionDisposition.Changed, ObjectBreak.ContainerSpilled), (broke.Disposition, broke.Hit!.Break));
        Assert.AreEqual(revision + 2, owner.Version.Revision, "the generation and the break");
        Assert.IsTrue(owner.QueryContainerGenerated(Exterior, "crate"), "recorded as generated");
        Assert.IsTrue(owner.IsItemDestroyed("gold-a") && owner.IsItemDestroyed("axe"), "the clear deletes all but the persistent ring (RK5)");
        var loot = RestockRules.GenerateOnce(CrateSet, HeroSeed());
        var goods = loot.BasicPrototypes.Select(number => 2300 + number).ToArray();
        Assert.IsTrue(goods.Length > 0, "the invented seed must draw lines");
        var ground = Lying(owner).Where(lying => lying.Item.Facts.Kind.Name is { } name && goods.Contains(name) || lying.Item.Entity == "ring").ToArray();
        Assert.AreEqual(goods.Length + 1, ground.Length, "the kept ring and every generated item lie on the ground");
        Assert.IsTrue(ground.All(lying => lying.Cell == CrateCell));
        // The drop walks the list last first (the goods in reverse, then the ring): from the second item on they gather into a pile.
        var piled = owner.QueryMapInventory(Exterior)!.JunkPiles.Single().Items.Select(item => item.Entity).ToArray();
        Assert.AreEqual("ring", piled[^1]);
        Assert.AreEqual(goods.Length + 1, owner.QueryItemDecays().Count(decay => decay.Entity == "ring" || SessionMadeItem.Number(decay.Entity) is not null), "each spilled item decays");
        Assert.AreEqual(0, Items(owner, "crate").Length, "a spilled container lists nothing");
        // A second break finds nothing to do.
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.DamageObject(owner.Version, CrateTarget(), 1).Failure);
    }

    [TestMethod]
    public void A_broken_container_already_generated_or_without_the_flag_spills_as_before()
    {
        using var generated = Owner();
        var world = new World();
        generated.GenerateOnceWorld = () => world;
        var opened = generated.GenerateContainerOnce(generated.Version, "crate", world).Regeneration!;
        var revision = generated.Version.Revision;
        Assert.AreEqual(ObjectBreak.ContainerSpilled, generated.DamageObject(generated.Version, CrateTarget(), 100).Hit!.Break);
        Assert.AreEqual(revision + 1, generated.Version.Revision, "the break alone: nothing is generated twice");
        // The whole list spills (ARC-WORLD-1306): the kept ring and the goods made at the open, but not the destroyed seed items.
        var lying = OnTheGround(generated);
        CollectionAssert.IsSubsetOf(opened.Created.ToArray(), lying);
        CollectionAssert.Contains(lying, "ring");
        CollectionAssert.DoesNotContain(lying, "axe");
        // A container without flag 0x200 is not generated: its stored items spill and none are destroyed.
        using var plain = Owner();
        plain.GenerateOnceWorld = () => new World();
        Assert.AreEqual(ObjectBreak.ContainerSpilled, plain.DamageObject(plain.Version, CrateTarget("chest"), 100).Hit!.Break);
        Assert.IsFalse(plain.QueryContainerGenerated(Exterior, "chest"));
        Assert.IsFalse(plain.IsItemDestroyed("axe"));
    }

    [TestMethod]
    public void Without_the_hosts_seed_facts_a_broken_container_spills_the_stored_items_and_stays_ungenerated()
    {
        foreach (var world in new World?[] { null, new World { Once = null }, new World { Ready = false }, new World { Sets = { [CrateSource] = new(SessionRestockSetStatus.Unsupported, null) } } })
        {
            using var owner = Owner();
            if (world is not null) owner.GenerateOnceWorld = () => world;
            Assert.AreEqual(ObjectBreak.ContainerSpilled, owner.DamageObject(owner.Version, CrateTarget(), 100).Hit!.Break);
            CollectionAssert.AreEquivalent(new[] { "gold-a", "axe", "ring" }, OnTheGround(owner));
            Assert.IsFalse(owner.QueryContainerGenerated(Exterior, "crate"));
        }
    }

    private static SessionEventPassResult Fire(GameSessionOwner owner, ISessionEventWorld world)
    {
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, RestockRules.MaxDelayMs, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        return owner.ProcessEvents(world);
    }
}
