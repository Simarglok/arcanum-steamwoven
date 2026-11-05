using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The NPC danger state in the session (ARC-WORLD-079, AI slice I3). The hero is the half-elf fixture; NPC ids, reaction facts and
// AI parameter 10 (30) are invented.
[TestClass]
public sealed class DangerStateTests
{
    private const string Wolf = "oid:wolf", Virgil = "oid:virgil", Sogg = "oid:sogg";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant WolfHandle = SessionCombatant.Critter(Wolf), VirgilHandle = SessionCombatant.Critter(Virgil),
        SoggHandle = SessionCombatant.Critter(Sogg);
    private static readonly ReactionNpcFacts Npc60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.HalfElf, 0, 0, [], [], [], false);
    private static readonly SessionDangerReaction Reaction = new(Npc60, Pc, 30);
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionDangerRequest Fight(string entity, SessionCombatant? target) =>
        new() { Entity = entity, State = DangerState.CombatFocus, Target = target, Reaction = Reaction };

    private static SessionDangerResult Changed(SessionDangerResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Rejected(SessionDangerResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    private static void Join(GameSessionOwner owner, string entity) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, entity, PartyStateTests.Facts(maxFollowers: 5), true).Disposition);

    [TestMethod]
    public void AFightOnAnNpcEntersCombatAndAHookNamesTheTarget()
    {
        using var owner = Owner();
        Assert.AreEqual((DangerState.None, (SessionCombatant?)null), (owner.QueryDanger(Wolf).State, owner.QueryDanger(Wolf).Source));
        var fight = Changed(owner.SetDangerState(owner.Version, Fight(Wolf, VirgilHandle) with { AlertSound = true }), owner, 1);
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.EnterCombat, VirgilHandle, Wolf), fight.Hook);
        Assert.IsTrue(fight.Transition!.Value.AlertSound);
        Assert.IsNull(fight.Reaction, "the target is not the hero");
        var record = owner.QueryDanger(Wolf);
        Assert.AreEqual((DangerState.CombatFocus, VirgilHandle, DangerFlags.Fighting | DangerFlags.CheckWield | DangerFlags.CheckGrenade),
            (record.State, record.Source, record.Flags));

        var again = owner.SetDangerState(owner.Version, Fight(Wolf, VirgilHandle));
        Assert.AreEqual((GameSessionDisposition.NoChange, (SessionDangerHook?)null), (again.Disposition, again.Hook));
        Assert.IsFalse(again.Transition!.Value.AlertSound, "no sound when not asked");
        Assert.AreEqual(1, owner.Version.Revision);

        // A new target moves the focus without a hook; a calm-down leaves the fight with the exit hook.
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, SoggHandle)), owner, 2);
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Wolf).Focus);
        var calm = Changed(owner.SetDangerState(owner.Version, new() { Entity = Wolf, State = DangerState.None }), owner, 3);
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.ExitCombat, null, Wolf), calm.Hook);
        Assert.AreEqual((DangerState.None, DangerFlags.CheckWield | DangerFlags.CheckGrenade | DangerFlags.DemaintainSpells, SoggHandle),
            (owner.QueryDanger(Wolf).State, owner.QueryDanger(Wolf).Flags, owner.QueryDanger(Wolf).Focus), "the focus field stays");
    }

    [TestMethod]
    public void AFightOnTheHeroLowersTheReactionInTheSameRevision()
    {
        using var owner = Owner();
        Rejected(owner.SetDangerState(owner.Version, Fight(Wolf, Hero) with { Reaction = null }), owner, GameSessionFailure.InvalidDangerFacts, 0);
        Assert.AreEqual(DangerState.None, owner.QueryDanger(Wolf).State);
        var hostile = ReactionRules.Effective(60, Npc60, Pc, ReactionQuery.Hostility);
        var fight = Changed(owner.SetDangerState(owner.Version, Fight(Wolf, Hero)), owner, 1);
        Assert.AreEqual(30 - hostile, fight.Reaction!.Value.Delta);
        Assert.AreEqual(60 + 30 - hostile, owner.QueryReactionRecord(Wolf)!.Value.Remembered);
        Assert.AreEqual(Hero, owner.QueryDanger(Wolf).Focus);
        // Lowered once: the next fight call finds it at parameter 10 and changes nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetDangerState(owner.Version, Fight(Wolf, Hero)).Disposition);

        // The hero on the NPC's enemy list makes the reaction read 0, so nothing is lowered.
        Changed(owner.AddDangerEnemy(owner.Version, Sogg, Hero), owner, 2);
        var listed = Changed(owner.SetDangerState(owner.Version, Fight(Sogg, Hero)), owner, 3);
        Assert.IsNull(listed.Reaction);
        Assert.IsNull(owner.QueryReactionRecord(Sogg));
    }

    [TestMethod]
    public void ASurrenderLeavesCombatModeAndCl4KeepsTheBitInTheOriginal()
    {
        foreach (var (rules, keeps) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            using var owner = Owner(rules);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, WolfHandle, true, Living).Disposition);
            Changed(owner.LockDangerTarget(owner.Version, Fight(Wolf, Hero)), owner, 2);
            var surrender = new SessionDangerRequest { Entity = Wolf, State = DangerState.Surrender, Target = Hero };
            Rejected(owner.SetDangerState(owner.Version, surrender), owner, GameSessionFailure.InvalidDangerFacts, 2);
            var result = Changed(owner.SetDangerState(owner.Version, surrender with { CombatMode = Living }), owner, 3);
            Assert.IsTrue(result.LeftCombatMode);
            Assert.AreEqual(keeps, owner.QueryCombatMode(WolfHandle), rules.ToString());
            var record = owner.QueryDanger(Wolf);
            Assert.AreEqual((DangerState.Surrender, Hero), (record.State, record.Source));
            Assert.IsFalse(record.Flags.HasFlag(DangerFlags.TargetLocked), "the toggle unlocks in both settings");
            Assert.IsTrue(record.Flags.HasFlag(DangerFlags.DemaintainSpells));
        }
    }

    [TestMethod]
    public void AToggleThatRefusesKeepsTheBitAndOnlyFo14DecidesTheLock()
    {
        foreach (var (rules, locked) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            using var owner = Owner(rules);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, WolfHandle, true, Living).Disposition);
            Changed(owner.LockDangerTarget(owner.Version, Fight(Wolf, Hero)), owner, 2);
            var stoned = new SessionCombatModeContext { Facts = new() { Speed = 7, Life = new LifeFacts { Traits = LifeTraits.Stoned } } };
            var result = Changed(owner.SetDangerState(owner.Version, new() { Entity = Wolf, State = DangerState.Surrender, Target = Hero, CombatMode = stoned }), owner, 3);
            Assert.IsFalse(result.LeftCombatMode);
            Assert.IsTrue(owner.QueryCombatMode(WolfHandle));
            // No toggle, no unlock: Steamwoven's FO14 fix clears the lock with the applied SURRENDER, the original keeps it.
            Assert.AreEqual(locked, owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.TargetLocked), rules.ToString());
        }
    }

    [TestMethod]
    public void DeathLeavesCombatModeAndUnlocks()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, WolfHandle, true, Living).Disposition);
        Assert.IsTrue(Changed(owner.LockDangerTarget(owner.Version, Fight(Wolf, Hero)), owner, 2).Locked);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Critter(Wolf, new LifeFacts(), default)).Disposition);
        Assert.IsFalse(owner.QueryCombatMode(WolfHandle));
        Assert.AreEqual(DangerState.CombatFocus, owner.QueryDanger(Wolf).State);
        Assert.IsFalse(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.TargetLocked));
    }

    [TestMethod]
    public void AFightOfAFollowerOnTheHeroArmsItsLeaderCheckAndTheReactionBudgetRefusesAll()
    {
        using var owner = Owner();
        Join(owner, Virgil);
        var revision = owner.Version.Revision;
        var fight = Changed(owner.SetDangerState(owner.Version, Fight(Virgil, Hero)), owner, revision + 1);
        Assert.IsTrue(fight.Reaction!.Value.LeaderCheck);
        Assert.AreEqual(CompanionLoyaltyRules.ArmReaction(CompanionLoyalty.None), owner.QueryCompanion(Virgil).Loyalty);

        using var tight = GameSessionOwner.Create(Descriptor(new GameSessionLimits(maxReactionRecords: 1)) with { Hero = HalfElf() }).Owner!;
        Changed(tight.SetDangerState(tight.Version, Fight(Sogg, Hero)), tight, 1);
        Rejected(tight.SetDangerState(tight.Version, Fight(Wolf, Hero)), tight, GameSessionFailure.BudgetExceeded, 1);
        Assert.AreEqual(DangerState.None, tight.QueryDanger(Wolf).State, "nothing of the refused call is applied");
    }

    [TestMethod]
    public void ALockHoldsTheFocusUntilUnlockedOrCombatModeEnds()
    {
        using var owner = Owner();
        Rejected(owner.LockDangerTarget(owner.Version, Fight(Wolf, null)), owner, GameSessionFailure.InvalidDangerTarget, 0);
        Rejected(owner.LockDangerTarget(owner.Version, Fight(Wolf, VirgilHandle) with { State = DangerState.Flee }), owner, GameSessionFailure.InvalidDangerFacts, 0);
        var locked = Changed(owner.LockDangerTarget(owner.Version, Fight(Wolf, VirgilHandle)), owner, 1);
        Assert.IsTrue(locked.Locked);
        Assert.AreEqual(DangerScriptPoint.EnterCombat, locked.Hook!.Value.Point);
        var ignored = owner.SetDangerState(owner.Version, Fight(Wolf, SoggHandle));
        Assert.AreEqual((GameSessionDisposition.NoChange, DangerSetterRule.TargetLocked), (ignored.Disposition, ignored.Transition!.Value.Rule));
        Assert.AreEqual(VirgilHandle, owner.QueryDanger(Wolf).Focus);
        // A new lock moves it (unlock first); an unlock clears it.
        Assert.IsTrue(Changed(owner.LockDangerTarget(owner.Version, Fight(Wolf, SoggHandle)), owner, 2).Locked);
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Wolf).Focus);
        Changed(owner.UnlockDangerTarget(owner.Version, Wolf), owner, 3);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.UnlockDangerTarget(owner.Version, Wolf).Disposition);

        // Leaving combat mode unlocks (0x4B411C).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, WolfHandle, true, Living).Disposition);
        Changed(owner.LockDangerTarget(owner.Version, Fight(Wolf, VirgilHandle)), owner, 5);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, WolfHandle, false, Living).Disposition);
        Assert.IsFalse(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.TargetLocked));
        Assert.AreEqual(DangerState.CombatFocus, owner.QueryDanger(Wolf).State);
    }

    [TestMethod]
    public void ALockThatTheSetterRefusesDoesNotHold()
    {
        using var owner = Owner();
        var refused = owner.LockDangerTarget(owner.Version, Fight(Wolf, VirgilHandle) with { Facts = new(NpcFightingOff: true) });
        Assert.AreEqual((GameSessionDisposition.NoChange, false, DangerSetterRule.FightingOff), (refused.Disposition, refused.Locked, refused.Transition!.Value.Rule));
        Assert.AreEqual(DangerFlags.None, owner.QueryDanger(Wolf).Flags);
    }

    [TestMethod]
    public void TheTeamOfTheHeroIsTheHeroAndItsFollowersAndFollowersSkipTheirOwnSide()
    {
        using var owner = Owner();
        Join(owner, Virgil);
        Join(owner, Sogg);
        var revision = owner.Version.Revision;
        Changed(owner.AddDangerEnemyTeam(owner.Version, Wolf, VirgilHandle), owner, revision + 1);
        CollectionAssert.AreEqual(new[] { Hero, VirgilHandle, SoggHandle }, owner.QueryDanger(Wolf).Enemies.ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AddDangerEnemyTeam(owner.Version, Wolf, Hero).Disposition, "all listed");
        // A follower of the hero never lists its own side; an NPC offender is its own team.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AddDangerEnemyTeam(owner.Version, Virgil, Hero).Disposition);
        Changed(owner.AddDangerEnemyTeam(owner.Version, Virgil, WolfHandle), owner, revision + 2);
        CollectionAssert.AreEqual(new[] { WolfHandle }, owner.QueryDanger(Virgil).Enemies.ToArray());

        // The remove fills the hole with the last entry.
        Changed(owner.RemoveDangerEnemy(owner.Version, Wolf, Hero), owner, revision + 3);
        CollectionAssert.AreEqual(new[] { SoggHandle, VirgilHandle }, owner.QueryDanger(Wolf).Enemies.ToArray());
        Changed(owner.SetDangerHitBy(owner.Version, Wolf, SoggHandle), owner, revision + 4);
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Wolf).HitBy);
        Rejected(owner.AddDangerEnemy(owner.Version, "bad id", Hero), owner, GameSessionFailure.InvalidDangerTarget, revision + 4);
    }

    [TestMethod]
    public void TheDangerRecordsRoundTripAndTheHeroEntersCombatModeAllowingItsFollowers()
    {
        using var owner = Owner();
        Join(owner, Virgil);
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, Hero)), owner, 2);
        Changed(owner.AddDangerEnemyTeam(owner.Version, Wolf, Hero), owner, 3);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        // Schema 65: the danger section holds the bits; the links are the rows of objects.links, on the map the hero stood on (ARC-DEV-095).
        StringAssert.Contains(text, ",\"danger\":{\"records\":[{\"entity\":\"oid:wolf\",\"flags\":[\"Fighting\",\"CheckWield\",\"CheckGrenade\"]}]},\"sound\"");
        StringAssert.Contains(text, ",\"links\":[{\"entity\":\"oid:wolf\",\"property\":\"link.enemies\",\"map\":79,\"targets\":[\"\",\"oid:virgil\"]}," +
            "{\"entity\":\"oid:wolf\",\"property\":\"link.focus\",\"map\":79,\"targets\":[\"\"]}]}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(Hero, restored.QueryDanger(Wolf).Focus);

        // A follower with NO_ATTACK (set by stop attacking, slice I4) may attack again when the hero enters combat mode.
        using var stopped = Restore(Decode(text.Replace("\"danger\":{\"records\":[{\"entity\":\"oid:wolf\"",
            "\"danger\":{\"records\":[{\"entity\":\"oid:virgil\",\"flags\":[\"NoAttack\"]},{\"entity\":\"oid:wolf\"")));
        Assert.AreEqual(DangerFlags.NoAttack, stopped.QueryDanger(Virgil).Flags);
        var refused = stopped.SetDangerState(stopped.Version, Fight(Virgil, WolfHandle));
        Assert.AreEqual((GameSessionDisposition.NoChange, DangerState.None), (refused.Disposition, refused.Transition!.Value.Applied));
        Assert.AreEqual(GameSessionDisposition.Changed, stopped.SetCombatMode(stopped.Version, Hero, true, SessionCombatModeContext.RealTime).Disposition);
        Assert.AreEqual(DangerFlags.None, stopped.QueryDanger(Virgil).Flags);
        Assert.AreEqual(0, stopped.QueryDangerRecords().Records.Count(record => record.Entity == Virgil), "a baseline record is dropped");
        Assert.AreEqual(GameSessionDisposition.NoChange, stopped.AllowAttacking(stopped.Version, VirgilHandle).Disposition);
        // One led NPC alone; an NPC the hero does not lead keeps NO_ATTACK.
        var noAttack = text.Replace("\"CheckGrenade\"]}", "\"CheckGrenade\",\"NoAttack\"]}")
            .Replace("\"danger\":{\"records\":[", "\"danger\":{\"records\":[{\"entity\":\"oid:virgil\",\"flags\":[\"NoAttack\"]},");
        using var single = Restore(Decode(noAttack));
        Assert.AreEqual(GameSessionDisposition.NoChange, single.AllowAttacking(single.Version, WolfHandle).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, single.AllowAttacking(single.Version, VirgilHandle).Disposition);
        Assert.AreEqual(DangerFlags.None, single.QueryDanger(Virgil).Flags);
        Assert.IsTrue(single.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.NoAttack));
        Rejected(single.UnlockDangerTarget(single.Version, null!), single, GameSessionFailure.InvalidDangerTarget, single.Version.Revision);
        Rejected(single.AllowAttacking(single.Version, SessionCombatant.Critter("bad id")), single, GameSessionFailure.InvalidDangerTarget, single.Version.Revision);

        // The last revision refuses a change and applies nothing.
        using var full = Restore(Decode(System.Text.RegularExpressions.Regex.Replace(text, "\"module\":(\"[^\"]*\"),\"revision\":\\d+,",
            "\"module\":$1,\"revision\":" + long.MaxValue + ",")));
        Rejected(full.SetDangerHitBy(full.Version, Sogg, Hero), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Assert.IsNull(full.QueryDanger(Sogg).HitBy);
    }

    [TestMethod]
    public void RestoreRefusesBadDangerRecords()
    {
        using var owner = Owner();
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, Hero)), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange,
            GameSessionSnapshotCodec.TryDecode(text.Replace("\"flags\":[\"Fighting\",", "\"flags\":[\"Angry\",")).Failure);
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical,
            GameSessionSnapshotCodec.TryDecode(text.Replace("[\"Fighting\",\"CheckWield\"", "[\"CheckWield\",\"Fighting\"")).Failure);
        var baseline = Decode(text.Replace("\"flags\":[\"Fighting\",\"CheckWield\",\"CheckGrenade\"]", "\"flags\":[]"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(baseline, RestoreOptions()).Failure);
        const string EnemyRow = "\"links\":[{\"entity\":\"oid:wolf\",\"property\":\"link.enemies\",\"map\":79,\"targets\":";
        var twice = Decode(text.Replace("\"links\":[", EnemyRow + "[\"oid:a\",\"oid:a\"]},"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(twice, RestoreOptions()).Failure);
        var many = Decode(text.Replace("\"links\":[", EnemyRow + "[\"oid:a\",\"oid:b\"]},"));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameSessionOwner.Restore(many, RestoreOptions(new GameSessionLimits(maxDangerEnemies: 1))).Failure);
    }

    [TestMethod]
    public void TheBudgetsRefuseANewRecordOrEnemy()
    {
        using var owner = GameSessionOwner.Create(Descriptor(new GameSessionLimits(maxDangerRecords: 1, maxDangerEnemies: 1)) with { Hero = HalfElf() }).Owner!;
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, Hero), owner, 1);
        Rejected(owner.AddDangerEnemy(owner.Version, Wolf, VirgilHandle), owner, GameSessionFailure.BudgetExceeded, 1);
        Rejected(owner.SetDangerHitBy(owner.Version, Virgil, Hero), owner, GameSessionFailure.BudgetExceeded, 1);
        Changed(owner.RemoveDangerEnemy(owner.Version, Wolf, Hero), owner, 2);
        Assert.AreEqual(0, owner.QueryDangerRecords().Records.Count);
        Changed(owner.SetDangerHitBy(owner.Version, Virgil, Hero), owner, 3);
    }
}
