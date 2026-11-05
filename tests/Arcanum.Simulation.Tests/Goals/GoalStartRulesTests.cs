using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-131 (MIG13 S2): the start and replacement rules (ledger GS11..GS15) and the retarget in place (GS14) on synthetic runs.</summary>
[TestClass]
public sealed class GoalStartRulesTests
{
    private const string A = "npc:a";
    private const string B = "npc:b";

    private sealed class Ports : IGoalStartPorts
    {
        public bool Gate { get; set; } = true;
        public bool Loading { get; set; }
        public HashSet<string> Destroyed { get; } = [];
        public List<GoalStartRequest> Asked { get; } = [];
        public bool GatePasses(GoalStartRequest request) { Asked.Add(request); return Gate; }
        public bool IsDestroyed(string owner) => Destroyed.Contains(owner);
    }

    private static GoalFrame F(GoalType goal, string? target = null) => new(goal, target);

    private static GoalStartRequest Request(GoalStartPlan plan, GoalType goal, string owner = A, string? target = null, GoalRunFlags flags = GoalRunFlags.None) =>
        new(owner, plan, F(goal, target), flags, DueMs: 5, PaceMs: 100);

    private static GoalRunTable With(GoalRunTable table, string owner, GoalType goal, string? target = null)
    {
        var allocation = table.Allocate(owner, F(goal, target));
        Assert.IsNotNull(allocation.Handle);
        return allocation.Table;
    }

    private static GoalStartResult Start(GoalRunTable table, GoalStartRequest request, Ports? ports = null) => GoalStartRules.Start(table, request, ports ?? new Ports());

    private static int[] Levels(GoalStartPlan plan, GoalType goal) => [.. plan.LevelsFor(GoalDescriptors.Of(goal)!)];

