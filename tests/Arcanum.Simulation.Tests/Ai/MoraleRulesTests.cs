using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-082 (AI slice I5): should-flee (0x4AB2F0) and the parties (0x4AE020, 0x4AE0A0) with MR3, the weighing (0x4AB2A0),
/// the flee-spell step (0x4AAF50) and MR7 in the setter, perform fleeing's goal (0x4AC620) with MR9, perform surrender
/// (0x4AC660), stop fleeing (0x4AF170), the back-off start (0x4AC6E0) and run (0x4AC7B0) and the kill notification (0x4AA1B0),
/// over an invented world: positions on one row, levels, follower lists and AI rows set per test.
/// </summary>
[TestClass]
public sealed class MoraleRulesTests
{
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;

    /// <summary>A synthetic world: critters on one row (distance = |x − x'|), follower lists, factions, danger records and one AI row.</summary>
    private sealed class World : IMoraleWorld
    {
        public readonly Dictionary<string, HostilityCritter> Critters = [];
        public readonly Dictionary<string, int> X = [];
        public readonly Dictionary<string, IReadOnlyList<string>> FollowerLists = [];
        public readonly HashSet<(string, string)> Factions = [];
        public readonly Dictionary<string, HostilityDanger> Dangers = [];
        public readonly Dictionary<string, IReadOnlyList<string>> Squares = [];
        public readonly List<(string Center, int HalfSize)> Queries = [];
        public MoraleParameters Row = new(FleeHpPercent: 20, FleeOutnumbered: 3, FleeOutlevelled: 10, StandWhileTargetWeakPercent: 10, CombatDistance: 3);

        public void Add(string id, HostilityKind kind, int x, Func<HostilityCritter, HostilityCritter>? edit = null)
        {
            var critter = new HostilityCritter { Id = id, Kind = kind, Level = 1, HealthPercent = 100 };
            Critters[id] = edit?.Invoke(critter) ?? critter;
            X[id] = x;
        }

        public void Edit(string id, Func<HostilityCritter, HostilityCritter> edit) => Critters[id] = edit(Critters[id]);

        public HostilityCritter? Find(string id) => Critters.GetValueOrDefault(id);
        public int Distance(string from, string to) => Math.Abs(X[from] - X[to]);
        public bool FactionSame(string a, string b) => Factions.Contains((a, b));
        public int HostilityReaction(string npc, string pc) => 50;
        public bool AllowsKillOnSight(string npc, string candidate) => true;
        public HostilityParameters Parameters(string npc) => new(FleeDistance: 5, HostileReaction: 20, HostileAlignmentGap: 2001, GoodFollowerStop: 50);
        public AwarenessResult See(string observer, string target) => new(AwarenessRules.NotPerceived, AwarenessReason.Distance);
        public AwarenessResult Hear(string observer, string target, Loudness loudness) => new(AwarenessRules.NotPerceived, AwarenessReason.Distance);
        public HostilityDanger Danger(string npc) => Dangers.GetValueOrDefault(npc, HostilityDanger.None);
        public string? MindController(string npc) => null;
        public IReadOnlyList<string> Square(string npc, int halfSize)
        {
            Queries.Add((npc, halfSize));
            return Squares.GetValueOrDefault(npc, []);
        }
        public MoraleParameters MoraleParameters(string npc) => Row;
        public IReadOnlyList<string> Followers(string id) => FollowerLists.GetValueOrDefault(id, []);
    }

    /// <summary>Rolls from a queue, recording each request.</summary>
    private sealed class Rolls(params int[] values) : IAiRollSource
    {
        private readonly Queue<int> _values = new(values);
        public readonly List<(AiRollPurpose Purpose, int Min, int Max)> Asked = [];

        public int Roll(AiRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return _values.Count > 0 ? _values.Dequeue() : min;
        }
    }

    // A guard at 0 and a PC at 2, both level 1.
    private static World Basic()
    {
        var world = new World();
        world.Add("guard", HostilityKind.Npc, 0);
        world.Add("pc", HostilityKind.Pc, 2);
        return world;
    }

