using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// Real-time combat (ARC-COMBAT-004): combat mode, the attack animation's rate, pause and playback, the auto-attack repeat
// and the run charge. Numbers checked in the original 1.0.7.4 executable (ledger RT1–RT12); clips are
// invented.
[TestClass]
public sealed class RealTimeCombatRulesTests
{
    private static readonly CombatModeFacts Able = new(false, true, false, false, false, false, false);

    [TestMethod]
    public void ToggleRefusalsFollowTheSourceOrderAndADeadCritterSkipsThemAll()
    {
        Assert.AreEqual(CombatModeRefusal.None, RealTimeCombatRules.ToggleRefusal(false, Able));
        Assert.AreEqual(CombatModeRefusal.None, RealTimeCombatRules.ToggleRefusal(true, Able));
        Assert.AreEqual(CombatModeRefusal.Stoned, RealTimeCombatRules.ToggleRefusal(true, Able with { Stoned = true, Paralysed = true }));
        Assert.AreEqual(CombatModeRefusal.Paralysed, RealTimeCombatRules.ToggleRefusal(false, Able with { Paralysed = true }));
        // Stunned blocks leaving only; an inactive critter (here stunned) cannot enter.
        Assert.AreEqual(CombatModeRefusal.Stunned, RealTimeCombatRules.ToggleRefusal(true, Able with { Stunned = true, Active = false }));
        Assert.AreEqual(CombatModeRefusal.Inactive, RealTimeCombatRules.ToggleRefusal(false, Able with { Stunned = true, Active = false }));
        Assert.AreEqual(CombatModeRefusal.None, RealTimeCombatRules.ToggleRefusal(true, Able with { Active = false }), "an unconscious critter may leave");
        Assert.AreEqual(CombatModeRefusal.Concealed, RealTimeCombatRules.ToggleRefusal(false, Able with { Concealed = true }));
        Assert.AreEqual(CombatModeRefusal.None, RealTimeCombatRules.ToggleRefusal(false, Able with { Concealed = true, LocalPc = true }));
        var dead = new CombatModeFacts(true, false, true, true, true, true, false);
        Assert.AreEqual(CombatModeRefusal.None, RealTimeCombatRules.ToggleRefusal(false, dead));
        Assert.AreEqual(CombatModeRefusal.None, RealTimeCombatRules.ToggleRefusal(true, dead));
    }

    [TestMethod]
    public void TheExitGateHoldsOnlyAnActiveCritterInCombatModeThatIsAttackingOrThreatened()
    {
        Assert.IsTrue(RealTimeCombatRules.CanExitCombatMode(false, true, true, true));
        Assert.IsTrue(RealTimeCombatRules.CanExitCombatMode(true, false, true, true));
        Assert.IsTrue(RealTimeCombatRules.CanExitCombatMode(true, true, false, false));
        Assert.IsFalse(RealTimeCombatRules.CanExitCombatMode(true, true, true, false));
        Assert.IsFalse(RealTimeCombatRules.CanExitCombatMode(true, true, false, true));
    }

