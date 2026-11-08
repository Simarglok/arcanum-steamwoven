using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-301: a critter's death and its loss of consciousness end its spell runs (0x45DA20 → 0x459740, 0x45D5A0 → 0x457450), the AI gate's demaintain
// (0x4A8570 → 0x457450) and an object's destroy (0x43CCA0 → 0x459740) reach the same run rules through the cast host. An invented spell record and map.
[TestClass]
public sealed class SessionSpellRunEndTests
{
    private const string Npc = "npc-a";
    private const string Other = "npc-b";
    private static readonly SpellLocation NpcTile = new(7, 5);

    // The hero and two NPCs standing; everything else absent.
    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc or Other => new(id, SpellTargetObject.Npc, NpcTile, HitPoints: 10),
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

    // A timed spell with no components of interest: a started run that has not begun is ended by event and freed, as a run of a vanished parent is.
    private static SessionSpellRecord Vine() =>
        SessionSpellRecords.Translate(Record(number: 51, beginTargets: 0x4, info: SpellInfo.Aggressive, components: [])
        with { Cost = 0, Range = 99, DurationSeconds = 10, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionSpellRecord Record) Setup()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var record = Vine();
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
        return (owner, caster, record);
    }

    // A run with the parent `parent` and the source `source` (a critter that is both needs the intelligence to cast, which the invented map does not give) at the hero.
    private static int Start(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string parent, string source = SessionSpellCaster.Hero)
    {
        var who = caster.Facts(parent)!;
        var by = caster.Facts(source)!;
        var hero = caster.Facts(SessionSpellCaster.Hero)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, who, by, hero, hero.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return started.Creation!.Run!.Id;
    }

    private static void Hurt(GameSessionOwner owner, string entity, LifeDamage damage) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Critter(entity, new LifeFacts(), default), damage).Disposition);

    [TestMethod]
    public void A_critters_death_ends_the_runs_it_casts_when_the_cast_host_is_idle_and_leaves_the_others()
    {
        var (owner, caster, record) = Setup();
        using var _ = owner;
        var mine = Start(owner, caster, record, Npc);
        var theirs = Start(owner, caster, record, Other);
        Hurt(owner, Npc, new LifeDamage { HitPoints = 1000 });
        // The ending is the host's to tell; the run rules were not under way, so the next call of the host does.
        Assert.IsNotNull(owner.QuerySpellRuns()[mine], "the run lives until the host tells it");
        caster.FireItemEvents([]);
        Assert.IsNull(owner.QuerySpellRuns()[mine], "the parent died: its run ends");
        Assert.IsNotNull(owner.QuerySpellRuns()[theirs], "another critter's run stays");
    }

    [TestMethod]
    public void A_source_that_loses_consciousness_demaintains_its_runs_but_a_wound_that_leaves_it_conscious_does_not()
    {
        var (owner, caster, record) = Setup();
        using var _ = owner;
        var mine = Start(owner, caster, record, Npc);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1 }).Disposition);
        caster.FireItemEvents([]);
        Assert.IsNotNull(owner.QuerySpellRuns()[mine], "a wound alone ends nothing");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 1000 }).Disposition);
        caster.FireItemEvents([]);
        Assert.IsNull(owner.QuerySpellRuns()[mine], "the source's loss of consciousness (the fatigue setter) demaintains its runs (0x457450)");
    }

    [TestMethod]
    public void A_heros_death_ends_the_runs_it_casts()
    {
        var (owner, caster, record) = Setup();
        using var _ = owner;
        var mine = Start(owner, caster, record, SessionSpellCaster.Hero, source: Npc);
        var max = owner.QueryHeroLife()!.Status.HitPoints;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = max + 1000 }).Disposition);
        caster.FireItemEvents([]);
        Assert.IsNull(owner.QuerySpellRuns()[mine]);
    }

    [TestMethod]
    public void The_gate_demaintain_and_an_objects_destroy_reach_the_run_rules_through_the_host()
    {
        var (owner, caster, record) = Setup();
        using var _ = owner;
        var kept = Start(owner, caster, record, Npc, source: Other);
        caster.EndMaintainedSpells(Npc);
        Assert.IsNotNull(owner.QuerySpellRuns()[kept], "the demaintain of a critter that is only the parent ends nothing");
        caster.EndMaintainedSpells(Other);
        Assert.IsNull(owner.QuerySpellRuns()[kept], "the gate's demaintain ends the runs the NPC is the source of");
        var parent = Start(owner, caster, record, Other);
        caster.ObjectGone(Other);
        Assert.IsNull(owner.QuerySpellRuns()[parent], "the destroy of the parent ends its run");
    }

    [TestMethod]
    public void Without_a_cast_host_a_death_tells_nobody_and_the_runs_stay()
    {
        var (owner, caster, record) = Setup();
        using var _ = owner;
        var mine = Start(owner, caster, record, Npc);
        owner.CritterRunEnd = null;
        Hurt(owner, Npc, new LifeDamage { HitPoints = 1000 });
        caster.FireItemEvents([]);
        Assert.IsNotNull(owner.QuerySpellRuns()[mine]);
    }
}
