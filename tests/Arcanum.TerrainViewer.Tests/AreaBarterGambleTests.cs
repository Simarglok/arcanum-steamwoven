using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Skills;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-1740 (FR-ITEM-019, ledger BT6, BW3, SK5): the barter window's bet over a session (invented prototypes, prices and entities). The hero has intelligence
// 20 and as many Gambling points as asked (level 4 each, to 20), so a level 20 hero against a merchant of level 0 has the capped effectiveness 95.
[TestClass]
public sealed class AreaBarterGambleTests
{
    private const int KitSword = 4030, Axe = 4040;

    private static CombatFields Weapon(int prototype, int worth, uint flags = 0) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Weapon, prototype, 0, 0, 0, 0, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(flags, 10, worth, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static CombatFields Gold() =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0, 3200, 33200, 0, 1, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 0, 1, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static AreaBarterData Data() => AreaBarterData.Create(Mes("{1}{Smith: 1,1}{2}{End: 1,1}"), Mes("{1}{4010 4020}"),
        new Dictionary<int, CombatFields> { [KitSword] = Weapon(KitSword, 100), [Axe] = Weapon(Axe, 400), [AreaBarterData.GoldPrototype] = Gold() });

    private static GameSessionOwner Session(int points, int gold = 1000, ulong seed = 0, SkillTraining training = SkillTraining.None, bool masterHaggle = false)
    {
        int[] basic = new int[12];
        basic[(int)Skill.Gambling] = points | (int)training << 6;
        if (masterHaggle) basic[(int)Skill.Haggle] = 20 | (int)SkillTraining.Master << 6;
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [20, 20, 20, 20, 20, 20, 20, 20, 0, 0], 1, 5, 0,
            null, null, gold, [new SessionHeroKitItem(new(SessionItemType.Weapon, KitSword, 4100, 34100))],
            Advancement: SessionHeroAdvancement.None with { BasicSkills = [.. basic] });
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load, randomSeed: seed).Owner!;
        SessionItemFacts Facts(SessionItemType type, int prototype, int location = 0, int? quantity = null, uint flags = 0) =>
            new(new(type, prototype, 4200, 34200), 10, 0, location, quantity, []) { };
        var seedInventory = new SessionMapInventorySeed(
            [new("merchant", SessionHolderKind.Npc, false, false, [], ["axe", "vest", "coins"])],
            [new("axe", Facts(SessionItemType.Weapon, Axe), null), new("vest", Facts(SessionItemType.Weapon, Axe, location: 1006), null),
             new("coins", Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, quantity: 100), null)]);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(session.QueryActor().Map, seedInventory).Failure);
        return session;
    }

    // Lines: category N says "lineN", and a line that carries a price says "lineN %d".
    private static string? Line(int category) => category is 4 or 8 or 1 ? $"line{category} %d" : $"line{category}";

    private static SessionSkill MerchantSkill(int points, int level) => new(Skill.Gambling, points, points, level, SkillTraining.None);

    private static AreaBarter Barter(GameSessionOwner session, List<int> changes, SessionSkill? merchant, bool lines = true, int reaction = 50) =>
        new(session, new AreaInventoryHost([]), Data(), new AreaBarterMerchant("merchant", "Smith", 100, false, false, Data().BuyList(1), false),
            () => reaction, change => { changes.Add(change); return true; }, (entity, _) => entity, () => merchant, lines ? Line : null);

    [TestMethod]
    public void The_gate_refuses_with_the_merchants_line_of_the_category()
    {
        using var rich = Session(5);
        var barter = Barter(rich, [], MerchantSkill(3, 0));
        barter.Open();
        var hers = barter.CheckGamble("kit:1");
        Assert.AreEqual((GambleRefusal.HeroSide, "line2"), (hers.Gate.Refusal, hers.Text), "the hero's own item");
        Assert.AreEqual(GambleRefusal.NoGambler, Barter(rich, [], MerchantSkill(0, 0)).CheckGamble("axe").Gate.Refusal);
        Assert.AreEqual("line9", Barter(rich, [], null).CheckGamble("axe").Text, "no sheet: no points");
        using var untrained = Session(0);
        var valuable = Barter(untrained, [], MerchantSkill(3, 0)).CheckGamble("axe");
        Assert.AreEqual((GambleRefusal.TooValuable, "line3"), (valuable.Gate.Refusal, valuable.Text), "level 0: the limit is 0, the axe quotes 720 for this haggle (intelligence 20: 20)");
        using var poor = Session(5, gold: 700);
        var short1 = Barter(poor, [], MerchantSkill(3, 0)).CheckGamble("axe");
        Assert.AreEqual((GambleRefusal.TooPoor, "line4 720"), (short1.Gate.Refusal, short1.Text), "the price goes into the line");
        Assert.AreEqual(720, short1.Price);
    }

    [TestMethod]
    public void An_allowed_gate_says_the_confirm_line_with_the_price_and_the_window_prints_its_own_text_without_lines()
    {
        using var session = Session(5);
        var check = Barter(session, [], MerchantSkill(3, 0)).CheckGamble("axe");
        Assert.AreEqual((true, 720, "line8 720"), (check.Allowed, check.Price, check.Text));
        var own = Barter(session, [], MerchantSkill(3, 0), lines: false).CheckGamble("axe");
        Assert.AreEqual("Stake it? A loss costs 720 coins.", own.Text, "the fallback text carries the price too");
    }

    [TestMethod]
    public void A_bet_wins_for_nothing_and_the_merchant_loses_reaction_or_loses_and_the_hero_pays()
    {
        // A seed whose first roll is at most 90 wins against the capped 95; one of 96 or more loses.
        var (won, lost) = (false, false);
        for (ulong seed = 0; seed < 600 && !(won && lost); seed++)
        {
            using var session = Session(5, seed: seed);
            var changes = new List<int>();
            var barter = Barter(session, changes, MerchantSkill(3, 0));
            barter.Open();
            var roll = session.QueryGamblingRoll()!.Value;
            if (roll is > 90 and < 96 || roll <= 90 && won || roll >= 96 && lost) continue;
            var worth = barter.View().Wares.Single().Worth;
            var result = barter.Gamble("axe");
            Assert.IsTrue(result.Settled, result.Text);
            Assert.AreEqual(roll, result.Bet!.Check!.Check.Roll, "the saved roll decides");
            Assert.IsTrue(session.QueryPcInventory().Any(item => item.Entity == "axe"));
            if (roll <= 90)
            {
                won = true;
                Assert.AreEqual((true, 0, 1000, 100, "line0"), (result.Won, result.Paid, session.QueryGold(), session.QueryNpcGold("merchant"), result.Text));
                var expected = GamblingRules.WonReaction(worth, 1, ReactionRules.Category(50));
                Assert.AreEqual((expected, expected < 0), (result.ReactionChange, true));
                CollectionAssert.AreEqual(new[] { expected }, changes);
            }
            else
            {
                lost = true;
                Assert.AreEqual((false, 720, 280, 820, "line1 720"), (result.Won, result.Paid, session.QueryGold(), session.QueryNpcGold("merchant"), result.Text));
                CollectionAssert.AreEqual(Array.Empty<int>(), changes, "a neutral merchant's reaction does not move on a paid loss");
            }
            Assert.AreEqual(result.Bet.Check.NextGamblingRoll, session.QueryGamblingRoll(), "a new roll was thrown for the next bet");
        }
        Assert.IsTrue(won && lost, "both outcomes were seen");
    }

    // ARC-UI-2139 (0x579840, 0x579B60, 0x461CA0; ledger BW3, GB1): a worn item of the merchant lifted from its doll is the merchant's item, still worn (location 1000..1008), and its buy
    // quote is 0 below Master haggle (0x462230), so the gate passes the price tests and asks the worn tier (Expert gambling) alone.
    [TestMethod]
    public void A_worn_merchant_item_meets_the_worn_tier_of_the_gate_and_costs_nothing_below_master_haggle()
    {
        using var novice = Session(5);
        var barter = Barter(novice, [], MerchantSkill(3, 0));
        var refused = barter.CheckGamble("vest");
        Assert.AreEqual((GambleRefusal.NeedsExpert, 0, "line5"), (refused.Gate.Refusal, refused.Price, refused.Text));
        var silent = barter.Gamble("vest");
        Assert.AreEqual((GambleRefusal.NeedsExpert, (SessionGambleResult?)null, ""), (silent.Gate.Refusal, silent.Bet, silent.Text));
        Assert.AreEqual("vest", barter.View().WornWares.Single().Entity, "it stays on the slot");

        using var expert = Session(5, training: SkillTraining.Expert);
        var allowed = Barter(expert, [], MerchantSkill(3, 0)).CheckGamble("vest");
        Assert.AreEqual((true, true, 0, "line7 line8 0"), (allowed.Allowed, allowed.Gate.Warned, allowed.Price, allowed.Text));
        Assert.AreEqual(720, Barter(expert, [], MerchantSkill(3, 0)).CheckGamble("axe").Price, "an unworn ware keeps its price");
    }

    [TestMethod]
    public void A_master_of_haggle_stakes_a_worn_merchant_item_at_its_buy_price()
    {
        using var session = Session(5, training: SkillTraining.Expert, masterHaggle: true);
        var barter = Barter(session, [], MerchantSkill(3, 0));
        var vest = barter.View().WornWares.Single();
        Assert.IsTrue(vest.Quote.Price > 0);
        var check = barter.CheckGamble("vest");
        Assert.AreEqual((true, vest.Quote.Price, $"line7 line8 {vest.Quote.Price}"), (check.Allowed, check.Price, check.Text));
    }

    [TestMethod]
    public void A_bet_on_a_worn_merchant_item_takes_it_off_the_slot_to_the_hero_for_nothing_won_or_lost()
    {
        var (won, lost) = (false, false);
        for (ulong seed = 0; seed < 600 && !(won && lost); seed++)
        {
            using var session = Session(5, seed: seed, training: SkillTraining.Expert);
            var changes = new List<int>();
            var barter = Barter(session, changes, MerchantSkill(3, 0));
            barter.Open();
            var roll = session.QueryGamblingRoll()!.Value;
            if (roll is > 90 and < 96 || roll <= 90 && won || roll >= 96 && lost) continue;
            var worth = barter.View().WornWares.Single().Worth;
            var result = barter.Gamble("vest");
            Assert.IsTrue(result.Settled, result.Text);
            Assert.AreEqual(roll, result.Bet!.Check!.Check.Roll, "the saved roll decides");
            Assert.IsTrue(session.QueryPcInventory().Any(item => item.Entity == "vest"), "it went to the hero's pack");
            Assert.AreEqual(0, barter.View().WornWares.Count, "it left the merchant's slot");
            Assert.AreEqual((1000, 100, 0), (session.QueryGold(), session.QueryNpcGold("merchant"), result.Paid), "no coin moves: the quote is 0");
            if (roll <= 90)
            {
                won = true;
                var expected = GamblingRules.WonReaction(worth, 1, ReactionRules.Category(50));
                Assert.AreEqual((true, "line0", expected), (result.Won, result.Text, result.ReactionChange), "the reaction takes the base worth, not the quote");
                CollectionAssert.AreEqual(new[] { expected }, changes);
            }
            else
            {
                lost = true;
                Assert.AreEqual((false, "line1 0"), (result.Won, result.Text));
                CollectionAssert.AreEqual(Array.Empty<int>(), changes);
            }
        }
        Assert.IsTrue(won && lost, "both outcomes were seen");
    }

    [TestMethod]
    public void A_refused_gate_runs_nothing_and_says_nothing()
    {
        using var session = Session(0);
        var barter = Barter(session, [], MerchantSkill(3, 0));
        barter.Open();
        var (revision, roll, gold) = (session.Version.Revision, session.QueryGamblingRoll(), session.QueryGold());
        var result = barter.Gamble("axe");
        Assert.AreEqual((GambleRefusal.TooValuable, (SessionGambleResult?)null, ""), (result.Gate.Refusal, result.Bet, result.Text));
        Assert.AreEqual((revision, roll, gold), (session.Version.Revision, session.QueryGamblingRoll(), session.QueryGold()));
        Assert.IsFalse(barter.Gamble("kit:1").Gate.Allowed, "the hero's own item goes back");
    }

    [TestMethod]
    public void The_open_throws_the_first_roll_once()
    {
        using var session = Session(5);
        var barter = Barter(session, [], MerchantSkill(3, 0));
        Assert.IsNull(session.QueryGamblingRoll());
        barter.Open();
        var roll = session.QueryGamblingRoll();
        Assert.IsTrue(roll is >= 1 and <= 100);
        barter.Open();
        Assert.AreEqual(roll, session.QueryGamblingRoll(), "a second open keeps the roll");
    }
}