    // ARC-COMBAT-022 (ledger RR1–RR4): the game view's right release.
    [TestMethod]
    public void ARightReleaseLeavesCombatModeOnlyForAnIdleActivePcAndNeverEntersIt()
    {
        var idle = new RightReleaseFacts(Active: true, KnockedDown: false, InCombatMode: true, Concealed: false, Animating: false, Fidgeting: false);
        Assert.AreEqual(RightReleaseAction.ToggleCombatMode, RealTimeCombatRules.RightRelease(idle));
        Assert.AreEqual(RightReleaseAction.ToggleCombatMode, RealTimeCombatRules.RightRelease(idle with { Concealed = true }), "combat mode first");
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(idle with { Animating = true }), "walking or attacking");
        Assert.AreEqual(RightReleaseAction.ToggleCombatMode, RealTimeCombatRules.RightRelease(idle with { Animating = true, Fidgeting = true }));
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(idle with { Active = false }), "stunned, paralysed, stoned, unconscious or dead");
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(idle with { KnockedDown = true }));
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(idle with { InCombatMode = false }), "the release never enters");
    }

    [TestMethod]
    public void ARightReleaseOutOfCombatModeEndsTheConcealmentOfAnIdlePc()
    {
        var concealed = new RightReleaseFacts(Active: true, KnockedDown: false, InCombatMode: false, Concealed: true, Animating: false, Fidgeting: false);
        Assert.AreEqual(RightReleaseAction.LeaveConcealment, RealTimeCombatRules.RightRelease(concealed));
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(concealed with { Animating = true }));
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(concealed with { Animating = true, Fidgeting = true }), "the fidget only matters in combat mode");
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(concealed with { Active = false }));
        Assert.AreEqual(RightReleaseAction.None, RealTimeCombatRules.RightRelease(concealed with { KnockedDown = true }));
    }

    [TestMethod]
    public void AttackRatesComeFromTheWeaponSlotAndSpeed()
    {
        // Unarmed attack: middle 15, low 7, high 23.
        int Unarmed(int speed) => RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Unarmed, speed, 99);
        Assert.AreEqual(8, Unarmed(1), "7 + 1·8/8");
        Assert.AreEqual(14, Unarmed(7), "7 + 7·8/8");
        Assert.AreEqual(15, Unarmed(8), "the middle value at Speed 8");
        Assert.AreEqual(17, Unarmed(9), "15 + 9·8/30 = 15 + 2");
        Assert.AreEqual(22, Unarmed(29), "15 + 29·8/30 = 15 + 7");
        Assert.AreEqual(23, Unarmed(30));
        Assert.AreEqual(23, Unarmed(100));

        var attack = new Dictionary<AttackArtWeapon, int>
        {
            [AttackArtWeapon.Unarmed] = 15, [AttackArtWeapon.Sword] = 15, [AttackArtWeapon.Dagger] = 10, [AttackArtWeapon.Bow] = 10,
            [AttackArtWeapon.Axe] = 14, [AttackArtWeapon.Mace] = 14, [AttackArtWeapon.TwoHandedSword] = 14, [AttackArtWeapon.Pistol] = 8,
            [AttackArtWeapon.Rifle] = 6, [AttackArtWeapon.Staff] = 12,
        };
        var low = new Dictionary<AttackArtWeapon, int>
        {
            [AttackArtWeapon.Unarmed] = 13, [AttackArtWeapon.Dagger] = 10, [AttackArtWeapon.Bow] = 10, [AttackArtWeapon.Staff] = 10,
            [AttackArtWeapon.Sword] = 14, [AttackArtWeapon.Axe] = 11, [AttackArtWeapon.Mace] = 11, [AttackArtWeapon.Pistol] = 6,
            [AttackArtWeapon.Rifle] = 6, [AttackArtWeapon.TwoHandedSword] = 13,
        };
        foreach (var weapon in Enum.GetValues<AttackArtWeapon>())
        {
            Assert.AreEqual(attack.GetValueOrDefault(weapon, 99), RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, weapon, 8, 99), weapon.ToString());
            Assert.AreEqual(low.GetValueOrDefault(weapon, 99), RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.AttackLow, weapon, 8, 99), weapon.ToString());
        }
        // Low and high ends of two more slots: rifle 3..10, two-handed sword (low animation) 9..17.
        Assert.AreEqual(3, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Rifle, 1, 99), "3 + 1·3/8");
        Assert.AreEqual(10, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Rifle, 40, 99));
        Assert.AreEqual(9, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.AttackLow, AttackArtWeapon.TwoHandedSword, 1, 99), "9 + 1·4/8");
        Assert.AreEqual(10, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.AttackLow, AttackArtWeapon.TwoHandedSword, 2, 99), "9 + 2·4/8");
        Assert.AreEqual(17, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.AttackLow, AttackArtWeapon.TwoHandedSword, 30, 99));
        // Not a critter ART, or under a body-of-element spell: the ART's own rate.
        Assert.AreEqual(12, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Sword, 20, 12, critterArt: false));
        Assert.AreEqual(12, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Sword, 20, 12, elementalBody: true));
        // ARC-RENDER-039 (WA9): the throw keeps its ART's rate whatever the Speed and weapon.
        foreach (var weapon in Enum.GetValues<AttackArtWeapon>())
            Assert.AreEqual(12, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Throw, weapon, 20, 12), weapon.ToString());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Sword, 0, 12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Sword, 8, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, (AttackArtWeapon)15, 8, 12));
    }

    [TestMethod]
    public void ThePauseClampsTheRateThenSubtractsTenPerWeaponSpeedAboveTen()
    {
        Assert.AreEqual(66, RealTimeCombatRules.AttackFramePauseMs(15, 10), "1000 / 15, unarmed");
        Assert.AreEqual(116, RealTimeCombatRules.AttackFramePauseMs(15, 5));
        Assert.AreEqual(36, RealTimeCombatRules.AttackFramePauseMs(15, 13));
        Assert.AreEqual(30, RealTimeCombatRules.AttackFramePauseMs(15, 14), "66 − 40 is raised to 30");
        Assert.AreEqual(800, RealTimeCombatRules.AttackFramePauseMs(1, 10), "1000 is first clamped to 800");
        Assert.AreEqual(800, RealTimeCombatRules.AttackFramePauseMs(15, -100));
        Assert.AreEqual(30, RealTimeCombatRules.AttackFramePauseMs(100, 10), "10 ms is raised to 30");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.AttackFramePauseMs(0, 10));
    }

    private static AttackClip Clip(int frames, int action, AttackArtWeapon weapon = AttackArtWeapon.Sword) => new(frames, action, 100, weapon);

    private static IReadOnlyList<(long, AttackUpdateKind, int)> Plan(AttackClip clip) =>
        RealTimeCombatRules.Schedule(clip).Select(m => (m.AtMs, m.Kind, m.Frame)).ToArray();

    [TestMethod]
    public void OneAttackShowsAFrameEachPauseAndHitsWithTheActionFrame()
    {
        CollectionAssert.AreEqual(new (long, AttackUpdateKind, int)[]
        {
            (0, AttackUpdateKind.Advanced, 1), (100, AttackUpdateKind.Advanced, 2), (200, AttackUpdateKind.Hit, 3),
            (300, AttackUpdateKind.Advanced, 4), (400, AttackUpdateKind.Advanced, 5), (500, AttackUpdateKind.Ended, 5),
        }, Plan(Clip(6, 3)).ToArray());
        // Action frame 0 or 1: the hit runs with the first update; on the last frame it still hits; past it never.
        Assert.AreEqual((0L, AttackUpdateKind.Hit, 1), Plan(Clip(6, 0))[0]);
        Assert.AreEqual((0L, AttackUpdateKind.Hit, 1), Plan(Clip(6, 1))[0]);
        Assert.AreEqual((400L, AttackUpdateKind.Hit, 5), Plan(Clip(6, 5))[4]);
        Assert.IsFalse(Plan(Clip(6, 6)).Any(m => m.Item2 == AttackUpdateKind.Hit));
        // A one-frame clip ends at its first update, even with action frame 0.
        CollectionAssert.AreEqual(new (long, AttackUpdateKind, int)[] { (0, AttackUpdateKind.Ended, 0) }, Plan(Clip(1, 0)).ToArray());
        Assert.AreEqual(new AttackPlayback(0, true, false, true), RealTimeCombatRules.Begin(Clip(6, 0)));
    }

    // Plays a clip with repeat shots allowed until the given number of hits, returning the hit times.
    private static List<long> Hits(AttackClip clip, int count, bool roundActive = false)
    {
        var hits = new List<long>();
        var playback = RealTimeCombatRules.Begin(clip);
        long at = 0;
        for (var step = 0; step < 100 && hits.Count < count; step++)
        {
            var update = RealTimeCombatRules.Update(clip, playback, true, roundActive, true);
            if (update.Kind == AttackUpdateKind.Hit) hits.Add(at);
            if (update.NextUpdateMs is not { } delay) break;
            at += delay; playback = update.Playback;
        }
        return hits;
    }

    [TestMethod]
    public void PistolsRiflesAndBowsFireAgainEveryFourPausesInRealTime()
    {
        CollectionAssert.AreEqual(new long[] { 200, 600, 1000 }, Hits(Clip(10, 3, AttackArtWeapon.Pistol), 3));
        CollectionAssert.AreEqual(new long[] { 200, 600, 1000 }, Hits(Clip(10, 3, AttackArtWeapon.Rifle), 3));
        CollectionAssert.AreEqual(new long[] { 200, 600, 1000 }, Hits(Clip(10, 3, AttackArtWeapon.Bow), 3));
        // The rewind itself: a pistol two frames after the action frame goes back three.
        var pistol = Clip(10, 3, AttackArtWeapon.Pistol);
        var rewound = RealTimeCombatRules.Update(pistol, new(5, true, true, true), true, false, true);
        Assert.AreEqual(new AttackUpdate(AttackUpdateKind.Rewound, new(2, false, false, true), 100), rewound);
        Assert.AreEqual(AttackUpdateKind.Advanced, RealTimeCombatRules.Update(pistol, new(5, true, true, true), true, false, false).Kind, "no ammunition or target");
        // In a turn-based round, and for melee weapons, there is one hit.
        CollectionAssert.AreEqual(new long[] { 200 }, Hits(Clip(10, 3, AttackArtWeapon.Pistol), 3, roundActive: true));
        CollectionAssert.AreEqual(new long[] { 200 }, Hits(Clip(10, 3, AttackArtWeapon.Axe), 3));
        // A rewind that would go before the first frame does not happen (project guard).
        CollectionAssert.AreEqual(new long[] { 100 }, Hits(Clip(10, 2, AttackArtWeapon.Bow), 3));
    }

    [TestMethod]
    public void AnAttackerThatCannotActEndsItsAttackAndAnEndedOneCannotUpdate()
    {
        var clip = Clip(6, 3);
        var ended = RealTimeCombatRules.Update(clip, new(2, true, false, true), false, false, false);
        Assert.AreEqual(new AttackUpdate(AttackUpdateKind.Ended, new(2, true, false, false), null), ended);
        Assert.ThrowsExactly<ArgumentException>(() => RealTimeCombatRules.Update(clip, ended.Playback, true, false, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.Update(clip, new(6, false, false, true), true, false, false));
        Assert.IsFalse(RealTimeCombatRules.Abort(new(3, true, true, true)).Playing);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AttackClip(6, 3, 29, AttackArtWeapon.Sword));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AttackClip(0, 0, 100, AttackArtWeapon.Sword));
    }

    [TestMethod]
    public void TheAttackRepeatsOnlyForAnAutoAttackingPcOnAValidTarget()
    {
        var repeat = new AutoAttackFacts(true, true, true, false, false, false, false, false, false, false);
        Assert.IsTrue(RealTimeCombatRules.Repeats(repeat));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { AttackerInCombatMode = false }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { AttackerIsPc = false }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { AutoAttackSetting = false }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { RoundActive = true }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { TargetDestroyedOrOff = true }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { TargetDead = true }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { TargetBusted = true }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { StopRequested = true }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { NonLethal = true }));
        Assert.IsFalse(RealTimeCombatRules.Repeats(repeat with { UsingBoomerang = true }));
    }

    [TestMethod]
    public void RunningInCombatModeChargesFatigueEveryFifthStep()
    {
        Assert.AreEqual(5, RealTimeCombatRules.RunChargePeriod(false, false));
        Assert.AreEqual(20, RealTimeCombatRules.RunChargePeriod(true, false));
        Assert.AreEqual(1, RealTimeCombatRules.RunChargePeriod(false, true), "5 / 4 = 1");
        Assert.AreEqual(5, RealTimeCombatRules.RunChargePeriod(true, true), "20 / 4");
        var counter = 0;
        var charges = new List<int>();
        for (var step = 1; step <= 12; step++)
        {
            (counter, var charge) = RealTimeCombatRules.RunStep(counter, 5);
            if (charge) charges.Add(step);
        }
        CollectionAssert.AreEqual(new[] { 5, 10 }, charges);
        Assert.AreEqual((0, true), RealTimeCombatRules.RunStep(0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.RunStep(5, 5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RealTimeCombatRules.RunStep(0, 0));
    }
}
