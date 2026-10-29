using Arcanum.Data.Area;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-039: the hero's low attack (21) and throw (14): which ART an attack plays and at which rate (ledger WA4, WA5,
/// WA9, MA1). Synthetic ARTs, archives and object rows only.</summary>
[TestClass]
public sealed class HeroAttackArtTests
{
    private const string Digest = "0000000000000000000000000000000000000000000000000000000000000000";
    private const uint Standing = 0x28000000u, ShortMonster = 0xC0000000u, TallMonster = 0xC0000000u | 8u << 23;

    private static AreaObjectRecord Target(uint art, uint flags = 0)
    {
        var key = new AreaObjectKey(Digest, 5051, AreaSourceKind.Mobile, null, 7, 700, Digest, 100);
        var fields = new[] { Field(0, art), Field(18, flags) };
        return new(key, 119, 16, 28359, Digest, Digest, Digest, Digest, AreaScope.Core, null, 92930, 82565,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, fields, fields, true, true, true);
    }

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static ActorDiagnosticResource Res(ActorDiagnosticAction action, uint selector, int frames, int fps) => new(action, selector, 1, 0, 8, frames, fps, fps);

    private static ActorDiagnosticProfile Profile(bool low, bool thrown, uint attackSelector = 0x20000501)
    {
        var human = ActorDiagnosticProfile.HumanFixedArea;
        return ActorDiagnosticProfile.ForHeroBody(human.Idle, human.Walk, human.Run!, human.Vault!, Res(ActorDiagnosticAction.Attack, attackSelector, 9, 15), 195,
            lowAttack: low ? Res(ActorDiagnosticAction.LowAttack, ActorDiagnosticProfile.HumanLowAttackSelector, 7, 13) : null,
            @throw: thrown ? Res(ActorDiagnosticAction.Throw, ActorDiagnosticProfile.HumanThrowSelector, 11, 12) : null);
    }

    [TestMethod]
    public void TheAttackAnimationReadsTheTargetsPlacedArtAndFlags()
    {
        // A standing critter: the attack; shrunk (object flag 0x80): the low attack; lying in a fall (7): the low attack.
        Assert.AreEqual(20, HeroAttackArt.Animation(CombatSkill.Melee, Target(Standing), null));
        Assert.AreEqual(21, HeroAttackArt.Animation(CombatSkill.Melee, Target(Standing, WeaponArtRules.ShrunkFlag), null));
        Assert.AreEqual(21, HeroAttackArt.Animation(CombatSkill.Melee, Target(Standing), 7));
        // A placed art lying (animation 9) without a host override; a bloody death (18) is no low target.
        Assert.AreEqual(21, HeroAttackArt.Animation(CombatSkill.Bow, Target(Standing | 9u << 6), null));
        Assert.AreEqual(20, HeroAttackArt.Animation(CombatSkill.Melee, Target(Standing), 18));
        // The host's animation wins over the placed art's.
        Assert.AreEqual(20, HeroAttackArt.Animation(CombatSkill.Melee, Target(Standing | 9u << 6), 0));
        // A short monster species (0) takes the low attack, a tall one (8) the attack.
        Assert.AreEqual(21, HeroAttackArt.Animation(CombatSkill.Melee, Target(ShortMonster), null));
        Assert.AreEqual(20, HeroAttackArt.Animation(CombatSkill.Melee, Target(TallMonster), null));
        // An object's art has no animation (0x503E20 answers 0): bits 6..10 of a scenery art do not make it lying.
        Assert.AreEqual(20, HeroAttackArt.Animation(CombatSkill.Melee, Target(0x50000000u | 7u << 6), null));
        // A returning weapon (the throwing skill) throws, whatever the target.
        Assert.AreEqual(14, HeroAttackArt.Animation(CombatSkill.Throwing, Target(ShortMonster, WeaponArtRules.ShrunkFlag), 7));
    }

