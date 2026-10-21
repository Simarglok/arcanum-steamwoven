using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// The pick-locks case of the skill handler (ARC-SKILLS-006, ledger PL1), as the retail 1.0.7.4 executable runs it.
[TestClass]
public sealed class PickLockRulesTests
{
    [TestMethod]
    public void ALockedTargetUnlocksOnAnySuccessAndJamsOnlyOnACriticalFailureWithWitnessesEitherWay()
    {
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Unlocked, true, false), PickLockRules.Resolve(locked: true, success: true, critical: false));
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Unlocked, true, false), PickLockRules.Resolve(locked: true, success: true, critical: true));
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Failed, true, true), PickLockRules.Resolve(locked: true, success: false, critical: false));
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Jammed, true, true), PickLockRules.Resolve(locked: true, success: false, critical: true));
    }

    [TestMethod]
    public void AnUnlockedTargetLocksOnASuccessAndCallsNoWitnesses()
    {
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Locked, false, false), PickLockRules.Resolve(locked: false, success: true, critical: true));
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Failed, false, false), PickLockRules.Resolve(locked: false, success: false, critical: true),
            "a critical failure jams only a locked target");
    }
}
