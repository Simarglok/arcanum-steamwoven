using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-035 (MA-4, ledger SO1 to SO8: 0x454A10 re-read against the disassembly): the social and AI flags of the ObjFlag component (charmed, mind controlled; undead, fleeing, blinded) with the
// reaction, peace, flee, follow and blind steps the original runs. Entities and values are invented; the AI host is a recorder.
[TestClass]
public sealed class SessionSpellCasterSocialTests
{
    private const string Target = "oid:target";
    private const string Hero = SessionSpellCaster.Hero;
    private const int Charmed = 0x800000, MindControlled = 0x8000000, Undead = 0x4, Fleeing = 0x10, Blinded = 0x80;
    private static readonly SpellLocation HeroTile = new(5, 5), TargetTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter, int dataSpellFlags = 0, int dataCritterFlags = 0) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Target => new(id, SpellTargetObject.Npc, TargetTile, SpellFlags: dataSpellFlags, CritterFlags: dataCritterFlags, HitPoints: 5),
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
        public SpellCastCritter? Critter(string entity) => entity == Target ? critter with { SpellFlags = dataSpellFlags, CritterFlags = dataCritterFlags } : null;
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

    // The AI host's recorder: each call in order; a step answers what the test says (true: it ran).
    private sealed class Social : ISpellSocialHost
    {
        public List<string> Calls { get; } = [];
        public bool Answer { get; set; } = true;
        public Action<string>? Observe { get; set; }
        private bool Record(string call) { Calls.Add(call); Observe?.Invoke(call); return Answer; }
        public bool ChangeReaction(string npc, int delta) => Record($"reaction {npc} {delta}");
        public bool Peace(string npc, string with) => Record($"peace {npc} {with}");
        public bool Flee(string npc, string from) => Record($"flee {npc} {from}");
        public bool StopFleeing(string npc) => Record($"stop-fleeing {npc}");
        public void EndFleeGoals(string npc) => Record($"end-flee-goals {npc}");
    }

    private static SpellCastCritter TargetCritter()
    {
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0);
        return new(SessionCritterCombat.From(Target, fields).Combat!, 0, 0, 0, 0, 0);
    }

    private static SessionSpellRecord Flag(int field, uint flag, int number = 93) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, true)),
                new(SpellAction.End, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, false))])
            with { Cost = 10, MaintainCost = 3, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0,
                SpellFlagsSet = field == SessionSpellCaster.SpellFlagsField ? flag : 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Social Host) Session(SessionSpellRecord record, int dataSpellFlags = 0, int dataCritterFlags = 0, bool host = true)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var social = new Social();
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, TargetCritter(), dataSpellFlags, dataCritterFlags), new Events()) { Social = host ? social : null };
        return (owner, caster, social);
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string to = Target)
    {
        var hero = caster.Facts(Hero)!;
        var at = caster.Facts(to)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, at, at.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        return run;
    }

    private static void EndComponent(GameSessionOwner owner, SessionSpellCaster caster, int run, string on = Target) =>
        caster.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(caster.RunObject(on), on == Hero ? HeroTile : TargetTile));

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    private static IEnumerable<SpellCastEvent> Social_(SessionSpellCaster caster) => caster.Log.Where(e => e.Kind == SpellCastEventKind.SocialEffect);

    private static bool Blind(GameSessionOwner owner, SessionSpellCaster caster, string id) =>
        owner.QueryLife(caster.LifeTarget(id)!).View!.Record.State.Conditions.HasFlag(LifeConditions.Blinded);

    [TestMethod]
    public void Charm_gives_plus_30_toward_the_hero_when_the_hero_casts_and_minus_30_at_the_end_and_the_flag_is_written_and_cleared()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, Charmed);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        CollectionAssert.AreEqual(new[] { $"reaction {Target} 30" }, host.Calls, Trail(caster));
        Assert.AreEqual(Charmed, owner.QueryObjectFlags(Target).SpellSet);
        // ARC-MAGIC-1142 (SO2): the NPC flag KOS_OVERRIDE the original adds is in the session's NPC flag layer, logged as a side effect of the write.
        Assert.AreEqual(SessionSpellCaster.KosOverrideNpcFlag, owner.QueryObjectFlags(Target).NpcSet);
        Assert.IsTrue(caster.Log.Contains(new(SpellCastEventKind.ObjectFlagWritten, run, SessionSpellCaster.KosOverrideNpcFlag, Target,
            64 | SessionSpellCaster.SideEffectDetail | 1)), Trail(caster));
        Assert.IsFalse(Social_(caster).Any(e => e.Value == SessionSpellCaster.SocialNotModelled), Trail(caster));
        host.Calls.Clear();
        EndComponent(owner, caster, run);
        CollectionAssert.AreEqual(new[] { $"reaction {Target} -30" }, host.Calls, "the end takes the +30 back (0x454F39)");
        // The clear branch takes the spell flag back and leaves the NPC flag for good (0x454F22 has no NPC flag write).
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0, 0, NpcSet: SessionSpellCaster.KosOverrideNpcFlag), owner.QueryObjectFlags(Target));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), Trail(caster));
    }

    [TestMethod]
    public void Charm_of_an_animal_makes_peace_with_the_caster_and_gives_no_reaction()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, Charmed);
        var (owner, caster, host) = Session(record, dataCritterFlags: SessionSpellCaster.AnimalCritterFlag);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        CollectionAssert.AreEqual(new[] { $"peace {Target} {Hero}" }, host.Calls, Trail(caster));
        Assert.AreEqual(Charmed, owner.QueryObjectFlags(Target).SpellSet);
        host.Calls.Clear();
        EndComponent(owner, caster, run);
        // The clear has no animal branch: the minus 30 goes to the hero for any NPC (0x454F39).
        CollectionAssert.AreEqual(new[] { $"reaction {Target} -30" }, host.Calls);
    }

    [TestMethod]
    public void Charm_on_the_hero_is_a_plain_write_with_no_step()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, Charmed);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        Cast(owner, caster, record, Hero);
        Assert.AreEqual(Charmed, owner.QueryObjectFlags(Hero).SpellSet);
        Assert.AreEqual(0, host.Calls.Count);
        Assert.IsFalse(Social_(caster).Any(), Trail(caster));
    }

    [TestMethod]
    public void Mind_control_gives_minus_50_before_the_flag_then_a_forced_follower_and_no_flee_and_the_end_disbands_it()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, MindControlled);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        // The reaction reads the NPC before the flag is written (the original's order).
        string? flagAtReaction = "unset";
        host.Observe = _ => flagAtReaction = owner.QueryObjectFlags(Target).IsEmpty ? "clear" : "written";
        var run = Cast(owner, caster, record);
        CollectionAssert.AreEqual(new[] { $"reaction {Target} -50" }, host.Calls, Trail(caster));
        Assert.AreEqual("clear", flagAtReaction, "minus 50 comes first");
        Assert.AreEqual(MindControlled, owner.QueryObjectFlags(Target).SpellSet);
        var companion = owner.QueryCompanion(Target);
        Assert.AreEqual(CompanionStanding.Following, companion.Standing, Trail(caster));
        Assert.IsTrue(companion.Forced, "forced join (0x45DE90 with force 1)");
        Assert.IsTrue(owner.QueryDanger(Target).Flags.HasFlag(DangerFlags.NoFlee), "no-flee for good (0x4AF1D0)");
        CollectionAssert.AreEqual(new[] { SessionSpellCaster.SocialReaction, SessionSpellCaster.SocialJoin, SessionSpellCaster.SocialNoFlee }, Social_(caster).Select(e => e.Value).ToArray(), Trail(caster));
        // The end: the flag off, a forced leave; the reaction is not given back and no-flee stays.
        host.Calls.Clear();
        EndComponent(owner, caster, run);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty);
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion(Target).Standing);
        Assert.AreEqual(0, host.Calls.Count, "no reaction at the end");
        Assert.IsTrue(owner.QueryDanger(Target).Flags.HasFlag(DangerFlags.NoFlee));
        Assert.IsTrue(Social_(caster).Any(e => e.Value == SessionSpellCaster.SocialLeave), Trail(caster));
    }

    [TestMethod]
    public void Mind_control_of_a_fleeing_npc_stops_the_fleeing_first_and_the_host_ends_its_flee_goals()
    {
        // ARC-AI-1513 (0x4AF1D0 runs 0x4AF170 first): the controlled NPC in FLEE goes to NONE in the same revision as NO_FLEE and the host is asked to end its flee goals.
        var record = Flag(SessionSpellCaster.SpellFlagsField, MindControlled);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        var flee = owner.SetDangerState(owner.Version, new SessionDangerRequest { Entity = Target, State = DangerState.Flee, Target = SessionCombatant.Hero });
        Assert.AreEqual(DangerState.Flee, owner.QueryDanger(Target).State, "setup: " + flee.Disposition);
        Cast(owner, caster, record);
        var danger = owner.QueryDanger(Target);
        Assert.AreEqual((DangerState.None, true), (danger.State, danger.Flags.HasFlag(DangerFlags.NoFlee)), Trail(caster));
        CollectionAssert.Contains(host.Calls, $"end-flee-goals {Target}");
    }

    [TestMethod]
    public void Mind_control_of_a_summoned_creature_gives_no_reaction()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, MindControlled);
        var (owner, caster, host) = Session(record, dataSpellFlags: SpellTargetObject.Summoned);
        using var _ = owner;
        Cast(owner, caster, record);
        Assert.AreEqual(0, host.Calls.Count, Trail(caster));
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion(Target).Standing);
    }

    [TestMethod]
    public void Mind_control_of_the_hero_by_a_caster_writes_nothing()
    {
        // The function returns before any write for an object that is no NPC when it has a caster (SO3).
        var record = Flag(SessionSpellCaster.SpellFlagsField, MindControlled);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record, Hero);
        Assert.IsTrue(owner.QueryObjectFlags(Hero).IsEmpty);
        Assert.AreEqual(0, host.Calls.Count);
        Assert.IsTrue(Social_(caster).Contains(new(SpellCastEventKind.SocialEffect, run, SessionSpellCaster.SocialIgnored, Hero, MindControlled)), Trail(caster));
    }

    [TestMethod]
    public void A_refusing_host_is_logged_and_the_flag_is_still_written()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, Charmed);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        host.Answer = false;
        var run = Cast(owner, caster, record);
        Assert.IsTrue(Social_(caster).Contains(new(SpellCastEventKind.SocialEffect, run, SessionSpellCaster.SocialRefused, Target, SessionSpellCaster.SocialReaction)), Trail(caster));
        Assert.AreEqual(Charmed, owner.QueryObjectFlags(Target).SpellSet);
    }

    [TestMethod]
    public void Fleeing_asks_the_flee_function_with_the_caster_and_the_end_asks_stop_fleeing_and_the_flag_is_never_written()
    {
        var record = Flag(SessionSpellCaster.CritterFlagsField, Fleeing);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        CollectionAssert.AreEqual(new[] { $"flee {Target} {Hero}" }, host.Calls, Trail(caster));
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, "0x454A10 returns before the closing write; the setter keeps the state");
        host.Calls.Clear();
        EndComponent(owner, caster, run);
        CollectionAssert.AreEqual(new[] { $"stop-fleeing {Target}" }, host.Calls);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), Trail(caster));
    }

    [TestMethod]
    public void Fleeing_on_the_hero_does_nothing()
    {
        var record = Flag(SessionSpellCaster.CritterFlagsField, Fleeing);
        var (owner, caster, host) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record, Hero);
        Assert.AreEqual(0, host.Calls.Count);
        Assert.IsTrue(owner.QueryObjectFlags(Hero).IsEmpty);
        Assert.IsTrue(Social_(caster).Contains(new(SpellCastEventKind.SocialEffect, run, SessionSpellCaster.SocialIgnored, Hero, Fleeing)), Trail(caster));
    }

    [TestMethod]
    public void Blinded_sets_the_blinded_life_condition_and_the_end_clears_it_for_an_NPC_and_the_hero()
    {
        foreach (var who in new[] { Target, Hero })
        {
            var record = Flag(SessionSpellCaster.CritterFlagsField, Blinded);
            var (owner, caster, host) = Session(record, host: false);
            using var _ = owner;
            Assert.IsFalse(Blind(owner, caster, who), who);
            var run = Cast(owner, caster, record, who);
            Assert.IsTrue(Blind(owner, caster, who), $"{who}: {Trail(caster)}");
            Assert.IsTrue(owner.QueryObjectFlags(who).IsEmpty, "the condition is the life state's, not a second flag word");
            EndComponent(owner, caster, run, who);
            Assert.IsFalse(Blind(owner, caster, who), who);
            Assert.AreEqual(2, Social_(caster).Count(e => e.Value == SessionSpellCaster.SocialBlinded), Trail(caster));
        }
    }

    [TestMethod]
    public void Blinded_reaches_the_combat_values_of_the_critter()
    {
        var record = Flag(SessionSpellCaster.CritterFlagsField, Blinded);
        var (owner, caster, _) = Session(record, host: false);
        using var __ = owner;
        Assert.IsFalse(caster.Combatant(new(Target, SpellObjectFacts.NpcType)).Conditions.HasFlag(CombatantConditions.Blinded));
        Cast(owner, caster, record);
        Assert.IsTrue(owner.QueryLife(caster.LifeTarget(Target)!).View!.Record.State.Conditions.HasFlag(LifeConditions.Blinded));
        Assert.IsTrue(caster.Combatant(new(Target, SpellObjectFacts.NpcType)).Conditions.HasFlag(CombatantConditions.Blinded), "the resolver's combatant carries it");
    }

    [TestMethod]
    public void The_end_of_blinded_after_the_critter_left_the_map_still_clears_the_recorded_condition()
    {
        var record = Flag(SessionSpellCaster.CritterFlagsField, Blinded);
        var (owner, caster, _) = Session(record, host: false);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.IsTrue(Blind(owner, caster, Target));
        var life = caster.LifeTarget(Target)!;
        // A host over a map that no longer has the critter.
        var away = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Gone(new Map(owner, TargetCritter())), new Events());
        away.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(new SpellRunObject(Target, SpellObjectFacts.NpcType), TargetTile));
        Assert.IsFalse(owner.QueryLife(life).View!.Record.State.Conditions.HasFlag(LifeConditions.Blinded), Trail(away));
    }

    // The map with the critter gone.
    private sealed class Gone(Map inner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id == Target ? null : inner.Object(id);
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

    [TestMethod]
    public void Undead_writes_the_object_flag_and_the_critter_flag_and_the_end_takes_both_back()
    {
        var record = Flag(SessionSpellCaster.CritterFlagsField, Undead);
        var (owner, caster, _) = Session(record, host: false);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.AreEqual(new SessionObjectFlags(0, 0, Undead, 0, SessionSpellCaster.ObjectUndeadMark, 0), owner.QueryObjectFlags(Target), Trail(caster));
        Assert.AreEqual(Undead, caster.Facts(Target)!.CritterFlags & Undead, "the host's facts read it");
        EndComponent(owner, caster, run);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, Trail(caster));
    }

    [TestMethod]
    public void Without_a_social_host_the_flags_that_need_the_AI_stay_not_ready_on_an_NPC_and_write_nothing()
    {
        foreach (var (field, flag) in new (int, uint)[] { (20, Charmed), (20, MindControlled), (218, Fleeing) })
        {
            var record = Flag(field, flag);
            var (owner, caster, _) = Session(record, host: false);
            using var _ = owner;
            var run = Cast(owner, caster, record);
            Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, run, (int)SpellComponentType.ObjFlag, Target), caster.Log.Single(e => e.Kind == SpellCastEventKind.ComponentNotReady), $"field {field} 0x{flag:X}");
            Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty);
            Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion(Target).Standing);
        }
    }
}
