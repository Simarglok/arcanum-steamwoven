using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-091 (NPC AI slice I9): the combat orders attack, back off and curse, the AI pass after each order and the follower
// rule's writes in the session (ARC-RES-067 FO1–FO5, FO7, FO9, FO10). The hero is the half-elf fixture; NPC ids, origins and reaction
// facts are invented.
[TestClass]
public sealed class CompanionCombatOrderTests
{
    private const string Virgil = "oid:virgil", Sogg = "oid:sogg", Wolf = "oid:wolf";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero, WolfHandle = SessionCombatant.Critter(Wolf),
        SoggHandle = SessionCombatant.Critter(Sogg);
    private static readonly ReactionNpcFacts Npc60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.HalfElf, 0, 0, [], [], [], false);
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };
    private static readonly SessionPeaceFacts Combat = new() { Reaction = new(Npc60, Pc), CombatMode = Living };

    /// <summary>The hero ("pc"), its followers and a wolf, as the follower rule reads them.</summary>
    private sealed class World : IHostilityWorld
    {
        public readonly Dictionary<string, HostilityCritter> Critters = [];

        public World()
        {
            Add(AiSchedulerRules.Pc, HostilityKind.Pc);
            Add(Virgil, HostilityKind.Npc, c => c with { Leader = AiSchedulerRules.Pc, PcLeader = AiSchedulerRules.Pc, Origin = 3 });
            Add(Sogg, HostilityKind.Npc, c => c with { Leader = AiSchedulerRules.Pc, PcLeader = AiSchedulerRules.Pc, Origin = 4 });
            Add(Wolf, HostilityKind.Npc);
        }

        public void Add(string id, HostilityKind kind, Func<HostilityCritter, HostilityCritter>? edit = null)
        {
            var critter = new HostilityCritter { Id = id, Kind = kind, Perception = 5, Intelligence = 8, Level = 1 };
            Critters[id] = edit?.Invoke(critter) ?? critter;
        }

        public HostilityCritter? Find(string id) => Critters.GetValueOrDefault(id);
        public int Distance(string from, string to) => 1;
        public bool FactionSame(string a, string b) => false;
        public int HostilityReaction(string npc, string pc) => 50;
        public bool AllowsKillOnSight(string npc, string candidate) => true;
        public HostilityParameters Parameters(string npc) => new(5, 20, 2001, 50);
        public AwarenessResult See(string observer, string target) => new(0, AwarenessReason.Perceived);
        public AwarenessResult Hear(string observer, string target, Loudness loudness) => new(0, AwarenessReason.Perceived);
        public HostilityDanger Danger(string npc) => HostilityDanger.None;
        public string? MindController(string npc) => null;
        public IReadOnlyList<string> Square(string npc, int halfSize) => [];
    }

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven, bool hero = true)
    {
        var result = GameSessionOwner.Create(hero ? Descriptor() with { Hero = HalfElf(), Rules = rules } : Descriptor() with { Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        foreach (var entity in new[] { Virgil, Sogg })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, entity, PartyStateTests.Facts(maxFollowers: 5), true).Disposition);
        return owner;
    }

    private static CompanionOrderFacts F(string entity, bool mindControlled = false, int charisma = 8) =>
        new(entity, entity, 2, mindControlled, PartyStateTests.Facts(maxFollowers: 5) with { PcCharisma = charisma }, Combat: Combat);

    private static SessionCompanionOrderResult Order(GameSessionOwner owner, CompanionOrderType type, SessionCombatant? target = null,
        IHostilityWorld? world = null, ISessionAiWorld? ai = null, params CompanionOrderFacts[] followers) =>
        owner.IssueCompanionOrder(owner.Version, new(type, CompanionOrderAddressing.All, followers.Length == 0 ? [F(Virgil), F(Sogg)] : followers)
        {
            Target = target, World = world ?? new World(), Ai = ai,
        });

    private static void StopAll(GameSessionOwner owner) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StopAttacking(owner.Version, new() { Combatant = Hero }).Disposition);

    [TestMethod]
    public void Attack_lifts_a_stop_and_locks_each_follower_onto_the_target_in_one_revision_each()
    {
        using var owner = Owner();
        StopAll(owner);
        Assert.IsTrue(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack));
        var revision = owner.Version.Revision;
        var result = Order(owner, CompanionOrderType.Attack, WolfHandle);
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 2), (result.Disposition, owner.Version.Revision));
        foreach (var outcome in result.Outcomes)
        {
            Assert.AreEqual((CompanionOrderEffect.AttackLocked, (CompanionOrderLine?)null, true), (outcome.Effect, outcome.Line, outcome.Locked),
                "no line acknowledges an attack order");
            Assert.AreEqual(FollowerRuleStep.Lock, outcome.Rule!.Value.Step);
            Assert.IsNull(outcome.AttackStop);
            Assert.AreEqual(DangerScriptPoint.EnterCombat, outcome.Danger!.Hook!.Value.Point);
            var danger = owner.QueryDanger(outcome.Entity);
            Assert.AreEqual((DangerState.CombatFocus, (SessionCombatant?)WolfHandle), (danger.State, danger.Focus));
            Assert.AreEqual(DangerFlags.TargetLocked, danger.Flags & (DangerFlags.TargetLocked | DangerFlags.NoAttack), "locked, NO_ATTACK cleared");
        }
        Assert.AreEqual(0, owner.QueryDanger(Wolf).Enemies.Count, "an ordered attack event writes nothing on the victim");
    }

    [TestMethod]
    public void Attack_without_a_target_or_on_the_party_outside_combat_mode_does_nothing()
    {
        using var owner = Owner();
        StopAll(owner);
        var revision = owner.Version.Revision;
        Assert.IsTrue(Order(owner, CompanionOrderType.Attack).Outcomes.All(o => o.Effect == CompanionOrderEffect.NoTarget));
        var party = Order(owner, CompanionOrderType.Attack, SoggHandle).Outcomes;
        Assert.IsTrue(party.All(o => o.Effect == CompanionOrderEffect.PartyTarget));
        Assert.AreEqual(revision, owner.Version.Revision, "the stop stands");
        Assert.IsTrue(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack));
    }

    [TestMethod]
    public void An_attack_whose_event_does_nothing_still_lifts_the_stop()
    {
        using var owner = Owner();
        StopAll(owner);
        // FO9: the hero as the target, the follower rule's case 1.
        var leader = Order(owner, CompanionOrderType.Attack, Hero, followers: F(Virgil)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.AttackIgnored, FollowerRuleStep.LeaderIsTarget), (leader.Effect, leader.Rule!.Value.Step));
        Assert.IsFalse(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack));
        // A dead victim stops the event before the follower rule.
        var world = new World();
        world.Critters[Wolf] = world.Critters[Wolf] with { Dead = true };
        var dead = Order(owner, CompanionOrderType.Attack, WolfHandle, world, followers: F(Sogg)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.AttackIgnored, (AttackEventStop?)AttackEventStop.VictimDead, (FollowerRuleOutcome?)null),
            (dead.Effect, dead.AttackStop, dead.Rule));
        Assert.AreEqual(DangerFlags.None, owner.QueryDanger(Sogg).Flags);
    }

    [TestMethod]
    public void In_combat_mode_an_attack_on_a_fellow_follower_is_a_complaint_with_minus_five_toward_the_hero()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, Hero, true, new()).Disposition);
        var before = owner.QueryReaction(Virgil, Npc60, Pc).View!.Effective;
        var outcome = Order(owner, CompanionOrderType.Attack, SoggHandle, followers: F(Virgil)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.Complained, UpsetReason.Party, true), (outcome.Effect, outcome.Rule!.Value.Upset, outcome.Rule!.Value.Speaks));
        Assert.AreEqual(new LoyaltyLines(4800, 4899), outcome.Rule!.Value.Line);
        Assert.AreEqual(-5, outcome.Reaction!.Value.Delta);
        Assert.AreEqual(before - 5, owner.QueryReaction(Virgil, Npc60, Pc).View!.Effective);
        Assert.AreEqual(DangerState.None, owner.QueryDanger(Virgil).State, "no lock on a complaint");
        // A complaint without reaction facts is refused for that follower only.
        var bare = Order(owner, CompanionOrderType.Attack, SoggHandle, followers: F(Virgil) with { Combat = null }).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.Failed, GameSessionFailure.InvalidDangerFacts), (bare.Effect, bare.Failure));
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.ApplyFollowerRule(owner.Version, new()
        {
            Outcome = new(Virgil, FollowerRuleStep.Complain), Leader = Hero, Target = SoggHandle, Complaint = new(Npc60, Pc),
        }).Failure, "a complaint names its reason");
    }

    [TestMethod]
    public void Back_off_acknowledges_and_stops_attacking()
    {
        using var owner = Owner();
        Order(owner, CompanionOrderType.Attack, WolfHandle);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, SessionCombatant.Critter(Virgil), true, Living).Disposition);
        var result = Order(owner, CompanionOrderType.BackOff);
        foreach (var outcome in result.Outcomes)
        {
            Assert.AreEqual((CompanionOrderEffect.Stopped, (CompanionOrderLine?)CompanionOrderLine.Acknowledge), (outcome.Effect, outcome.Line));
            Assert.AreEqual((WolfHandle, true, true), (outcome.Stop!.Head, outcome.Stop.Interrupted, outcome.Stop.NoAttack));
            var danger = owner.QueryDanger(outcome.Entity);
            Assert.AreEqual((DangerState.None, DangerFlags.NoAttack | DangerFlags.DemaintainSpells),
                (danger.State, danger.Flags & (DangerFlags.NoAttack | DangerFlags.TargetLocked | DangerFlags.DemaintainSpells)),
                "peace (which ends the fight: DEMAINTAIN_SPELLS), unlock and NO_ATTACK");
        }
        Assert.IsTrue(result.Outcomes[0].Stop!.LeftCombatMode);
        Assert.IsFalse(owner.QueryCombatMode(SessionCombatant.Critter(Virgil)));
    }

    [TestMethod]
    public void Curse_sends_the_follower_away_and_leaves_the_heros_attack_on_it_to_the_host()
    {
        using var owner = Owner();
        StopAll(owner);
        var result = Order(owner, CompanionOrderType.Curse, followers: [F(Virgil), F(Sogg, mindControlled: true)]);
        var cursed = result.Outcomes[0];
        Assert.AreEqual((CompanionOrderEffect.Cursed, true), (cursed.Effect, cursed.Insult));
        Assert.AreEqual(new CompanionOrderAttack(Hero, SessionCombatant.Critter(Virgil), Loudness.Normal, AttackEventFlags.None), cursed.CurseAttack);
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion(Virgil).Standing);
        Assert.IsFalse(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack), "FO7: leaving clears the stop");
        Assert.AreEqual(CompanionOrderEffect.MindControlled, result.Outcomes[1].Effect);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion(Sogg).Standing);

        // Original (FO7): the stop survives the leave.
        using var original = Owner(RuleMode.Original);
        StopAll(original);
        Order(original, CompanionOrderType.Curse, followers: F(Virgil));
        Assert.IsTrue(original.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack));
    }

    [TestMethod]
    public void An_extraordinary_leader_refuses_the_curses_leave()
    {
        using var owner = Owner(hero: false);
        var refused = Order(owner, CompanionOrderType.Curse, followers: F(Virgil, charisma: 25)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.LeaveRefused, false, (CompanionOrderAttack?)null), (refused.Effect, refused.Insult, refused.CurseAttack));
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion(Virgil).Standing);
    }

    [TestMethod]
    public void Every_reached_follower_gets_one_ai_pass_after_its_order()
    {
        using var owner = Owner();
        var ai = new AiSchedulerTests.World().With(Virgil).With(Sogg);
        foreach (var type in new[] { CompanionOrderType.StayClose, CompanionOrderType.None, CompanionOrderType.Attack })
        {
            var result = Order(owner, type, ai: ai);
            CollectionAssert.AreEqual(new[] { Virgil, Sogg }, result.Outcomes.Select(o => o.Entity).ToArray());
            Assert.IsTrue(result.Outcomes.All(o => o.Pass is { Failure: GameSessionFailure.None, Pass: not null }), $"{type}");
        }
        Assert.IsTrue(Order(owner, CompanionOrderType.StayClose).Outcomes.All(o => o.Pass is null), "no host: the host runs it");
    }

    [TestMethod]
    public void Each_pass_runs_before_the_next_followers_order()
    {
        using var owner = Owner();
        var ai = new AiSchedulerTests.World().With(Virgil).With(Sogg);
        Order(owner, CompanionOrderType.SpreadOut);
        foreach (var entity in new[] { Virgil, Sogg }) owner.RunAiPass(owner.Version, entity, ai);
        var start = owner.Version.Revision;
        var result = Order(owner, CompanionOrderType.StayClose, ai: ai);
        Assert.AreEqual((start + 1, start + 2), (result.Outcomes[0].Pass!.Version.Revision, result.Outcomes[1].Pass!.Version.Revision),
            "Virgil's pass sees only his own order; Sogg's comes after it (FO1)");
    }

    [TestMethod]
    public void Through_the_order_an_origin_complaint_a_target_in_combat_and_a_door_follow_the_rule()
    {
        using var owner = Owner();
        var world = new World();
        world.Add("oid:kin", HostilityKind.Npc, c => c with { Origin = 3 });
        world.Add("oid:door", HostilityKind.Object);
        var kin = SessionCombatant.Critter("oid:kin");
        var complaint = Order(owner, CompanionOrderType.Attack, kin, world, followers: F(Virgil)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.Complained, UpsetReason.Origin, -5), (complaint.Effect, complaint.Rule!.Value.Upset, complaint.Reaction!.Value.Delta));
        world.Critters["oid:kin"] = world.Critters["oid:kin"] with { InCombatMode = true };
        var silent = Order(owner, CompanionOrderType.Attack, kin, world, followers: F(Virgil)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.AttackIgnored, FollowerRuleStep.UpsetIgnored, (ReactionAdjustment?)null),
            (silent.Effect, silent.Rule!.Value.Step, silent.Reaction));
        var door = Order(owner, CompanionOrderType.Attack, SessionCombatant.Critter("oid:door"), world, followers: F(Sogg)).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.AttackLocked, true), (door.Effect, door.Locked));
        Assert.AreEqual(SessionCombatant.Critter("oid:door"), owner.QueryDanger(Sogg).Focus);
    }

    [TestMethod]
    public void A_failed_attack_step_still_lifts_the_stop()
    {
        using var owner = Owner();
        StopAll(owner);
        var noWorld = owner.IssueCompanionOrder(owner.Version, new(CompanionOrderType.Attack, CompanionOrderAddressing.All, [F(Virgil)]) { Target = WolfHandle })
            .Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.Failed, GameSessionFailure.InvalidDangerFacts), (noWorld.Effect, noWorld.Failure));
        Assert.IsFalse(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack), "FO9");
    }

    [TestMethod]
    public void Invalid_combat_facts_or_target_refuse_the_whole_order()
    {
        using var owner = Owner();
        var broken = new SessionCombatModeContext { Facts = new() { Speed = int.MaxValue, Life = new LifeFacts() } };
        var facts = F(Virgil) with { Combat = new() { CombatMode = broken } };
        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, Order(owner, CompanionOrderType.BackOff, followers: facts).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, Order(owner, CompanionOrderType.Attack, SessionCombatant.Critter(""), followers: F(Virgil)).Failure);
    }

    [TestMethod]
    public void The_follower_rules_writes_lock_focus_or_complain_and_nothing_else()
    {
        using var owner = Owner();
        var setter = new DangerSetterFacts();
        SessionDangerResult Apply(FollowerRuleStep step, SessionCombatant target, SessionCombatant? leader = null, SessionPeaceReaction? complaint = null) =>
            owner.ApplyFollowerRule(owner.Version, new()
            {
                Outcome = new(Virgil, step, step == FollowerRuleStep.Complain ? UpsetReason.Origin : UpsetReason.None, true),
                Leader = leader ?? Hero, Target = target, Setter = setter, Complaint = complaint,
            });

        Assert.AreEqual(GameSessionDisposition.NoChange, Apply(FollowerRuleStep.ReactToAttack, WolfHandle).Disposition, "the caller's");
        var focus = Apply(FollowerRuleStep.Focus, SessionCombatant.Critter("oid:door"));
        Assert.AreEqual((GameSessionDisposition.Changed, false), (focus.Disposition, focus.Locked));
        Assert.AreEqual(SessionCombatant.Critter("oid:door"), owner.QueryDanger(Virgil).Focus);
        Assert.IsTrue(Apply(FollowerRuleStep.Lock, WolfHandle).Locked);
        Assert.IsTrue(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.TargetLocked));

        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, Apply(FollowerRuleStep.Complain, WolfHandle).Failure, "toward the hero needs facts");
        var complaint = Apply(FollowerRuleStep.Complain, WolfHandle, complaint: new(Npc60, Pc));
        Assert.AreEqual((GameSessionDisposition.Changed, -5, true), (complaint.Disposition, complaint.Reaction!.Value.Delta, complaint.Reaction!.Value.LeaderCheck));
        Assert.AreEqual(GameSessionDisposition.NoChange, Apply(FollowerRuleStep.Complain, WolfHandle, SoggHandle).Disposition,
            "an NPC leader: no reaction change (R2)");
    }
}