    [TestMethod]
    public void EachAnimationPlaysItsOwnArtOrTheRepairedAttack()
    {
        var full = Profile(true, true);
        Assert.AreEqual((ActorDiagnosticAction.Attack, full.Attack!, AttackAnimation.Attack), HeroAttackArt.Clip(full, 20)!.Value);
        Assert.AreEqual((ActorDiagnosticAction.LowAttack, full.LowAttack!, AttackAnimation.AttackLow), HeroAttackArt.Clip(full, 21)!.Value);
        Assert.AreEqual((ActorDiagnosticAction.Throw, full.Throw!, AttackAnimation.Throw), HeroAttackArt.Clip(full, 14)!.Value);
        // MA1: without a low attack ART the attack's plays it, at the low attack's rate (the art id keeps 21); no throw ART, no attack.
        var plain = Profile(false, false);
        Assert.AreEqual((ActorDiagnosticAction.Attack, plain.Attack!, AttackAnimation.AttackLow), HeroAttackArt.Clip(plain, 21)!.Value);
        Assert.IsNull(HeroAttackArt.Clip(plain, 14));
        // A weapon the repair does not list (9) has no low attack to fall back on.
        Assert.IsNull(HeroAttackArt.Clip(Profile(false, false, 0x20000509), 21));
        Assert.IsNotNull(HeroAttackArt.Clip(Profile(false, false, 0x2000050D), 21), "the staff (13) is repaired");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HeroAttackArt.Clip(full, 7));
        // The fixed human stand-in (not a hero body) has neither: it plays its attack ART at the chosen animation's rate.
        var standIn = ActorDiagnosticProfile.HumanFixedArea;
        Assert.AreEqual((ActorDiagnosticAction.Attack, standIn.Attack!, AttackAnimation.Throw), HeroAttackArt.Clip(standIn, 14)!.Value);
        Assert.AreEqual((ActorDiagnosticAction.Attack, standIn.Attack!, AttackAnimation.AttackLow), HeroAttackArt.Clip(standIn, 21)!.Value);
        // The throw keeps its ART's rate (WA9), the low attack takes the Speed table.
        Assert.AreEqual(12, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Throw, AttackArtWeapon.Unarmed, 20, full.Throw!.RawFramesPerSecond));
        Assert.AreEqual(13, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.AttackLow, AttackArtWeapon.Unarmed, 8, plain.Attack!.RawFramesPerSecond));
    }

    [TestMethod]
    public void TheProfileCarriesTheLowAttackAndThrowAfterTheFalls()
    {
        var full = Profile(true, true);
        CollectionAssert.AreEqual(new[] { ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run, ActorDiagnosticAction.Vault,
            ActorDiagnosticAction.Attack, ActorDiagnosticAction.LowAttack, ActorDiagnosticAction.Throw }, full.Actions.ToArray());
        Assert.AreEqual(ActorDiagnosticAction.LowAttack, full.ActionFor(ActorDiagnosticProfile.HumanLowAttackSelector));
        Assert.AreEqual(ActorDiagnosticAction.Throw, full.ActionFor(ActorDiagnosticProfile.HumanThrowSelector));
        Assert.AreEqual(ActorDiagnosticProfile.HumanThrowSelector, ActorDiagnosticProfile.HumanSelector(ActorDiagnosticAction.Throw));
        Assert.AreEqual(ActorDiagnosticProfile.HumanLowAttackSelector, ActorDiagnosticProfile.HumanSelector(ActorDiagnosticAction.LowAttack));
        var plain = Profile(false, false);
        Assert.AreEqual(5, plain.Actions.Count);
        Assert.ThrowsExactly<TerrainViewerException>(() => plain.Resource(ActorDiagnosticAction.LowAttack));
        Assert.ThrowsExactly<TerrainViewerException>(() => plain.Resource(ActorDiagnosticAction.Throw));
        Assert.IsNull(ActorDiagnosticProfile.HumanFixedArea.LowAttack);
        Assert.IsNull(ActorDiagnosticProfile.HumanFixedArea.Throw);
    }

    /// <summary>The files (MA2, MA3): the low attack keeps weapon and shield, the throw (14) drops both; MA1 tries the attack after a
    /// missing low attack of a repaired weapon.</summary>
    [TestMethod]
    public void TheLowAttackKeepsTheWeaponAndTheThrowDropsIt()
    {
        var man = HeroBody.For(CharacterRace.Human, CharacterGender.Male)!.Value;
        var shielded = man.Armed(true, 0x60000000u | 2u << 6, WeaponArtRules.ArmorItemType)!.Value;
        var low = shielded.Selector(ActorDiagnosticProfile.HumanLowAttackSelector);
        Assert.AreEqual("art/critter/HMM/HMMUWSCv.art", shielded.ArtPath(low));
        Assert.AreEqual("art/critter/HMM/HMMUWXAo.art", shielded.ArtPath(shielded.Selector(ActorDiagnosticProfile.HumanThrowSelector)));
        // Out of combat mode the low attack keeps the human's bare hands (nothing attacks out of combat mode).
        Assert.AreEqual("art/critter/HMM/HMMUWXBv.art", man.ArtPath(man.Selector(ActorDiagnosticProfile.HumanLowAttackSelector)));
        var attack = shielded.Selector(ActorDiagnosticProfile.HumanFixedArea.Attack!.Selector);
        CollectionAssert.AreEqual(new[] { low, attack }, shielded.Candidates(ActorDiagnosticProfile.HumanLowAttackSelector).ToArray());
        // Weapon 9 is not one of the ten repaired low attacks: no fallback; the throw is never repaired.
        var nine = man with { Weapon = 9 };
        Assert.AreEqual(1, nine.Candidates(ActorDiagnosticProfile.HumanLowAttackSelector).Count);
        Assert.AreEqual(1, shielded.Candidates(ActorDiagnosticProfile.HumanThrowSelector).Count);
    }

    [TestMethod]
    public void ABodyReadsItsLowAttackAndThrowWhenTheyRead()
    {
        // Five used groups: 50 keys for the five ARTs, then 5 × 4 for the low attack and 5 × 6 for the throw.
        var both = AreaHeroKnockdownTests.LoadWoman(("art/critter/HMF/HMFUWXBv.art", AreaHeroKnockdownTests.Art(4, 9, actionFrame: 2)),
            ("art/critter/HMF/HMFUWXAo.art", AreaHeroKnockdownTests.Art(6, 12, actionFrame: 3)));
        Assert.AreEqual((4, 9, 2, ActorDiagnosticProfile.HumanLowAttackSelector), (both.Profile.LowAttack!.FramesPerGroup,
            both.Profile.LowAttack.RawFramesPerSecond, both.Profile.LowAttack.ActionFrame, both.Profile.LowAttack.Selector));
        Assert.AreEqual((6, 12, 3, ActorDiagnosticProfile.HumanThrowSelector), (both.Profile.Throw!.FramesPerGroup,
            both.Profile.Throw.RawFramesPerSecond, both.Profile.Throw.ActionFrame, both.Profile.Throw.Selector));
        Assert.AreEqual((100, 100), (both.Profile.DistinctKeys, both.Textures.Count));
        Assert.AreEqual(ActorDiagnosticAction.Throw, both.Plan(new ActorDiagnosticPlacementInput(6, 1, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached,
            100, false, ActorDiagnosticAction.Throw, 5, 5, 0, 0, 0, 0, 0, 0)).Current.Action);
        // Missing (the repair's attack plays the low attack), unreadable or with an action frame outside its frames: loaded without.
        foreach (var load in new[] { AreaHeroKnockdownTests.LoadWoman(),
            AreaHeroKnockdownTests.LoadWoman(("art/critter/HMF/HMFUWXBv.art", AreaHeroKnockdownTests.Art(4, actionFrame: 4)),
                ("art/critter/HMF/HMFUWXAo.art", AreaHeroKnockdownTests.Art(6, palette: false))) })
        {
            Assert.IsNull(load.Profile.LowAttack); Assert.IsNull(load.Profile.Throw);
            Assert.AreEqual((50, 5), (load.Textures.Count, load.Profile.Actions.Count));
        }
    }

    [TestMethod]
    public void TheAttackPoseDrawsTheChosenAction()
    {
        using var fixture = new GridWireFixture();
        var actor = new DiagnosticGridActor();
        actor.Configure(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        actor.AttackPose = (ActorDiagnosticAction.LowAttack, 2, 3);
        Assert.AreEqual((ActorDiagnosticAction.LowAttack, 2, 3), (actor.State.Action, actor.State.Facing, actor.CurrentFrame));
        actor.AttackPose = (ActorDiagnosticAction.Throw, 4, 1);
        Assert.AreEqual((ActorDiagnosticAction.Throw, 4, 1), (actor.State.Action, actor.State.Facing, actor.CurrentFrame));
    }
}
