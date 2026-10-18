using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// Combat mode in the session (ARC-COMBAT-004). The hero is the half-elf fixture (28 fatigue) at (100, 200) on map 79; NPC ids
// and facts are invented (30 hit points and 30 fatigue each).
[TestClass]
public sealed class CombatModeTests
{
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant Wolf = SessionCombatant.Critter("oid:wolf");
    private static readonly SessionCombatant Virgil = SessionCombatant.Critter("oid:virgil");
    private static readonly SessionCombatModeContext RealTime = SessionCombatModeContext.RealTime;

    private static SessionCombatantFacts Npc(LifeTraits traits = LifeTraits.None, LifeState loaded = default) =>
        new() { Speed = 7, Life = new LifeFacts { Traits = traits }, Loaded = loaded };

    private static SessionCombatModeContext For(SessionCombatantFacts facts, bool concealed = false) => new() { Facts = facts, Concealed = concealed };

    private sealed class World : ISessionCombatWorld
    {
        internal Action? DuringScan { get; set; }

        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map)
        {
            DuringScan?.Invoke();
            return [new(Hero, new(0, 0)), new(Wolf, new(105, 200))];
        }

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => Npc();
    }

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionCombatModeResult Changed(SessionCombatModeResult result, GameSessionOwner owner, bool inCombatMode)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, SessionCombatModeRefusal.None, inCombatMode),
            (result.Disposition, result.Failure, result.Refusal, result.InCombatMode));
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Refused(SessionCombatModeResult result, GameSessionOwner owner, SessionCombatModeRefusal refusal, bool inCombatMode, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, refusal, inCombatMode),
            (result.Disposition, result.Failure, result.Refusal, result.InCombatMode));
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void TheHeroEntersAndLeavesCombatModeWithOneRevisionEach()
    {
        using var owner = Owner();
        Assert.IsFalse(owner.QueryCombatMode(Hero));
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, true);
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.IsTrue(owner.QueryCombatMode(Hero));
        var again = owner.SetCombatMode(owner.Version, Hero, true, RealTime);
        Assert.AreEqual((GameSessionDisposition.NoChange, true), (again.Disposition, again.InCombatMode));
        Changed(owner.ToggleHeroCombatMode(owner.Version, RealTime, SessionCombatExit.Clear), owner, false);
        Changed(owner.ToggleHeroCombatMode(owner.Version, RealTime, SessionCombatExit.Clear), owner, true);
        Assert.AreEqual(3, owner.Version.Revision);
        Assert.IsNull(owner.QueryCombatRound(), "real time starts no round");
        var stale = owner.SetCombatMode(new GameSessionVersion(owner.Identity, 0), Hero, false, RealTime);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
    }

    [TestMethod]
    public void WithoutAHeroThereIsNoCombatModeForIt()
    {
        using var plain = Create();
        Assert.AreEqual(GameSessionFailure.CombatUnavailable, plain.ToggleHeroCombatMode(plain.Version, RealTime, SessionCombatExit.Clear).Failure);
        Assert.AreEqual(GameSessionFailure.CombatUnavailable, plain.LeaveHeroCombatMode(plain.Version, RealTime, SessionCombatExit.Clear).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCombatant, plain.SetCombatMode(plain.Version, Hero, true, RealTime).Failure);
        Assert.IsTrue(plain.QueryCanExitCombatMode(SessionCombatExit.Clear));
        Assert.ThrowsExactly<ArgumentException>(() => plain.QueryCombatMode(Hero));
        // An NPC can still be in combat mode in a heroless session.
        Changed(plain.SetCombatMode(plain.Version, Wolf, true, For(Npc())), plain, true);
    }

    [TestMethod]
    public void AnUnconsciousHeroCannotEnterButMayLeaveAndAStunnedOneCannotLeave()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 28 }).Disposition);
        Refused(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, SessionCombatModeRefusal.Inactive, false, 1);
        using var awake = Owner();
        Changed(awake.SetCombatMode(awake.Version, Hero, true, RealTime), awake, true);
        Assert.AreEqual(GameSessionDisposition.Changed, awake.ApplyDamage(awake.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Stun }).Disposition);
        Refused(awake.SetCombatMode(awake.Version, Hero, false, RealTime), awake, SessionCombatModeRefusal.Stunned, true, 2);
        // The exit gate lets an inactive hero through; the deactivation then refuses the stunned one.
        Refused(awake.LeaveHeroCombatMode(awake.Version, RealTime, SessionCombatExit.Clear), awake, SessionCombatModeRefusal.Stunned, true, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, awake.ClearStun(awake.Version, SessionLifeTarget.Hero).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, awake.ApplyDamage(awake.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 28 }).Disposition);
        Changed(awake.SetCombatMode(awake.Version, Hero, false, RealTime), awake, false);
    }

    [TestMethod]
    public void DeathEndsCombatModeAndTheKeyDoesNothingForADeadHero()
    {
        using var owner = Owner();
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, true);
        Changed(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc())), owner, true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Critter("oid:wolf", new LifeFacts(), default)).Disposition);
        Assert.IsFalse(owner.QueryCombatMode(Wolf));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        Assert.IsFalse(owner.QueryCombatMode(Hero));
        var revision = owner.Version.Revision;
        Refused(owner.ToggleHeroCombatMode(owner.Version, RealTime, SessionCombatExit.Clear), owner, SessionCombatModeRefusal.HeroDead, false, revision);
        // The source's activation skips every refusal for the dead, so a direct call still toggles.
        Changed(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc(loaded: new(32_000, 0, 0, LifeConditions.None, 0)))), owner, true);
    }

    [TestMethod]
    public void AnNpcNeedsItsFactsAndIsRefusedStonedParalysedOrConcealed()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidCombatFacts, owner.SetCombatMode(owner.Version, Wolf, true, RealTime).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCombatFacts, owner.SetCombatMode(owner.Version, Wolf, true,
            For(new SessionCombatantFacts { Speed = TurnBasedRules.MaxStat + 1, Life = new LifeFacts() })).Failure);
        Refused(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc(LifeTraits.Stoned))), owner, SessionCombatModeRefusal.Stoned, false, 0);
        Refused(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc(LifeTraits.Paralyzed))), owner, SessionCombatModeRefusal.Paralysed, false, 0);
        Refused(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc(), concealed: true)), owner, SessionCombatModeRefusal.Concealed, false, 0);
        Changed(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc())), owner, true);
        Assert.IsTrue(owner.QueryCombatMode(Wolf));
        Assert.IsFalse(owner.QueryCombatMode(Virgil));
        CollectionAssert.AreEqual(new[] { "oid:wolf" }, owner.QueryCombatModes().Critters.ToArray());
        // The hero may toggle while concealed.
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime with { Concealed = true }), owner, true);
    }

    [TestMethod]
    public void TheExitGateHoldsTheHeroWhileAFightingNpcTargetsItOrAFollower()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:virgil").Disposition);
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, true);
        var revision = owner.Version.Revision;
        SessionCombatExit Threat(SessionCombatant focus, bool active = true, bool fighting = true) => new() { Threats = [new(focus, active, fighting)] };
        Assert.IsFalse(owner.QueryCanExitCombatMode(Threat(Hero)));
        Assert.IsFalse(owner.QueryCanExitCombatMode(Threat(Virgil)), "a follower under attack");
        Assert.IsTrue(owner.QueryCanExitCombatMode(Threat(Hero, active: false)));
        Assert.IsTrue(owner.QueryCanExitCombatMode(Threat(Hero, fighting: false)));
        Assert.IsTrue(owner.QueryCanExitCombatMode(Threat(Wolf)));
        Assert.IsFalse(owner.QueryCanExitCombatMode(new SessionCombatExit { AttemptingAttack = true }));
        Refused(owner.ToggleHeroCombatMode(owner.Version, RealTime, Threat(Hero)), owner, SessionCombatModeRefusal.UnderAttack, true, revision);
        Refused(owner.LeaveHeroCombatMode(owner.Version, RealTime, Threat(Virgil)), owner, SessionCombatModeRefusal.UnderAttack, true, revision);
        // A waiting companion is not in the follower array.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "oid:virgil").Disposition);
        Assert.IsTrue(owner.QueryCanExitCombatMode(Threat(Virgil)));
        Changed(owner.LeaveHeroCombatMode(owner.Version, RealTime, Threat(Virgil)), owner, false);
        var outside = owner.LeaveHeroCombatMode(owner.Version, RealTime, Threat(Hero));
        Assert.AreEqual((GameSessionDisposition.NoChange, SessionCombatModeRefusal.None, false), (outside.Disposition, outside.Refusal, outside.InCombatMode));
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryCanExitCombatMode(new SessionCombatExit { Threats = [new(null!, true, true)] }));
    }

    [TestMethod]
    public void WithTurnBasedOnEnteringStartsARoundAndLeavingEndsIt()
    {
        using var owner = Owner();
        var turnBased = new SessionCombatModeContext { TurnBased = true, World = new World() };
        Assert.AreEqual(GameSessionFailure.InvalidCombatFacts, owner.SetCombatMode(owner.Version, Hero, true, new SessionCombatModeContext { TurnBased = true }).Failure);
        var entered = Changed(owner.SetCombatMode(owner.Version, Hero, true, turnBased), owner, true);
        Assert.AreEqual(GameSessionDisposition.Changed, entered.Round!.Disposition);
        Assert.AreEqual(2, owner.Version.Revision, "the round's revision, then the flag's");
        Assert.IsNotNull(owner.QueryCombatRound());
        var left = Changed(owner.ToggleHeroCombatMode(owner.Version, turnBased, SessionCombatExit.Clear), owner, false);
        Assert.AreEqual(GameSessionDisposition.Changed, left.Round!.Disposition);
        Assert.IsNull(owner.QueryCombatRound());

        // A world answer that moves the session makes the whole change stale; nothing changes.
        var world = new World();
        world.DuringScan = () => owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("x", "y"), 1);
        var revision = owner.Version.Revision;
        var stale = owner.SetCombatMode(owner.Version, Hero, true, new SessionCombatModeContext { TurnBased = true, World = world });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
        Assert.AreEqual((false, (SessionCombatRound?)null), (owner.QueryCombatMode(Hero), owner.QueryCombatRound()));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "only the world's own change");
    }

    [TestMethod]
    public void TheHerosDeathInARoundEndsTheRoundWithItsCombatMode()
    {
        using var owner = Owner();
        Changed(owner.SetCombatMode(owner.Version, Hero, true, new SessionCombatModeContext { TurnBased = true, World = new World() }), owner, true);
        Assert.IsNotNull(owner.QueryCombatRound());
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        Assert.AreEqual((false, (SessionCombatRound?)null, revision + 1), (owner.QueryCombatMode(Hero), owner.QueryCombatRound(), owner.Version.Revision));
        // A round the hero is not in combat mode for (started directly) stays.
        using var direct = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, direct.StartCombatRound(direct.Version, new World()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, direct.Kill(direct.Version, SessionLifeTarget.Hero).Disposition);
        Assert.IsNotNull(direct.QueryCombatRound());
    }

    [TestMethod]
    public void SwitchingTurnBasedStartsOrEndsTheRoundOfAHeroInCombatMode()
    {
        using var owner = Owner();
        var world = new World();
        var outside = owner.SwitchTurnBased(owner.Version, false, true, world);
        Assert.AreEqual((GameSessionDisposition.NoChange, true), (outside.Disposition, outside.Store));
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, true);
        Assert.IsFalse(owner.SwitchTurnBased(owner.Version, false, true, world, animationsInterruptible: false).Store);
        Assert.IsTrue(owner.SwitchTurnBased(owner.Version, true, true, world).Store, "no change stores");
        var on = owner.SwitchTurnBased(owner.Version, false, true, world);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (on.Disposition, on.Store));
        Assert.IsNotNull(owner.QueryCombatRound());
        var off = owner.SwitchTurnBased(owner.Version, true, false, world);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (off.Disposition, off.Store));
        Assert.IsNull(owner.QueryCombatRound());
        Assert.IsTrue(owner.QueryCombatMode(Hero), "the hero stays in combat mode, now in real time");
    }

    [TestMethod]
    public void ARunChargeAddsOneFatigueToACritterInCombatMode()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.CombatModeInactive, owner.ChargeCombatRun(owner.Version, SessionLifeTarget.Hero).Failure);
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, true);
        var charged = owner.ChargeCombatRun(owner.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(GameSessionDisposition.Changed, charged.Disposition);
        Assert.AreEqual(1, owner.QueryHeroLife()!.Record.State.FatigueDamage);
        Assert.AreEqual(1, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.HeroFatigue), "the 80-second recovery is scheduled");
        var wolf = SessionLifeTarget.Critter("oid:wolf", new LifeFacts(), default);
        Assert.AreEqual(GameSessionFailure.CombatModeInactive, owner.ChargeCombatRun(owner.Version, wolf).Failure);
        Changed(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc())), owner, true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ChargeCombatRun(owner.Version, wolf).Disposition);
        Assert.AreEqual(1, owner.QueryLifeRecord("oid:wolf")!.Value.State.FatigueDamage);
    }

    [TestMethod]
    public void CombatModeSurvivesCanonicalTextAndRestore()
    {
        using var owner = Owner();
        Changed(owner.SetCombatMode(owner.Version, Hero, true, RealTime), owner, true);
        Changed(owner.SetCombatMode(owner.Version, Wolf, true, For(Npc())), owner, true);
        Changed(owner.SetCombatMode(owner.Version, SessionCombatant.Critter("oid:bandit"), true, For(Npc())), owner, true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string saved = ",\"combat\":null,\"combatMode\":{\"hero\":true,\"critters\":[\"oid:bandit\",\"oid:wolf\"]},\"sound\":";
        StringAssert.Contains(text, saved);
        using var restored = Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.IsTrue(restored.QueryCombatMode(Hero) && restored.QueryCombatMode(Wolf));

        void DecodeFails(string replacement, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(saved, replacement));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), replacement);
        }
        DecodeFails(",\"combat\":null,\"combatMode\":{\"hero\":true,\"critters\":[\"oid:wolf\",\"oid:bandit\"]},\"sound\":", SnapshotDecodeFailure.NotCanonical, "$.combatMode.critters[1]");
        DecodeFails(",\"combat\":null,\"combatMode\":{\"hero\":true,\"critters\":[\"oid:wolf\",\"oid:wolf\"]},\"sound\":", SnapshotDecodeFailure.NotCanonical, "$.combatMode.critters[1]");
        DecodeFails(",\"combat\":null,\"combatMode\":{\"hero\":1,\"critters\":[]},\"sound\":", SnapshotDecodeFailure.WrongType, "$.combatMode.hero");
        DecodeFails(",\"combat\":null,\"combatMode\":{\"hero\":true},\"sound\":", SnapshotDecodeFailure.MissingField, "$.combatMode.critters");
        DecodeFails(",\"combat\":null,\"combatMode\":{\"hero\":true,\"critters\":[],\"npcs\":[]},\"sound\":", SnapshotDecodeFailure.UnknownField, "$.combatMode.npcs");

        using var golden = GoldenSession();
        var heroless = GameSessionSnapshotCodec.Encode(golden.Capture()).Replace(NoCombatMode, ",\"combatMode\":{\"hero\":true,\"critters\":[]}");
        var noHero = GameSessionOwner.Restore(Decode(heroless), RestoreOptions());
        Assert.AreEqual((null, GameSessionFailure.InvalidCombatMode), (noHero.Owner, noHero.Failure));
    }
}
