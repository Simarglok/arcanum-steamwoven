using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero steals and plants (ARC-SKILLS-010, ledger PP2–PP8) over the synthetic seed of InventoryStateTests (virgil alive with a ring,
// a sword, a worn vest and a ring copy; the merchant with 5 coins; a dead corpse). Entities and numbers are invented.
[TestClass]
public sealed class PickPocketStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionInsertCheck Refuse = _ => SessionInsertVerdict.NoRoom;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind NoStack = default;

    // Dexterity 20 (pick pocket's key stat), pick-pocket points and training as asked, the synthetic seed attached.
    private static GameSessionOwner Thief(int points, SkillTraining training, ulong seed = 0, Func<SessionItemFacts, SessionItemFacts>? vest = null)
    {
        var stats = Defaults(); stats[2] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.PickPocket] = points | (int)training << 6;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var created = GameSessionOwner.Create(Descriptor() with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        var owner = created.Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, vest is null ? Seed() : Seed() with { Items = [.. Seed().Items.Select(item => item.Entity == "vest" ? item with { Facts = vest(item.Facts) } : item)] }).Failure);
        return owner;
    }

    private static SessionPickPocketVictim Victim(string npc = "virgil", int perception = 0, int alignment = 0) =>
        new(npc) { Perception = perception, Alignment = alignment };

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static SessionPickPocketResult Steal(GameSessionOwner owner, string item, SessionPickPocketVictim? victim = null, SessionInsertCheck? check = null,
        SessionItemKind? stack = null) =>
        owner.PickPocket(owner.Version, victim ?? Victim(), PickPocketDirection.Steal, item, stack ?? NoStack, check ?? Accept);

    [TestMethod]
    public void AMasterStealsAnItemInOneRevisionAndATheftFromAGoodVictimLowersTheAlignment()
    {
        using var owner = Thief(20, SkillTraining.Master);
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var result = Steal(owner, "sword", Victim(alignment: 200));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true, 1), (result.Disposition, result.Failure, result.Check!.Success, result.Moved));
        Assert.AreEqual((revision + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws), "the check, the move and the alignment: one revision");
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "sword"));
        CollectionAssert.AreEqual(new[] { "ring", "vest", "ring-copy" }, List(owner, "virgil"));
        Assert.AreEqual((-5, -5, false, (int?)null), (result.Alignment, owner.QueryHeroPartySide()!.Alignment, result.Noticed, result.PerceptionRoll));
        // A theft from a victim the hero summoned changes no alignment.
        var summoned = Steal(owner, "ring", Victim(alignment: 200) with { SummonedByHero = true });
        Assert.AreEqual((1, (int?)null, -5), (summoned.Moved, summoned.Alignment, owner.QueryHeroPartySide()!.Alignment));
    }

    // ARC-DEV-1338 (PP3, PP12): the pick-pocket case refuses no worn item (its slot only raises the difficulty); the move is the transfer 0x4617F0 → 0x461CA0, whose removal 0x466E00 → 0x467860 takes a worn
    // item off its slot and marks an NPC giver to re-wield (WL3, NT3). A fated theft (a critical success) keeps the tests off the armour slot's higher difficulty.
    private static void ArmFate(GameSessionOwner owner)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GrantHeroFatePoint(owner.Version).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArmHeroFate(owner.Version, FateKind.PickPocket).Disposition);
    }

    [TestMethod]
    public void AMasterStealsAWornItemOffTheVictimAndTheInsertCheckSeesItUnwielded()
    {
        using var owner = Thief(20, SkillTraining.Master);
        ArmFate(owner);
        var revision = owner.Version.Revision;
        SessionInventoryItem? seen = null;
        var result = Steal(owner, "vest", check: item => { seen = item; return SessionInsertVerdict.Accepted; });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true, 1), (result.Disposition, result.Failure, result.Check!.Success, result.Moved));
        Assert.AreEqual(-1, seen!.Facts.InventoryLocation, "the insert check sees the item unwielded");
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "vest"));
        CollectionAssert.AreEqual(new[] { "ring", "sword", "ring-copy" }, List(owner, "virgil"));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the check, the move and the alignment: one revision");
    }

    [TestMethod]
    public void ARefusedInsertOfAStolenWornItemLeavesItWornAndUnnoticed()
    {
        using var owner = Thief(20, SkillTraining.Master);
        ArmFate(owner);
        var result = Steal(owner, "vest", check: Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, true, 0, false, GameSessionFailure.InsertRefused), (result.Disposition, result.Check!.Success, result.Moved, result.Noticed, result.Move!.Value.Failure));
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "vest"));
        var vest = owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "virgil").Items.Single(item => item.Entity == "vest");
        Assert.AreEqual(1006, vest.Facts.InventoryLocation, "it stays on the slot");
        Assert.IsTrue(vest.Facts.Worn);
    }

    // A worn no-drop item fails at the move's remove check (quiet, as any refused move); a worn item with a wield-off script and no script host refuses the attempt after the charge, nothing moved or noticed.
    [TestMethod]
    public void AWornNoDropVestIsAQuietFailureAndOneWithAWieldOffScriptNeedsAHost()
    {
        using var sticky = Thief(20, SkillTraining.Master, vest: facts => facts with { NoDrop = true });
        ArmFate(sticky);
        var failed = Steal(sticky, "vest");
        Assert.AreEqual((GameSessionDisposition.Changed, true, 0, false, GameSessionFailure.ItemNotDroppable), (failed.Disposition, failed.Check!.Success, failed.Moved, failed.Noticed, failed.Move!.Value.Failure));
        CollectionAssert.Contains(List(sticky, "virgil"), "vest");

        using var scripted = Thief(20, SkillTraining.Master, vest: facts => facts with { ScriptPoints = [SessionScriptPoint.WieldOff] });
        ArmFate(scripted);
        var refused = Steal(scripted, "vest");
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        CollectionAssert.Contains(List(scripted, "virgil"), "vest");
        Assert.IsFalse(scripted.QueryPcInventory().Any(item => item.Entity == "vest"));
    }

    [TestMethod]
    public void AStealMovesAtMostAHundredCoinsAndTwentyUnits()
    {
        using var owner = Thief(20, SkillTraining.Master);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpc(owner.Version, "virgil", 150, GoldPile, Accept).Disposition);
        var pile = List(owner, "virgil").Last();
        var gold = owner.QueryGold();
        Assert.AreEqual(100, Steal(owner, pile).Moved);
        Assert.AreEqual((gold + 100, 50), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "the rest of the pile stays");
        Assert.AreEqual(50, Steal(owner, pile).Moved);
        Assert.AreEqual((gold + 150, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")));

        var stack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, Steal(owner, stack).Failure, "ammunition needs the kind of a new stack");
        Assert.AreEqual(20, Steal(owner, stack, stack: Bullets).Moved);
        Assert.AreEqual((10, 20), (owner.QueryAmmo(stack)!.Value.Units, owner.QueryHeroAmmo().Single().Units));
        // A plant moves units back the same way.
        var mine = owner.QueryHeroAmmo().Single().Entity;
        var planted = owner.PickPocket(owner.Version, Victim(), PickPocketDirection.Plant, mine, Bullets, Accept);
        Assert.AreEqual((20, (int?)null), (planted.Moved, planted.Alignment), "a plant changes no alignment");
        Assert.AreEqual(30, owner.QueryAmmo(stack)!.Value.Units);
    }

    [TestMethod]
    public void AHeroItemIsPlantedOnTheVictim()
    {
        using var owner = Thief(20, SkillTraining.Master);
        var result = owner.PickPocket(owner.Version, Victim(), PickPocketDirection.Plant, "kit:1", NoStack, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, 1, false), (result.Disposition, result.Moved, result.Noticed));
        Assert.AreEqual("kit:1", List(owner, "virgil").Last());
        Assert.IsFalse(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"));
    }

    [TestMethod]
    public void AnUntrainedFailureFacesThePerceptionDieAndACriticalFailureIsAlwaysNoticed()
    {
        bool rolled = false, critical = false;
        for (ulong seed = 0; seed < 400 && !(rolled && critical); seed++)
        {
            using var owner = Thief(0, SkillTraining.None, seed);
            var draws = owner.QueryRandom().Draws;
            var result = Steal(owner, "sword", Victim(perception: 10));
            Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, "a failure still spends the draws and a revision");
            if (result.Check!.Success) continue;
            Assert.AreEqual((0, (int?)null), (result.Moved, result.Alignment));
            CollectionAssert.Contains(List(owner, "virgil"), "sword");
            if (result.Check.Critical)
            {
                Assert.IsTrue(result.Noticed && result.PerceptionRoll is null && owner.QueryRandom().Draws == draws + 2);
                critical = true;
            }
            else
            {
                Assert.AreEqual((true, draws + 3), (result.PerceptionRoll is not null, owner.QueryRandom().Draws));
                Assert.AreEqual(result.PerceptionRoll <= 10, result.Noticed);
                rolled = true;
            }
        }
        Assert.IsTrue(rolled && critical, "both failures within 400 seeds");
    }

    [TestMethod]
    public void ATrainedThiefsPlainFailureAndAMastersFailedPlantAreNeverNoticed()
    {
        for (ulong seed = 0; seed < 400; seed++)
        {
            // An apprentice of level 0 against perception 20 rarely succeeds.
            using var owner = Thief(0, SkillTraining.Apprentice, seed);
            var result = Steal(owner, "sword", Victim(perception: 20));
            if (result.Check!.Success || result.Check.Critical) continue;
            Assert.AreEqual((false, (int?)null), (result.Noticed, result.PerceptionRoll));
            break;
        }
        for (ulong seed = 0; seed < 400; seed++)
        {
            using var owner = Thief(0, SkillTraining.Master, seed);
            var result = owner.PickPocket(owner.Version, Victim(perception: 20), PickPocketDirection.Plant, "kit:1", NoStack, Accept);
            if (result.Check!.Success || !result.Check.Critical) continue;
            Assert.IsFalse(result.Noticed, "a master planting is never noticed, even on a critical failure");
            return;
        }
        Assert.Fail("no critical failure of a master's plant in 400 seeds");
    }

    [TestMethod]
    public void AnArmedFateForcesACriticalSuccessAndIsUsed()
    {
        using var owner = Thief(0, SkillTraining.None);
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.PickPocket);
        var result = Steal(owner, "sword", Victim(perception: 20));
        Assert.IsTrue(result.FateUsed && result.Check!.Success && result.Check.Critical && !result.Noticed);
        Assert.AreEqual(1, result.Moved);
        Assert.IsFalse(owner.HeroFateArmed(FateKind.PickPocket));
    }

    [TestMethod]
    public void AMoveTheInsertionRefusesIsAQuietFailure()
    {
        using var owner = Thief(20, SkillTraining.Master);
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var result = Steal(owner, "sword", Victim(alignment: 200), Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, true, 0, false, (int?)null), (result.Disposition, result.Check!.Success, result.Moved, result.Noticed, result.Alignment));
        Assert.AreEqual((GameSessionFailure.InsertRefused, revision + 1, draws + 2), (result.Move!.Value.Failure, owner.Version.Revision, owner.QueryRandom().Draws));
        CollectionAssert.Contains(List(owner, "virgil"), "sword");
    }

    [TestMethod]
    public void InTheHerosSubturnFourActionPointsArePaidFirst()
    {
        using var owner = Thief(20, SkillTraining.Master);
        var world = new World();
        Assert.AreEqual(GameSessionFailure.None, owner.StartCombatRound(owner.Version, world).Failure);
        for (var i = 0; i < 4 && owner.QueryCombatRound()!.Actor != SessionCombatant.Hero; i++)
            owner.EndCombatTurn(owner.Version, owner.QueryCombatRound()!.Actor, world);
        var points = owner.QueryCombatRound()!.ActionPoints;
        var result = owner.PickPocket(owner.Version, Victim(), PickPocketDirection.Steal, "sword", NoStack, Accept, world);
        Assert.AreEqual((SessionActionPointUse.Paid, points - TurnBasedRules.UseItemCost, 1), (result.Charge!.Charge, owner.QueryCombatRound()!.ActionPoints, result.Moved));
        // Another's subturn refuses the attempt before any charge or roll (the goal needs the hero's turn, PP2).
        owner.EndCombatTurn(owner.Version, SessionCombatant.Hero, world);
        Assert.AreNotEqual(SessionCombatant.Hero, owner.QueryCombatRound()!.Actor);
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var outOfTurn = owner.PickPocket(owner.Version, Victim(), PickPocketDirection.Steal, "ring", NoStack, Accept, world);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.PickPocketOutOfTurn, revision, draws),
            (outOfTurn.Disposition, outOfTurn.Failure, owner.Version.Revision, owner.QueryRandom().Draws));
    }

    [TestMethod]
    public void RefusalsComeBeforeAnyRollOrCharge()
    {
        using var owner = Thief(20, SkillTraining.Master);
        var draws = owner.QueryRandom().Draws;
        void Refused(SessionPickPocketResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
        }
        Refused(Steal(owner, "note", Victim("corpse")), GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal(owner, "sword", Victim("chest")), GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal(owner, "sword", Victim("nobody")), GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal(owner, "sword", Victim(perception: 1001)), GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal(owner, "sword", Victim(alignment: -1001)), GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal(owner, "sword", Victim() with { Conditions = (SkillTargetConditions)64 }), GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal(owner, "cup"), GameSessionFailure.ItemNotAvailable);
        Refused(owner.PickPocket(owner.Version, Victim(), PickPocketDirection.Plant, "sword", NoStack, Accept), GameSessionFailure.ItemNotAvailable);
        Refused(owner.PickPocket(new GameSessionVersion(owner.Identity, 7), Victim(), PickPocketDirection.Steal, "sword", NoStack, Accept), GameSessionFailure.StaleVersion);
        using var nobody = InventoryStateTests.Attached();
        Assert.AreEqual(GameSessionFailure.PickPocketUnavailable, Steal(nobody, "sword").Failure, "no hero");
    }

    // The hero and one wolf on the exterior, for a turn-based round.
    private sealed class World : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) =>
            [new(SessionCombatant.Hero, new(0, 0)), new(SessionCombatant.Critter("oid:wolf"), new(105, 200))];

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) =>
            new() { Speed = 7, Life = new LifeFacts() };
    }
}
