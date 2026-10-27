using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-014: the cast host over a synthetic map (the hero and a box): the casting goal, the release frame's BEGIN with its payment
// and Damage component, and MG39–MG41 in both modes. Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterTests
{
    private static readonly SpellLocation HeroTile = new(5, 5), BoxTile = new(6, 5);

    private sealed class Map(GameSessionOwner owner, bool boxDestroyed = true, int boxFlags = 0) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            "box" => new(id, SpellTargetObject.Scenery, BoxTile, boxFlags | (boxDestroyed ? SpellTargetObject.Destroyed : 0)),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    // A self spell (BEGIN area self, cost 7, instant) whose BEGIN deals 3 normal damage to its target.
    private static SessionSpellRecord SelfBolt(ulong beginTargets = 0x1, int cost = 7, bool technology = false, bool heal = false, int duration = 0) =>
        SessionSpellRecords.Translate(Record(number: 90, beginTargets: beginTargets, info: SpellInfo.Friendly, technology: technology,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x1, 0, 0, 0, -1), 0, 0, new DamageData(0, 3, 3, 0)),
                .. heal ? new[] { new SpellComponent(SpellAction.Begin, SpellComponentType.Heal, new(0x1, 0, 0, 0, -1), 0, 0, new DamageData(0, 2, 2, 0)) } : []])
        with { Cost = cost, DurationSeconds = duration, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(RuleMode mode, SessionSpellRecord record, bool boxDestroyed = true)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.SelectRules(mode);
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, boxDestroyed), new Events()));
    }

    private static int Start(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string target = SessionSpellCaster.Hero, bool free = false)
    {
        var hero = caster.Facts(SessionSpellCaster.Hero)!;
        var to = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, to, to.Location, ParentIsLocalPlayer: true), Free: free),
            record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return started.Creation!.Run!.Id;
    }

    private static (int Fatigue, int HitPoints) Hero(GameSessionOwner owner) => owner.QueryHeroLife()!.Status is var s ? (s.Fatigue, s.HitPoints) : default;

    [TestMethod]
    public void A_cast_pays_at_its_release_frame_and_its_damage_component_hurts_the_target()
    {
        var record = SelfBolt();
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner);
        var run = Start(owner, caster, record);
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(before, Hero(owner), "nothing paid before the release frame");
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
        Assert.AreEqual((before.Fatigue - 7, before.HitPoints - 3), Hero(owner), "the cost and the damage");
        Assert.IsNull(owner.QuerySpellRuns()[run], "an instant spell ends and is freed at once");
        // The component applies during the pass; the host pays the stage's payment from the call's notes after it.
        CollectionAssert.AreEquivalent(new[] { SpellCastEventKind.FatiguePaid, SpellCastEventKind.Damaged, SpellCastEventKind.Freed },
            caster.Log.Select(e => e.Kind).ToArray());
    }

    [TestMethod]
    public void Mg39_a_goal_refused_at_its_start_frees_the_run_charged_in_the_original_and_unpaid_in_Steamwoven()
    {
        foreach (var (mode, paid) in new[] { (RuleMode.Original, 7), (RuleMode.Steamwoven, 0) })
        {
            var record = SelfBolt();
            var (owner, caster) = Session(mode, record);
            using var _ = owner;
            var before = Hero(owner).Fatigue;
            var run = Start(owner, caster, record);
            caster.EndGoal(run, refused: true);
            Assert.AreEqual((before - paid, (SpellRun?)null), (Hero(owner).Fatigue, owner.QuerySpellRuns()[run]), $"{mode}");
        }
    }

    [TestMethod]
    public void Mg40_a_free_run_ended_before_its_begin_is_charged_in_the_original_only()
    {
        foreach (var (mode, paid) in new[] { (RuleMode.Original, 7), (RuleMode.Steamwoven, 0) })
        {
            var record = SelfBolt();
            var (owner, caster) = Session(mode, record);
            using var _ = owner;
            var before = Hero(owner).Fatigue;
            var run = Start(owner, caster, record, free: true);
            caster.EndGoal(run);
            Assert.AreEqual(before - paid, Hero(owner).Fatigue, $"{mode}");
        }
    }

    [TestMethod]
    public void A_goal_ends_nothing_after_its_release_frame_and_a_reused_slot_is_freed_again()
    {
        // Original MG41: the box gone at the release frame keeps the run in its slot; its goal's end then frees nothing.
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster) = Session(RuleMode.Original, record, boxDestroyed: false);
        using var _ = owner;
        var run = Start(owner, caster, record, "box");
        var gone = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
        Assert.IsNull(gone.Release(run));
        var before = Hero(owner).Fatigue;
        Assert.IsNull(gone.EndGoal(run), "the release frame handled the goal's frame");
        Assert.AreEqual((before, true), (Hero(owner).Fatigue, owner.QuerySpellRuns()[run] is not null), "nothing charged or freed");

        // Freed through the same host, its slot reused: the new run's goal ends charged, once.
        gone.Act(new(SessionSpellRunActionKind.Free, run));
        var second = Start(owner, caster, record, "box");
        Assert.AreEqual(run, second, "the slot is reused");
        Assert.IsNotNull(gone.EndGoal(second));
        Assert.IsNull(gone.EndGoal(second), "a goal ends once");
        Assert.AreEqual(before - 7, Hero(owner).Fatigue, "charged once");
    }

    [TestMethod]
    public void A_goal_ends_nothing_for_a_run_whose_begin_ran()
    {
        var record = SelfBolt(duration: 30);
        var (owner, caster) = Session(RuleMode.Original, record);
        using var _ = owner;
        var run = Start(owner, caster, record);
        caster.Release(run);
        Assert.IsNotNull(owner.QuerySpellRuns()[run], "a lasting spell keeps its slot after BEGIN");
        var before = Hero(owner).Fatigue;
        // Another host, which never saw the release frame, reads the run's processed flag.
        var other = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
        Assert.IsNull(other.EndGoal(run));
        Assert.AreEqual(before, Hero(owner).Fatigue);
    }

    [TestMethod]
    public void A_goal_is_refused_when_the_source_cannot_afford_the_cast_or_its_target_is_gone()
    {
        var dear = SelfBolt(cost: 1000);
        var (owner, caster) = Session(RuleMode.Steamwoven, dear);
        using (owner)
        {
            var run = Start(owner, caster, dear);
            Assert.AreEqual(SpellCastGoalOutcome.CannotAfford, caster.StartGoal(run, new NoCombat()).Outcome);
        }

        var record = SelfBolt(beginTargets: 0x4);
        var (other, aimed) = Session(RuleMode.Steamwoven, record, boxDestroyed: false);
        using (other)
        {
            var run = Start(other, aimed, record, "box");
            var gone = new SessionSpellCaster(other, n => n == record.Data.Number ? record : null, new Map(other), new Events());
            Assert.AreEqual(SpellCastGoalOutcome.TargetGone, gone.StartGoal(run, new NoCombat()).Outcome);
        }
    }

    [TestMethod]
    public void Mg40_a_technology_run_ended_before_its_begin_is_charged_in_the_original_only()
    {
        foreach (var (mode, paid) in new[] { (RuleMode.Original, 7), (RuleMode.Steamwoven, 0) })
        {
            var record = SelfBolt(technology: true);
            var (owner, caster) = Session(mode, record);
            using var _ = owner;
            var before = Hero(owner).Fatigue;
            caster.EndGoal(Start(owner, caster, record));
            Assert.AreEqual(before - paid, Hero(owner).Fatigue, $"{mode}");
        }
    }

    [TestMethod]
    public void A_heal_component_restores_hit_points_to_its_target()
    {
        var record = SelfBolt(heal: true);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner).HitPoints;
        caster.Release(Start(owner, caster, record));
        Assert.AreEqual(before - 1, Hero(owner).HitPoints, "3 damage, then 2 healed");
        Assert.AreEqual(2, caster.Log.Single(e => e.Kind == SpellCastEventKind.Healed).Value);
    }

    [TestMethod]
    public void The_release_re_check_refuses_an_object_the_picker_refuses()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster) = Session(RuleMode.Steamwoven, record, boxDestroyed: false);
        using var _ = owner;
        var id = Start(owner, caster, record, "box");
        var run = owner.QuerySpellRuns()[id]!;
        Assert.IsTrue(caster.TargetStillValid(run));
        var refused = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null,
            new Map(owner, boxDestroyed: false, boxFlags: SessionSpellCaster.PickerRefused), new Events());
        Assert.IsFalse(refused.TargetStillValid(run));
    }

    [TestMethod]
    public void Mg41_a_target_gone_at_the_release_frame_keeps_the_run_in_the_original_and_frees_it_in_Steamwoven()
    {
        foreach (var (mode, kept) in new[] { (RuleMode.Original, true), (RuleMode.Steamwoven, false) })
        {
            // A BEGIN area that takes objects (0x4): the box, destroyed by the release frame, no longer passes.
            var record = SelfBolt(beginTargets: 0x4);
            var (owner, caster) = Session(mode, record, boxDestroyed: false);
            using var _ = owner;
            var run = Start(owner, caster, record, "box");
            var gone = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
            Assert.IsFalse(gone.TargetStillValid(owner.QuerySpellRuns()[run]!));
            var before = Hero(owner).Fatigue;
            Assert.IsNull(gone.Release(run), "nothing begins");
            Assert.AreEqual(kept, owner.QuerySpellRuns()[run] is not null, $"{mode}");
            Assert.IsNull(gone.EndGoal(run), "the release frame handled the goal's frame");
            Assert.AreEqual(before, Hero(owner).Fatigue, $"{mode}: unpaid");
        }
    }

    private sealed class SoloRound : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [new(SessionCombatant.Hero, new(0, 0))];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    [TestMethod]
    public void A_goal_charges_4_action_points_in_a_turn_based_round_and_the_hero_overexerts_without_them()
    {
        var record = SelfBolt(cost: 1);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var round = new SoloRound();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StartCombatRound(owner.Version, round).Disposition);
        var points = owner.QueryCombatRound()!.ActionPoints;
        Assert.IsTrue(points >= TurnBasedRules.CastSpellCost);

        var paid = caster.StartGoal(Start(owner, caster, record), round);
        Assert.AreEqual((SpellCastGoalOutcome.Started, SessionActionPointUse.Paid), (paid.Outcome, paid.ActionPoints!.Charge));
        Assert.AreEqual(points - TurnBasedRules.CastSpellCost, owner.QueryCombatRound()!.ActionPoints);

        // Spend the round's points with further goals until fewer than 4 are left: with some left the hero overexerts (paying them
        // all) and the goal starts; with none the charge is refused and the goal ends.
        SpellCastGoalStart last;
        do last = caster.StartGoal(Start(owner, caster, record), round);
        while (last.ActionPoints!.Charge == SessionActionPointUse.Paid);
        if (last.ActionPoints.Charge == SessionActionPointUse.Overexerted)
        {
            Assert.AreEqual(SpellCastGoalOutcome.Started, last.Outcome);
            Assert.IsTrue(owner.QueryHeroLife()!.Record.State.FatigueDamage > 0, "the overexertion's fatigue damage");
            last = caster.StartGoal(Start(owner, caster, record), round);
        }
        Assert.AreEqual((SpellCastGoalOutcome.NoActionPoints, SessionActionPointUse.Refused), (last.Outcome, last.ActionPoints!.Charge));
    }

    [TestMethod]
    public void A_goal_ended_before_its_release_frame_is_charged_in_Steamwoven_when_its_begin_would_pay()
    {
        var record = SelfBolt();
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner).Fatigue;
        var run = Start(owner, caster, record);
        Assert.IsNotNull(caster.EndGoal(run));
        Assert.AreEqual((before - 7, (SpellRun?)null), (Hero(owner).Fatigue, owner.QuerySpellRuns()[run]));
    }

    [TestMethod]
    public void A_release_frame_begins_a_run_once()
    {
        var record = SelfBolt(duration: 30);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner);
        var run = Start(owner, caster, record);
        Assert.IsNotNull(caster.Release(run));
        Assert.IsNull(caster.Release(run), "the release gate fires once");
        // Another host, which never saw the release frame, reads that the BEGIN ran.
        Assert.IsNull(new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events()).Release(run));
        Assert.AreEqual((before.Fatigue - 7, before.HitPoints - 3), Hero(owner), "paid and hurt once");
    }

    [TestMethod]
    public void A_slot_freed_outside_the_host_starts_a_new_goal_that_its_end_frees()
    {
        var record = SelfBolt(duration: 30);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Start(owner, caster, record);
        caster.Release(run);
        // Freed by the session directly, not through this host: the host never sees the note.
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ActOnSpellRun(owner.Version, caster, new(SessionSpellRunActionKind.Free, run)).Disposition);
        var second = Start(owner, caster, record);
        Assert.AreEqual(run, second, "the slot is reused");
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(second, new NoCombat()).Outcome);
        Assert.IsNotNull(caster.EndGoal(second, refused: true));
        Assert.IsNull(owner.QuerySpellRuns()[second], "the new goal's end frees its run");
    }
}
