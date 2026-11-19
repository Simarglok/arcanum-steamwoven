using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2855 (ledger NT1, AM4, audit TQ19; exe 0x464830, 0x4658E0 → 0x441980 with point 21): a gold or ammunition move with a destination runs the source pile's insert-item script first, the destination as the triggerer and
// the pile as the attachee, before the pile changes; its answer cannot refuse. Over the synthetic seed of InventoryStateTests with scripted piles (the merchant's coins, an archer's arrows, a chest's quiver and coins, a ground
// stack of shells); script numbers, programs and texts are invented.
[TestClass]
public sealed class PileInsertScriptTests
{
    private const int InsertScript = 702;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public HashSet<string> Scripted { get; } = new(StringComparer.Ordinal) { "coins", "arrows", "quiver", "chest-coins", "shells" };
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [InsertScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault)) };
        public List<string> Asked { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point)
        {
            Asked.Add($"slot {attachee.Code} {point}");
            return point == SessionScriptPoint.InsertItem && Scripted.Contains(attachee.Code) ? new ScrSlot(InsertScript, 0, 0) : null;
        }

        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => session.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.FlagVersion, new FlagId(index))).Value ?? false;
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;

        public SessionPrintedLine PrintedLine(int script, int line)
        {
            Asked.Add($"hero line {line}");
            return new(SessionPrintedLineStatus.Shown, "hero reads it", "test");
        }

        public SessionPrintedLine PrintedLineForNpc(int script, int line)
        {
            Asked.Add($"npc line {line}");
            return new(SessionPrintedLineStatus.Shown, "npc reads it", "test");
        }
    }

    private static SessionMapInventorySeed PileSeed()
    {
        var seed = Seed();
        return seed with
        {
            Holders = [.. seed.Holders.Select(holder => holder.Entity == "chest" ? holder with { Items = ["quiver", "chest-coins"] } : holder),
                new("archer", SessionHolderKind.Npc, false, false, [], ["arrows"])],
            Items = [.. seed.Items.Select(item => item.Entity == "coins" ? item with { Facts = Facts(SessionItemType.Gold, 3200, quantity: 5, scripts: SessionScriptPoint.InsertItem) } : item),
                new SessionSeedItem("arrows", Facts(SessionItemType.Ammo, 3300, quantity: 30, scripts: SessionScriptPoint.InsertItem) with { AmmoType = 0 }, null),
                new SessionSeedItem("quiver", Facts(SessionItemType.Ammo, 3300, quantity: 20, scripts: SessionScriptPoint.InsertItem) with { AmmoType = 0 }, null),
                new SessionSeedItem("chest-coins", Facts(SessionItemType.Gold, 3200, quantity: 7, scripts: SessionScriptPoint.InsertItem), null),
                new SessionSeedItem("shells", Facts(SessionItemType.Ammo, 3302, quantity: 10, scripts: SessionScriptPoint.InsertItem) with { AmmoType = 0 }, new CellCoord(105, 200))],
        };
    }

    private static GameSessionOwner Hero(SessionHeroDefinition? hero = null)
    {
        var owner = CreateWith(hero ?? HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, PileSeed()).Failure);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void The_hero_taking_coins_runs_the_pile_script_first_with_the_pile_as_attachee_and_the_hero_reading_its_line()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var taken = owner.TakeGoldFromNpc(owner.Version, "merchant", 3, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "coins", 1L), (taken.Disposition, taken.Failure, taken.Item, owner.Version.Revision));
        Assert.AreEqual((2, 388, true), (owner.QueryNpcGold("merchant"), owner.QueryGold(), ReadFlag(owner, 3) == true), "the move and the run's flag apply in one revision");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "coins") }, taken.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.IsNotNull(taken.InsertScript);
        CollectionAssert.AreEqual(new[] { "hero reads it" }, taken.Messages.Select(message => message.Text).ToArray());
        CollectionAssert.AreEqual(new[] { $"slot coins {SessionScriptPoint.InsertItem}", "hero line 2" }, host.Asked);
    }

    [TestMethod]
    public void A_pile_script_that_skips_the_default_cannot_refuse_the_move_and_an_emptied_pile_is_destroyed_after_it_ran()
    {
        using var owner = Hero();
        var host = new Host(owner);
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var taken = owner.TakeGoldFromNpc(owner.Version, "merchant", 5, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (int?)null), (taken.Disposition, taken.Failure, taken.DeclinedBy), "the event cannot refuse (NT1)");
        Assert.AreEqual((0, 390, true, true), (owner.QueryNpcGold("merchant"), owner.QueryGold(), owner.IsItemDestroyed("coins"), ReadFlag(owner, 3) == true));
    }

    [TestMethod]
    public void Without_a_host_a_scripted_pile_is_still_refused_and_so_is_a_run_the_session_cannot_apply()
    {
        using var owner = Hero();
        var refused = owner.TakeGoldFromNpc(owner.Version, "merchant", 1);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 5, Bullets, Accept).Failure);

        var host = new Host(owner);
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var unsupported = owner.TakeGoldFromNpc(owner.Version, "merchant", 1, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L, 385, 5), (unsupported.Disposition, unsupported.Failure, owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("merchant")));
        Assert.AreNotEqual(true, ReadFlag(owner, 3), "nothing of the refused run applied");
    }

    [TestMethod]
    public void The_hero_taking_ammunition_from_an_npc_or_a_container_runs_the_stack_script_with_the_hero_as_triggerer()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var taken = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 12, Bullets with { Prototype = 7058 }, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1"), (taken.Disposition, taken.Failure, taken.Item));
        Assert.AreEqual((18, 12, true), (owner.QueryAmmo("arrows")!.Value.Units, owner.QueryAmmo("made:1")!.Value.Units, ReadFlag(owner, 3) == true));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "arrows") }, taken.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.IsTrue(host.Asked.Contains("hero line 2"));

        // A container's stack, bought through the barter with a free price: the same event.
        var bought = owner.BarterBuy(owner.Version, "merchant", "quiver", 5, 0, Bullets, GoldPile, Accept, Accept, host, container: "chest");
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (bought.Disposition, bought.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "quiver") }, bought.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(15, owner.QueryAmmo("quiver")!.Value.Units);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.BarterBuy(owner.Version, "merchant", "quiver", 5, 0, Bullets, GoldPile, Accept, Accept, null, container: "chest").Failure, "no host: refused");
    }

    [TestMethod]
    public void A_move_between_npcs_runs_the_source_script_with_the_destination_npc_as_triggerer_and_its_line_read_for_an_npc()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var moved = owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 5, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (moved.Disposition, moved.Failure));
        Assert.AreEqual((0, 5, true), (owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil"), ReadFlag(owner, 3) == true));
        Assert.IsTrue(host.Asked.Contains("npc line 2") && !host.Asked.Contains("hero line 2"), string.Join("|", host.Asked));
        using var bare = Hero();
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.MoveGoldBetweenNpcs(bare.Version, "merchant", "virgil", 1, GoldPile, Accept).Failure, "no host: refused");
    }

    [TestMethod]
    public void A_sale_pays_from_the_merchants_pile_through_its_insert_script_and_the_window_open_runs_it_for_the_containers_piles()
    {
        using var owner = Hero();
        var host = new Host(owner);
        // The window's open moves the chest's coins onto the merchant: 21 on the chest's pile with the merchant as the triggerer; its line is in the open result.
        var opened = owner.OpenBarter(owner.Version, "merchant", "chest", false, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 7), (opened.Disposition, opened.Failure, opened.Coins));
        CollectionAssert.AreEqual(new[] { "npc reads it" }, opened.Messages.Select(message => message.Text).ToArray());
        Assert.AreEqual((12, true), (owner.QueryNpcGold("merchant"), ReadFlag(owner, 3) == true));
        Assert.IsTrue(owner.IsItemDestroyed("chest-coins"));

        // The sale's payment is the merchant's pile taken by the hero: 21 on "coins" with the hero as the triggerer.
        host.Asked.Clear();
        var sold = owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 4, Bullets, Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (sold.Disposition, sold.Failure));
        Assert.AreEqual((8, 389), (owner.QueryNpcGold("merchant"), owner.QueryGold()));
        Assert.IsTrue(host.Asked.Contains($"slot coins {SessionScriptPoint.InsertItem}") && host.Asked.Contains("hero line 2"));

        using var bare = Hero();
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.OpenBarter(bare.Version, "merchant", "chest", false, GoldPile, Accept).Failure, "no host: refused");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.BarterSell(bare.Version, "merchant", "kit:1", 1, 4, Bullets, Accept).Failure, "no host: refused before the item moves");
    }

    [TestMethod]
    public void A_steal_of_coins_or_ammunition_runs_the_pile_script_and_a_plant_of_a_scripted_stack_runs_the_heros_stack_script()
    {
        var stats = Defaults(); stats[2] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.PickPocket] = 20 | (int)SkillTraining.Master << 6;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        using var owner = Hero(hero);
        var host = new Host(owner);
        var victim = new SessionPickPocketVictim("merchant") { Perception = 0, Alignment = 0 };

        var bare = owner.PickPocket(owner.Version, victim, PickPocketDirection.Steal, "coins", default, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (bare.Disposition, bare.Failure), "no host: refused");

        var stolen = owner.PickPocket(owner.Version, victim, PickPocketDirection.Steal, "coins", default, Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 5), (stolen.Disposition, stolen.Failure, stolen.Moved));
        Assert.AreEqual((390, 0, true), (owner.QueryGold(), owner.QueryNpcGold("merchant"), ReadFlag(owner, 3) == true));

        // The archer's arrows, stolen by the units (at most 20): the stack's own script.
        var arrows = owner.PickPocket(owner.Version, new SessionPickPocketVictim("archer") { Perception = 0, Alignment = 0 }, PickPocketDirection.Steal, "arrows", Bullets with { Prototype = 7058 }, Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 20), (arrows.Disposition, arrows.Failure, arrows.Moved));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "arrows") }, arrows.Move!.Value.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
    }

    [TestMethod]
    public void The_heros_own_scripted_stack_given_to_an_npc_runs_its_script_with_the_npc_as_triggerer()
    {
        using var owner = Hero();
        var host = new Host(owner);
        // The hero picks the ground stack up (its own insert-item run), then gives units of it to virgil.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "shells", Accept, host).Disposition);
        host.Asked.Clear();
        var given = owner.GiveAmmoToNpc(owner.Version, "virgil", "shells", 4, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (given.Disposition, given.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "shells") }, given.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.IsTrue(host.Asked.Contains("npc line 2"), "the recipient is the triggerer");
        Assert.AreEqual(6, owner.QueryAmmo("shells")!.Value.Units);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.GiveAmmoToNpc(owner.Version, "virgil", "shells", 1, Bullets, Accept).Failure, "no host: refused");
    }

    [TestMethod]
    public void A_sale_reports_the_payments_script_lines_and_is_refused_whole_when_the_payment_run_cannot_be_applied()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var sold = owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 4, Bullets, Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (sold.Disposition, sold.Failure));
        CollectionAssert.AreEqual(new[] { "hero reads it" }, sold.Messages.Select(message => message.Text).ToArray(), "the payment's printed line reaches the sale's result");
        Assert.AreEqual(SessionScriptPoint.InsertItem, sold.Scripts.Single().Point);

        // A run the session cannot apply is found before the item moves: the hero keeps it.
        using var other = Hero();
        var bad = new Host(other);
        bad.Programs[InsertScript] = Program(C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var refused = other.BarterSell(other.Version, "merchant", "kit:1", 1, 4, Bullets, Accept, scripts: bad);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, other.Version.Revision));
        Assert.IsTrue(other.QueryHeroKit().Any(item => item.Entity == "kit:1"), "the item never moved");
    }

    [TestMethod]
    public void A_made_stack_with_a_prototype_insert_script_runs_it_when_given_and_is_refused_without_a_host()
    {
        using var owner = Hero();
        owner.AttachItemScripts(prototype => prototype == 7059 ? [new(SessionScriptPoint.InsertItem, InsertScript, 0, 0)] : null);
        var host = new Host(owner);
        var taken = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1"), (taken.Disposition, taken.Item));
        // The made stack inherits the prototype's insert-item script, so giving it away runs 21 for it and, with no host, is refused.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.GiveAmmoToNpc(owner.Version, "virgil", "made:1", 5, Bullets, Accept).Failure);
        var given = owner.GiveAmmoToNpc(owner.Version, "virgil", "made:1", 5, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionScriptPoint.InsertItem, "made:1"), (given.Disposition, given.Scripts.Single().Point, given.Scripts.Single().Attachee));
    }
}
