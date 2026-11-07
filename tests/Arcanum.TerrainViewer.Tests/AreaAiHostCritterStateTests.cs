using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-137 (MIG13 S6, goal-table.md §3.5): the AI host steps the critter-state goals the session starts on an NPC's life step. A death plays the dying goal frame by frame and leaves
/// its last frame (the pool of the viewer waits for the run), a knock-down of a conscious NPC falls, gets up and stands, a stun lasts its animation's plays (the count from constitution) and then clears the stunned bit
/// (at once when the viewer reads no stun ART). The host records the art the goals set. Invented sectors and NPCs, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostCritterStateTests
{
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host, SessionLifeTarget Target, Arcanum.World.FrameStep Step) Open(Func<string, int, AiAnimationClip?>? clips)
    {
        var f = AreaAiHostRebindTests.Open();
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]) with { AnimationClip = clips };
        var host = f.Build(scene);
        var target = f.Combat.Build(f.Session, f.A).Combat?.Target ?? throw new AssertFailedException("the NPC has no combat build");
        return (f, host, target, host.Frames());
    }

    private static bool Running(AreaAiHost host, string who) => host.Session.QueryGoals().RunsOf(who).Any(r => AiExecutorRules.IsCritterState(r.Bottom.Goal));

    private static int Drive(AreaAiHost host, Arcanum.World.FrameStep step, string who, Action? each = null)
    {
        var frames = 0;
        for (; frames < 400 && Running(host, who); frames++)
        {
            step.AdvanceHost(50);
            each?.Invoke();
        }
        return frames;
    }

    [TestMethod]
    public void AKilledNpcPlaysItsDyingGoalFrameByFrameAndKeepsTheLastFrame()
    {
        var (f, host, target, step) = Open((_, animation) => animation == 7 ? new(5, 10) : null);
        using var fixture = f;
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.Kill(f.Session.Version, target).Disposition);
        Assert.IsTrue(host.DyingRunning(f.A), "the kill started the dying goal on the table");
        var seen = new SortedSet<int>();
        var frames = Drive(host, step, f.A, () => { if (host.ArtOf(f.A) is { } art) seen.Add(art.Frame); });
        Assert.IsTrue(frames < 400, "the dying goal ended");
        Assert.IsFalse(host.DyingRunning(f.A), "the pool of blood no longer waits");
        Assert.AreEqual((7, 4), host.ArtOf(f.A), $"the last frame stays; frames={frames} trace={host.TraceOf(f.A)?.Step} seen={string.Join(",", seen)}");
        Assert.IsTrue(seen.Count >= 3, "its frames were shown on the way: " + string.Join(",", seen));
        Assert.AreEqual(DyingAnimationRules.FallDown, host.ShownAnimation(f.A));
    }

    [TestMethod]
    public void ADeathByAHeadHitShowsTheDecapitationAnimation()
    {
        var (f, host, target, step) = Open((_, animation) => new(3, 10));
        using var fixture = f;
        Assert.AreEqual(GameSessionDisposition.Changed,
            f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { HitPoints = 5000, Location = HitLocation.Head }).Disposition);
        var run = host.Session.QueryGoals().RunsOf(f.A).Single();
        Assert.AreEqual(GoalType.Dying, run.Bottom.Goal);
        Assert.IsTrue(run.Bottom.Animation is 7 or 17, "the death animation of the damage: " + run.Bottom.Animation);
        Drive(host, step, f.A);
        Assert.AreEqual(run.Bottom.Animation, host.ArtOf(f.A)?.Animation);
    }

    [TestMethod]
    public void AKnockedDownConsciousNpcFallsGetsUpAndStandsAndBecomesTheRightMover()
    {
        var (f, host, target, step) = Open((_, animation) => animation switch { 7 => new(4, 10), 8 => new(3, 20), _ => null });
        using var fixture = f;
        f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { Effects = CriticalHitEffects.Knockdown, HitPoints = 1 });
        var shown = new List<int>();
        var lying = false;
        Drive(host, step, f.A, () =>
        {
            if (host.ArtOf(f.A) is { } art && (shown.Count == 0 || shown[^1] != art.Animation)) shown.Add(art.Animation);
            lying |= host.Mover(f.A).KnockedDown;
        });
        CollectionAssert.AreEqual(new[] { 7, 8 }, shown, "the fall, then the get-up");
        Assert.IsTrue(lying, "while it lay conscious the executor saw it knocked down");
        Assert.IsNull(host.ArtOf(f.A), "standing again");
        Assert.IsFalse(host.Mover(f.A).KnockedDown);
    }

    [TestMethod]
    public void AStunLastsItsAnimationsPlaysAndThenClearsTheStunnedBit()
    {
        var (f, host, target, step) = Open((_, animation) => animation == 23 ? new(3, 10) : null);
        using var fixture = f;
        f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { Effects = CriticalHitEffects.Stun });
        Assert.IsTrue(f.Session.QueryLifeRecord(f.A)!.Value.State.Conditions.HasFlag(LifeConditions.Stunned));
        var before = host.NowMs;
        var frames = Drive(host, step, f.A);
        Assert.IsTrue(frames < 400, "the stun ended");
        // One play of 3 frames at 10 per second is 2 delays of 100 ms; the count (20 − constitution) / 2 of the fixture's NPC is at least 1.
        Assert.IsTrue(host.NowMs - before >= 200, $"it played its animation: {host.NowMs - before} ms");
        Assert.IsFalse(f.Session.QueryLifeRecord(f.A)!.Value.State.Conditions.HasFlag(LifeConditions.Stunned), "the end node cleared the bit");
        Assert.IsNull(host.ArtOf(f.A), "and stood the critter up");
    }

    [TestMethod]
    public void AStunWhoseArtTheViewerCannotReadEndsAtOnce()
    {
        var (f, host, target, step) = Open(null);
        using var fixture = f;
        f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { Effects = CriticalHitEffects.Stun });
        Drive(host, step, f.A);
        Assert.IsFalse(f.Session.QueryLifeRecord(f.A)!.Value.State.Conditions.HasFlag(LifeConditions.Stunned));
        Assert.IsNull(host.ArtOf(f.A));
    }

    [TestMethod]
    public void TheAiGateSeesAStunRunAsTheStunAnimationRunning()
    {
        var (f, host, target, _) = Open((_, animation) => animation == 23 ? new(3, 10) : null);
        using var fixture = f;
        Assert.IsFalse(host.StunAnimationRunning(f.A));
        f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { Effects = CriticalHitEffects.Stun });
        Assert.IsTrue(host.StunAnimationRunning(f.A), "the first run is the stun: the gate does not clear its bit (GT35)");
    }

    [TestMethod]
    public void AnNpcWithNoGoalShowsWhatItsLifeSaysAndAStaleDyingArtGoesWhenItLives()
    {
        var (f, host, target, step) = Open(null);
        using var fixture = f;
        Assert.AreEqual(0, host.ShownAnimation(f.A));
        f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { Fatigue = 5000 });
        Assert.AreEqual(DyingAnimationRules.FallDown, host.ShownAnimation(f.A), "an unconscious NPC with no art written lies in its fall");
        host.ShowArt(f.A, 17, 2);
        Assert.IsNull(host.ArtOf(f.A), "a dying animation of a critter that is not dead is a stale write");
    }
}