    [TestMethod]
    public void ShouldFleeTakesItsTestsInTheOriginalsOrder()
    {
        FleeReason Case(Action<World> edit)
        {
            var world = Basic();
            edit(world);
            return MoraleRules.ShouldFlee(world, "guard", "pc", Steamwoven).Reason;
        }

        Assert.AreEqual(FleeReason.Fight, Case(_ => { }));
        Assert.AreEqual(FleeReason.NoFlee, Case(w => w.Edit("guard", n => n with { NoFlee = true, MindControlled = true, HealthPercent = 0 })));
        Assert.AreEqual(FleeReason.MindControlled, Case(w => w.Edit("guard", n => n with { MindControlled = true, HealthPercent = 0 })));
        Assert.AreEqual(FleeReason.TargetWeak, Case(w =>
        {
            w.Edit("pc", p => p with { HealthPercent = 9 });
            w.Edit("guard", n => n with { HealthPercent = 0 });
        }), "a weak target comes before the NPC's own health");
        Assert.AreEqual(FleeReason.Fight, Case(w => w.Edit("pc", p => p with { HealthPercent = 10 })), "not below parameter 3");
        Assert.AreEqual(FleeReason.LowHealth, Case(w => w.Edit("guard", n => n with { HealthPercent = 20 })), "own HP% at most parameter 0");
        Assert.AreEqual(FleeReason.Fight, Case(w => w.Edit("guard", n => n with { HealthPercent = 21 })));
        Assert.AreEqual(FleeReason.Outnumbered, Case(w =>
        {
            foreach (var id in new[] { "a", "b", "c" }) w.Add(id, HostilityKind.Npc, 2, f => f with { Leader = "pc", PcLeader = "pc" });
            w.FollowerLists["pc"] = ["a", "b", "c"];
        }), "three followers: 4 − 1 ≥ 3");
        Assert.AreEqual(FleeReason.Fight, Case(w =>
        {
            foreach (var id in new[] { "a", "b" }) w.Add(id, HostilityKind.Npc, 2, f => f with { Leader = "pc", PcLeader = "pc" });
            w.FollowerLists["pc"] = ["a", "b"];
        }), "two followers: 3 − 1 < 3");
        // Parameter 3 = 101 never lets a healthy NPC flee from anyone (installed rows); parameter 1 = 0 flees from an equal party.
        Assert.AreEqual(FleeReason.TargetWeak, Case(w => w.Row = w.Row with { StandWhileTargetWeakPercent = 101 }));
        Assert.AreEqual(FleeReason.Outnumbered, Case(w => w.Row = w.Row with { FleeOutnumbered = 0 }));
    }

    [TestMethod]
    public void ThePartiesCountEachMembersOwnLevelAndMr3CountsAnNpcLeaderAlone()
    {
        var world = Basic();
        world.Edit("pc", p => p with { Level = 10 });
        world.Add("virgil", HostilityKind.Npc, 3, v => v with { Level = 4, Leader = "pc", PcLeader = "pc" });
        world.Add("dog", HostilityKind.Npc, 3, d => d with { Level = 2, Leader = "virgil", PcLeader = "pc" });
        world.FollowerLists["pc"] = ["virgil", "dog"];
        world.FollowerLists["virgil"] = ["dog"];
        foreach (var profile in new[] { Steamwoven, Original })
        {
            Assert.AreEqual(new PartyStrength(3, 16), MoraleRules.Party(world, "pc", profile), "a PC: itself and its recursive list (P2)");
            Assert.AreEqual(new PartyStrength(3, 16), MoraleRules.Party(world, "virgil", profile), "a led critter: its leader's party");
            Assert.AreEqual(new PartyStrength(2, 6), MoraleRules.Party(world, "dog", profile), "the direct leader roots the party, even when it follows a PC");
        }

        world.Edit("guard", g => g with { Level = 6 });
        world.Add("recruit", HostilityKind.Npc, 1, r => r with { Level = 3, Leader = "guard" });
        world.FollowerLists["guard"] = ["recruit"];
        Assert.AreEqual(new PartyStrength(2, 9), MoraleRules.Party(world, "guard", Steamwoven), "Steamwoven: the NPC leader counts its own followers");
        Assert.AreEqual(new PartyStrength(1, 6), MoraleRules.Party(world, "guard", Original), "MR3: a leaderless NPC counts alone");
        Assert.AreEqual(new PartyStrength(2, 9), MoraleRules.Party(world, "recruit", Original), "while its follower counts the whole group");

        world.Edit("recruit", r => r with { Leader = "gone" });
        Assert.AreEqual(new PartyStrength(1, 3), MoraleRules.Party(world, "recruit", Original), "a leader the world no longer knows counts as none");
        Assert.ThrowsExactly<ArgumentException>(() => MoraleRules.Party(world, "nobody", Steamwoven));
    }

