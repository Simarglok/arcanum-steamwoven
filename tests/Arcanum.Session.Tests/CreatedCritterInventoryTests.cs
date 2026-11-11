using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-386 (E1-c4, ledger OM16, RK3, RK14): a critter the session created is a holder of its map's seed (the restock, loot, barter and pickpocket machinery finds a holder through the attached seed), so the
/// restock body the original's create finish runs (0x463C60 from 0x442260) gives it its goods through <see cref="GameSessionOwner.RegenerateInventory"/>, and the destroy of the critter destroys them with it.
/// Invented prototype numbers, maps and cells only.
/// </summary>
[TestClass]
public sealed class CreatedCritterInventoryTests
{
    private const int Prototype = 28359, Source = 9;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Sword = new(SessionItemType.Weapon, 4021, 2003, 32003);

    // Every step is open.
    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    // The host's restock facts of a created critter: a living NPC with the prototype's inventory source and no PC leader (RK3), and one set of a pile of 25 coins and two swords.
    private sealed class World : ISessionRestockWorld
    {
        public bool Dead { get; set; }
        public int InventorySource { get; set; } = Source;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) =>
            entity.StartsWith(SessionCreatedObject.CritterPrefix, StringComparison.Ordinal) ? new(RestockHolderKind.Npc, InventorySource, Dead, false, 0, false) : null;
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => false;
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Read, new(25, 25, [new(100, 1), new(100, 1)]));
        public SessionRestockItem? RestockItem(int basicPrototype) => basicPrototype == 1 ? new(Sword) : null;
        public SessionItemKind RestockPileKind => GoldPile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
    }

    private static SessionMapInventorySeed MapSeed() => new([new("smith", SessionHolderKind.Npc, false, false, [], [])], []);

    private static GameSessionOwner Owner(bool attach = true)
    {
        var owner = Create();
        if (attach) Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, MapSeed()).Failure);
        return owner;
    }

    private static SessionCreatedObject Make(GameSessionOwner owner) =>
        owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Object!;

    private static string[] Holders(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Holders.Select(holder => holder.Entity).ToArray();

    [TestMethod]
    public void A_created_critter_is_a_holder_of_the_attached_seed_after_the_maps_own_and_the_seed_is_not_state()
    {
        using var owner = Owner();
        CollectionAssert.AreEqual(new[] { "smith" }, Holders(owner));
        var critter = Make(owner);
        CollectionAssert.AreEqual(new[] { "smith", critter.Entity }, Holders(owner), "it joins the seed at its creation");
        var view = owner.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == critter.Entity);
        Assert.AreEqual((SessionHolderKind.Npc, false, 0), (view.Kind, view.Dead, view.Items.Count), "a living NPC with an empty list");
        // The host attaches the map's own seed again (a window move, a restore): the created critter is still a holder.
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, MapSeed()).Failure);
        CollectionAssert.AreEqual(new[] { "smith", critter.Entity }, Holders(owner));
        // A second critter is the next holder; the seed's own list (the caller's) is untouched.
        var second = Make(owner);
        CollectionAssert.AreEqual(new[] { "smith", critter.Entity, second.Entity }, Holders(owner));
        var own = MapSeed();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, own).Failure);
        Assert.AreEqual(1, own.Holders.Count, "the host's own seed object is not changed by the created holders");
    }

    [TestMethod]
    public void A_critter_made_before_any_seed_is_attached_joins_at_the_attach_and_one_of_another_map_does_not()
    {
        using var owner = Owner(attach: false);
        var critter = Make(owner);
        var elsewhere = owner.CreateCritterNear(owner.Version, Prototype, new SessionMapId(80), new(100, 201), new OpenMap()).Object!;
        Assert.IsNull(owner.QueryMapInventory(Exterior), "no seed yet");
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, MapSeed()).Failure);
        CollectionAssert.AreEqual(new[] { "smith", critter.Entity }, Holders(owner));
        CollectionAssert.DoesNotContain(Holders(owner), elsewhere.Entity, "a critter of another map is that map's holder");
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(new SessionMapId(80), new([], [])).Failure);
        CollectionAssert.AreEqual(new[] { elsewhere.Entity }, owner.QueryMapInventory(new SessionMapId(80))!.Holders.Select(holder => holder.Entity).ToArray());
    }

    [TestMethod]
    public void The_restock_body_gives_a_created_critter_its_goods_as_it_gives_a_map_npc()
    {
        using var owner = Owner();
        var critter = Make(owner);
        var revision = owner.Version.Revision;
        var result = owner.RegenerateInventory(owner.Version, critter.Entity, new World());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        var done = result.Regeneration!;
        Assert.AreEqual((critter.Entity, SessionRestockOutcome.Regenerated, 0, 25), (done.Holder, done.Outcome, done.Removed, done.Coins));
        Assert.AreEqual(2, done.Created.Count, "the two swords the set's lines made");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        Assert.AreEqual(25, owner.QueryNpcGold(critter.Entity));
        var list = owner.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == critter.Entity).Items;
        Assert.AreEqual(3, list.Count, "the coin pile and the two swords");
        CollectionAssert.Contains(list.Select(item => item.Entity).ToArray(), done.Pile);
    }

    [TestMethod]
    public void The_session_given_a_source_of_nothing_only_clears_and_an_unlisted_entity_is_refused()
    {
        using var owner = Owner();
        var critter = Make(owner);
        var none = owner.RegenerateInventory(owner.Version, critter.Entity, new World { InventorySource = 0 });
        // The session's body is the shop path's too (RK15: a source of 0 ends after the clear); that the creation does not run it for source 0 is the host's gate (AreaRestockHost.GeneratesAtCreation).
        Assert.AreEqual(0, none.Regeneration?.Created.Count ?? 0, "a source of 0 generates nothing");
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == critter.Entity).Items.Count);
        // An entity the seed does not list is refused before any effect.
        var unknown = owner.RegenerateInventory(owner.Version, "oid:critter:99", new World());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder), (unknown.Disposition, unknown.Failure));
    }

    [TestMethod]
    public void The_goods_survive_a_save_and_the_attach_that_follows_and_go_with_the_critter_when_it_is_destroyed()
    {
        using var owner = Owner();
        var critter = Make(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RegenerateInventory(owner.Version, critter.Entity, new World()).Disposition);
        // A restore holds the created critter and the NPC lists; the host's attach of the map's seed gives the holder back with its items.
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionFailure.None, back.AttachMapInventory(Exterior, MapSeed()).Failure);
        CollectionAssert.AreEqual(new[] { "smith", critter.Entity }, Holders(back));
        Assert.AreEqual(3, back.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == critter.Entity).Items.Count);
        Assert.AreEqual(25, back.QueryNpcGold(critter.Entity));
        // Destroyed (the encounter creature of a map left): its holder leaves the seed and what it held is destroyed, so no list stays in the save.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter.Entity).Disposition);
        CollectionAssert.AreEqual(new[] { "smith" }, Holders(owner));
        Assert.AreEqual(0, owner.Capture().Inventory.Npcs.Count, "its added items left the lists");
        Assert.AreEqual(0, owner.Capture().Inventory.NpcWorn.Count, "and its worn record is gone");
        Assert.IsNull(owner.QueryNpcGold(critter.Entity), "its coin pile is destroyed with it");
        // The attach that follows does not bring it back.
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, MapSeed()).Failure);
        CollectionAssert.AreEqual(new[] { "smith" }, Holders(owner));
    }
}
