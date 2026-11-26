using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero picks locks (ARC-SKILLS-006, ledger PL1–PL6). The door, the lockpicks and their values are invented.
[TestClass]
public sealed class PickLockStateTests
{
    private const string Picks = "kit:4";
    private static readonly SessionOpenable Door = new(Exterior, "door:1");
    private static readonly SessionOpenableBaseline LockedDoor = new(OpenableKind.Portal, LockRules.LockedBit, 0, 0);
    private static readonly ItemCondition Sound = new(10, 0, 0, false);

    private static SessionLockpick Lockpick(int bonus = 0, uint flags = SessionLockpick.LockpickFlag, int[]? scripts = null) =>
        new(Picks, flags, bonus, Sound, scripts ?? []);

    private static SessionPickLockTarget Target(SessionOpenableBaseline? baseline = null, int difficulty = 0, int light = SessionPickLockTarget.FullLight) =>
        new(Door, baseline ?? LockedDoor, difficulty, light);

    // Dexterity 20 (pick locks' key stat), pick-locks points and training as asked; the kit's fourth item is the lockpicks.
    private static GameSessionOwner Picker(int points, SkillTraining training, ulong seed = 0)
    {
        var stats = Defaults(); stats[2] = 20;
        int[] tech = new int[4];
        tech[2] = points | (int)training << 6;
        var hero = HalfElf(stats) with
        {
            Kit = [.. Kit, new(new(SessionItemType.Generic, 15200, 6000, 6001))],
            Advancement = SessionHeroAdvancement.None with { TechSkills = [.. tech] },
        };
        var created = GameSessionOwner.Create(Descriptor() with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    private static int LockEvents(GameSessionOwner owner) => owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.LockReset);

    [TestMethod]
    public void AMasterUnlocksALockedDoorInOneRevisionAndItRelocksAnHourLater()
    {
        using var owner = Picker(5, SkillTraining.Master);
        var before = owner.Version.Revision;
        var draws = owner.QueryRandom().Draws;
        var result = owner.PickLock(owner.Version, Target(difficulty: 20), Lockpick(bonus: 30));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.IsTrue(result.Check!.Success);
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Unlocked, true, false), result.Outcome);
        Assert.AreEqual((false, before + 1, draws + 2), (result.State!.Value.Locked, owner.Version.Revision, owner.QueryRandom().Draws));
        var relock = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.LockReset);
        Assert.AreEqual(LockRules.UnlockResetDelayMs, relock.DeadlineMs - owner.QueryClockSnapshot().GameTimestampMs);
        // A success on the unlocked door locks it again; locking schedules nothing more.
        var locked = owner.PickLock(owner.Version, Target(difficulty: 20), Lockpick(bonus: 30));
        Assert.AreEqual((PickLockResult.Locked, false, true, 1), (locked.Outcome!.Value.Result, locked.Outcome.Value.WitnessesCalled, locked.State!.Value.Locked, LockEvents(owner)));
    }

    [TestMethod]
    public void AnUntrainedZeroLevelPickerFailsAndACriticalFailureJamsForADay()
    {
        for (ulong seed = 0; seed < 400; seed++)
        {
            using var owner = Picker(0, SkillTraining.None, seed);
            var result = owner.PickLock(owner.Version, Target(), Lockpick());
            Assert.IsFalse(result.Check!.Success);
            Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, "a failure still spends the draws and a revision");
            if (!result.Check.Critical)
            {
                Assert.AreEqual((PickLockResult.Failed, true, true, 0), (result.Outcome!.Value.Result, result.Outcome.Value.WitnessFailure, result.State!.Value.Locked, LockEvents(owner)));
                continue;
            }
            Assert.AreEqual(PickLockResult.Jammed, result.Outcome!.Value.Result);
            Assert.AreEqual(LockRules.JammedBit, result.State!.Value.Flags & LockRules.JammedBit);
            var relock = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.LockReset);
            Assert.AreEqual(LockRules.JamResetDelayMs, relock.DeadlineMs - owner.QueryClockSnapshot().GameTimestampMs);
            Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == Picks), "lockpicks are never used up");
            return;
        }
        Assert.Fail("no critical failure in 400 seeds");
    }

    [TestMethod]
    public void AnArmedFateMakesACriticalSuccessWithoutLockpicksAndIsUsed()
    {
        using var owner = Picker(0, SkillTraining.None);
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.PickLocks);
        var result = owner.PickLock(owner.Version, Target(difficulty: 50), null);
        Assert.IsTrue(result.FateUsed && result.Check!.Success && result.Check.Critical);
        Assert.AreEqual(PickLockResult.Unlocked, result.Outcome!.Value.Result);
        Assert.IsFalse(owner.HeroFateArmed(FateKind.PickLocks));
    }

    [TestMethod]
    public void AJunkPileHasNoLockToWrite()
    {
        using var owner = Picker(5, SkillTraining.Master);
        var junk = new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0, JunkPile: true);
        var before = owner.Version.Revision;
        var result = owner.PickLock(owner.Version, Target(junk), Lockpick());
        Assert.AreEqual((PickLockResult.Locked, false, before + 1), (result.Outcome!.Value.Result, result.State!.Value.Locked, owner.Version.Revision),
            "the check's draws still take a revision");
        Assert.IsNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity(Door.Entity, GameSessionOwner.FlagsProperty)));
    }

    [TestMethod]
    public void RefusalsComeBeforeAnyRollOrCharge()
    {
        using var owner = Picker(5, SkillTraining.Master);
        var draws = owner.QueryRandom().Draws;
        void Refused(SessionPickLockResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
        }
        Refused(owner.PickLock(owner.Version, Target(), Lockpick(flags: SessionHealingItem.HealingFlag)), GameSessionFailure.InvalidLockpick);
        Refused(owner.PickLock(owner.Version, Target(), Lockpick(bonus: 1001)), GameSessionFailure.InvalidLockpick);
        Refused(owner.PickLock(owner.Version, Target(), Lockpick(scripts: [SessionLockpick.UseScript])), GameSessionFailure.ItemScriptUnsupported);
        Refused(owner.PickLock(owner.Version, Target(), Lockpick() with { Entity = "kit:9" }), GameSessionFailure.ItemNotAvailable);
        Refused(owner.PickLock(owner.Version, Target(difficulty: 1001), Lockpick()), GameSessionFailure.InvalidOpenable);
        Refused(owner.PickLock(owner.Version, Target(light: 256), Lockpick()), GameSessionFailure.InvalidOpenable);
        Refused(owner.PickLock(new(owner.Identity, 5), Target(), Lockpick()), GameSessionFailure.StaleVersion);
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.PickLockUnavailable, heroless.PickLock(heroless.Version, Target(), Lockpick()).Failure);
        using var dead = Picker(5, SkillTraining.Master);
        dead.Kill(dead.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(GameSessionFailure.PickLockUnavailable, dead.PickLock(dead.Version, Target(), Lockpick()).Failure);
    }

    // The hero and one wolf on the exterior, for a turn-based round.
    private sealed class World : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) =>
            [new(SessionCombatant.Hero, new(0, 0)), new(SessionCombatant.Critter("oid:wolf"), new(105, 200))];

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => new() { Speed = 7, Life = new LifeFacts() };
    }

    [TestMethod]
    public void InTheHerosSubturnLockpicksCostFourActionPointsAndNoLockpicksNothing()
    {
        using var owner = Picker(5, SkillTraining.Master);
        var world = new World();
        Assert.AreEqual(GameSessionFailure.None, owner.StartCombatRound(owner.Version, world).Failure);
        for (var i = 0; i < 4 && owner.QueryCombatRound()!.Actor != SessionCombatant.Hero; i++)
            owner.EndCombatTurn(owner.Version, owner.QueryCombatRound()!.Actor, world);
        var points = owner.QueryCombatRound()!.ActionPoints;
        var before = owner.Version.Revision;
        var result = owner.PickLock(owner.Version, Target(), Lockpick(), world);
        Assert.AreEqual((SessionActionPointUse.Paid, points - TurnBasedRules.UseItemCost, before + 2),
            (result.Charge!.Charge, owner.QueryCombatRound()!.ActionPoints, owner.Version.Revision));
        Assert.IsNull(owner.PickLock(owner.Version, Target(), null, world).Charge, "without the item there is no item use to charge");
        Assert.AreEqual(points - TurnBasedRules.UseItemCost, owner.QueryCombatRound()!.ActionPoints);
    }

    [TestMethod]
    public void AFullLockEventShareIsRefusedBeforeTheCharge()
    {
        using var owner = Picker(5, SkillTraining.Master);
        // Every Lock event the queue keeps, each from unlocking another locked door.
        for (var i = 0; i < SessionEventKinds.MaxLockResets; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetOpenableLocked(owner.Version, new(Exterior, $"door:x{i}"), LockedDoor, false).Disposition);
        var world = new World();
        Assert.AreEqual(GameSessionFailure.None, owner.StartCombatRound(owner.Version, world).Failure);
        for (var i = 0; i < 4 && owner.QueryCombatRound()!.Actor != SessionCombatant.Hero; i++)
            owner.EndCombatTurn(owner.Version, owner.QueryCombatRound()!.Actor, world);
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryCombatRound()!.Actor, "the precheck runs only in the hero's subturn");
        var (points, revision, draws) = (owner.QueryCombatRound()!.ActionPoints, owner.Version.Revision, owner.QueryRandom().Draws);
        var refused = owner.PickLock(owner.Version, Target(), Lockpick(), world);
        Assert.AreEqual((GameSessionFailure.EventBudgetExceeded, (SessionCombatResult?)null), (refused.Failure, refused.Charge));
        Assert.AreEqual((points, revision, draws), (owner.QueryCombatRound()!.ActionPoints, owner.Version.Revision, owner.QueryRandom().Draws), "nothing charged or drawn");
    }
}