    [TestMethod]
    public void TheOutlevelledTestComparesTheLevelSumsAfterTheSizes()
    {
        var world = Basic();
        world.Edit("pc", p => p with { Level = 11 });
        var decision = MoraleRules.ShouldFlee(world, "guard", "pc", Steamwoven);
        Assert.AreEqual((FleeReason.Outlevelled, true), (decision.Reason, decision.Flees), "11 − 1 ≥ 10");
        Assert.AreEqual((new PartyStrength(1, 1), new PartyStrength(1, 11)), (decision.Own!.Value, decision.Target!.Value));
        world.Edit("pc", p => p with { Level = 10 });
        Assert.AreEqual(FleeReason.Fight, MoraleRules.ShouldFlee(world, "guard", "pc", Steamwoven).Reason, "10 − 1 < 10");

        // The guard leads two recruits: Steamwoven counts three against the PC's one, the Original setting one (MR3).
        world.Row = world.Row with { FleeOutnumbered = 0, FleeOutlevelled = 1000 };
        world.Add("r1", HostilityKind.Npc, 1, r => r with { Leader = "guard" });
        world.Add("r2", HostilityKind.Npc, 1, r => r with { Leader = "guard" });
        world.FollowerLists["guard"] = ["r1", "r2"];
        Assert.AreEqual(FleeReason.Fight, MoraleRules.ShouldFlee(world, "guard", "pc", Steamwoven).Reason, "1 − 3 < 0");
        Assert.AreEqual(FleeReason.Outnumbered, MoraleRules.ShouldFlee(world, "guard", "pc", Original).Reason, "1 − 1 ≥ 0");
        Assert.AreEqual(new PartyStrength(3, 3), MoraleRules.ShouldFlee(world, "guard", "pc", Steamwoven).Own, "the guard and its two recruits");
    }

    [TestMethod]
    public void TheWeighingAsksShouldFleeAboutTheAttackerOnly()
    {
        var world = Basic();
        world.FollowerLists["pc"] = ["a", "b", "c"];
        foreach (var id in new[] { "a", "b", "c" }) world.Add(id, HostilityKind.Npc, 2, f => f with { Leader = "pc", PcLeader = "pc" });
        var weighed = MoraleRules.ReactToAttack(world, "guard", "pc", new Rolls(), Steamwoven);
        Assert.AreEqual(new AttackReaction(AttackReactionStep.Weigh, DangerState.Flee, "pc", true), weighed.Reaction);
        Assert.AreEqual(FleeReason.Outnumbered, weighed.Flee!.Value.Reason);

        world.Edit("guard", g => g with { NoFlee = true });
        var fights = MoraleRules.ReactToAttack(world, "guard", "pc", new Rolls(), Steamwoven);
        Assert.AreEqual((DangerState.CombatFocus, FleeReason.NoFlee), (fights.Reaction.Request, fights.Flee!.Value.Reason));

        world.Dangers["guard"] = new(DangerState.Flee, "pc", null, null, []);
        var keeps = MoraleRules.ReactToAttack(world, "guard", "pc", new Rolls(), Steamwoven);
        Assert.AreEqual((AttackReactionStep.KeepFleeing, (FleeDecision?)null), (keeps.Reaction.Step, keeps.Flee), "no weighing, no question");
    }

