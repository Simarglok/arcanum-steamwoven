using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-014: the cast host over a synthetic map (the hero and a box): the casting goal, the release frame's BEGIN with its payment
// and Damage component, MG39–MG40 in both modes and MG41 fixed in both. Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterTests
{
    private static readonly SpellLocation HeroTile = new(5, 5), BoxTile = new(6, 5);

    private sealed class Map(GameSessionOwner owner, bool boxDestroyed = true, int boxFlags = 0, bool noLineOfFire = false, int heroSpellFlags = 0,
        int heroCritterFlags = 0) : ISpellCastMap
    {
        // ARC-MAGIC-018: the box destroyed later (while a missile flies).
        public bool BoxGone { get; set; }

        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, HeroTile, SpellFlags: heroSpellFlags, CritterFlags: heroCritterFlags,
                HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            "box" => new(id, SpellTargetObject.Scenery, BoxTile, boxFlags | (boxDestroyed || BoxGone ? SpellTargetObject.Destroyed : 0)),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(noLineOfFire ? 100 : 0, null);
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
    private static SessionSpellRecord SelfBolt(ulong beginTargets = 0x1, int cost = 7, bool technology = false, bool heal = false, int duration = 0,
        int range = 99, uint cancels = 0x4) =>
        SessionSpellRecords.Translate(Record(number: 90, beginTargets: beginTargets, info: SpellInfo.Friendly, technology: technology,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x1, 0, 0, 0, -1), 0, 0, new DamageData(0, 3, 3, 0)),
                .. heal ? new[] { new SpellComponent(SpellAction.Begin, SpellComponentType.Heal, new(0x1, 0, 0, 0, -1), 0, 0, new DamageData(0, 2, 2, 0)) } : []])
        with { Cost = cost, Range = range, DurationSeconds = duration, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = cancels });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(RuleMode mode, SessionSpellRecord record, bool boxDestroyed = true,
        bool noLineOfFire = false)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.SelectRules(mode);
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, boxDestroyed, noLineOfFire: noLineOfFire),
            new Events()));
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
    public void A_taking_damage_script_that_skips_the_default_cancels_a_damage_component_with_the_run_parent_as_the_attacker()
    {
        // ARC-DEV-261 (GT198, DH1): the victim's point-0x17 script is asked before the damage step of each damaged object, the run's parent as the attacker; false cancels that step (logged), the rest of the run goes on.
        var record = SelfBolt();
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var asked = new List<(string Victim, string? Attacker)>();
        caster.TakingDamage = (victim, attacker) => { asked.Add((victim, attacker)); return false; };
        var before = Hero(owner);
        var run = Start(owner, caster, record);
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
        Assert.AreEqual((before.Fatigue - 7, before.HitPoints), Hero(owner), "the cost is paid, the damage is cancelled");
        CollectionAssert.AreEqual(new[] { (SessionSpellCaster.Hero, (string?)SessionSpellCaster.Hero) }, asked);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.DamageVetoed && e.Object == SessionSpellCaster.Hero));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged));
        Assert.IsNull(owner.QuerySpellRuns()[run], "the run still ends");
    }

    [TestMethod]
    public void Eye_candy_components_are_logged_for_the_host_on_their_target_with_their_entry_and_play_flags()
    {
        // ARC-MAGIC-017: Add of number 2 (the destination) with the callback flag, then Remove of number 3 with no_id, on the self target.
        var record = SessionSpellRecords.Translate(Record(number: 90, beginTargets: 0x1, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.EyeCandy, new(0x1, 0, 0, 0, -1), 0, 0, new EyeCandyData(2, true, 0x8)),
                new(SpellAction.Begin, SpellComponentType.EyeCandy, new(0x1, 0, 0, 0, -1), 0, 0, new EyeCandyData(3, false, 0x100))])
            with { Cost = 7, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });
        Assert.AreEqual(new SpellEyeCandyComponent(2, true, 0x8), record.Components[0].EyeCandy);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Start(owner, caster, record);
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        var eye = caster.Log.Where(e => e.Kind is SpellCastEventKind.EyeCandyAdded or SpellCastEventKind.EyeCandyRemoved).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            new SpellCastEvent(SpellCastEventKind.EyeCandyAdded, run, 90 * 6 + 2, SessionSpellCaster.Hero, 0x8),
            new SpellCastEvent(SpellCastEventKind.EyeCandyRemoved, run, 90 * 6 + 3, SessionSpellCaster.Hero, 0x100),
        }, eye);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), "an eye candy is no component left undone");
    }

    [TestMethod]
    public void An_AGoal_component_for_the_knockback_asks_the_host_to_push_the_target_from_the_source_tile()
    {
        // ARC-DEV-141 (GT59): AGoal 43 logs the push request with the source tile (the source object's location); other animation goals stay not ready.
        var record = SessionSpellRecords.Translate(Record(number: 91, beginTargets: 0x1, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.AGoal, new(0x1, 0, 0, 0, -1), 0, 0, new AnimationGoalData(43, 0)),
                new(SpellAction.Begin, SpellComponentType.AGoal, new(0x1, 0, 0, 0, -1), 0, 0, new AnimationGoalData(62, 0))])
            with { Cost = 7, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });
        Assert.AreEqual(43, record.Components[0].AnimationGoal);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Start(owner, caster, record);
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        var push = caster.Log.Single(e => e.Kind == SpellCastEventKind.Knockback);
        Assert.AreEqual(run, push.Run);
        Assert.AreEqual(SessionSpellCaster.Hero, push.Object);
        var hero = caster.Facts(SessionSpellCaster.Hero)!.Location;
        Assert.AreEqual((unchecked((int)hero.X), unchecked((int)hero.Y)), (push.Value, push.Detail), "the source object's location");
        Assert.AreEqual(1, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady), "knock-down 62 is still not ready");
    }

    [TestMethod]
    public void An_AGoal_component_for_the_floating_goal_asks_the_host_to_hover_the_target_and_an_AGoalTerminate_to_end_it()
    {
        // ARC-DEV-143 (GT69): AGoal 44 logs the hover request, AGoalTerminate 44 the end; another terminated goal stays not ready.
        var record = SessionSpellRecords.Translate(Record(number: 92, beginTargets: 0x1, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.AGoal, new(0x1, 0, 0, 0, -1), 0, 0, new AnimationGoalData(44, 0)),
                new(SpellAction.Begin, SpellComponentType.AGoalTerminate, new(0x1, 0, 0, 0, -1), 0, 0, new AnimationGoalTerminateData(44)),
                new(SpellAction.Begin, SpellComponentType.AGoalTerminate, new(0x1, 0, 0, 0, -1), 0, 0, new AnimationGoalTerminateData(43))])
            with { Cost = 7, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });
        Assert.AreEqual(44, record.Components[0].AnimationGoal);
        Assert.AreEqual(44, record.Components[1].TerminatedGoal);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Start(owner, caster, record);
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        var hover = caster.Log.Where(e => e.Kind is SpellCastEventKind.Hover or SpellCastEventKind.HoverEnded).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            new SpellCastEvent(SpellCastEventKind.Hover, run, 0, SessionSpellCaster.Hero),
            new SpellCastEvent(SpellCastEventKind.HoverEnded, run, 0, SessionSpellCaster.Hero),
        }, hover);
        Assert.AreEqual(1, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady), "terminating goal 43 is still not ready");
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
        // MG41 (owner decision 2026-10-28: a slot leak, fixed in both settings): the box gone at the release frame frees the run unpaid;
        // its goal's end then frees and charges nothing.
        foreach (var mode in new[] { RuleMode.Original, RuleMode.Steamwoven })
        {
            var record = SelfBolt(beginTargets: 0x4);
            var (owner, caster) = Session(mode, record, boxDestroyed: false);
            using var _ = owner;
            var run = Start(owner, caster, record, "box");
            var gone = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
            var before = Hero(owner).Fatigue;
            Assert.IsNull(gone.Release(run), $"{mode}");
            Assert.IsNull(gone.EndGoal(run), $"{mode}: the release frame handled the goal's frame");
            Assert.AreEqual((before, true), (Hero(owner).Fatigue, owner.QuerySpellRuns()[run] is null), $"{mode}: freed unpaid");

            // Its slot reused: the new run's goal ends charged, once.
            var second = Start(owner, caster, record, "box");
            Assert.AreEqual(run, second, $"{mode}: the slot is reused");
            Assert.IsNotNull(gone.EndGoal(second), $"{mode}");
            Assert.IsNull(gone.EndGoal(second), $"{mode}: a goal ends once");
            Assert.AreEqual(before - 7, Hero(owner).Fatigue, $"{mode}: charged once");
        }
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
    public void Mg41_a_target_gone_at_the_release_frame_frees_the_run_unpaid_in_both_modes()
    {
        // Owner decision 2026-10-28: the original keeps the run in its slot until the game ends (a slot leak), fixed in both modes.
        foreach (var (mode, kept) in new[] { (RuleMode.Original, false), (RuleMode.Steamwoven, false) })
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

    // ARC-MAGIC-015: the spell UI's cast at a picked target (0x57C110).

    [TestMethod]
    public void The_ui_cast_starts_a_run_at_its_target_object_whose_goal_then_plays()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster) = Session(RuleMode.Steamwoven, record, boxDestroyed: false);
        using var _ = owner;
        var attempt = caster.CastAt(SessionSpellCaster.Hero, record.Data.Number, "box", SpellLocation.None);
        Assert.AreEqual(SpellCastAttemptOutcome.Started, attempt.Outcome, $"{attempt.Run?.Failure} {attempt.Run?.Creation?.Check}");
        var run = owner.QuerySpellRuns()[attempt.RunId!.Value]!;
        Assert.AreEqual(("box", SpellLocation.None, SessionSpellCaster.Hero), (run.Target?.Id, run.TargetTile, run.Source?.Id), "the object, no tile");
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run.Id, new NoCombat()).Outcome);
    }

    [TestMethod]
    public void The_ui_cast_shows_600_when_the_caster_cannot_afford_it_and_604_without_a_line_of_fire()
    {
        var dear = SelfBolt(beginTargets: 0x4, cost: 100000);
        var (owner, caster) = Session(RuleMode.Steamwoven, dear, boxDestroyed: false);
        using (owner)
        {
            var attempt = caster.CastAt(SessionSpellCaster.Hero, dear.Data.Number, "box", SpellLocation.None);
            Assert.AreEqual((SpellCastAttemptOutcome.CannotAfford, (int?)600), (attempt.Outcome, attempt.Message));
            Assert.AreEqual(600, caster.Log.Single(e => e.Kind == SpellCastEventKind.Message).Value);
            Assert.AreEqual(0, owner.QuerySpellRuns().Count, "no run");
        }
        var record = SelfBolt(beginTargets: 0x4);
        var (blocked, blockedCaster) = Session(RuleMode.Steamwoven, record, boxDestroyed: false, noLineOfFire: true);
        using (blocked)
        {
            var attempt = blockedCaster.CastAt(SessionSpellCaster.Hero, record.Data.Number, "box", SpellLocation.None);
            Assert.AreEqual((SpellCastAttemptOutcome.NoLineOfFire, (int?)604), (attempt.Outcome, attempt.Message));
            // A self-cast needs no line of fire (0x456BC0).
            Assert.AreEqual(SpellCastAttemptOutcome.Started, blockedCaster.CastAt(SessionSpellCaster.Hero, record.Data.Number, SessionSpellCaster.Hero,
                SpellLocation.None).Outcome);
        }
    }

    [TestMethod]
    public void The_cursor_tests_a_candidate_against_its_mask()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster) = Session(RuleMode.Steamwoven, record, boxDestroyed: false);
        using var _ = owner;
        Assert.IsTrue(caster.TestTarget(SessionSpellCaster.Hero, record.Data.Number, 0x4, "box", SpellLocation.None).Passes);
        Assert.IsFalse(caster.TestTarget(SessionSpellCaster.Hero, record.Data.Number, 0x44, "box", SpellLocation.None).Passes, "a box is no critter");
        Assert.IsFalse(caster.TestTarget(SessionSpellCaster.Hero, record.Data.Number, 0x4, "nobody", SpellLocation.None).Passes);
        Assert.IsTrue(caster.TestTarget(SessionSpellCaster.Hero, record.Data.Number, 0x8000UL << 32, null, BoxTile).Passes, "a tile spell takes a tile");
    }

    [TestMethod]
    public void The_cursor_picks_in_the_original_order()
    {
        // ARC-MAGIC-021 (SU15–SU19): the object under the point, else (here no tile lists an object) the tile; a tile spell clicked
        // on the box casts at the box's own tile, not at the clicked one.
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster) = Session(RuleMode.Steamwoven, record, boxDestroyed: false);
        using var _ = owner;
        var clicked = new SpellLocation(7, 7);
        var box = caster.PickTarget(SessionSpellCaster.Hero, record.Data.Number, 0x4, "box", clicked);
        Assert.AreEqual(("box", SpellLocation.None, true), (box.Object?.Id, box.Tile, box.Found));
        var tile = caster.PickTarget(SessionSpellCaster.Hero, record.Data.Number, 0x8000UL << 32, "box", clicked);
        Assert.AreEqual(((string?)null, BoxTile, true), (tile.Object?.Id, tile.Tile, tile.Found));
        Assert.IsFalse(caster.PickTarget(SessionSpellCaster.Hero, record.Data.Number, 0x44, "box", null).Found, "no critter, no tile");
        Assert.IsTrue(caster.PickSpared(SessionSpellCaster.Hero, 0x10000004)(SessionSpellCaster.Hero), "no-self spares the hero");
        Assert.IsFalse(caster.PickSpared(SessionSpellCaster.Hero, 0x4)(SessionSpellCaster.Hero));
        Assert.IsFalse(caster.PickSpared(SessionSpellCaster.Hero, 0x10000004)("box"));
    }

    [TestMethod]
    public void The_ui_cast_checks_the_cost_before_the_line_of_fire_and_refuses_a_missing_target_or_an_out_of_range_run()
    {
        var dear = SelfBolt(beginTargets: 0x4, cost: 100000);
        var (owner, caster) = Session(RuleMode.Steamwoven, dear, boxDestroyed: false, noLineOfFire: true);
        using (owner)
        {
            Assert.AreEqual(SpellCastAttemptOutcome.CannotAfford, caster.CastAt(SessionSpellCaster.Hero, dear.Data.Number, "box", SpellLocation.None).Outcome,
                "600 before 604");
        }
        var near = SelfBolt(beginTargets: 0x4, range: 0);
        var (closed, closedCaster) = Session(RuleMode.Steamwoven, near, boxDestroyed: false);
        using (closed)
        {
            var attempt = closedCaster.CastAt(SessionSpellCaster.Hero, near.Data.Number, "box", SpellLocation.None);
            Assert.AreEqual((SpellCastAttemptOutcome.Refused, (int?)null), (attempt.Outcome, attempt.RunId), $"{attempt.Run?.Creation?.Check}");
            Assert.AreEqual(0, closed.QuerySpellRuns().Count, "the invocation check refused the run");
            Assert.AreSame(closed, closedCaster.Owner);
            Assert.AreEqual(SpellCastAttemptOutcome.Nothing, closedCaster.CastAt(SessionSpellCaster.Hero, near.Data.Number, "nobody", SpellLocation.None).Outcome,
                "a target missing from the map");
        }
    }

    [TestMethod]
    public void The_cursor_refuses_an_object_the_picker_marks()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        using var _ = owner;
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null,
            new Map(owner, boxDestroyed: false, boxFlags: SessionSpellCaster.PickerRefused), new Events());
        Assert.IsFalse(caster.TestTarget(SessionSpellCaster.Hero, record.Data.Number, 0x4, "box", SpellLocation.None).Passes);
    }

    // ARC-MAGIC-019: a second cast while a goal plays (SU13) and the queued casts a stack drops (MG43).

    private static int Cast(SessionSpellCaster caster, SessionSpellRecord record)
    {
        var attempt = caster.CastAt(SessionSpellCaster.Hero, record.Data.Number, SessionSpellCaster.Hero, SpellLocation.None);
        Assert.AreEqual(SpellCastAttemptOutcome.Started, attempt.Outcome, $"{attempt.Run?.Failure} {attempt.Run?.Creation?.Check}");
        return attempt.RunId!.Value;
    }

    [TestMethod]
    public void Su13_casts_queue_beneath_a_playing_goal_up_to_a_stack_index_of_4_and_each_plays_later_through_its_own_goal()
    {
        // No Cancels_SF: a queued run is active from the click, so the playing run's cancels (0x4534E0) would interrupt it.
        var record = SelfBolt(cost: 1, cancels: 0);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner).Fatigue;
        var playing = Cast(caster, record);
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(playing, new NoCombat()).Outcome);
        var queued = new List<int>();
        for (var depth = 0; depth < SpellUiRules.QueueRefusedFromDepth; depth++)
        {
            var run = Cast(caster, record);
            Assert.AreEqual(SpellCastQueueOutcome.Queued, caster.Queue(run, depth), $"depth {depth}");
            queued.Add(run);
        }
        var refused = Cast(caster, record);
        Assert.AreEqual(SpellCastQueueOutcome.Refused, caster.Queue(refused, SpellUiRules.QueueRefusedFromDepth));
        Assert.IsNull(owner.QuerySpellRuns()[refused], "a refused cast's run is freed");
        Assert.AreEqual(before, Hero(owner).Fatigue, "nothing paid at the clicks: each run waits for its own release");
        Assert.IsTrue(queued.All(r => owner.QuerySpellRuns()[r] is { Action: SpellRunAction.Begin }), "the queued runs exist from the click");

        // The playing goal releases and ends; then each queued goal starts, releases and pays in turn.
        foreach (var run in queued.Prepend(playing))
        {
            if (run != playing) Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
            Assert.IsTrue(owner.QuerySpellRuns()[run] is not null, $"run {run} waits for its goal");
            Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
            Assert.IsNull(caster.EndGoal(run), "nothing freed after the release frame");
        }
        Assert.AreEqual(before - 5, Hero(owner).Fatigue, "five casts paid once each");
        Assert.AreEqual(SpellCastQueueOutcome.NoRun, caster.Queue(refused, 0));
    }

    [TestMethod]
    public void Su13_a_queued_run_is_active_so_the_playing_runs_cancels_interrupt_it()
    {
        var record = SelfBolt(cost: 1);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var playing = Cast(caster, record);
        caster.StartGoal(playing, new NoCombat());
        var queued = Cast(caster, record);
        Assert.AreEqual(SpellCastQueueOutcome.Queued, caster.Queue(queued, 0));
        caster.Release(playing);
        Assert.IsNull(owner.QuerySpellRuns()[queued], "the record's Cancels_SF matches its own: the BEGIN interrupts the queued run");
        Assert.AreEqual(SpellCastGoalOutcome.NoRun, caster.StartGoal(queued, new NoCombat()).Outcome);
    }

    [TestMethod]
    public void Su13_no_cast_is_queued_in_a_turn_based_round()
    {
        var record = SelfBolt(cost: 1);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StartCombatRound(owner.Version, new SoloRound()).Disposition);
        var run = Cast(caster, record);
        Assert.AreEqual(SpellCastQueueOutcome.Refused, caster.Queue(run, 0));
        Assert.IsNull(owner.QuerySpellRuns()[run]);
    }

    [TestMethod]
    public void Mg43_a_dropped_queued_cast_is_freed_unpaid_in_both_modes()
    {
        // A technical bug (the run leaks its slot): fixed in both modes, no switch (owner decision 2026-10-28).
        foreach (var mode in new[] { RuleMode.Original, RuleMode.Steamwoven })
        {
            var record = SelfBolt();
            var (owner, caster) = Session(mode, record);
            using var _ = owner;
            var before = Hero(owner).Fatigue;
            var run = Cast(caster, record);
            Assert.AreEqual(SpellCastQueueOutcome.Queued, caster.Queue(run, 0));
            Assert.IsNotNull(caster.DropQueued(run), $"{mode}");
            Assert.AreEqual(((SpellRun?)null, before), (owner.QuerySpellRuns()[run], Hero(owner).Fatigue), $"{mode}: freed unpaid");
        }
        // A run whose BEGIN ran is no queued cast: nothing is freed.
        var begun = SelfBolt(duration: 30);
        var (session, host) = Session(RuleMode.Steamwoven, begun);
        using var __ = session;
        var started = Cast(host, begun);
        host.Release(started);
        Assert.IsNull(host.DropQueued(started));
        Assert.IsNotNull(session.QuerySpellRuns()[started]);
    }

    // ---- ARC-MAGIC-018: the missile and the facing turn ----

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Map Map) MissileSession(RuleMode mode, SessionSpellRecord record, int heroSpellFlags = 0,
        int heroCritterFlags = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.SelectRules(mode);
        var map = new Map(owner, boxDestroyed: false, heroSpellFlags: heroSpellFlags, heroCritterFlags: heroCritterFlags);
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, map, new Events()), map);
    }

    [TestMethod]
    public void A_missile_launch_handles_the_release_frame_and_its_arrival_begins_the_run()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster, _) = MissileSession(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner).Fatigue;
        var run = Start(owner, caster, record, "box");
        var start = caster.StartGoal(run, new NoCombat());
        Assert.AreEqual((SpellCastGoalOutcome.Started, new SpellFacing(SpellFacingKind.Turned, 5)), (start.Outcome, start.Facing), "the hero turns to the box (+x)");
        Assert.IsTrue(caster.LaunchMissile(run));
        Assert.IsFalse(caster.LaunchMissile(run), "one missile a goal");
        Assert.IsTrue(caster.MissileFlies(run));
        Assert.IsNull(caster.Release(run), "the release frame is handled");
        Assert.IsNull(caster.EndGoal(run), "the casting goal's end frees nothing while the missile flies");
        Assert.AreEqual((before, true), (Hero(owner).Fatigue, owner.QuerySpellRuns()[run] is not null), "nothing paid before the arrival");
        Assert.AreEqual(GameSessionDisposition.Changed, caster.ArriveMissile(run)!.Disposition);
        Assert.AreEqual(before - 7, Hero(owner).Fatigue, "the BEGIN paid at the arrival");
        Assert.IsFalse(caster.MissileFlies(run));
        Assert.IsNull(caster.ArriveMissile(run), "a missile arrives once");
        Assert.IsNull(caster.LoseMissile(run));
    }

    [TestMethod]
    public void Mg41_a_target_gone_when_the_missile_arrives_frees_the_run_unpaid_in_both_modes()
    {
        foreach (var (mode, kept) in new[] { (RuleMode.Original, false), (RuleMode.Steamwoven, false) })
        {
            var record = SelfBolt(beginTargets: 0x4);
            var (owner, caster, map) = MissileSession(mode, record);
            using var _ = owner;
            var run = Start(owner, caster, record, "box");
            caster.StartGoal(run, new NoCombat());
            Assert.IsTrue(caster.LaunchMissile(run));
            map.BoxGone = true;
            var before = Hero(owner).Fatigue;
            Assert.IsNull(caster.ArriveMissile(run), "nothing begins");
            Assert.AreEqual((kept, before), (owner.QuerySpellRuns()[run] is not null, Hero(owner).Fatigue), $"{mode}");
        }
    }

    [TestMethod]
    public void Mg44_a_lost_missile_frees_its_run_unpaid_in_both_modes()
    {
        // Owner decision 2026-10-28: the original's slot leak is fixed in both rule modes.
        foreach (var mode in new[] { RuleMode.Original, RuleMode.Steamwoven })
        {
            var record = SelfBolt(beginTargets: 0x4);
            var (owner, caster, _) = MissileSession(mode, record);
            using var _ = owner;
            var run = Start(owner, caster, record, "box");
            caster.StartGoal(run, new NoCombat());
            Assert.IsNull(caster.LoseMissile(run), "no missile flies yet");
            caster.LaunchMissile(run);
            var before = Hero(owner).Fatigue;
            var lost = caster.LoseMissile(run);
            Assert.AreEqual((false, before, false), (owner.QuerySpellRuns()[run] is not null, Hero(owner).Fatigue, lost is null), $"{mode}");
            Assert.IsFalse(caster.MissileFlies(run));
            Assert.IsNull(caster.ArriveMissile(run), "a lost missile never arrives");
            Assert.IsNull(caster.EndGoal(run), "the casting goal's end still frees nothing");
        }
    }

    [TestMethod]
    public void A_missile_whose_run_is_freed_meanwhile_stops_flying_and_a_new_goal_for_the_slot_forgets_it()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster, _) = MissileSession(RuleMode.Original, record);
        using var _ = owner;
        var run = Start(owner, caster, record, "box");
        caster.StartGoal(run, new NoCombat());
        Assert.IsTrue(caster.LaunchMissile(run));
        caster.Act(new(SessionSpellRunActionKind.Free, run));
        Assert.IsFalse(caster.MissileFlies(run), "the freed note forgets the missile");
        Assert.IsNull(caster.ArriveMissile(run));
        // The slot reused: the new run's goal launches its own missile, which the old one's end cannot touch.
        var second = Start(owner, caster, record, "box");
        Assert.AreEqual(run, second, "the slot is reused");
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(second, new NoCombat()).Outcome);
        Assert.IsFalse(caster.MissileFlies(second));
        Assert.IsTrue(caster.LaunchMissile(second));
        Assert.AreEqual(GameSessionDisposition.Changed, caster.ArriveMissile(second)!.Disposition);
    }

    [TestMethod]
    public void The_facing_turn_refuses_a_stunned_or_stoned_caster_and_turns_nothing_toward_itself()
    {
        foreach (var (spellFlags, critterFlags) in new[] { (SpellFacingRules.Stoned, 0), (0, 0x20), (0, 0x40) })
        {
            var record = SelfBolt(beginTargets: 0x4);
            var (owner, caster, _) = MissileSession(RuleMode.Steamwoven, record, spellFlags, critterFlags);
            using var _ = owner;
            var run = Start(owner, caster, record, "box");
            var start = caster.StartGoal(run, new NoCombat());
            Assert.AreEqual((SpellCastGoalOutcome.CannotTurn, SpellFacingKind.Refused), (start.Outcome, start.Facing?.Kind), $"0x{spellFlags:X}/0x{critterFlags:X}");
            caster.EndGoal(run, refused: true);
            Assert.IsNull(owner.QuerySpellRuns()[run], "ended through the goal's end (MG39: unpaid in Steamwoven)");
        }
        var self = SelfBolt();
        var (o, c, _) = MissileSession(RuleMode.Steamwoven, self, heroCritterFlags: 0x20);
        using var __ = o;
        var started = c.StartGoal(Start(o, c, self), new NoCombat());
        Assert.AreEqual((SpellCastGoalOutcome.Started, SpellFacingKind.Unturned), (started.Outcome, started.Facing?.Kind), "its own target: no turn, no refusal");
    }

    // ARC-DEV-302 (GT145–GT147, 0x454A10, 0x455100): a maintained spell whose BEGIN turns a hold flag on and whose END turns it off, with SpellFlagsSet as the record parse gives it.
    private static SessionSpellRecord Hold(int field, uint flag, int number = 93) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x1, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.ObjFlag, new(0x1, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, true)),
                new(SpellAction.End, SpellComponentType.ObjFlag, new(0x1, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, false))])
            with { Cost = 7, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0,
                SpellFlagsSet = field == SessionSpellCaster.SpellFlagsField ? flag : 0,
                // The END's area is the caster too, so its component reaches the target when the run rules end the run.
                Areas = [new(0x1, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x1, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record)
    {
        var run = Start(owner, caster, record);
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        return run;
    }

    // The END component of a run, applied as the run's END action does.
    private static void EndComponent(GameSessionOwner owner, SessionSpellCaster caster, int run) =>
        caster.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(caster.RunObject(SessionSpellCaster.Hero), HeroTile));

    [TestMethod]
    public void An_entangle_writes_the_spell_flag_asks_the_host_to_interrupt_and_counts_the_runs_that_provide_it()
    {
        var record = Hold(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.EntangledSpellFlag);
        Assert.AreEqual(new SpellObjectFlagComponent(20, 0x1000000, true), record.Components[0].ObjectFlag);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var first = Cast(owner, caster, record);
        Assert.AreEqual(new SessionObjectFlags(0x1000000, 0, 0, 0), owner.QueryObjectFlags(SessionSpellCaster.Hero));
        CollectionAssert.AreEqual(new[]
        {
            new SpellCastEvent(SpellCastEventKind.ObjectFlagWritten, first, 0x1000000, SessionSpellCaster.Hero, 1),
            new SpellCastEvent(SpellCastEventKind.HoldStarted, first, 0x1000000, SessionSpellCaster.Hero, 1),
        }, caster.Log.Where(e => e.Kind is SpellCastEventKind.ObjectFlagWritten or SpellCastEventKind.HoldStarted or SpellCastEventKind.ObjectFlagKept).ToArray());
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
        Assert.AreEqual(0x1000000, caster.SpellFlags(caster.RunObject(SessionSpellCaster.Hero)!), "the run rules read the written flag");
        Assert.IsTrue(owner.QueryHeroLife()!.Status.IsActive, "entangled is not in can-act (GT150)");
        // A second entangle while the first provides the bit: nothing happens, no interrupt (0x455100 counts both runs).
        var second = Cast(owner, caster, record);
        Assert.AreEqual(1, caster.Log.Count(e => e.Kind == SpellCastEventKind.HoldStarted));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagKept, second, 0x1000000, SessionSpellCaster.Hero, 1),
            caster.Log.Last(e => e.Kind is SpellCastEventKind.ObjectFlagKept or SpellCastEventKind.ObjectFlagWritten));
        // An END while another run provides the bit keeps it; the last provider's END clears it.
        EndComponent(owner, caster, first);
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, caster, new(SessionSpellRunActionKind.Free, first)).Disposition);
        EndComponent(owner, caster, second);
        Assert.AreEqual((false, false, false), caster.Holds(SessionSpellCaster.Hero));
        Assert.IsTrue(owner.QueryObjectFlags(SessionSpellCaster.Hero).IsEmpty, "the hero's data has no flag: a clear leaves nothing behind");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagWritten, second, 0x1000000, SessionSpellCaster.Hero, 0), caster.Log[^1]);
    }

    [TestMethod]
    public void The_END_of_an_interrupted_run_counts_the_runs_the_same_action_left()
    {
        // The END runs inside the run rules (the interrupt 0x4571E0): the count sees the processing's own table.
        var record = Hold(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.EntangledSpellFlag);
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var first = Cast(owner, caster, record);
        var second = Cast(owner, caster, record);
        caster.Act(new(SessionSpellRunActionKind.Interrupt, first));
        Assert.IsNull(owner.QuerySpellRuns()[first], "the first run ended");
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero), "the second still provides the bit");
        caster.Act(new(SessionSpellRunActionKind.Interrupt, second));
        Assert.IsNull(owner.QuerySpellRuns()[second]);
        Assert.AreEqual((false, false, false), caster.Holds(SessionSpellCaster.Hero), "the last provider's END clears it");
    }

    [TestMethod]
    public void A_paralysis_holds_the_hero_and_is_not_counted_and_a_stone_asks_for_no_interrupt()
    {
        var paralysis = Hold(SessionSpellCaster.CritterFlagsField, SessionSpellCaster.ParalysedCritterFlag, 94);
        var (owner, caster) = Session(RuleMode.Original, paralysis);
        using var _ = owner;
        var first = Cast(owner, caster, paralysis);
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0x40, 0), owner.QueryObjectFlags(SessionSpellCaster.Hero));
        Assert.IsFalse(owner.QueryHeroLife()!.Status.IsActive, "a paralysed hero cannot act (0x45D790)");
        Cast(owner, caster, paralysis);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.HoldStarted), "the critter flags count no run: a second paralysis interrupts again");
        // The first run's END clears the bit although the second still holds it (the original's own behaviour).
        EndComponent(owner, caster, first);
        Assert.AreEqual((false, false, false), caster.Holds(SessionSpellCaster.Hero));
        Assert.IsTrue(owner.QueryHeroLife()!.Status.IsActive);

        var stone = Hold(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.StonedSpellFlag, 95);
        var (o, c) = Session(RuleMode.Steamwoven, stone);
        using var __ = o;
        Cast(o, c, stone);
        Assert.AreEqual((false, true, false), c.Holds(SessionSpellCaster.Hero));
        Assert.IsFalse(c.Log.Any(e => e.Kind == SpellCastEventKind.HoldStarted), "stone interrupts nothing (GT146)");
        Assert.IsFalse(o.QueryHeroLife()!.Status.IsActive, "a stoned hero cannot act");
    }

    [TestMethod]
    public void Another_flag_or_field_of_an_ObjFlag_component_stays_not_ready()
    {
        foreach (var (field, flag) in new (int, uint)[] { (20, 0x1), (218, 0x10), (19, 0x100) })
        {
            var record = Hold(field, flag);
            var (owner, caster) = Session(RuleMode.Steamwoven, record);
            using var _ = owner;
            var run = Cast(owner, caster, record);
            Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, run, (int)SpellComponentType.ObjFlag, SessionSpellCaster.Hero),
                caster.Log.Single(e => e.Kind == SpellCastEventKind.ComponentNotReady), $"field {field} flag 0x{flag:X}");
            Assert.IsTrue(owner.QueryObjectFlags(SessionSpellCaster.Hero).IsEmpty);
        }
    }
}
