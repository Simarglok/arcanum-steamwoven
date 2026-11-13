using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-1731: the item identify and item repair windows' model over a session (ledger IS1–IS6; invented prototypes, prices and entities).
[TestClass]
public sealed class AreaItemServiceTests
{
    private const int KitSword = 4030, KitAxe = 4040;

    private static CombatFields Weapon(int prototype, int worth, int complexity = 0) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Weapon, prototype, 0, 0, 0, 0, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 10, worth, 0, complexity, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static CombatFields Gold() =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0, 3200, 33200, 0, 1, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 0, 1, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static AreaBarterData Data() => AreaBarterData.Create(Mes("{1}{Smith: 1,1}{2}{End: 1,1}"), Mes("{1}{4010 4020}"),
        new Dictionary<int, CombatFields>
        {
            [KitSword] = Weapon(KitSword, 100), [KitAxe] = Weapon(KitAxe, 400, complexity: 30), [AreaBarterData.GoldPrototype] = Gold(),
        });

    // kit:1 is a plain sword (worth 100, identified), kit:2 a magickal axe (worth 400, complexity 30, not identified); the hero has 1000 gold.
    private static GameSessionOwner Session(RuleMode rules = RuleMode.Steamwoven)
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 1000, [new SessionHeroKitItem(new(SessionItemType.Weapon, KitSword, 4100, 34100)), new SessionHeroKitItem(new(SessionItemType.Weapon, KitAxe, 4200, 34200))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var cell = AreaGridSession.Cell(AreaGridSession.Spawn);
        var flags = AreaStartingHero.NewGameFlags(new(new(Guid.NewGuid()), new(Guid.NewGuid())), new(Guid.NewGuid()), load.Flag);
        var session = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, flags,
            new(new(1), new(checked((int)cell.X), checked((int)cell.Y)), Arcanum.Simulation.Movement.NeighborDirection.XPlus), Hero: load.Definition,
            Experience: load.Experience, AutoLevel: load.AutoLevel) { Rules = rules }).Owner!;
        var seed = new SessionMapInventorySeed(
            [new("merchant", SessionHolderKind.Npc, false, false, [], ["coins"])],
            [new("coins", new(new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 4300, 34300), 10, 0, 0, 100, []), null)]);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(session.QueryActor().Map, seed).Failure);
        return session;
    }

    private static AreaBarter Barter(GameSessionOwner session, AreaBarterData data) =>
        new(session, new AreaInventoryHost([]), data, new AreaBarterMerchant("merchant", "Smith", 100, false, false, data.BuyList(1), false),
            () => 50, _ => true, (entity, _) => entity);

    private static (SessionNpcAdvancement, AdvancementModifiers)? Repairer(int points, SkillTraining training)
    {
        int[] tech = new int[4];
        tech[0] = points | (int)training << 6;
        return (new SessionNpcAdvancement("merchant", new(CharacterRace.Human, false, [8, 8, 8, 8, 20, 8, 8, 8], 0, 0, 1, 0,
            [.. new int[12]], [.. tech], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0), AdvancementModifiers.None);
    }

    private static AreaItemService Service(GameSessionOwner session, ItemServiceKind kind, int points = 4, SkillTraining training = SkillTraining.Expert) =>
        new(Barter(session, Data()), kind, () => Repairer(points, training));

    private static ItemCondition Stock() => SessionItemConditions.FromCombatFields(Weapon(KitSword, 100))!.Value;

    private static void Damage(GameSessionOwner session, string entity, int amount) =>
        Assert.AreEqual(GameSessionDisposition.Changed, session.DamageItem(session.Version, entity, Stock(), amount).Disposition);

    [TestMethod]
    public void Identify_quotes_a_hundred_for_an_unidentified_item_and_refuses_an_identified_one()
    {
        using var session = Session();
        var view = Service(session, ItemServiceKind.Identify).View();
        var byEntity = view.Items.ToDictionary(line => line.Entity);
        Assert.AreEqual(ItemServiceStatus.AlreadyIdentified, byEntity["kit:1"].Quote.Status, "no complexity: identified");
        Assert.AreEqual((ItemServiceStatus.Quoted, 100, 5800, 5899), (byEntity["kit:2"].Quote.Status, byEntity["kit:2"].Quote.Price, byEntity["kit:2"].Quote.LineFirst, byEntity["kit:2"].Quote.LineLast));
        Assert.AreEqual(1000, view.HeroGold);
    }

    [TestMethod]
    public void The_second_click_pays_and_identifies_and_the_item_is_then_refused()
    {
        using var session = Session();
        var service = Service(session, ItemServiceKind.Identify);
        var first = service.Click("kit:2");
        Assert.AreEqual((AreaItemServiceAction.Selected, 0), (first.Action, first.Paid), "the first click quotes only");
        Assert.AreEqual((1000, 100, false), (session.QueryGold(), session.QueryNpcGold("merchant"), session.QueryItemIdentified("kit:2")));
        var second = service.Click("kit:2");
        Assert.AreEqual((AreaItemServiceAction.Done, 100), (second.Action, second.Paid), second.Text);
        Assert.AreEqual((900, 200, true), (session.QueryGold(), session.QueryNpcGold("merchant"), session.QueryItemIdentified("kit:2")));
        var third = service.Click("kit:2");
        Assert.AreEqual((AreaItemServiceAction.Refused, ItemServiceStatus.AlreadyIdentified, 0), (third.Action, third.Quote!.Value.Status, third.Paid));
        Assert.AreEqual(900, session.QueryGold(), "nothing more is paid");
    }

    [TestMethod]
    public void A_hero_with_too_little_gold_is_told_so_and_nothing_changes()
    {
        using var session = Session();
        Assert.AreEqual(GameSessionFailure.None, session.GiveGoldToNpc(session.Version, "merchant", 950, Data().GoldKind(), AreaInventoryHost.DiagnosticInsertCheck).Failure);
        var service = Service(session, ItemServiceKind.Identify);
        service.Click("kit:2");
        var second = service.Click("kit:2");
        Assert.AreEqual((AreaItemServiceAction.NoMoney, 0, 50, false), (second.Action, second.Paid, session.QueryGold(), session.QueryItemIdentified("kit:2")));
    }

    [TestMethod]
    public void A_refused_item_after_a_quoted_one_costs_nothing_in_steamwoven()
    {
        // The Original setting keeps the first item's price (ledger IS5; ItemServiceRulesTests); the session's default setting does not.
        using var session = Session();
        Assert.IsFalse(session.Rules.Items.ServiceKeepsStalePrice);
        var service = Service(session, ItemServiceKind.Identify);
        service.Click("kit:2");
        service.Click("kit:1");
        var confirm = service.Click("kit:1");
        Assert.AreEqual((AreaItemServiceAction.Refused, 0, 1000), (confirm.Action, confirm.Paid, session.QueryGold()));
    }

    [TestMethod]
    public void A_refused_item_after_a_quoted_one_keeps_the_stale_price_in_the_original_setting()
    {
        // Ledger IS5: after quoting A (100) and refusing B, the next event on B finds the kept price 100 and pays it for nothing.
        using var session = Session(RuleMode.Original);
        Assert.IsTrue(session.Rules.Items.ServiceKeepsStalePrice);
        var service = Service(session, ItemServiceKind.Identify);
        service.Click("kit:2");
        var refusedQuote = service.Click("kit:1");
        Assert.AreEqual((AreaItemServiceAction.Selected, ItemServiceStatus.AlreadyIdentified), (refusedQuote.Action, refusedQuote.Quote!.Value.Status));
        Assert.AreEqual(100, service.View().KeptPrice, "the refusal leaves the price of the first item");
        var confirm = service.Click("kit:1");
        Assert.AreEqual((AreaItemServiceAction.Done, 100, 900), (confirm.Action, confirm.Paid, session.QueryGold()), confirm.Text);
        Assert.IsFalse(session.QueryItemIdentified("kit:2"), "the item that was quoted is not the one served");
    }

    [TestMethod]
    public void Repair_quotes_by_damage_and_the_repairers_forced_run_restores_and_is_paid()
    {
        using var session = Session();
        Damage(session, "kit:1", 20);
        var service = Service(session, ItemServiceKind.Repair);
        var line = service.View().Items.Single(item => item.Entity == "kit:1");
        var condition = session.QueryItemCondition("kit:1", Stock())!.Value;
        Assert.AreEqual(20, condition.Damage);
        // The PC's sale price of the sword to the repairer: 100 × (10 / 2 + 50) / 100 = 55 (haggle 10, multiplier 100), before the damage scaling.
        var expected = ItemDurabilityRules.RepairQuote(condition, 55, RepairTraining.Expert, session.Rules.Items);
        Assert.AreEqual((ItemServiceStatus.Quoted, expected.Cost, 6100, 6199), (line.Quote.Status, line.Quote.Price, line.Quote.LineFirst, line.Quote.LineLast));
        var untouched = service.View().Items.Single(item => item.Entity == "kit:2").Quote;
        Assert.AreEqual(ItemServiceStatus.NothingToRepair, untouched.Status);

        var draws = session.QueryRandom().Draws;
        service.Click("kit:1");
        var done = service.Click("kit:1");
        Assert.AreEqual(AreaItemServiceAction.Done, done.Action, done.Text);
        Assert.AreEqual(expected.Cost, done.Paid);
        Assert.AreEqual(1000 - expected.Cost, session.QueryGold());
        Assert.AreEqual(100 + expected.Cost, session.QueryNpcGold("merchant"));
        Assert.IsTrue(done.Condition!.Value.Damage < 20, "the run restored hit points");
        Assert.AreEqual(done.Condition, session.QueryItemCondition("kit:1", Stock()));
        Assert.AreEqual(draws + 2, session.QueryRandom().Draws, "the skill run always takes its two draws");
    }

    [TestMethod]
    public void The_forced_run_restores_at_least_one_point_and_takes_the_trainings_share_of_the_adjustment()
    {
        // Ledger RP2/DU8: the forced run's amount is the maximum x effectiveness / 100, at least 1; hp_adj falls by the training's share (none 10, expert 1).
        foreach (var (training, percent) in new[] { (SkillTraining.None, 10), (SkillTraining.Expert, 1) })
        {
            using var session = Session();
            Damage(session, "kit:1", 20);
            var before = session.QueryItemCondition("kit:1", Stock())!.Value;
            var service = Service(session, ItemServiceKind.Repair, points: 0, training: training);
            service.Click("kit:1");
            var done = service.Click("kit:1");
            Assert.AreEqual(AreaItemServiceAction.Done, done.Action, done.Text);
            var after = done.Condition!.Value;
            Assert.IsTrue(before.Damage - after.Damage >= 1, $"{training}: at least one point is restored");
            Assert.AreEqual(before.Adjustment - (int)((long)before.MaximumHitPoints * percent / 100), after.Adjustment, $"{training}: the share of the adjustment");
        }
    }

    [TestMethod]
    public void A_broken_item_is_mended_only_by_a_master()
    {
        using var session = Session();
        Damage(session, "kit:1", 400);
        Assert.IsTrue(session.QueryItemCondition("kit:1", Stock())!.Value.Broken);
        Assert.AreEqual(ItemServiceStatus.NeedsMaster, Service(session, ItemServiceKind.Repair).View().Items.Single(item => item.Entity == "kit:1").Quote.Status);
        var master = Service(session, ItemServiceKind.Repair, training: SkillTraining.Master);
        var quote = master.View().Items.Single(item => item.Entity == "kit:1").Quote;
        Assert.AreEqual((ItemServiceStatus.Quoted, true), (quote.Status, quote.Price >= 2));
        master.Click("kit:1");
        var done = master.Click("kit:1");
        Assert.AreEqual(AreaItemServiceAction.Done, done.Action, done.Text);
        Assert.IsFalse(session.QueryItemCondition("kit:1", Stock())!.Value.Broken, "a master clears the destroyed bit of the current art");
    }

    [TestMethod]
    public void Without_the_repairers_skills_a_repair_is_not_offered()
    {
        using var session = Session();
        var service = new AreaItemService(Barter(session, Data()), ItemServiceKind.Repair, () => null);
        Assert.AreEqual(AreaItemServiceAction.None, service.Click("kit:1").Action);
        Assert.AreEqual(1000, session.QueryGold());
    }
}
