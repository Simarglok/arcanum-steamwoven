using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog $$ code over the session (ARC-DEV-179, ledger DCC5, DAC2, GM1–GM4) and the two moves it needs: coins from nothing to the hero and
// coins from one NPC's pile to another's. Over the synthetic seed of InventoryStateTests (virgil with no coins, the merchant with a pile of 5)
// and the half-elf hero (385 gold). Entities and numbers are invented.
[TestClass]
public sealed class DialogGoldPortTests
{
    private static readonly DialogParticipantId Pc = new(1), Npc = new(2);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionDialogGoldPort Port(GameSessionOwner owner, bool original, string? npc = "virgil", Action<string>? prepare = null) =>
        new(owner, Pc, participant => participant == Npc ? npc : null, GoldPile, Accept, original, prepare);

    private static void Changed(InventoryMutationResult result)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
    }

    // The hero keeps `coins` and gives the rest to virgil, who is the talking NPC and holds it in a new pile.
    private static void HeroKeeps(GameSessionOwner owner, int coins) => Changed(owner.GiveGoldToNpc(owner.Version, "virgil", owner.QueryGold() - coins, GoldPile, Accept));

    [TestMethod]
    public void GiveGoldToHeroAddsCoinsFromNothing()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        Changed(owner.GiveGoldToHero(owner.Version, 15));
        Assert.AreEqual((400, revision + 1), (owner.QueryGold(), owner.Version.Revision));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.GiveGoldToHero(owner.Version, 0).Disposition);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.GiveGoldToHero(owner.Version, -1).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.GiveGoldToHero(new(owner.Identity, 99), 1).Failure);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.GiveGoldToHero(owner.Version, int.MaxValue).Failure, "a sum past 32 bits");
        Assert.AreEqual(400, owner.QueryGold());
        using var heroless = Attached();
        Assert.AreEqual(GameSessionFailure.GoldUnavailable, heroless.GiveGoldToHero(heroless.Version, 1).Failure);
    }

    [TestMethod]
    public void CoinsMoveBetweenNpcsInPartAllOrNothingAndAnEmptiedPileIsDestroyed()
    {
        using var owner = Hero();
        // virgil has no pile: a new one is made after the host's insert check.
        var checkedItems = new List<SessionInventoryItem>();
        Changed(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 2, GoldPile, item => { checkedItems.Add(item); return SessionInsertVerdict.Accepted; }));
        Assert.AreEqual((3, 2, 2), (owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil"), checkedItems.Single().Facts.Quantity));
        Changed(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 1, GoldPile, _ => SessionInsertVerdict.NoRoom));
        Assert.AreEqual((2, 3), (owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil")), "a pile grows with no insert check");
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 3, GoldPile, Accept).Failure, "never part of what was asked");
        Assert.AreEqual((revision, 2, 3), (owner.Version.Revision, owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil")));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 0, GoldPile, Accept).Disposition);
        Changed(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 2, GoldPile, Accept));
        Assert.AreEqual((0, 5), (owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil")));
        Assert.IsTrue(owner.IsItemDestroyed("coins"), "exactly the count destroys the pile");
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 1, GoldPile, Accept).Failure, "no pile at all");
    }

    [TestMethod]
    public void NpcToNpcMovesAreRefusedBeforeAnyEffect()
    {
        using var owner = Hero(new GameSessionLimits(maxGoldRecords: 1));
        void Refused(InventoryMutationResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 5, 0), (owner.Version.Revision, owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil")));
        }
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "merchant", 1, GoldPile, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", -1, GoldPile, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "made:1", 1, GoldPile, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 1, GoldPile with { Type = SessionItemType.Generic }, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.MoveGoldBetweenNpcs(new(owner.Identity, 7), "merchant", "virgil", 1, GoldPile, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "ghost", "virgil", 1, GoldPile, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 6, GoldPile, Accept), GameSessionFailure.GoldShort);
        // Past the budget of one record: the source's changed pile and the new pile would need two.
        Refused(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 1, GoldPile, Accept), GameSessionFailure.BudgetExceeded);
        using var roomy = Hero();
        var result = roomy.MoveGoldBetweenNpcs(roomy.Version, "merchant", "virgil", 1, GoldPile, _ => SessionInsertVerdict.NoRoom);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused), (result.Disposition, result.Failure));
        Assert.AreEqual((0L, 5, 0), (roomy.Version.Revision, roomy.QueryNpcGold("merchant"), roomy.QueryNpcGold("virgil")), "a refused new pile moves nothing");
    }

    [TestMethod]
    public void TheTotalIsTheHerosGoldPlusEachFollowersAndTheConditionSeesItWrapped()
    {
        using var owner = Hero();
        var port = Port(owner, original: true);
        Assert.AreEqual(385, port.Total(new DialogGoldQuery(Pc)));
        Assert.AreEqual(0, port.Total(new DialogGoldQuery(new DialogParticipantId(7))), "another PC");
        var prepared = new List<string>();
        var counted = Port(owner, original: true, prepare: prepared.Add);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "merchant").Disposition);
        Assert.AreEqual(390, counted.Total(new DialogGoldQuery(Pc)), "the merchant's pile of 5 counts as a follower's");
        CollectionAssert.AreEqual(new[] { "merchant" }, prepared, "each follower is prepared before its gold is read");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.AreEqual(390, counted.Total(new DialogGoldQuery(Pc)), "a follower without coins adds nothing");
    }

    [TestMethod]
    public void APositiveAmountGivesTheHeroCoinsAndZeroChangesNothing()
    {
        using var owner = Hero();
        var port = Port(owner, original: true);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GoldCommand(Pc, Npc, 40)));
        Assert.AreEqual((425, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "from nothing: the NPC pays nothing");
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GoldCommand(Pc, Npc, 0)));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new GoldCommand(new DialogParticipantId(7), Npc, 1)));
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, true, npc: null).Execute(new GoldCommand(Pc, Npc, -1)), "an NPC the host does not map");
        using var heroless = Attached();
        Assert.AreEqual(DialogCommandResult.Rejected, Port(heroless, true).Execute(new GoldCommand(Pc, Npc, 5)));
    }

    [TestMethod]
    public void ANegativeAmountIsPaidByTheHeroAloneWhenItHoldsEnough()
    {
        foreach (var original in new[] { true, false })
        {
            using var owner = Hero();
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "merchant").Disposition);
            Assert.AreEqual(DialogCommandResult.Applied, Port(owner, original).Execute(new GoldCommand(Pc, Npc, -100)));
            Assert.AreEqual((285, 100, 5), (owner.QueryGold(), owner.QueryNpcGold("virgil"), owner.QueryNpcGold("merchant")), $"original {original}: the follower pays nothing");
        }
    }

    [TestMethod]
    public void AShortHeroLetsAFollowerPayTheRestAndTheOriginalKeepsTheHerosCoinsBack()
    {
        // The hero keeps 2, the merchant (a follower) holds 5, virgil (the talking NPC) holds the rest of the hero's coins; the debt is 6.
        using var original = Hero();
        HeroKeeps(original, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, original.ForceJoinParty(original.Version, "merchant").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(original, original: true).Execute(new GoldCommand(Pc, Npc, -6)));
        // GM3: the hero's call is refused (2 < 6) yet the debt falls to 4; the merchant holds 5 >= 4 and pays 4: the NPC got 4 of 6.
        Assert.AreEqual((2, 383 + 4, 1), (original.QueryGold(), original.QueryNpcGold("virgil"), original.QueryNpcGold("merchant")));

        using var steamwoven = Hero();
        HeroKeeps(steamwoven, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, steamwoven.ForceJoinParty(steamwoven.Version, "merchant").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(steamwoven, original: false).Execute(new GoldCommand(Pc, Npc, -6)));
        // The hero pays its 2, the merchant the 4 left: the NPC got all 6.
        Assert.AreEqual((0, 383 + 6, 1), (steamwoven.QueryGold(), steamwoven.QueryNpcGold("virgil"), steamwoven.QueryNpcGold("merchant")));
    }

    [TestMethod]
    public void AFollowerThatCannotCoverWhatIsLeftPaysNothingInTheOriginalAndAllItHasOtherwise()
    {
        // Hero keeps 2, merchant holds 5, debt 9: 7 is owed after the hero, the merchant has less.
        using var original = Hero();
        HeroKeeps(original, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, original.ForceJoinParty(original.Version, "merchant").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(original, original: true).Execute(new GoldCommand(Pc, Npc, -9)));
        Assert.AreEqual((2, 383, 5), (original.QueryGold(), original.QueryNpcGold("virgil"), original.QueryNpcGold("merchant")), "nobody pays");

        using var steamwoven = Hero();
        HeroKeeps(steamwoven, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, steamwoven.ForceJoinParty(steamwoven.Version, "merchant").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(steamwoven, original: false).Execute(new GoldCommand(Pc, Npc, -9)));
        Assert.AreEqual((0, 383 + 7, 0), (steamwoven.QueryGold(), steamwoven.QueryNpcGold("virgil"), steamwoven.QueryNpcGold("merchant")), "all they have");
    }

    [TestMethod]
    public void CoinsFromNothingGrowOrMakeAnNpcsPileAndTheQueryTellsAPileFromNone()
    {
        using var owner = Hero();
        Assert.IsTrue(owner.QueryNpcHasGoldPile("merchant"));
        Assert.IsFalse(owner.QueryNpcHasGoldPile("virgil"));
        Assert.IsFalse(owner.QueryNpcHasGoldPile("ghost"));
        Changed(owner.GiveGoldToNpcFromNothing(owner.Version, "merchant", 4, GoldPile, _ => SessionInsertVerdict.NoRoom));
        Assert.AreEqual((9, 385), (owner.QueryNpcGold("merchant"), owner.QueryGold()), "a pile grows with no insert check and no coins leave the hero");
        Changed(owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 7, GoldPile, Accept));
        Assert.AreEqual((7, 385, true), (owner.QueryNpcGold("virgil"), owner.QueryGold(), owner.QueryNpcHasGoldPile("virgil")));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 0, GoldPile, Accept).Disposition);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", -1, GoldPile, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.GiveGoldToNpcFromNothing(owner.Version, "ghost", 1, GoldPile, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", int.MaxValue, GoldPile, Accept).Failure, "a sum past 32 bits");
        Assert.AreEqual(7, owner.QueryNpcGold("virgil"));
    }

    [TestMethod]
    public void APayerWithoutAGoldStackCreditsTheNpcFromNothingInTheOriginalAndPaysNothingInSteamwoven()
    {
        // The hero has no gold at all (no stack); the merchant, a follower, holds 5; virgil talks and keeps the hero's 385; the debt is 100.
        // GM2: the hero's call is not refused (no stack to be short), it credits virgil 100 from nothing, and the debt is still 100 for the merchant,
        // whose 5 is less than that, so it pays nothing.
        using var original = Hero();
        HeroKeeps(original, 0);
        Assert.AreEqual(GameSessionDisposition.Changed, original.ForceJoinParty(original.Version, "merchant").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(original, original: true).Execute(new GoldCommand(Pc, Npc, -100)));
        Assert.AreEqual((0, 485, 5), (original.QueryGold(), original.QueryNpcGold("virgil"), original.QueryNpcGold("merchant")));

        using var steamwoven = Hero();
        HeroKeeps(steamwoven, 0);
        Assert.AreEqual(GameSessionDisposition.Changed, steamwoven.ForceJoinParty(steamwoven.Version, "merchant").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(steamwoven, original: false).Execute(new GoldCommand(Pc, Npc, -100)));
        Assert.AreEqual((0, 390, 0), (steamwoven.QueryGold(), steamwoven.QueryNpcGold("virgil"), steamwoven.QueryNpcGold("merchant")), "the merchant pays all 5");
    }

    [TestMethod]
    public void AFollowerWithoutAPileAlsoCreditsTheNpcFromNothingInTheOriginal()
    {
        // The merchant talks (5 coins), virgil follows with no pile, the hero keeps 2; the debt is 6. The hero (a stack, 2 < 6) pays nothing, 4 is
        // left, and virgil (no stack) credits the merchant 4 from nothing.
        using var original = Hero();
        Changed(original.GiveGoldToNpc(original.Version, "virgil", 383, GoldPile, Accept));
        Assert.AreEqual(GameSessionDisposition.Changed, original.ForceJoinParty(original.Version, "virgil").Disposition);
        Changed(original.MoveGoldBetweenNpcs(original.Version, "virgil", "merchant", 383, GoldPile, Accept));
        Assert.IsFalse(original.QueryNpcHasGoldPile("virgil"), "paying out the last coin leaves no stack (AM5)");
        Assert.AreEqual(DialogCommandResult.Applied, Port(original, original: true, npc: "merchant").Execute(new GoldCommand(Pc, Npc, -6)));
        Assert.AreEqual((2, 388 + 4, 0), (original.QueryGold(), original.QueryNpcGold("merchant"), original.QueryNpcGold("virgil")));

        using var steamwoven = Hero();
        Changed(steamwoven.GiveGoldToNpc(steamwoven.Version, "virgil", 383, GoldPile, Accept));
        Assert.AreEqual(GameSessionDisposition.Changed, steamwoven.ForceJoinParty(steamwoven.Version, "virgil").Disposition);
        Changed(steamwoven.MoveGoldBetweenNpcs(steamwoven.Version, "virgil", "merchant", 383, GoldPile, Accept));
        Assert.AreEqual(DialogCommandResult.Applied, Port(steamwoven, original: false, npc: "merchant").Execute(new GoldCommand(Pc, Npc, -6)));
        Assert.AreEqual((0, 388 + 2, 0), (steamwoven.QueryGold(), steamwoven.QueryNpcGold("merchant"), steamwoven.QueryNpcGold("virgil")), "the hero pays its 2, nobody else has coins");
    }

    [TestMethod]
    public void TheTalkingNpcAsAFollowerPaysItselfNothing()
    {
        // virgil follows the PC and talks: its own gold counts toward the debt but moves nowhere.
        using var owner = Hero();
        HeroKeeps(owner, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, Port(owner, original: false).Execute(new GoldCommand(Pc, Npc, -6)));
        Assert.AreEqual((0, 383 + 2), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "the hero paid 2; virgil's own 383 covers the rest on paper");
    }

    [TestMethod]
    public void ARefusedPaymentRejectsTheCommand()
    {
        using var owner = Hero(new GameSessionLimits(maxGoldRecords: 1));
        // The merchant's pile takes the only record; a new pile for virgil is over the budget.
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 1));
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, original: true).Execute(new GoldCommand(Pc, Npc, -10)));
        Assert.AreEqual((386, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "nothing moved after the one coin the hero took");
    }
}
