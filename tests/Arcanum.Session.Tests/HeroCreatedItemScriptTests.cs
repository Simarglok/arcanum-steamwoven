using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.CreatedItemScriptTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2910 (ledger TQ19, NT2; exe 0x4617F0 → 0x461810 fresh path, 0x466640): the same fresh path for a created object whose recipient is the hero: its new ammunition stack (a take of units from an NPC, a container or a junk pile), its first gold pile (a take or the dialog's gift)
// and an item a script creates for it (the trap device, the create-item action) run the new item's get script (4) before the insert check, with the hero as the triggerer, and its insert-item script (21) at the end of the insert. A get script that skips the default refuses the insert:
// the item stays on the hero's tile. Over the synthetic seed of InventoryStateTests; script numbers, programs and texts are invented.
[TestClass]
public sealed class HeroCreatedItemScriptTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, AmmoProto, 3301, 33301);
    private static readonly SessionItemKind Trinket = new(SessionItemType.Generic, 5700, 3700, 33700);
    private static readonly SessionItemKind HeroGold = SessionHeroGrid.GoldKind;

    /// <summary>The prototype arrays of the new items: the ammunition, the trinket and the hero's gold pile all carry a get and an insert-item script.</summary>
    private static readonly Func<int, IReadOnlyList<SessionPrototypeScript>?> Prototypes = prototype =>
        prototype is AmmoProto or 5700 or SessionHeroGrid.GoldPrototype ? new SessionPrototypeScript[] { new(SessionScriptPoint.Get, GetScript, 0, 0), new(SessionScriptPoint.InsertItem, InsertScript, 0, 0) } : null;

    private static GameSessionOwner Hero(bool attach = true)
    {
        var owner = CreateWith(HalfElf());
        var seed = Seed();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed with
        {
            Holders = [.. seed.Holders, new("archer", SessionHolderKind.Npc, false, false, [], ["arrows"])],
            Items = [.. seed.Items, new SessionSeedItem("arrows", Facts(SessionItemType.Ammo, 3300, quantity: 30) with { AmmoType = 0 }, null)],
        }).Failure);
        if (attach) owner.AttachItemScripts(Prototypes);
        return owner;
    }

    // A hero with no gold pile: all its coins are with virgil, who holds them in a new pile.
    private static GameSessionOwner Broke()
    {
        var owner = Hero(attach: false);
        var given = owner.GiveGoldToNpc(owner.Version, "virgil", owner.QueryGold(), new(SessionItemType.Gold, 9099, 3200, 33200), Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (given.Disposition, owner.QueryGold()));
        owner.AttachItemScripts(Prototypes);
        return owner;
    }

    private static (int Point, string Attachee)[] Points(InventoryMutationResult result) => result.Scripts.Select(run => (run.Point, run.Attachee)).ToArray();

    [TestMethod]
    public void A_new_ammunition_stack_taken_by_the_hero_runs_the_get_then_the_insert_script_with_the_hero_as_the_triggerer()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var taken = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1"), (taken.Disposition, taken.Failure, taken.Item));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1"), (SessionScriptPoint.InsertItem, "made:1") }, Points(taken));
        Assert.IsNotNull(taken.InsertScript);
        CollectionAssert.AreEqual(new[] { "hero reads 5", "hero reads 6" }, taken.Messages.Select(message => message.Text).ToArray(), "the hero is the triggerer, so the lines are the hero's");
        Assert.AreEqual(true, ReadFlag(owner, 3), "the get run's flag applies with the move");
        Assert.AreEqual(30, owner.QueryHeroAmmo().Single(stack => stack.Entity == "made:1").Units);
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_refuses_the_new_hero_stack_and_the_source_keeps_its_units()
    {
        using var owner = Hero();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var revision = owner.Version.Revision;
        var refused = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, (int?)SessionScriptPoint.Get), (refused.Disposition, refused.Failure, refused.DeclinedBy));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1") }, Points(refused), "the insert-item script is not run");
        Assert.AreEqual((revision, 0), (owner.Version.Revision, owner.QueryHeroAmmo().Count));
        Assert.AreEqual(false, ReadFlag(owner, 3) == true, "a refused move applies none of its script's effects");
    }

    [TestMethod]
    public void A_stack_taken_into_a_held_stack_merges_and_runs_no_scripts_and_a_missing_host_refuses_a_scripted_new_stack()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var refused = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 10, Bullets, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        Assert.AreEqual(0, owner.QueryHeroAmmo().Count);
        Assert.AreEqual("made:1", owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 10, Bullets, Accept, host).Item);
        var merged = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 10, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1", 0), (merged.Disposition, merged.Item, merged.Scripts.Count), "the merge into a held stack creates no object (0x4658E0 raises the quantity), so runs none (ARC-SCRIPT-2909)");
    }

    [TestMethod]
    public void An_insert_check_refusal_after_the_get_script_applies_none_of_the_hero_stacks_effects()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var refused = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, _ => SessionInsertVerdict.NoRoom, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, SessionInsertVerdict.NoRoom), (refused.Disposition, refused.Failure, refused.Verdict));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1") }, Points(refused));
        Assert.AreEqual(false, ReadFlag(owner, 3) == true);
    }

    [TestMethod]
    public void An_item_created_for_the_hero_runs_its_get_then_its_insert_script_and_enters_the_pack()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var made = owner.CreateItemForHero(owner.Version, Trinket, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1", (SessionInsertVerdict?)SessionInsertVerdict.Accepted, (int?)null), (made.Disposition, made.Failure, made.Item, made.Verdict, made.DeclinedBy));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1"), (SessionScriptPoint.InsertItem, "made:1") }, Points(made));
        Assert.IsNotNull(made.InsertScript);
        CollectionAssert.AreEqual(new[] { "hero reads 5", "hero reads 6" }, made.Messages.Select(message => message.Text).ToArray());
        Assert.AreEqual(true, ReadFlag(owner, 3));
        Assert.AreEqual(SessionObjectPlace.HeroPack, owner.QueryObject("made:1")!.Place);
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_leaves_the_created_item_on_the_heros_tile_with_its_effects_applied()
    {
        using var owner = Hero();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 5), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var made = owner.CreateItemForHero(owner.Version, Trinket, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (SessionInsertVerdict?)null, (int?)SessionScriptPoint.Get), (made.Disposition, made.Failure, made.Verdict, made.DeclinedBy));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1") }, Points(made), "the item left on the ground runs no insert-item script");
        Assert.AreEqual((SessionObjectPlace.Ground, owner.QueryActor().Cell), (owner.QueryObject("made:1")!.Place, owner.QueryObject("made:1")!.Cell));
        Assert.AreEqual(true, ReadFlag(owner, 3), "the get run happened, the created item exists either way");
        CollectionAssert.AreEqual(new[] { "hero reads 5" }, made.Messages.Select(message => message.Text).ToArray());
    }

    [TestMethod]
    public void An_insert_check_refusal_of_a_created_item_runs_the_get_script_but_not_the_insert_script()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var made = owner.CreateItemForHero(owner.Version, Trinket, _ => SessionInsertVerdict.TooHeavy, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionInsertVerdict?)SessionInsertVerdict.TooHeavy), (made.Disposition, made.Verdict));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1") }, Points(made));
        Assert.AreEqual(SessionObjectPlace.Ground, owner.QueryObject("made:1")!.Place);
    }

    [TestMethod]
    public void A_created_item_whose_prototype_has_scripts_is_refused_without_a_host_and_one_without_a_source_runs_none()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var refused = owner.CreateItemForHero(owner.Version, Trinket, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, revision), (refused.Disposition, refused.Failure, owner.Version.Revision));
        using var plain = Hero(attach: false);
        var made = plain.CreateItemForHero(plain.Version, Trinket, Accept, new Host(plain));
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (made.Disposition, made.Scripts.Count), "no prototype source: no attachment is known, as before");
    }

    [TestMethod]
    public void An_insert_script_the_session_cannot_apply_refuses_the_whole_created_item()
    {
        using var owner = Hero();
        var host = new Host(owner);
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var revision = owner.Version.Revision;
        var refused = owner.CreateItemForHero(owner.Version, Trinket, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, revision, 0), (refused.Disposition, refused.Failure, owner.Version.Revision, owner.QueryMadeItems().Count));
        Assert.AreEqual(false, ReadFlag(owner, 3) == true, "the get run's flag was not applied either");
    }

    [TestMethod]
    public void The_heros_first_gold_pile_taken_from_an_npc_runs_the_get_then_the_insert_script_with_the_hero_as_the_triggerer()
    {
        using var owner = Broke();
        var host = new Host(owner);
        var taken = owner.TakeGoldFromNpc(owner.Version, "virgil", 40, null, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 40), (taken.Disposition, taken.Failure, owner.QueryGold()));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, SessionHeroGrid.GoldEntity), (SessionScriptPoint.InsertItem, SessionHeroGrid.GoldEntity) }, Points(taken));
        Assert.IsNotNull(taken.InsertScript);
        CollectionAssert.AreEqual(new[] { "hero reads 5", "hero reads 6" }, taken.Messages.Select(message => message.Text).ToArray());
        Assert.AreEqual(true, ReadFlag(owner, 3));
        // The pile now exists: more coins grow it and run nothing.
        var more = owner.TakeGoldFromNpc(owner.Version, "virgil", 10, null, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 50, 0), (more.Disposition, owner.QueryGold(), more.Scripts.Count));
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_leaves_the_taken_coins_on_the_ground_and_the_pile_unmade()
    {
        using var owner = Broke();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var taken = owner.TakeGoldFromNpc(owner.Version, "virgil", 40, null, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (int?)SessionScriptPoint.Get, 0), (taken.Disposition, taken.DeclinedBy, owner.QueryGold()), "the call succeeds and the coins never reach the hero");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, SessionHeroGrid.GoldEntity) }, Points(taken));
        Assert.AreEqual(1, owner.QueryPlacedItems().Count(item => item.Kind.Type == SessionItemType.Gold), "the new pile lies on the hero's tile");
        Assert.AreEqual(true, ReadFlag(owner, 3));
    }

    [TestMethod]
    public void The_dialogs_gold_gift_to_a_hero_without_a_pile_runs_the_new_piles_scripts()
    {
        using var owner = Broke();
        var host = new Host(owner);
        var given = owner.GiveGoldToHero(owner.Version, 25, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 25), (given.Disposition, owner.QueryGold()));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, SessionHeroGrid.GoldEntity), (SessionScriptPoint.InsertItem, SessionHeroGrid.GoldEntity) }, Points(given));
        Assert.AreEqual(true, ReadFlag(owner, 3));
        var more = owner.GiveGoldToHero(owner.Version, 5, host);
        Assert.AreEqual((30, 0), (owner.QueryGold(), more.Scripts.Count), "a hero with a pile grows it and runs nothing");
    }

    [TestMethod]
    public void A_gold_gift_with_a_scripted_prototype_is_refused_without_a_host_and_a_skipping_get_script_lays_the_pile_on_the_ground()
    {
        using var owner = Broke();
        var revision = owner.Version.Revision;
        var refused = owner.GiveGoldToHero(owner.Version, 25);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, revision, 0), (refused.Disposition, refused.Failure, owner.Version.Revision, owner.QueryGold()));
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var given = owner.GiveGoldToHero(owner.Version, 25, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (int?)SessionScriptPoint.Get, 0), (given.Disposition, given.DeclinedBy, owner.QueryGold()));
        Assert.AreEqual(SessionObjectPlace.Ground, owner.QueryObject(given.Item!)!.Place);
    }

    [TestMethod]
    public void The_slots_a_new_gold_piles_scripts_write_are_not_kept_for_the_fixed_pile_entity()
    {
        using var owner = Broke();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)), C(ScrCatalogue.Action.ReturnRunDefault));
        var given = owner.GiveGoldToHero(owner.Version, 25, host);
        Assert.AreEqual(GameSessionDisposition.Changed, given.Disposition);
        Assert.AreEqual(0, owner.QueryItemScriptSlots().Count(slot => slot.Entity == SessionHeroGrid.GoldEntity), "the owner's ARC-UI-090 model: the pile keeps no slots");
    }
}