    [TestMethod]
    public void TheFleeSpellStepAlternatesAndMr7ClearsAStaleFlag()
    {
        var asked = 0;
        bool Chooses() { asked++; return true; }
        Assert.AreEqual(new FleeSpellStep(FleeSpellOutcome.Chosen, DangerFlags.Fleeing | DangerFlags.SpellFlee), MoraleRules.FleeSpell(DangerFlags.Fleeing, Chooses));
        Assert.AreEqual(new FleeSpellStep(FleeSpellOutcome.ClearsFlag, DangerFlags.Fleeing),
            MoraleRules.FleeSpell(DangerFlags.Fleeing | DangerFlags.SpellFlee, Chooses), "the pass after a flee spell flees");
        Assert.AreEqual(1, asked, "the chooser is not asked while the flag is set");
        Assert.AreEqual(new FleeSpellStep(FleeSpellOutcome.NoneChosen, DangerFlags.Fleeing), MoraleRules.FleeSpell(DangerFlags.Fleeing, () => false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MoraleRules.FleeSpell((DangerFlags)(1 << 12), Chooses));

        // A flight left before the next flee-spell step: Steamwoven clears SPELL_FLEE, the Original setting keeps it (MR7).
        var flight = DangerFlags.Fleeing | DangerFlags.SpellFlee;
        foreach (var state in new[] { DangerState.CombatFocus, DangerState.Surrender, DangerState.None })
        {
            Assert.IsFalse(DangerRules.Set(flight, state, new(), Steamwoven).Flags.HasFlag(DangerFlags.SpellFlee), state.ToString());
            Assert.IsTrue(DangerRules.Set(flight, state, new(), Original).Flags.HasFlag(DangerFlags.SpellFlee), state.ToString());
        }
        Assert.IsTrue(DangerRules.Set(flight, DangerState.Flee, new(), Steamwoven).Flags.HasFlag(DangerFlags.SpellFlee), "a new flight keeps it");
        Assert.IsFalse(DangerRules.Set(flight, DangerState.Flee, new(NoFlee: true), Steamwoven).Flags.HasFlag(DangerFlags.SpellFlee),
            "rule 2 turns the flight into a fight");
    }

    [TestMethod]
    public void PerformFleeingStartsTheFleeGoalOnlyAfterASurrenderUnlessMr9()
    {
        var applied = DangerRules.Set(DangerFlags.Fleeing, DangerState.Surrender, new(), Steamwoven);
        var refused = DangerRules.Set(DangerFlags.Fleeing, DangerState.Surrender, new(Invulnerable: true), Steamwoven);
        var charisma = DangerRules.Set(DangerFlags.Fleeing, DangerState.Surrender, new(LeaderExtraordinaryCharisma: true), Steamwoven);
        Assert.AreEqual((DangerState.Surrender, DangerState.None, DangerState.CombatFocus), (applied.Applied, refused.Applied, charisma.Applied));
        Assert.IsTrue(MoraleRules.StartsFleeGoal(applied, Steamwoven));
        Assert.IsFalse(MoraleRules.StartsFleeGoal(refused, Steamwoven));
        Assert.IsFalse(MoraleRules.StartsFleeGoal(charisma, Steamwoven));
        Assert.IsTrue(MoraleRules.StartsFleeGoal(refused, Original), "MR9");
        Assert.IsTrue(MoraleRules.StartsFleeGoal(charisma, Original), "MR9");
        Assert.ThrowsExactly<ArgumentException>(() => MoraleRules.StartsFleeGoal(DangerRules.Set(DangerFlags.Fleeing, DangerState.None, new(), Steamwoven), Steamwoven));
    }

    [TestMethod]
    public void ASurrenderEndsWhenItsSourceIsGoneDeadOrUnconscious()
    {
        var world = Basic();
        world.Dangers["guard"] = new(DangerState.Surrender, "pc", null, null, []);
        Assert.IsFalse(MoraleRules.SurrenderEnds(world, "guard"));
        world.Edit("pc", p => p with { Unconscious = true });
        Assert.IsTrue(MoraleRules.SurrenderEnds(world, "guard"));
        world.Edit("pc", p => p with { Unconscious = false, Dead = true });
        Assert.IsTrue(MoraleRules.SurrenderEnds(world, "guard"));
        world.Dangers["guard"] = new(DangerState.Surrender, "left", null, null, []);
        Assert.IsTrue(MoraleRules.SurrenderEnds(world, "guard"), "a source the world no longer knows");
        world.Dangers["guard"] = new(DangerState.Surrender, null, null, null, []);
        Assert.IsTrue(MoraleRules.SurrenderEnds(world, "guard"));
        world.Dangers["guard"] = new(DangerState.Flee, "pc", null, null, []);
        Assert.ThrowsExactly<InvalidOperationException>(() => MoraleRules.SurrenderEnds(world, "guard"));
    }

    [TestMethod]
    public void StopFleeingCalmsOnlyAFleeingNpcAndInterruptsEveryNpcsFleeGoals()
    {
        var npc = new HostilityCritter { Id = "n", Kind = HostilityKind.Npc };
        Assert.AreEqual(new StopFleeingStep(true, true), MoraleRules.StopFleeing(npc, DangerState.Flee));
        Assert.AreEqual(new StopFleeingStep(false, true), MoraleRules.StopFleeing(npc, DangerState.Surrender), "a surrender is kept");
        Assert.AreEqual(new StopFleeingStep(false, true), MoraleRules.StopFleeing(npc, DangerState.None));
        Assert.AreEqual(new StopFleeingStep(false, false), MoraleRules.StopFleeing(npc with { Kind = HostilityKind.Pc }, DangerState.Flee));
    }

    [TestMethod]
    public void TheBackOffStartsOnOneInTwentyWhenCloserThanParameterFifteen()
    {
        var world = Basic();
        var rolls = new Rolls(1, 2);
        Assert.IsTrue(MoraleRules.StartsBackOff(world, "guard", "pc", rolls), "2 tiles, parameter 15 = 3, roll 1");
        Assert.IsFalse(MoraleRules.StartsBackOff(world, "guard", "pc", rolls), "roll 2");
        CollectionAssert.AreEqual(new[] { (AiRollPurpose.BackOffStart, 1, 20), (AiRollPurpose.BackOffStart, 1, 20) }, rolls.Asked);

        var none = new Rolls(1);
        world.X["pc"] = 3;
        Assert.IsFalse(MoraleRules.StartsBackOff(world, "guard", "pc", none), "not closer than parameter 15");
        world.X["pc"] = 0;
        world.Row = world.Row with { CombatDistance = 1 };
        Assert.IsFalse(MoraleRules.StartsBackOff(world, "guard", "pc", none), "parameter 15 at most 1 never starts one");
        Assert.AreEqual(0, none.Asked.Count, "no roll without the conditions");
    }

    [TestMethod]
    public void TheBackOffRunsToItsRangeOrFive()
    {
        var world = Basic();
        Assert.AreEqual(new BackOffRun(BackOffRunStep.NotBackingOff, 3), MoraleRules.BackOff(world, "guard", "pc", DangerFlags.Fighting));
        Assert.AreEqual(new BackOffRun(BackOffRunStep.Runs, 3), MoraleRules.BackOff(world, "guard", "pc", DangerFlags.BackingOff));
        world.X["pc"] = 3;
        Assert.AreEqual(new BackOffRun(BackOffRunStep.Reached, 3), MoraleRules.BackOff(world, "guard", "pc", DangerFlags.BackingOff));
        world.Row = world.Row with { CombatDistance = 0 };
        Assert.AreEqual(new BackOffRun(BackOffRunStep.Runs, 5), MoraleRules.BackOff(world, "guard", "pc", DangerFlags.BackingOff), "a kill back-off of row 0 runs to 5");
        world.X["pc"] = 5;
        Assert.AreEqual(new BackOffRun(BackOffRunStep.Reached, 5), MoraleRules.BackOff(world, "guard", "pc", DangerFlags.BackingOff));
        CollectionAssert.AreEqual(new[] { 4, 5, 3, 6, 2, 7, 1, 0 }, MoraleRules.AwayOffsets.ToArray());
    }

    [TestMethod]
    public void APcsKillMakesTheVictimsFactionMatesFightingItBackOff()
    {
        var world = Basic();
        world.Add("victim", HostilityKind.Npc, 1, v => v with { Dead = true });
        world.Dangers["victim"] = new(DangerState.Flee, "pc", null, null, []);
        HostilityDanger OnPc() => new(DangerState.CombatFocus, "pc", "pc", null, []);
        world.Add("mate", HostilityKind.Npc, 4);
        world.Add("brave", HostilityKind.Npc, 4, b => b with { NoFlee = true });
        world.Add("stranger", HostilityKind.Npc, 4);
        world.Add("fallen", HostilityKind.Npc, 4, f => f with { Dead = true });
        world.Add("idle", HostilityKind.Npc, 4);
        world.Add("other", HostilityKind.Npc, 4);
        world.Add("friend", HostilityKind.Pc, 4);
        foreach (var id in new[] { "mate", "brave", "fallen", "idle", "other", "friend" }) world.Factions.Add(("victim", id));
        foreach (var id in new[] { "mate", "brave", "stranger", "fallen", "friend" }) world.Dangers[id] = OnPc();
        world.Dangers["other"] = new(DangerState.CombatFocus, "friend", "friend", null, []);
        world.Squares["pc"] = ["pc", "friend", "other", "idle", "fallen", "stranger", "brave", "victim", "mate"];

        var plan = MoraleRules.KillNotification(world, "victim", "pc");
        Assert.AreEqual(new StopFleeingStep(true, true), plan.VictimStopsFleeing);
        CollectionAssert.AreEqual(new[] { "mate" }, plan.BacksOff.ToArray());
        Assert.AreEqual(("pc", 3), world.Queries.Single(), "the 7×7 square around the killer");

        world.Queries.Clear();
        Assert.AreEqual(0, MoraleRules.KillNotification(world, "victim", "mate").BacksOff.Count, "an NPC killer starts no back-off");
        Assert.AreEqual(0, MoraleRules.KillNotification(world, "victim", null).BacksOff.Count);
        Assert.AreEqual(0, MoraleRules.KillNotification(world, "victim", "gone").BacksOff.Count);
        Assert.AreEqual(0, world.Queries.Count);
        world.Dangers["victim"] = new(DangerState.Surrender, "pc", null, null, []);
        Assert.AreEqual(new StopFleeingStep(false, true), MoraleRules.KillNotification(world, "victim", null).VictimStopsFleeing);
    }
}
