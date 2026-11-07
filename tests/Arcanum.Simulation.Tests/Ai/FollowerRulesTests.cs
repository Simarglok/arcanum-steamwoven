using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

// ARC-WORLD-091 (NPC AI slice I9): the follower rule, tell followers, the attack order's party filter and the follower comment
// lines (ARC-RES-067 FO1–FO17). Critters, alignments and origins are invented.
[TestClass]
public sealed class FollowerRulesTests
{
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven;

    private sealed class World : IHostilityWorld
    {
        public readonly Dictionary<string, HostilityCritter> Critters = [];
        public readonly HashSet<(string, string)> Factions = [];
        public readonly Dictionary<string, string> Controllers = [];

        public World()
        {
            Add("pc", HostilityKind.Pc);
            Add("f", HostilityKind.Npc, c => c with { Leader = "pc", PcLeader = "pc", Alignment = 10, Origin = 1 });
            Add("g", HostilityKind.Npc, c => c with { Leader = "pc", PcLeader = "pc", Origin = 2 });
            Add("orc", HostilityKind.Npc, c => c with { Origin = 7 });
        }

        public void Add(string id, HostilityKind kind, Func<HostilityCritter, HostilityCritter>? edit = null)
        {
            var critter = new HostilityCritter { Id = id, Kind = kind, Perception = 5, Intelligence = 8, Level = 1 };
            Critters[id] = edit?.Invoke(critter) ?? critter;
        }

        public void Edit(string id, Func<HostilityCritter, HostilityCritter> edit) => Critters[id] = edit(Critters[id]);

        public HostilityCritter? Find(string id) => Critters.GetValueOrDefault(id);
        public int Distance(string from, string to) => 1;
        public bool FactionSame(string a, string b) => Factions.Contains((a, b)) || Factions.Contains((b, a));
        public int HostilityReaction(string npc, string pc) => 50;
        public bool AllowsKillOnSight(string npc, string candidate) => true;
        public HostilityParameters Parameters(string npc) => new(5, 20, 2001, 50);
        public AwarenessResult See(string observer, string target) => new(0, AwarenessReason.Perceived);
        public AwarenessResult Hear(string observer, string target, Loudness loudness) => new(0, AwarenessReason.Perceived);
        public HostilityDanger Danger(string npc) => HostilityDanger.None;
        public string? MindController(string npc) => Controllers.GetValueOrDefault(npc);
        public IReadOnlyList<string> Square(string npc, int halfSize) => [];
    }

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

    private static FollowerRuleOutcome Rule(World world, string target, FollowerSide side = FollowerSide.Attacker, bool accidental = false,
        bool ordered = false, string follower = "f", string leader = "pc") =>
        FollowerRules.Rule(world, follower, leader, target, side, accidental, ordered, Steamwoven);

    [TestMethod]
    public void The_rule_joins_locks_or_ignores_in_the_original_case_order()
    {
        var world = new World();
        Assert.AreEqual(FollowerRuleStep.LeaderIsTarget, Rule(world, "pc", ordered: true).Step, "case 1 before every other");
        Assert.AreEqual(FollowerRuleStep.ReactToAttack, Rule(world, "orc").Step, "case 5: fight or flee by its own morale");
        Assert.AreEqual(FollowerRuleStep.Lock, Rule(world, "orc", ordered: true).Step, "an ordered attack locks");
        Assert.AreEqual(FollowerRuleStep.Accidental, Rule(world, "orc", accidental: true, ordered: true).Step, "accidental before ordered");
        Assert.AreEqual(FollowerRuleStep.ReactToAttack, Rule(world, "orc", FollowerSide.Victim).Step, "the victim's side joins too");
        Assert.AreEqual(FollowerRuleStep.SelfOrDead, Rule(world, "f", ordered: true).Step);
        world.Edit("f", c => c with { Dead = true });
        Assert.AreEqual(FollowerRuleStep.SelfOrDead, Rule(world, "orc", ordered: true).Step);
    }

    [TestMethod]
    public void A_non_critter_target_is_locked_when_ordered_focused_otherwise_and_ignored_when_accidental()
    {
        var world = new World();
        world.Add("door", HostilityKind.Object);
        Assert.AreEqual(FollowerRuleStep.Lock, Rule(world, "door", ordered: true).Step);
        Assert.AreEqual(FollowerRuleStep.Focus, Rule(world, "door").Step, "the PC bashes a door: its followers bash it too");
        Assert.AreEqual(FollowerRuleStep.AccidentalObject, Rule(world, "door", accidental: true).Step);
        world.Edit("f", c => c with { Dead = true });
        Assert.AreEqual(FollowerRuleStep.Focus, Rule(world, "door").Step, "case 2 comes before the dead test");
    }

