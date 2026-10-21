using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-045: the wait timer and follower loyalty in the session (ARC-RES-064 OL17–OL26). NPCs, AI rows and reactions are
// invented; the session clock starts at 15:00 (T0) on the exterior. The PC facts are those of PartyStateTests (charisma 8, no
// Persuasion, alignment 0) over a made-up AI row (dislike ≤ −20, alignment gaps 300/200).
[TestClass]
public sealed class CompanionLoyaltyStateTests
{
    private const string Npc = "oid:sogg";
    private const long T0 = 54_000_000, Minute = 60_000, Hour = 60 * Minute, Day = 24 * Hour;
    private static readonly ReactionNpcFacts Human50 = new(50, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.Human, 0, 0, [], [], [], false);
    private const CompanionLoyalty ReactionMarks = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestReaction;

    private static PartyWaitFacts Wait(int index = 3, int charisma = 8, PersuasionTraining persuasion = PersuasionTraining.None,
        bool mindControlled = false) => new(index, persuasion, charisma, mindControlled);

    private static PartyConsentFacts Facts(int reaction = 50, int pcAlignment = 0, int charisma = 8) =>
        PartyStateTests.Facts(reaction: reaction) with { PcAlignment = pcAlignment, PcCharisma = charisma };

    // A host that knows every NPC: its life facts, and the reaction facts of a wait timeout unless told otherwise.
    private sealed class World(bool reactions = true) : ISessionPartyEventWorld
    {
        public int NpcQuestions { get; private set; }
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => new();
        public ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) { NpcQuestions++; return reactions ? Human50 : null; }
        public ReactionPcFacts? ReactionPcFacts() => reactions ? Pc : null;
    }

    // A host that answers life facts only (no party facts).
    private sealed class PlainWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static GameSessionOwner Waiting(PartyWaitFacts? timer, out long revision, GameSessionOwner? existing = null)
    {
        var owner = existing ?? Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        var before = owner.Version.Revision;
        Changed(owner.WaitParty(owner.Version, Npc, timer), owner, before + 1);
        revision = owner.Version.Revision;
        return owner;
    }

    [TestMethod]
    public void A_wait_schedules_the_timer_of_its_index_on_the_actors_map()
    {
        (int Index, long? Delay)[] cases = [(0, 1), (1, 15 * Minute), (2, 2 * Hour), (3, Day), (4, 7 * Day), (5, 30 * Day), (6, null), (9, null)];
        foreach (var (index, delay) in cases)
        {
            using var owner = Waiting(Wait(index), out _);
            Assert.AreEqual(delay is { } d ? T0 + d : null, owner.QueryWaitTimer(Npc), $"index {index}");
            if (delay is null) { Assert.AreEqual(0, owner.QueryEvents().Count); continue; }
            var pending = owner.QueryEvents().Single();
            Assert.AreEqual((SessionEventKinds.PartyWait, Exterior.RuntimeMap, Npc), (pending.Kind, pending.ScopeMap, pending.Payload.Entities[0].Code));
            CollectionAssert.AreEqual(new long[] { T0, delay == 1 ? 0 : delay.Value, 8 }, pending.Payload.Values.ToArray(), "scheduled at, duration, charisma");
        }
    }

    [TestMethod]
    public void Persuasion_training_adds_one_step_before_the_cut()
    {
        using var apprentice = Waiting(Wait(0, persuasion: PersuasionTraining.Apprentice), out _);
        Assert.AreEqual(T0 + 15 * Minute, apprentice.QueryWaitTimer(Npc));
        using var master = Waiting(Wait(5, persuasion: PersuasionTraining.Master), out _);
        Assert.IsNull(master.QueryWaitTimer(Npc), "index 5 has none with any training");
        // With a hero, its training and charisma stand in for the caller's: master persuasion and charisma 8.
        using var hero = Waiting(Wait(0, charisma: 25), out _, HeroOwner(HeroSkillsTests.Skilled()));
        Assert.AreEqual(T0 + 15 * Minute, hero.QueryWaitTimer(Npc));
        Assert.AreEqual(8, hero.QueryEvents().Single().Payload.Values[2]);
        using var plain = Waiting(Wait(0, persuasion: PersuasionTraining.Master), out _, HeroOwner(HalfElf()));
        Assert.AreEqual(T0 + 1, plain.QueryWaitTimer(Npc), "the hero has no training: index 0 stays 0");
    }

    [TestMethod]
    public void No_timer_under_extraordinary_charisma_mind_control_or_without_facts()
    {
        using var charming = Waiting(Wait(charisma: 20), out _);
        Assert.AreEqual(0, charming.QueryEvents().Count);
        using var almost = Waiting(Wait(charisma: 19), out _);
        Assert.AreEqual(T0 + Day, almost.QueryWaitTimer(Npc));
        using var controlled = Waiting(Wait(mindControlled: true), out _);
        Assert.AreEqual(0, controlled.QueryEvents().Count);
        using var untimed = Waiting(null, out _);
        Assert.AreEqual(CompanionStanding.Waiting, untimed.QueryCompanion(Npc).Standing);
        Assert.AreEqual(0, untimed.QueryEvents().Count);
        // An undefined training is refused before anything changes.
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Rejected(owner.WaitParty(owner.Version, Npc, Wait(persuasion: (PersuasionTraining)9)), owner, GameSessionFailure.InvalidConsentFacts, 1);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion(Npc).Standing);
    }

    [TestMethod]
    public void The_timeout_jilts_after_the_reaction_change_made_while_the_pc_still_leads()
    {
        using var owner = Waiting(Wait(1), out var revision);
        Advance(owner, 15 * Minute - 1);
        var world = new World();
        Assert.AreEqual(0, owner.ProcessEvents(world).Waits.Count);
        Advance(owner, 1);
        var pass = owner.ProcessEvents(world);
        var firing = pass.Waits.Single();
        Assert.AreEqual((Npc, 8, GameSessionFailure.None), (firing.Entity, firing.LeaderCharisma, firing.ReactionFailure));
        // 2 × (8 − 20) = −24 with the leader still set: the follower's leader check is asked and its marks armed (OL22).
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -24, true), firing.Reaction);
        Assert.AreEqual(new SessionReaction(Npc, 26, null), owner.QueryReactionRecord(Npc));
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Free, false, false, true, ReactionMarks), owner.QueryCompanion(Npc));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        Assert.AreEqual(0, owner.QueryEvents().Count);
        // The marks stay until it follows again (one stale test then).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Assert.AreEqual(ReactionMarks, owner.QueryCompanion(Npc).Loyalty);
    }

    [TestMethod]
    public void A_concealed_follower_that_leaves_by_wait_expiry_or_loyalty_is_no_longer_concealed_and_the_save_loads()
    {
        // ARC-SKILLS-008 (ST2): every leave clears the follower's concealment, so the session's own save restores.
        using var waiter = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionDisposition.Changed, waiter.ForceJoinParty(waiter.Version, Npc).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, waiter.ToggleHeroConcealment(waiter.Version).Disposition);
        var before = waiter.Version.Revision;
        Changed(waiter.WaitParty(waiter.Version, Npc, Wait(0)), waiter, before + 1);
        Assert.IsTrue(waiter.IsConcealed(SessionCombatant.Critter(Npc)), "waiting keeps it");
        Advance(waiter, 1);
        Assert.AreEqual(Npc, waiter.ProcessEvents(new World()).Waits.Single().Entity);
        Assert.IsFalse(waiter.IsConcealed(SessionCombatant.Critter(Npc)));
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(GameSessionSnapshotCodec.Encode(waiter.Capture())), RestoreOptions()).Failure);

        using var leaver = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionDisposition.Changed, leaver.JoinParty(leaver.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, leaver.ToggleHeroConcealment(leaver.Version).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, leaver.AdjustReaction(leaver.Version, Npc, Human50, Pc, -5).Disposition);
        Assert.AreEqual(LoyaltyStepKind.Left, leaver.RunLoyaltyStep(leaver.Version, Npc, Facts(pcAlignment: 301), false).Step!.Value.Kind);
        Assert.IsFalse(leaver.IsConcealed(SessionCombatant.Critter(Npc)));
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(GameSessionSnapshotCodec.Encode(leaver.Capture())), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void A_zero_index_timer_jilts_at_the_next_pass()
    {
        using var owner = Waiting(Wait(0), out _);
        Assert.AreEqual(0, owner.ProcessEvents(new World()).Waits.Count, "stored as 1 ms: not due at once");
        Advance(owner, 1);
        Assert.AreEqual(Npc, owner.ProcessEvents(new World()).Waits.Single().Entity);
        Assert.IsTrue(owner.QueryCompanion(Npc).Jilted);
    }

    [TestMethod]
    public void A_host_without_reaction_facts_jilts_without_the_reaction_change()
    {
        using var owner = Waiting(Wait(1), out _);
        Advance(owner, 15 * Minute);
        // A plain host keeps the waiting NPC's event (the session records it waiting) and gives no facts.
        var firing = owner.ProcessEvents(new PlainWorld()).Waits.Single();
        Assert.AreEqual((null, GameSessionFailure.InvalidReactionFacts), (firing.Reaction, firing.ReactionFailure));
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Free, false, false, true), owner.QueryCompanion(Npc));
        Assert.IsNull(owner.QueryReactionRecord(Npc));

        // A charisma whose change would leave the reaction bounds jilts without it.
        using var odd = Waiting(Wait(1, charisma: int.MinValue), out _);
        Advance(odd, 15 * Minute);
        Assert.AreEqual(GameSessionFailure.InvalidReactionAmount, odd.ProcessEvents(new World()).Waits.Single().ReactionFailure);
        Assert.IsTrue(odd.QueryCompanion(Npc).Jilted);
        Assert.IsNull(odd.QueryReactionRecord(Npc));

        using var silent = Waiting(Wait(1), out _);
        Advance(silent, 15 * Minute);
        Assert.AreEqual(GameSessionFailure.InvalidReactionFacts, silent.ProcessEvents(new World(reactions: false)).Waits.Single().ReactionFailure);
        Assert.IsTrue(silent.QueryCompanion(Npc).Jilted);
    }

    [TestMethod]
    public void A_hero_leader_is_read_again_when_the_timer_fires()
    {
        using var owner = Waiting(Wait(1), out _, HeroOwner(HalfElf()));
        Advance(owner, 15 * Minute);
        var firing = owner.ProcessEvents(new World()).Waits.Single();
        Assert.AreEqual(8, firing.LeaderCharisma);
        Assert.AreEqual(-24, firing.Reaction!.Value.Delta);
    }

    [TestMethod]
    public void Every_way_out_of_waiting_removes_the_timer()
    {
        var unwait = Waiting(Wait(), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, unwait.UnwaitParty(unwait.Version, Npc, Facts(), false).Disposition);
        var join = Waiting(Wait(), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, join.JoinParty(join.Version, Npc, Facts(), false).Disposition);
        var script = Waiting(Wait(), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, script.ForceJoinParty(script.Version, Npc).Disposition);
        var leave = Waiting(Wait(), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, leave.LeaveParty(leave.Version, Npc, new(false, LeaderCharisma: 8)).Disposition);
        var disband = Waiting(Wait(), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, disband.LeaveParty(disband.Version, Npc, new(true)).Disposition);
        var death = Waiting(Wait(), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, death.Kill(death.Version, SessionLifeTarget.Critter(Npc, new LifeFacts(), LifeState.Unharmed)).Disposition);
        foreach (var owner in new[] { unwait, join, script, leave, disband, death })
            using (owner)
            {
                Assert.IsNull(owner.QueryWaitTimer(Npc));
                Assert.IsFalse(owner.QueryEvents().Any(e => e.Kind == SessionEventKinds.PartyWait));
            }
    }

    [TestMethod]
    public void A_refused_unwait_or_leave_keeps_the_timer()
    {
        using var owner = Waiting(Wait(), out var revision);
        var refused = owner.UnwaitParty(owner.Version, Npc, Facts(reaction: -50), false);
        Assert.AreEqual((GameSessionDisposition.Rejected, FollowConsent.Dislike), (refused.Disposition, refused.Consent));
        Rejected(owner.LeaveParty(owner.Version, Npc, new(false, LeaderCharisma: 20)), owner, GameSessionFailure.LeaveRefused, revision);
        Assert.AreEqual(T0 + Day, owner.QueryWaitTimer(Npc));
        // Waiting again is NoChange and keeps the one timer.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WaitParty(owner.Version, Npc, Wait(1)).Disposition);
        Assert.AreEqual(T0 + Day, owner.QueryWaitTimer(Npc));
    }

    [TestMethod]
    public void The_timer_stays_parked_on_its_map_and_fires_overdue_when_the_pc_returns()
    {
        using var owner = Waiting(Wait(1), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryEvents().Single().ScopeMap, "a waiting NPC does not travel");
        Advance(owner, Hour);
        Assert.AreEqual(0, owner.ProcessEvents(new World()).Waits.Count, "parked while the PC is away");
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion(Npc).Standing);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start).Disposition);
        var firing = owner.ProcessEvents(new World()).Waits.Single();
        Assert.IsTrue(owner.QueryCompanion(Npc).Jilted);
        Assert.AreEqual(T0 + 15 * Minute, firing.Event.DeadlineMs, "overdue, at the first pass after the return");
    }

    [TestMethod]
    public void Restore_admits_a_wait_timer_only_for_a_waiting_npc()
    {
        using var owner = Waiting(Wait(1), out _);
        var saved = owner.Capture();
        GameSessionFailure Restored(SessionPartyState party, SessionEventState? events = null)
        {
            var result = GameSessionOwner.Restore(With(saved, party, events), RestoreOptions());
            result.Owner?.Dispose();
            return result.Failure;
        }
        Assert.AreEqual(GameSessionFailure.None, Restored(saved.Party));
        // A crafted save: the NPC follows again, or is free, but its timer is still pending.
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(new([new(Npc, CompanionStanding.Following, false, false, false)], [])));
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(new([], [new(Npc, CompanionStanding.Free, false, false, true)])));
        var timer = saved.Events.Pending.Single();
        SessionEventState Events(params SessionPendingEvent[] pending) => new(saved.Events.Registry, saved.Events.NextSequence + 1, pending);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(saved.Party, Events(timer with { DeadlineMs = timer.DeadlineMs + 1 })), "not its duration");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(saved.Party, Events(timer with { Values = [T0, 3 * Minute, 8], DeadlineMs = T0 + 3 * Minute })),
            "not a duration of the table");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(saved.Party, Events(timer with { Values = [T0 + 1, 15 * Minute, 8], DeadlineMs = T0 + 1 + 15 * Minute })),
            "set after the saved clock");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(saved.Party, Events(timer, timer with { Sequence = timer.Sequence + 1 })), "two for one NPC");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(saved.Party, Events(timer with { Values = [T0, 15 * Minute, 1L << 40] })), "a charisma beyond int");
    }

    [TestMethod]
    public void A_negative_reaction_change_by_the_leader_arms_the_reaction_marks()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, 5).Disposition);
        Assert.AreEqual(CompanionLoyalty.None, owner.QueryCompanion(Npc).Loyalty, "a rise arms nothing");
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -5).Disposition);
        Assert.AreEqual((ReactionMarks, revision + 1), (owner.QueryCompanion(Npc).Loyalty, owner.Version.Revision), "one revision for both");
    }

    [TestMethod]
    public void Arming_follows_the_leader_and_survives_a_change_that_effects_reduce_to_zero()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Changed(owner.WaitParty(owner.Version, Npc), owner, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -1).Disposition);
        Assert.AreEqual(ReactionMarks, owner.QueryCompanion(Npc).Loyalty, "waiting keeps the PC as leader");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, "oid:stranger", Human50, Pc, -10).Disposition);
        Assert.IsNull(owner.QueryParty().Others.SingleOrDefault(c => c.Entity == "oid:stranger").Entity, "no leader, no record");

        using var shrugging = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, shrugging.ForceJoinParty(shrugging.Version, Npc).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, shrugging.AdjustReaction(shrugging.Version, Npc, Human50, Pc, -5).Disposition);
        var armed = shrugging.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, shrugging.RunLoyaltyStep(shrugging.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(CompanionLoyalty.None, shrugging.QueryCompanion(Npc).Loyalty);
        // A change the PC's bad-reaction effects cancel leaves the stored value but still arms.
        var shrug = Pc with { BadReactionEffects = [new(StatModifierOperator.Add, 5)] };
        var result = shrugging.AdjustReaction(shrugging.Version, Npc, Human50, shrug, -5);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (result.Disposition, result.Adjustment!.Value.Delta));
        Assert.AreEqual(ReactionMarks, shrugging.QueryCompanion(Npc).Loyalty);
        Assert.AreEqual(armed + 2, shrugging.Version.Revision);
        // Mind control and an extraordinary leader skip the change and arm nothing.
        Assert.AreEqual(GameSessionDisposition.Changed, shrugging.RunLoyaltyStep(shrugging.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, shrugging.AdjustReaction(shrugging.Version, Npc, Human50, Pc with { ExtraordinaryCharisma = true }, -5).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, shrugging.AdjustReaction(shrugging.Version, Npc, Human50 with { MindControlledByPc = true }, Pc, -5).Disposition);
        Assert.AreEqual(CompanionLoyalty.None, shrugging.QueryCompanion(Npc).Loyalty);
    }

    [TestMethod]
    public void An_alignment_change_arms_each_follower_in_the_array_but_not_a_waiting_one()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:a").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:b").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Changed(owner.WaitParty(owner.Version, Npc), owner, 4);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordPcAlignmentChange(owner.Version, 10, 10).Disposition);
        Changed(owner.RecordPcAlignmentChange(owner.Version, 10, 30), owner, 5);
        var good = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood;
        Assert.AreEqual((good, good, CompanionLoyalty.None), (owner.QueryCompanion("oid:a").Loyalty, owner.QueryCompanion("oid:b").Loyalty,
            owner.QueryCompanion(Npc).Loyalty));
        Changed(owner.RecordPcAlignmentChange(owner.Version, 30, -5), owner, 6);
        Assert.AreEqual(good | CompanionLoyalty.TestTooBad, owner.QueryCompanion("oid:a").Loyalty);
        CollectionAssert.AreEqual(new[] { "oid:a", "oid:b" }, owner.QueryParty().Followers.Select(f => f.Entity).ToArray(), "the array order stays");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordPcAlignmentChange(owner.Version, 0, 5).Disposition, "already armed with both: nothing new");
        Rejected(owner.RecordPcAlignmentChange(new GameSessionVersion(owner.Identity, 0), 0, 5), owner, GameSessionFailure.StaleVersion, owner.Version.Revision);
    }

    [TestMethod]
    public void A_refused_loyalty_step_makes_the_follower_leave_jilted()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -5).Disposition);
        var result = owner.RunLoyaltyStep(owner.Version, Npc, Facts(pcAlignment: 301), false);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Left, FollowConsent.TooGood, false, LeaderWarning.None, ReactionMarks), result.Step);
        Assert.AreEqual(new LoyaltyLines(1600, 1699), result.Step!.Value.RefusalLines);
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Free, false, false, true, ReactionMarks), owner.QueryCompanion(Npc));
        Assert.AreEqual(0, owner.QueryParty().Followers.Count);
        Assert.IsNull(owner.QueryReactionRecord(Npc)!.Value.Conversation, "no reaction change");
        Assert.AreEqual(45, owner.QueryReactionRecord(Npc)!.Value.Remembered);
    }

    [TestMethod]
    public void The_charisma_limit_is_ignored_and_a_forced_follower_stays()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:a", Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, Facts(), false).Disposition);
        var limited = PartyStateTests.Facts(maxFollowers: 1);
        Assert.AreEqual(LoyaltyStepKind.Stayed, owner.RunLoyaltyStep(owner.Version, Npc, limited, false).Step!.Value.Kind);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:forced").Disposition);
        var step = owner.RunLoyaltyStep(owner.Version, "oid:forced", Facts(reaction: -100), false);
        Assert.AreEqual((GameSessionDisposition.NoChange, LoyaltyStepKind.Stayed, FollowConsent.Accepted), (step.Disposition, step.Step!.Value.Kind, step.Step.Value.Consent));
    }

    [TestMethod]
    public void A_refused_leave_goes_on_to_the_leader_check_and_its_warning()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -5).Disposition);
        var revision = owner.Version.Revision;
        // Charisma 20: the unforced leave is refused, the step goes on as if accepted.
        var result = owner.RunLoyaltyStep(owner.Version, Npc, Facts(reaction: -30, charisma: 20), false);
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Stayed, FollowConsent.Dislike, true, LeaderWarning.LowReaction, CompanionLoyalty.None), result.Step);
        Assert.AreEqual(new LoyaltyLines(5400, 5499), result.Step!.Value.WarningLines);
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (result.Disposition, owner.Version.Revision));
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Following, false, false, false), owner.QueryCompanion(Npc));
        // Unarmed now: the same refused check finds nothing to test and changes nothing.
        var again = owner.RunLoyaltyStep(owner.Version, Npc, Facts(reaction: -30, charisma: 20), false);
        Assert.AreEqual((GameSessionDisposition.NoChange, LeaderWarning.None, true), (again.Disposition, again.Step!.Value.Warning, again.Step.Value.LeaveRefused));
        // Mind control refuses the leave too.
        var controlled = PartyStateTests.Facts(reaction: -30) with { MindControlledByPc = true };
        Assert.AreEqual(FollowConsent.Accepted, owner.RunLoyaltyStep(owner.Version, Npc, controlled, false).Step!.Value.Consent, "controlled by this PC: accepted");
    }

    [TestMethod]
    public void The_step_waits_for_the_action_goal_and_runs_only_for_a_follower()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -5).Disposition);
        var revision = owner.Version.Revision;
        var goal = owner.RunLoyaltyStep(owner.Version, Npc, Facts(reaction: -100), true);
        Assert.AreEqual((GameSessionDisposition.NoChange, LoyaltyStepKind.GoalRunning), (goal.Disposition, goal.Step!.Value.Kind));
        Assert.AreEqual((revision, ReactionMarks), (owner.Version.Revision, owner.QueryCompanion(Npc).Loyalty));
        Changed(owner.WaitParty(owner.Version, Npc), owner, revision + 1);
        var waiting = owner.RunLoyaltyStep(owner.Version, Npc, Facts(reaction: -100), false);
        Assert.AreEqual((GameSessionDisposition.NoChange, (LoyaltyStepResult?)null), (waiting.Disposition, waiting.Step), "a waiting NPC stops before the step");
        Assert.IsNull(owner.RunLoyaltyStep(owner.Version, "oid:free", Facts(), false).Step);
        // Refusals.
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, owner.RunLoyaltyStep(owner.Version, "", Facts(), false).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidConsentFacts, owner.RunLoyaltyStep(owner.Version, Npc, Facts() with { OtherLeaderCharisma = 3 }, false).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidConsentFacts, owner.RunLoyaltyStep(owner.Version, Npc, Facts() with { PcPersuasion = (PersuasionTraining)8 }, false).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RunLoyaltyStep(new GameSessionVersion(owner.Identity, 0), Npc, Facts(), false).Failure);
    }

    [TestMethod]
    public void A_hero_leader_with_persuasion_master_gets_no_warning()
    {
        using var owner = HeroOwner(HeroSkillsTests.Skilled());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -5).Disposition);
        var step = owner.RunLoyaltyStep(owner.Version, Npc, Facts(reaction: -15), false).Step!.Value;
        Assert.AreEqual((LoyaltyStepKind.Stayed, LeaderWarning.None, CompanionLoyalty.None), (step.Kind, step.Warning, step.Loyalty),
            "master skips the follow check's reaction step and the leader check");
    }

    [TestMethod]
    public void Loyalty_marks_and_a_pending_timer_survive_a_save_and_load()
    {
        using var owner = Waiting(Wait(2), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Npc, Human50, Pc, -5).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:a").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordPcAlignmentChange(owner.Version, 0, -10).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "{\"entity\":\"oid:a\",\"forced\":true,\"spreadOut\":false,\"loyalty\":9}");
        StringAssert.Contains(text, "{\"entity\":\"oid:sogg\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false,\"loyalty\":3}");
        StringAssert.Contains(text, "\"kind\":\"party.wait\"");
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, owner.Capture().SchemaVersion);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(T0 + 2 * Hour, restored.QueryWaitTimer(Npc));
        Advance(restored, 2 * Hour);
        Assert.AreEqual(Npc, restored.ProcessEvents(new World()).Waits.Single().Entity);
        Assert.IsTrue(restored.QueryCompanion(Npc).Jilted);
    }

    [TestMethod]
    public void Decode_and_restore_refuse_bad_loyalty_marks()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Marks = "\"loyalty\":0}";
        Assert.AreEqual(1, text.Split(Marks).Length - 1);
        foreach (var bad in new[] { "\"loyalty\":16}", "\"loyalty\":-1}", "\"loyalty\":true}" })
        {
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(Marks, bad));
            Assert.AreNotEqual(SnapshotDecodeFailure.None, result.Failure, bad);
            Assert.AreEqual("$.party.followers[0].loyalty", result.Path, bad);
        }
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(text.Replace(",\"loyalty\":0", "")).Failure);
        Assert.AreNotEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(text.Replace($"\"schemaVersion\":{GameSessionSnapshot.CurrentSchemaVersion}", "\"schemaVersion\":26")).Failure);

        // A free record with only marks is a state the transitions produce (OL22); undefined marks are not.
        var saved = owner.Capture();
        var free = new SessionPartyState([], [new(Npc, CompanionStanding.Free, false, false, false, ReactionMarks)]);
        using (var kept = Restore(With(saved, free))) Assert.AreEqual(ReactionMarks, kept.QueryCompanion(Npc).Loyalty);
        foreach (var party in new[]
        {
            new SessionPartyState([new(Npc, CompanionStanding.Following, false, false, false, (CompanionLoyalty)16)], []),
            new SessionPartyState([], [new(Npc, CompanionStanding.Waiting, false, false, false, (CompanionLoyalty)(-1))]),
        })
        {
            var result = GameSessionOwner.Restore(With(saved, party), RestoreOptions());
            result.Owner?.Dispose();
            Assert.AreEqual(GameSessionFailure.InvalidCompanion, result.Failure);
        }
    }

    [TestMethod]
    public void Every_entry_stops_at_the_revision_limit_and_changes_nothing()
    {
        using var owner = Waiting(Wait(1), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:a").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, "oid:a", Human50, Pc, -5).Disposition);
        var saved = owner.Capture();
        using var full = Restore(With(saved, saved.Party, revision: long.MaxValue));
        var max = new GameSessionVersion(full.Identity, long.MaxValue);
        Rejected(full.WaitParty(max, "oid:a", Wait(1)), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Assert.AreEqual(1, full.QueryEvents().Count, "no timer for the refused wait");
        Assert.AreEqual(GameSessionFailure.RevisionExhausted, full.RunLoyaltyStep(max, "oid:a", Facts(), false).Failure);
        Assert.AreEqual(ReactionMarks, full.QueryCompanion("oid:a").Loyalty);
        Rejected(full.RecordPcAlignmentChange(max, 0, 5), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        // The due timer is refused by its handler (a defect guard): the NPC still waits.
        Advance(full, 15 * Minute);
        var pass = full.ProcessEvents(new World());
        Assert.AreEqual(0, pass.Waits.Count);
        Assert.AreEqual(CompanionStanding.Waiting, full.QueryCompanion(Npc).Standing);
    }

    [TestMethod]
    public void A_full_reaction_budget_jilts_without_the_reaction_change_or_its_marks()
    {
        using var owner = Create(new GameSessionLimits(maxReactionRecords: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, "oid:other", Human50, Pc, 3).Disposition);
        Waiting(Wait(1), out _, owner);
        Advance(owner, 15 * Minute);
        var firing = owner.ProcessEvents(new World()).Waits.Single();
        Assert.AreEqual((GameSessionFailure.BudgetExceeded, (ReactionAdjustment?)null), (firing.ReactionFailure, firing.Reaction));
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Free, false, false, true), owner.QueryCompanion(Npc));
        Assert.IsNull(owner.QueryReactionRecord(Npc));
    }

    [TestMethod]
    public void Restore_refuses_a_wait_timer_on_another_map_than_the_npcs_life_timers()
    {
        using var owner = Waiting(Wait(1), out _);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version,
            SessionLifeTarget.Critter(Npc, new LifeFacts(), LifeState.Unharmed), new LifeDamage { Fatigue = 1 }).Disposition);
        var saved = owner.Capture();
        Assert.AreEqual(2, saved.Events.Pending.Count);
        var moved = saved.Events.Pending.Select(e => e.Kind == SessionEventKinds.PartyWait
            ? e with { Entities = [new(Cave.RuntimeMap, Npc)] } : e).ToArray();
        var result = GameSessionOwner.Restore(With(saved, saved.Party, new(saved.Events.Registry, saved.Events.NextSequence, moved)), RestoreOptions());
        result.Owner?.Dispose();
        Assert.AreEqual(GameSessionFailure.InvalidEvents, result.Failure);
    }

    private static GameSessionOwner HeroOwner(SessionHeroDefinition hero) => CreateWith(hero);

    private static GameSessionSnapshot With(GameSessionSnapshot saved, SessionPartyState party, SessionEventState? events = null,
        long? revision = null) => new(
        saved.SchemaVersion, saved.Identity, revision ?? saved.Revision, saved.Clock, saved.Flags, saved.Actor, saved.Maps.ToArray(), saved.Story, party,
        saved.Hero, saved.Inventory, saved.Equipment, saved.Durability, saved.Schematics, saved.Effects, saved.Experience, saved.Advancement,
        saved.Random, saved.Life, saved.Reactions, saved.Combat, saved.CombatMode, saved.Concealment, events ?? saved.Events, saved.Lighting, saved.Sound);
}
