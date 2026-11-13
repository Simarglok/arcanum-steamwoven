using System.Buffers.Binary;
using Arcanum.Data;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    // A minimal 8-bit ART: 8 groups of `frames` 1×1 frames each moving the anchor (1, 1), palette 0 white unless `palette` is false.
    internal static byte[] Art(int frames, int fps = 10, int actionFrame = 0, bool palette = true)
    {
        const int headerBytes = 132, paletteBytes = 1024, frameHeaderBytes = 28;
        var total = 8 * frames;
        var table = headerBytes + paletteBytes;
        var payload = table + total * frameHeaderBytes;
        var bytes = new byte[payload + total];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fps);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), palette ? 1u : 0u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), actionFrame);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), frames);
        bytes[headerBytes + 4] = 255; bytes[headerBytes + 5] = 255; bytes[headerBytes + 6] = 255; bytes[headerBytes + 7] = 255;
        for (var index = 0; index < total; index++)
        {
            var frame = table + index * frameHeaderBytes;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 20), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 24), 1);
            bytes[payload + index] = 1;
        }
        return bytes;
    }

    internal static HeroBodyLoad LoadWoman(params (string Name, byte[] Payload)[] downArts)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arc-combat-021-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(path, AreaCombatTests.Dat([("art/critter/HMF/HMFUWXAa.art", Art(1)), ("art/critter/HMF/HMFUWXAb.art", Art(2)),
                ("art/critter/HMF/HMFUWXAg.art", Art(2)), ("art/critter/HMF/HMFUWXAn.art", Art(2)), ("art/critter/HMF/HMFUWXBu.art", Art(3, actionFrame: 1)),
                .. downArts]));
            using var archive = DatArchive.Open(path);
            return HeroBodyArt.Load(archive, HeroBody.HumanFemale);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void AHeroBodyReadsItsFallAndGetUpOnlyWhenBothRead()
    {
        // Five used groups a frame: 1 + 2 + 2 + 2 + 3 = 10 frames → 50 keys, with a 4-frame fall and a 3-frame get-up 35 more.
        var both = LoadWoman(("art/critter/HMF/HMFUWXAh.art", Art(4, 11)), ("art/critter/HMF/HMFUWXAi.art", Art(3, 14)));
        Assert.AreEqual((4, 11, 3, 14), (both.Profile.FallDown!.FramesPerGroup, both.Profile.FallDown.RawFramesPerSecond,
            both.Profile.GetUp!.FramesPerGroup, both.Profile.GetUp.RawFramesPerSecond));
        Assert.AreEqual((85, 85), (both.Profile.DistinctKeys, both.Textures.Count));
        Assert.AreEqual(ActorDiagnosticProfile.HumanFallDownSelector, both.Profile.FallDown.Selector);
        Assert.AreEqual(ActorDiagnosticAction.FallDown, both.Plan(new ActorDiagnosticPlacementInput(6, 1, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached,
            100, false, ActorDiagnosticAction.FallDown, 5, 3, 0, 0, 0, 0, 0, 0)).Current.Action);
        // Without both, or with one that does not read in the body's palette, the body loads without them (no failure).
        foreach (var load in new[] { LoadWoman(), LoadWoman(("art/critter/HMF/HMFUWXAh.art", Art(4))),
            LoadWoman(("art/critter/HMF/HMFUWXAh.art", Art(4)), ("art/critter/HMF/HMFUWXAi.art", Art(3, palette: false))),
            LoadWoman(("art/critter/HMF/HMFUWXAh.art", [1, 2, 3]), ("art/critter/HMF/HMFUWXAi.art", Art(3))) })
        {
            Assert.IsNull(load.Profile.FallDown); Assert.IsNull(load.Profile.GetUp);
            Assert.AreEqual((50, 50, 5), (load.Profile.DistinctKeys, load.Textures.Count, load.Profile.Actions.Count));
        }
        // A required ART that does not read still fails the body.
        Assert.ThrowsExactly<TerrainViewerException>(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-combat-021-{Guid.NewGuid():N}.dat");
            try
            {
                File.WriteAllBytes(path, AreaCombatTests.Dat([("art/critter/HMF/HMFUWXAa.art", Art(1, palette: false))]));
                using var archive = DatArchive.Open(path);
                HeroBodyArt.Load(archive, HeroBody.HumanFemale);
            }
            finally { File.Delete(path); }
        });
    }

    /// <summary>ARC-COMBAT-1793 (AC1): the hero body reads the optional bloody dying ARTs (17, 18, 19; weapon part cleared like the fall) each on its own, within the key cap; a missing or over-cap one is a typed fallback to the fall.</summary>
    [TestMethod]
    public void AHeroBodyReadsTheBloodyDyingArtsEachOptionally()
    {
        var fall = ("art/critter/HMF/HMFUWXAh.art", Art(4, 11)); var getUp = ("art/critter/HMF/HMFUWXAi.art", Art(3, 14));
        // Five used groups a frame: 50 keys, the fall and get-up 35 more, then 5 a frame of each dying ART.
        var all = LoadWoman(fall, getUp, ("art/critter/HMF/HMFUWXAr.art", Art(6, 12)), ("art/critter/HMF/HMFUWXAs.art", Art(7, 13)), ("art/critter/HMF/HMFUWXAt.art", Art(5, 9)));
        Assert.AreEqual((6, 12, 7, 13, 5, 9), (all.Profile.Decapitation!.FramesPerGroup, all.Profile.Decapitation.RawFramesPerSecond, all.Profile.BlownOutChunk!.FramesPerGroup,
            all.Profile.BlownOutChunk.RawFramesPerSecond, all.Profile.SeveredLeg!.FramesPerGroup, all.Profile.SeveredLeg.RawFramesPerSecond));
        Assert.AreEqual((85 + 5 * (6 + 7 + 5), 85 + 5 * (6 + 7 + 5)), (all.Profile.DistinctKeys, all.Textures.Count));
        Assert.AreEqual(ActorDiagnosticProfile.HumanDecapitationSelector, all.Profile.Decapitation.Selector);
        Assert.AreEqual(ActorDiagnosticProfile.HumanBlownOutChunkSelector, all.Profile.BlownOutChunk.Selector);
        Assert.AreEqual(ActorDiagnosticProfile.HumanSeveredLegSelector, all.Profile.SeveredLeg.Selector);
        Assert.AreEqual(ActorDiagnosticAction.SeveredLeg, all.Profile.ActionFor(ActorDiagnosticProfile.HumanSeveredLegSelector));
        Assert.AreEqual(ActorDiagnosticAction.Decapitation, all.Plan(new ActorDiagnosticPlacementInput(6, 1, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached,
            100, false, ActorDiagnosticAction.Decapitation, 5, 3, 0, 0, 0, 0, 0, 0)).Current.Action);
        // The animation the dying goal names picks the ART; the plain fall (7) and any other number pick the fall.
        Assert.AreEqual((ActorDiagnosticAction.Decapitation, 6), (all.Profile.DyingArt(17)!.Value.Action, all.Profile.DyingArt(17)!.Value.Resource.FramesPerGroup));
        Assert.AreEqual(ActorDiagnosticAction.BlownOutChunk, all.Profile.DyingArt(18)!.Value.Action);
        Assert.AreEqual(ActorDiagnosticAction.SeveredLeg, all.Profile.DyingArt(19)!.Value.Action);
        Assert.AreEqual(ActorDiagnosticAction.FallDown, all.Profile.DyingArt(7)!.Value.Action);
        Assert.AreEqual(ActorDiagnosticAction.FallDown, all.Profile.DyingArt(0)!.Value.Action);
        // One missing, or one that does not read in the body's palette: that ART falls back to the fall, the others still load.
        foreach (var partial in new[] { LoadWoman(fall, getUp, ("art/critter/HMF/HMFUWXAr.art", Art(6)), ("art/critter/HMF/HMFUWXAt.art", Art(5))),
            LoadWoman(fall, getUp, ("art/critter/HMF/HMFUWXAr.art", Art(6)), ("art/critter/HMF/HMFUWXAs.art", Art(7, palette: false)), ("art/critter/HMF/HMFUWXAt.art", Art(5))),
            LoadWoman(fall, getUp, ("art/critter/HMF/HMFUWXAr.art", Art(6)), ("art/critter/HMF/HMFUWXAs.art", [1, 2, 3]), ("art/critter/HMF/HMFUWXAt.art", Art(5))) })
        {
            Assert.IsNull(partial.Profile.BlownOutChunk);
            Assert.IsNotNull(partial.Profile.Decapitation); Assert.IsNotNull(partial.Profile.SeveredLeg);
            Assert.AreEqual(ActorDiagnosticAction.FallDown, partial.Profile.DyingArt(18)!.Value.Action);
            Assert.AreEqual(ActorDiagnosticAction.Decapitation, partial.Profile.DyingArt(17)!.Value.Action);
            Assert.AreEqual(85 + 5 * (6 + 5), partial.Profile.DistinctKeys);
        }
        // Over the key cap (768): 85 keys + 5 x 64 frames twice fits (725); a third such ART (1045) is left out and falls back to the fall; a smaller later one would still fit, a larger one would not.
        var capped = LoadWoman(fall, getUp, ("art/critter/HMF/HMFUWXAr.art", Art(64)), ("art/critter/HMF/HMFUWXAs.art", Art(64)), ("art/critter/HMF/HMFUWXAt.art", Art(64)));
        Assert.IsNotNull(capped.Profile.Decapitation); Assert.IsNotNull(capped.Profile.BlownOutChunk);
        Assert.IsNull(capped.Profile.SeveredLeg);
        Assert.AreEqual(ActorDiagnosticAction.FallDown, capped.Profile.DyingArt(19)!.Value.Action);
        Assert.AreEqual(ActorDiagnosticAction.BlownOutChunk, capped.Profile.DyingArt(18)!.Value.Action);
        Assert.AreEqual(85 + 5 * 128, capped.Profile.DistinctKeys);
        // An ART with more frames than the decoder admits (64 a group) does not read: a typed fallback too.
        Assert.IsNull(LoadWoman(fall, getUp, ("art/critter/HMF/HMFUWXAr.art", Art(100))).Profile.Decapitation);
        // A body with no fall takes no dying art either: nothing is drawn falling (the hero lies without a drawn fall).
        var bare = LoadWoman(("art/critter/HMF/HMFUWXAr.art", Art(6)));
        Assert.IsNull(bare.Profile.Decapitation);
        Assert.IsNull(bare.Profile.DyingArt(17)); Assert.IsNull(bare.Profile.DyingArt(7));
    }

    [TestMethod]
    public void TheDownPoseWinsOverTheAttackPoseOnASettledActor()
    {
        using var fixture = new GridWireFixture();
        var actor = new DiagnosticGridActor();
        actor.Configure(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        actor.AttackPose = (ActorDiagnosticAction.Attack, 3, 4);
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