    [TestMethod]
    public void An_upset_follower_complains_only_on_the_attacker_side_for_a_deliberate_hit_on_a_target_out_of_combat()
    {
        var world = new World();
        world.Add("kin", HostilityKind.Npc, c => c with { Origin = 1 });
        var complaint = Rule(world, "kin", ordered: true);
        Assert.AreEqual(new FollowerRuleOutcome("f", FollowerRuleStep.Complain, UpsetReason.Origin, true), complaint);
        Assert.AreEqual(new LoyaltyLines(5000, 5099), complaint.Line);
        Assert.AreEqual(FollowerRuleStep.Complain, Rule(world, "kin").Step, "the fan-out complains as the order does");
        Assert.AreEqual(FollowerRuleStep.UpsetIgnored, Rule(world, "kin", FollowerSide.Victim).Step);
        Assert.AreEqual(FollowerRuleStep.UpsetIgnored, Rule(world, "kin", accidental: true).Step);
        world.Edit("kin", c => c with { InCombatMode = true });
        Assert.AreEqual(new FollowerRuleOutcome("f", FollowerRuleStep.UpsetIgnored, UpsetReason.Origin), Rule(world, "kin"));
        world.Edit("kin", c => c with { InCombatMode = false });
        // An inactive follower still complains (the −5), but floats no line.
        world.Edit("f", c => c with { Unconscious = true });
        var silent = Rule(world, "kin");
        Assert.AreEqual((FollowerRuleStep.Complain, false, (LoyaltyLines?)null), (silent.Step, silent.Speaks, silent.Line));
        // A follower mind-controlled by its leader obeys.
        world.Edit("f", c => c with { Unconscious = false, MindControlled = true });
        world.Controllers["f"] = "pc";
        Assert.AreEqual(FollowerRuleStep.Lock, Rule(world, "kin", ordered: true).Step);
    }

    [TestMethod]
    public void Each_upset_reason_has_its_own_line_range()
    {
        var world = new World();
        world.Add("saint", HostilityKind.Npc, c => c with { Alignment = 60 });
        world.Add("brother", HostilityKind.Npc);
        world.Factions.Add(("f", "brother"));
        world.Edit("pc", c => c with { InCombatMode = true });
        Assert.AreEqual((UpsetReason.Good, (LoyaltyLines?)new LoyaltyLines(4900, 4999)), Pair(Rule(world, "saint")));
        Assert.AreEqual((UpsetReason.Faction, (LoyaltyLines?)new LoyaltyLines(5100, 5199)), Pair(Rule(world, "brother")));
        Assert.AreEqual((UpsetReason.Party, (LoyaltyLines?)new LoyaltyLines(4800, 4899)), Pair(Rule(world, "g", ordered: true)),
            "another of the PC's followers (the order lets it through only in combat mode)");
        Assert.IsNull(FollowerRules.UpsetLines(UpsetReason.None));

        static (UpsetReason, LoyaltyLines?) Pair(FollowerRuleOutcome o) => (o.Upset, o.Line);
    }

    [TestMethod]
    public void Tell_followers_runs_the_rule_for_each_direct_follower_in_array_order_and_nothing_when_the_leader_is_the_other()
    {
        var world = new World();
        world.Add("kin", HostilityKind.Npc, c => c with { Origin = 2 });
        var told = FollowerRules.TellFollowers(world, "pc", "kin", ["g", "f"], FollowerSide.Attacker, false, Steamwoven);
        CollectionAssert.AreEqual(new[] { ("g", FollowerRuleStep.Complain), ("f", FollowerRuleStep.ReactToAttack) },
            told.Select(o => (o.Follower, o.Step)).ToArray());
        Assert.AreEqual(0, FollowerRules.TellFollowers(world, "pc", "pc", ["g", "f"], FollowerSide.Victim, false, Steamwoven).Count);
    }

    [TestMethod]
    public void The_attack_order_refuses_the_pcs_own_party_only_outside_combat_mode()
    {
        Assert.IsTrue(FollowerRules.AttackOrderRefused(pcInCombatMode: false, targetLedByPc: true));
        Assert.IsFalse(FollowerRules.AttackOrderRefused(pcInCombatMode: true, targetLedByPc: true));
        Assert.IsFalse(FollowerRules.AttackOrderRefused(pcInCombatMode: false, targetLedByPc: false));
    }