    [TestMethod]
    public void EachStarterInterruptsAtTheLevelsOfGs12AndGs13()
    {
        // Level 3 for the walks, runs, follow, attack, throw and spread out.
        foreach (var (plan, goal) in new[] { (GoalStarters.Walk, GoalType.MoveToTile), (GoalStarters.Run, GoalType.RunToTile), (GoalStarters.Follow, GoalType.Follow),
            (GoalStarters.Attack, GoalType.Attack), (GoalStarters.Throw, GoalType.ThrowItem), (GoalStarters.SpreadOut, GoalType.SpreadOut) })
            CollectionAssert.AreEqual(new[] { 3 }, Levels(plan, goal), $"{plan.Kind}");
        // Stun 4 then its own priority 1, projectile 4 then 5, knock-down 5 then 3.
        CollectionAssert.AreEqual(new[] { 4, 1 }, Levels(GoalStarters.Stun, GoalType.AnimateStunned));
        CollectionAssert.AreEqual(new[] { 4, 5 }, Levels(GoalStarters.Projectile, GoalType.Projectile));
        CollectionAssert.AreEqual(new[] { 5, 3 }, Levels(GoalStarters.KnockDown, GoalType.KnockDown));
        // A generic start interrupts at the goal's own priority, 0 for a no-interrupt goal; the no-interrupt starts interrupt nothing.
        CollectionAssert.AreEqual(new[] { 3 }, Levels(GoalStarters.Generic, GoalType.Talk));
        CollectionAssert.AreEqual(new[] { 2 }, Levels(GoalStarters.Generic, GoalType.MoveToTile));
        CollectionAssert.AreEqual(new[] { 5 }, Levels(GoalStarters.Generic, GoalType.Dying));
        CollectionAssert.AreEqual(new[] { 0 }, Levels(GoalStarters.Generic, GoalType.EyeCandy));
        CollectionAssert.AreEqual(Array.Empty<int>(), Levels(GoalStarters.NoInterrupt, GoalType.CastSpellFriendly));
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalStarters.Walk.LevelsFor(null!));
        // A plan built with a default array of fixed levels has none, instead of failing.
        var defaulted = new GoalStartPlan(GoalStarterKind.Generic, default, true, false, false);
        CollectionAssert.AreEqual(new[] { 3 }, Levels(defaulted, GoalType.Talk));
        CollectionAssert.AreEqual(Array.Empty<int>(), Levels(defaulted with { InterruptsAtOwnPriority = false }, GoalType.Talk));
        // The duplicate refusal is the attack, throw, follow and spread-out starters'.
        CollectionAssert.AreEquivalent(new[] { GoalStarterKind.Follow, GoalStarterKind.Attack, GoalStarterKind.Throw, GoalStarterKind.SpreadOut },
            new[] { GoalStarters.Walk, GoalStarters.Run, GoalStarters.Follow, GoalStarters.Attack, GoalStarters.Throw, GoalStarters.SpreadOut, GoalStarters.Stun,
                GoalStarters.Projectile, GoalStarters.KnockDown, GoalStarters.Generic, GoalStarters.NoInterrupt }.Where(p => p.RefusesDuplicate).Select(p => p.Kind).ToArray());
        Assert.IsTrue(GoalStarters.GenericStartsAnyway.StartsOnRefusal);
        Assert.IsFalse(GoalStarters.Generic.StartsOnRefusal);
    }

    [TestMethod]
    public void AStartOnAnOwnerWithNoRunAllocatesTheRunWithTheRequestsValues()
    {
        var result = Start(GoalRunTable.Empty, Request(GoalStarters.Walk, GoalType.MoveToTile, flags: GoalRunFlags.Running));
        Assert.AreEqual(GoalStartOutcome.Started, result.Outcome);
        Assert.AreEqual(new GoalHandle(0, 1), result.Handle);
        Assert.AreEqual(0, result.Ended.Length);
        Assert.IsTrue(result.Table.TryGet(result.Handle!.Value, out var run));
        Assert.AreEqual(GoalType.MoveToTile, run.Bottom.Goal);
        Assert.AreEqual(A, run.Owner);
        Assert.AreEqual(GoalRunFlags.Running, run.Flags);
        Assert.AreEqual(5, run.DueMs);
        Assert.AreEqual(100, run.PaceMs);
    }

    [TestMethod]
    public void ThePriorityDecidesReplacementAWalkStarterReplacesAnAttackButAGenericWalkGoalCannot()
    {
        var running = With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe");
        // The walk starter's fixed level 3 ends the attack (priority 3) ...
        var walk = Start(running, Request(GoalStarters.Walk, GoalType.MoveToTile));
        Assert.AreEqual(GoalStartOutcome.Started, walk.Outcome);
        Assert.AreEqual(1, walk.Ended.Length);
        Assert.AreEqual(GoalType.Attack, walk.Ended[0].Run.Bottom.Goal);
        Assert.AreEqual(1, walk.Table.InUseCount);
        // ... but a generic start of the same walk goal interrupts at its own priority 2, which an attack refuses.
        var generic = Start(running, Request(GoalStarters.Generic, GoalType.MoveToTile));
        Assert.AreEqual(GoalStartOutcome.Interrupt, generic.Outcome);
        Assert.IsNull(generic.Handle);
        Assert.AreSame(running, generic.Table, "nothing ended");
        // An attack cannot replace a spell release (priority 4) at level 3; a knock-down (level 5) can.
        var casting = With(GoalRunTable.Empty, A, GoalType.AttemptSpell);
        Assert.AreEqual(GoalStartOutcome.Interrupt, Start(casting, Request(GoalStarters.Attack, GoalType.Attack, target: "npc:foe")).Outcome);
        var knock = Start(casting, Request(GoalStarters.KnockDown, GoalType.KnockDown));
        Assert.AreEqual(GoalStartOutcome.Started, knock.Outcome);
        Assert.AreEqual(GoalType.AttemptSpell, knock.Ended.Single().Run.Bottom.Goal);
    }

    [TestMethod]
    public void NothingButLevelSixStopsDyingAProjectileOrAKnockback()
    {
        foreach (var goal in new[] { GoalType.Dying, GoalType.Projectile, GoalType.Knockback })
        {
            var table = With(GoalRunTable.Empty, A, goal);
            foreach (var plan in new[] { GoalStarters.Walk, GoalStarters.Attack, GoalStarters.Stun, GoalStarters.Projectile, GoalStarters.KnockDown, GoalStarters.Generic })
            {
                var frame = plan.Kind == GoalStarterKind.Stun ? GoalType.AnimateStunned : plan.Kind == GoalStarterKind.KnockDown ? GoalType.KnockDown : GoalType.Attack;
                var result = Start(table, new(A, plan, F(frame, "npc:foe")));
                Assert.AreEqual(GoalStartOutcome.Interrupt, result.Outcome, $"{goal} refuses {plan.Kind}");
            }
        }
    }

    [TestMethod]
    public void TheStunEndsEveryOrdinaryRunAtLevelFourAndThenFindsNoneAtLevelOne()
    {
        var table = With(With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe"), A, GoalType.Fidget);
        var stun = Start(table, Request(GoalStarters.Stun, GoalType.AnimateStunned));
        Assert.AreEqual(GoalStartOutcome.Started, stun.Outcome);
        Assert.AreEqual(2, stun.Ended.Length, "level 4 ends both; level 1 then finds none");
        Assert.AreEqual(1, stun.Table.InUseCount);
    }

    [TestMethod]
    public void TheGateComesFirstAndNothingIsInterruptedWhenItRefuses()
    {
        var running = With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe");
        var ports = new Ports { Gate = false };
        var request = Request(GoalStarters.Attack, GoalType.Attack, target: "npc:foe");
        var result = Start(running, request, ports);
        Assert.AreEqual(GoalStartOutcome.Gate, result.Outcome, "the gate answers before the duplicate test");
        Assert.AreSame(running, result.Table);
        Assert.AreEqual(1, ports.Asked.Count);
        Assert.AreSame(request, ports.Asked[0]);
    }

    [TestMethod]
    public void ADuplicateIsRefusedBeforeAnythingIsInterruptedForTheFourStartersOnly()
    {
        var running = With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe");
        var same = Start(running, Request(GoalStarters.Attack, GoalType.Attack, target: "npc:foe"));
        Assert.AreEqual(GoalStartOutcome.Duplicate, same.Outcome);
        Assert.AreSame(running, same.Table);
        Assert.AreEqual(GoalStartOutcome.Duplicate, Start(running, Request(GoalStarters.Attack, GoalType.AttemptAttack, target: "npc:foe")).Outcome, "an alias is the same activity");
        var other = Start(running, Request(GoalStarters.Attack, GoalType.Attack, target: "npc:friend"));
        Assert.AreEqual(GoalStartOutcome.Started, other.Outcome, "another target replaces the attack");
        Assert.AreEqual(1, other.Ended.Length);
        // A walk started over a running walk is not a duplicate: it interrupts it (the in-place retarget is the starter's first step).
        var walking = With(GoalRunTable.Empty, A, GoalType.MoveToTile);
        Assert.AreEqual(GoalStartOutcome.Started, Start(walking, Request(GoalStarters.Walk, GoalType.MoveToTile)).Outcome);
        Assert.AreEqual(GoalStartOutcome.Duplicate, Start(With(GoalRunTable.Empty, A, GoalType.Follow, "npc:lead"), Request(GoalStarters.Follow, GoalType.Follow, target: "npc:lead")).Outcome);
        Assert.AreEqual(GoalStartOutcome.Duplicate, Start(With(GoalRunTable.Empty, A, GoalType.ThrowItem), Request(GoalStarters.Throw, GoalType.ThrowItem)).Outcome);
        Assert.AreEqual(GoalStartOutcome.Duplicate, Start(With(GoalRunTable.Empty, A, GoalType.RunNear), Request(GoalStarters.SpreadOut, GoalType.SpreadOut)).Outcome, "56 is in 69's list");
    }

    [TestMethod]
    public void ANoInterruptGoalStartsBesideTheMainRunOnlyThroughANoInterruptStarter()
    {
        var main = With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe");
        // Through 0x44D500 (nothing interrupted): the overlay starts beside the attack.
        var overlay = Start(main, Request(GoalStarters.NoInterrupt, GoalType.CastSpellFriendly));
        Assert.AreEqual(GoalStartOutcome.Started, overlay.Outcome);
        Assert.AreEqual(2, overlay.Table.InUseCount);
        Assert.AreEqual(0, overlay.Ended.Length);
        // Through 0x44D4E0 the level is 0, which the attack's priority 3 refuses: the start fails ...
        var generic = Start(main, Request(GoalStarters.Generic, GoalType.EyeCandy));
        Assert.AreEqual(GoalStartOutcome.Interrupt, generic.Outcome);
        // ... unless it is one of the three sites that start anyway: the new run coexists with the one that refused.
        var anyway = Start(main, Request(GoalStarters.GenericStartsAnyway, GoalType.EyeCandy));
        Assert.AreEqual(GoalStartOutcome.Started, anyway.Outcome);
        Assert.AreEqual(2, anyway.Table.InUseCount);
        // Beside overlays only, level 0 leaves them running and the start succeeds.
        var overlays = With(GoalRunTable.Empty, A, GoalType.EyeCandy);
        Assert.AreEqual(GoalStartOutcome.Started, Start(overlays, Request(GoalStarters.Generic, GoalType.Floating)).Outcome);
    }

    [TestMethod]
    public void ARefusedInterruptKeepsTheRunsThatEndedBeforeIt()
    {
        var table = With(With(GoalRunTable.Empty, A, GoalType.MoveToTile), A, GoalType.Dying);
        var result = Start(table, Request(GoalStarters.Walk, GoalType.MoveToTile));
        Assert.AreEqual(GoalStartOutcome.Interrupt, result.Outcome);
        Assert.AreEqual(1, result.Ended.Length, "the walk in slot 0 ended before dying refused (GS17)");
        Assert.AreEqual(1, result.Table.InUseCount);
        Assert.IsFalse(result.Table.TryGet(new GoalHandle(0, 1), out _));
    }

    [TestMethod]
    public void LoadingAndDestroyedRefuseAfterTheInterruptsAndTheTableFullAfterThat()
    {
        var running = With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe");
        var loading = Start(running, Request(GoalStarters.Walk, GoalType.MoveToTile), new Ports { Loading = true });
        Assert.AreEqual(GoalStartOutcome.Loading, loading.Outcome);
        Assert.AreEqual(1, loading.Ended.Length, "the interrupts have already run (GS11)");
        Assert.AreEqual(0, loading.Table.InUseCount);
        var ports = new Ports();
        ports.Destroyed.Add(A);
        var destroyed = Start(running, Request(GoalStarters.Walk, GoalType.MoveToTile), ports);
        Assert.AreEqual(GoalStartOutcome.Destroyed, destroyed.Outcome);
        Assert.AreEqual(1, destroyed.Ended.Length);
        Assert.AreEqual(GoalStartOutcome.Started, Start(running, Request(GoalStarters.Walk, GoalType.MoveToTile, owner: B)).Outcome, "a destroyed owner is the one the port names");

        var full = GoalRunTable.Empty;
        for (var i = 0; i < GoalRunTable.SlotCount; i++) full = With(full, $"npc:{i}", GoalType.Dying);
        var refused = Start(full, Request(GoalStarters.Walk, GoalType.MoveToTile, owner: "npc:new"));
        Assert.AreEqual(GoalStartOutcome.TableFull, refused.Outcome);
        Assert.IsNull(refused.Handle);
        Assert.IsTrue(refused.Table.ExhaustionPending);
        // The same owner's own run frees a slot first: the interrupt of a walk, then the start fits.
        var fullOfWalks = GoalRunTable.Empty;
        for (var i = 0; i < GoalRunTable.SlotCount; i++) fullOfWalks = With(fullOfWalks, i == 100 ? "npc:same" : $"npc:{i}", GoalType.MoveToTile);
        Assert.AreEqual(GoalStartOutcome.Started, Start(fullOfWalks, Request(GoalStarters.Walk, GoalType.MoveToTile, owner: "npc:same")).Outcome);
    }

    [TestMethod]
    public void AGoalWithNoDescriptorCannotBeStartedAndChangesNothing()
    {
        var running = With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe");
        var ports = new Ports();
        foreach (var goal in new[] { GoalType.UnlockDoor, GoalType.PickWeapon })
        {
            var result = Start(running, Request(GoalStarters.Generic, goal), ports);
            Assert.AreEqual(GoalStartOutcome.NoDescriptor, result.Outcome);
            Assert.AreSame(running, result.Table);
        }
        Assert.AreEqual(0, ports.Asked.Count, "refused before the gate");
    }

    [TestMethod]
    public void TheRulesRefuseNullArguments()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalStartRules.Start(null!, Request(GoalStarters.Walk, GoalType.MoveToTile), new Ports()));
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalStartRules.Start(GoalRunTable.Empty, null!, new Ports()));
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalStartRules.Start(GoalRunTable.Empty, Request(GoalStarters.Walk, GoalType.MoveToTile), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalStartRules.RetargetWalk(null!, A, GoalType.MoveToTile, new CellCoord(1, 1)));
    }

    // ---- retarget in place (GS14) ----

    private static GoalRunTable Walking(string owner, GoalType goal, CellCoord to)
    {
        var allocation = GoalRunTable.Empty.Allocate(owner, new GoalFrame(goal, tile: to));
        return allocation.Table;
    }

    [TestMethod]
    public void AWalkToTheSameTileIsAcceptedAndToAnotherTileIsRetargetedInPlace()
    {
        var table = Walking(A, GoalType.MoveToTile, new CellCoord(2, 2));
        var same = GoalStartRules.RetargetWalk(table, A, GoalType.MoveToTile, new CellCoord(2, 2));
        Assert.AreEqual(GoalRetargetOutcome.Unchanged, same.Outcome);
        Assert.AreSame(table, same.Table);
        var moved = GoalStartRules.RetargetWalk(table, A, GoalType.MoveToTile, new CellCoord(7, 8));
        Assert.AreEqual(GoalRetargetOutcome.Retargeted, moved.Outcome);
        Assert.IsTrue(moved.Table.TryGet(moved.Handle!.Value, out var run));
        Assert.AreEqual(new CellCoord(7, 8), run.Bottom.Tile);
        Assert.AreEqual(GoalPathFlags.Replan, run.PathFlags);
        Assert.AreEqual(1, moved.Table.InUseCount, "the same run, not a new one");
    }

    [TestMethod]
    public void TheRetargetTakesAnAliasRunButNotAnotherOwnersOrAHiddenOne()
    {
        // A run to a tile (4) is the walk's alias (3 lists 4).
        var running = Walking(A, GoalType.RunToTile, new CellCoord(1, 1));
        Assert.AreEqual(GoalRetargetOutcome.Retargeted, GoalStartRules.RetargetWalk(running, A, GoalType.MoveToTile, new CellCoord(5, 5)).Outcome);
        Assert.AreEqual(GoalRetargetOutcome.NoRun, GoalStartRules.RetargetWalk(running, B, GoalType.MoveToTile, new CellCoord(5, 5)).Outcome);
        Assert.AreEqual(GoalRetargetOutcome.NoRun, GoalStartRules.RetargetWalk(GoalRunTable.Empty, A, GoalType.MoveToTile, new CellCoord(5, 5)).Outcome);
        Assert.AreEqual(GoalRetargetOutcome.NoRun, GoalStartRules.RetargetWalk(With(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe"), A, GoalType.MoveToTile, new CellCoord(5, 5)).Outcome, "an attack is not a walk");
        // An eye-candy overlay in the first slot hides the walk behind it: the starter then starts a new run (GS4).
        var hidden = With(GoalRunTable.Empty, A, GoalType.EyeCandy);
        hidden = hidden.Allocate(A, new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(1, 1))).Table;
        Assert.AreEqual(GoalRetargetOutcome.NoRun, GoalStartRules.RetargetWalk(hidden, A, GoalType.MoveToTile, new CellCoord(5, 5)).Outcome);
    }
}
