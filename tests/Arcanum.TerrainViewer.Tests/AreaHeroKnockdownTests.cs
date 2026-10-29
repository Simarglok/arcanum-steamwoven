using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-021: the AREA hero falls when it goes unconscious, lies on the fall's last frame and gets up when it comes round
/// (ledger UC1–UC4).</summary>
[TestClass]
public sealed class AreaHeroKnockdownTests
{
    private static readonly AreaKnockdownClip Fall = new(10, 10), GetUp = new(5, 10);
    private static bool Yes() => true;

    [TestMethod]
    public void AnUnconsciousHeroFallsLiesAndGetsUpWhenItComesRound()
    {
        var knockdown = new AreaHeroKnockdown();
        Assert.IsFalse(knockdown.Tick(16, down: false, settled: true, Yes, Fall, GetUp));
        // A step under way ends first (UC1's knockdown plays on the settled critter here).
        Assert.IsFalse(knockdown.Tick(16, down: true, settled: false, Yes, Fall, GetUp));
        Assert.AreEqual(AreaKnockdownStage.Standing, knockdown.Stage);
        Assert.IsTrue(knockdown.Tick(16, down: true, settled: true, Yes, Fall, GetUp));
        Assert.AreEqual((AreaKnockdownStage.Falling, 0), (knockdown.Stage, knockdown.Frame));
        Assert.IsTrue(knockdown.Lying);
        // 10 frames at 10 fps: frame 4 at 450 ms, the last frame held from 1000 ms.
        knockdown.Tick(450, true, true, Yes, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.Falling, 4), (knockdown.Stage, knockdown.Frame));
        knockdown.Tick(550, true, true, Yes, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.Down, 9), (knockdown.Stage, knockdown.Frame));
        // Still unconscious: it stays down, asking nothing.
        var asked = 0;
        bool Refuse() { asked++; return false; }
        knockdown.Tick(10_000, true, true, Refuse, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.Down, 9, 0), (knockdown.Stage, knockdown.Frame, asked));
        // Conscious again: a refused get-up (another participant's subturn, no action points) keeps it down; the next try stands it up.
        knockdown.Tick(16, false, true, Refuse, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.Down, 1), (knockdown.Stage, asked));
        Assert.IsTrue(knockdown.Tick(16, false, true, Yes, Fall, GetUp));
        Assert.AreEqual((AreaKnockdownStage.GettingUp, 0), (knockdown.Stage, knockdown.Frame));
        knockdown.Tick(250, false, true, Yes, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.GettingUp, 2), (knockdown.Stage, knockdown.Frame));
        knockdown.Tick(250, false, true, Yes, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.Standing, false), (knockdown.Stage, knockdown.Lying));
    }

    [TestMethod]
    public void AHeroDownAgainWhileGettingUpFallsFromTheStart()
    {
        var knockdown = new AreaHeroKnockdown();
        knockdown.Rebase(down: true, Fall);
        Assert.AreEqual((AreaKnockdownStage.Down, 9), (knockdown.Stage, knockdown.Frame));
        knockdown.Tick(16, false, true, Yes, Fall, GetUp);
        knockdown.Tick(200, false, true, Yes, Fall, GetUp);
        Assert.AreEqual(AreaKnockdownStage.GettingUp, knockdown.Stage);
        knockdown.Tick(16, true, true, Yes, Fall, GetUp);
        Assert.AreEqual((AreaKnockdownStage.Falling, 0), (knockdown.Stage, knockdown.Frame));
        // A conscious hero at a load stands.
        knockdown.Rebase(down: false, Fall);
        Assert.AreEqual((AreaKnockdownStage.Standing, 0, false), (knockdown.Stage, knockdown.Frame, knockdown.Lying));
    }

    [TestMethod]
    public void ABodyWithoutTheArtsStillLiesWhileDown()
    {
        var knockdown = new AreaHeroKnockdown();
        knockdown.Tick(16, true, true, Yes, null, null);
        Assert.AreEqual((AreaKnockdownStage.Down, true), (knockdown.Stage, knockdown.Lying));
        knockdown.Tick(16, false, true, Yes, null, null);
        Assert.AreEqual(AreaKnockdownStage.Standing, knockdown.Stage);
        Assert.Throws<ArgumentOutOfRangeException>(() => knockdown.Tick(-1, false, true, Yes, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => knockdown.Tick(double.NaN, false, true, Yes, null, null));
    }

    private static ActorDiagnosticResource Res(ActorDiagnosticAction action, uint selector, int frames) => new(action, selector, 1, 0, 8, frames, 10, 10);

    private static ActorDiagnosticProfile Profile(bool fall, bool getUp)
    {
        var human = ActorDiagnosticProfile.HumanFixedArea;
        return ActorDiagnosticProfile.ForHeroBody(human.Idle, human.Walk, human.Run!, human.Vault!, human.Attack!, 195,
            fall ? Res(ActorDiagnosticAction.FallDown, ActorDiagnosticProfile.HumanFallDownSelector, 3) : null,
            getUp ? Res(ActorDiagnosticAction.GetUp, ActorDiagnosticProfile.HumanGetUpSelector, 2) : null);
    }

    [TestMethod]
    public void AHeroBodyCarriesTheFallAndGetUpOnlyTogether()
    {
        var both = Profile(true, true);
        CollectionAssert.AreEqual(new[] { ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run, ActorDiagnosticAction.Vault,
            ActorDiagnosticAction.Attack, ActorDiagnosticAction.FallDown, ActorDiagnosticAction.GetUp }, both.Actions.ToArray());
        Assert.AreEqual(ActorDiagnosticAction.FallDown, both.ActionFor(ActorDiagnosticProfile.HumanFallDownSelector));
        Assert.AreEqual(ActorDiagnosticAction.GetUp, both.ActionFor(ActorDiagnosticProfile.HumanGetUpSelector));
        Assert.AreEqual(3, both.Resource(ActorDiagnosticAction.FallDown).FramesPerGroup);
        // The selectors: animation 7 and 8 in bits 6..10, no weapon; a body's over them names its fall file.
        Assert.AreEqual(ActorDiagnosticProfile.HumanFallDownSelector, ActorDiagnosticProfile.HumanSelector(ActorDiagnosticAction.FallDown));
        Assert.AreEqual(ActorDiagnosticProfile.HumanFixedArea.Run!.Selector, ActorDiagnosticProfile.HumanSelector(ActorDiagnosticAction.Run));
        var elf = HeroBody.For(Arcanum.Simulation.Character.CharacterRace.Elf, Arcanum.Simulation.Character.CharacterGender.Male)!.Value;
        Assert.AreEqual("art/critter/EFM/EFMUWXAh.art", elf.ArtPath(elf.Selector(ActorDiagnosticProfile.HumanFallDownSelector)));
        Assert.AreEqual("art/critter/EFM/EFMUWXAi.art", elf.ArtPath(elf.Selector(ActorDiagnosticProfile.HumanGetUpSelector)));
        foreach (var half in new[] { Profile(true, false), Profile(false, true), ActorDiagnosticProfile.HumanFixedArea })
        {
            Assert.IsNull(half.FallDown); Assert.IsNull(half.GetUp);
            Assert.AreEqual(5, half.Actions.Count);
            Assert.Throws<TerrainViewerException>(() => half.Resource(ActorDiagnosticAction.FallDown));
            Assert.Throws<TerrainViewerException>(() => half.ActionFor(ActorDiagnosticProfile.HumanGetUpSelector));
        }
    }

    [TestMethod]
    public void TheDownPoseWinsOverTheAttackPoseOnASettledActor()
    {
        using var fixture = new GridWireFixture();
        var actor = new DiagnosticGridActor();
        actor.Configure(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        actor.AttackPose = (3, 4);
        Assert.AreEqual((ActorDiagnosticAction.Attack, 3, 4), (actor.State.Action, actor.State.Facing, actor.CurrentFrame));
        actor.DownPose = (ActorDiagnosticAction.FallDown, 6, 2);
        Assert.AreEqual((ActorDiagnosticAction.FallDown, 6, 2), (actor.State.Action, actor.State.Facing, actor.CurrentFrame));
        actor.DownPose = null;
        Assert.AreEqual(ActorDiagnosticAction.Attack, actor.State.Action);
        // A new session drops both poses.
        actor.DownPose = (ActorDiagnosticAction.GetUp, 1, 0);
        actor.Configure(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        Assert.IsNull(actor.DownPose);
        Assert.AreEqual(ActorDiagnosticAction.Idle, actor.State.Action);
    }
}