    [TestMethod]
    public void The_taunt_kind_needs_a_pc_hit_on_an_object_outside_its_party()
    {
        var hit = new TauntFacts { TauntsOn = true, AttackerIsPc = true, Struck = true, Hit = true, StruckIsAimed = true, Critical = true };
        Assert.AreEqual(TauntKind.Critical, FollowerRules.TauntKindOf(hit));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { Critical = false }));
        Assert.AreEqual(TauntKind.Stray, FollowerRules.TauntKindOf(hit with { StruckIsAimed = false, StrayFlag = true }));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { StruckIsAimed = false, Critical = true }));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { TauntsOn = false }));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { AttackerIsPc = false }));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { StruckInPcParty = true }));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { Hit = false }));
        Assert.IsNull(FollowerRules.TauntKindOf(hit with { Struck = false }));
    }

    [TestMethod]
    public void The_taunt_rolls_to_speak_then_draws_an_active_follower()
    {
        var a = new HostilityCritter { Id = "a", Kind = HostilityKind.Npc };
        var b = new HostilityCritter { Id = "b", Kind = HostilityKind.Npc };
        var rolls = new Rolls(2, 1);
        Assert.AreEqual(new FollowerComment("b", new(2400, 2499)), FollowerRules.Taunt(TauntKind.Critical, [a, b], rolls));
        CollectionAssert.AreEqual(new[] { (AiRollPurpose.TauntSpeak, 1, 2), (AiRollPurpose.TauntFollower, 0, 1) }, rolls.Asked);

        var silent = new Rolls(1);
        Assert.IsNull(FollowerRules.Taunt(TauntKind.Stray, [a, b], silent), "half of the cases say nothing");
        Assert.AreEqual(1, silent.Asked.Count, "no follower draw after a 1");

        var one = new Rolls(2);
        Assert.AreEqual(new FollowerComment("a", new(2500, 2599)), FollowerRules.Taunt(TauntKind.Stray, [a], one));
        Assert.AreEqual(1, one.Asked.Count, "one follower: no draw");
        Assert.IsNull(FollowerRules.Taunt(TauntKind.Critical, [], new Rolls(2)), "no followers");
        Assert.IsNull(FollowerRules.Taunt(TauntKind.Critical, [a with { Stunned = true }], new Rolls(2)), "an inactive follower says nothing");
        // FO17: a null entry is skipped, not tested.
        Assert.IsNull(FollowerRules.Taunt(TauntKind.Critical, [null, b], new Rolls(2, 0)));
    }

    [TestMethod]
    public void The_near_death_line_crosses_twenty_percent_on_a_hurt_follower_of_a_pc_and_never_on_a_killed_one()
    {
        Assert.AreEqual(new FollowerComment("f", new(5500, 5599)), FollowerRules.NearDeath("f", true, 3, 20, 19, false));
        Assert.IsNull(FollowerRules.NearDeath("f", true, 3, 19, 10, false), "already below");
        Assert.IsNull(FollowerRules.NearDeath("f", true, 3, 40, 20, false), "still at 20");
        Assert.IsNull(FollowerRules.NearDeath("f", false, 3, 40, 10, false), "no PC leader");
        Assert.IsNull(FollowerRules.NearDeath("f", true, 0, 40, 10, false), "no hit-point damage");
        // ARC-DEV-245: the same from hit points (20 % of 50 is 10; 9 of 50 is 18 %).
        Assert.AreEqual(new FollowerComment("f", new(5500, 5599)), FollowerRules.NearDeathOfHit("f", true, 10, 9, 50, false));
        Assert.IsNull(FollowerRules.NearDeathOfHit("f", true, 9, 4, 50, false), "already below");
        Assert.IsNull(FollowerRules.NearDeathOfHit("f", true, 30, 0, 50, true), "killed: FO16 fixed");
        Assert.IsNull(FollowerRules.NearDeathOfHit("f", true, 5, 0, 0, false), "a maximum of 0 reads 0 %");
        Assert.IsNull(FollowerRules.NearDeathOfHit("f", true, 10, 12, 50, false), "a heal");
        Assert.IsNull(FollowerRules.NearDeath("f", true, 30, 40, 0, true), "FO16: a killed follower floats nothing");
    }

    [TestMethod]
    public void Active_is_the_original_activity_test()
    {
        var c = new HostilityCritter { Id = "c", Kind = HostilityKind.Npc };
        Assert.IsTrue(FollowerRules.Active(c));
        Assert.IsTrue(FollowerRules.Active(c with { Asleep = true }), "sleep is not tested");
        foreach (var inactive in new[] { c with { Destroyed = true }, c with { Off = true }, c with { Dead = true }, c with { Unconscious = true },
                     c with { Stunned = true }, c with { Stoned = true } })
            Assert.IsFalse(FollowerRules.Active(inactive));
    }
}
